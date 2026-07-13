# Batch-export teaching-only PDFs from Azure Blob Storage via the CLI.
#
# Lists blobs in shabbat-service, skips *-teaching.pdf, and runs:
#   dotnet run ... --blob <name> --teaching-only --skip-existing
#
# Does NOT create Markdown (.md). Only uploads *-teaching.pdf to shabbat-service.
#
# Prerequisites:
#   - Blob:ConnectionString in user secrets (or Blob__ConnectionString env)
#     Same secret the CLI uses for --blob runs.
#   - Smoke-test one blob first:
#       --blob "YYYY-MM-DD-Citation.pdf" --teaching-only
#
# Blob listing auth (first match wins):
#   1. -ConnectionString parameter
#   2. $env:Blob__ConnectionString
#   3. $env:AZURE_STORAGE_CONNECTION_STRING
#   4. dotnet user-secrets "Blob:ConnectionString" on the CLI project
#   5. az --auth-mode key (needs permission to list account keys)
#   6. az --auth-mode login (needs Storage Blob Data Reader RBAC)
#
# From repo root:
#   .\scripts\batch-blob-parse.ps1
#   .\scripts\batch-blob-parse.ps1 -WhatIf
#   .\scripts\batch-blob-parse.ps1 -MaxCount 5
#   .\scripts\batch-blob-parse.ps1 -NoSkipExisting

param(
    [string] $AccountName = "livingmessiahstorage",
    [string] $ContainerName = "shabbat-service",
    [string] $ProjectPath = "src/LivingMessiah.ShabbatPdf.Cli",
    [string] $ConnectionString = "",
    [switch] $NoSkipExisting,
    [switch] $AllowNonstandardName,
    [switch] $DryRun,
    [switch] $WhatIf,
    [int] $MaxCount = 0,
    [string] $NameContains = "",
    [string] $LogPath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    Write-Error "Azure CLI (az) not found. Install it from https://aka.ms/installazurecliwindows"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet SDK not found."
}

if ([string]::IsNullOrWhiteSpace($LogPath)) {
    $logDir = Join-Path $repoRoot "out"
    if (-not (Test-Path $logDir)) {
        New-Item -ItemType Directory -Path $logDir | Out-Null
    }
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $LogPath = Join-Path $logDir "batch-blob-parse-$stamp.log"
}

function Write-Log {
    param([string] $Message, [string] $Color = "White")
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $Message"
    Write-Host $line -ForegroundColor $Color
    Add-Content -Path $LogPath -Value $line
}

function Get-BlobConnectionString {
    param([string] $Explicit, [string] $CliProject)

    if (-not [string]::IsNullOrWhiteSpace($Explicit)) {
        return $Explicit.Trim()
    }

    if (-not [string]::IsNullOrWhiteSpace($env:Blob__ConnectionString)) {
        return $env:Blob__ConnectionString.Trim()
    }

    if (-not [string]::IsNullOrWhiteSpace($env:AZURE_STORAGE_CONNECTION_STRING)) {
        return $env:AZURE_STORAGE_CONNECTION_STRING.Trim()
    }

    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $secretsOut = & dotnet user-secrets list --project $CliProject 2>&1 | ForEach-Object { "$_" }
    $ErrorActionPreference = $prevEap

    foreach ($line in $secretsOut) {
        if ($line -match '^\s*Blob:ConnectionString\s*=\s*(.+)\s*$') {
            $value = $Matches[1].Trim()
            if (-not [string]::IsNullOrWhiteSpace($value)) {
                return $value
            }
        }
    }

    return $null
}

function Invoke-AzJson {
    param([string[]] $AzArgs)

    $outFile = [System.IO.Path]::GetTempFileName()
    $errFile = [System.IO.Path]::GetTempFileName()
    try {
        $prevEap = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        $p = Start-Process -FilePath "az.cmd" -ArgumentList $AzArgs `
            -NoNewWindow -Wait -PassThru `
            -RedirectStandardOutput $outFile `
            -RedirectStandardError $errFile
        if (-not $p) {
            # Fallback if az.cmd is not resolved; try az
            $p = Start-Process -FilePath "az" -ArgumentList $AzArgs `
                -NoNewWindow -Wait -PassThru `
                -RedirectStandardOutput $outFile `
                -RedirectStandardError $errFile
        }
        $ErrorActionPreference = $prevEap

        $stdout = ""
        $stderr = ""
        if (Test-Path $outFile) {
            $stdout = [System.IO.File]::ReadAllText($outFile)
        }
        if (Test-Path $errFile) {
            $stderr = [System.IO.File]::ReadAllText($errFile)
        }

        return [pscustomobject]@{
            ExitCode = $p.ExitCode
            StdOut   = $stdout
            StdErr   = $stderr
        }
    }
    finally {
        Remove-Item -LiteralPath $outFile, $errFile -Force -ErrorAction SilentlyContinue
    }
}

function Get-AgendaBlobNames {
    param(
        [string] $Container,
        [string] $Account,
        [string] $ConnString
    )

    # No JMESPath filters here: on Windows they break under cmd when passed via Start-Process.
    # Filter .pdf / -teaching in PowerShell instead.
    $attempts = @()

    if (-not [string]::IsNullOrWhiteSpace($ConnString)) {
        $attempts += @{
            Label = "connection-string"
            Args  = @(
                "storage", "blob", "list",
                "--connection-string", $ConnString,
                "--container-name", $Container,
                "-o", "json"
            )
        }
    }

    $attempts += @{
        Label = "auth-mode key"
        Args  = @(
            "storage", "blob", "list",
            "--account-name", $Account,
            "--container-name", $Container,
            "--auth-mode", "key",
            "-o", "json"
        )
    }

    $attempts += @{
        Label = "auth-mode login"
        Args  = @(
            "storage", "blob", "list",
            "--account-name", $Account,
            "--container-name", $Container,
            "--auth-mode", "login",
            "-o", "json"
        )
    }

    $errors = New-Object System.Collections.Generic.List[string]

    foreach ($attempt in $attempts) {
        Write-Log "Listing blobs with az ($($attempt.Label)) ..." "DarkGray"
        $result = Invoke-AzJson -AzArgs $attempt.Args
        if ($result.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($result.StdOut)) {
            return $result.StdOut
        }

        $detail = if ($result.StdErr) { $result.StdErr.Trim() } else { "(no stderr, exit $($result.ExitCode))" }
        $errors.Add("[$($attempt.Label)] $detail") | Out-Null
        Write-Log "  failed: $($detail.Split([char]10)[0])" "Yellow"
    }

    $joined = ($errors -join "`n`n")
    throw @"
az storage blob list failed with all auth methods.

$joined

Fix options (pick one):
  1. Set the same connection string the CLI uses:
       dotnet user-secrets set "Blob:ConnectionString" "<full-connection-string>" ``
         --project $ProjectPath
     (Must look like DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net)
  2. Or: `$env:Blob__ConnectionString = "<full-connection-string>"
  3. Or grant RBAC "Storage Blob Data Reader" on the storage account and run az login
"@
}

Write-Log "Log file: $LogPath" "DarkGray"
Write-Log "Repo: $repoRoot" "DarkGray"
Write-Log "Listing pdf blobs in $AccountName / $ContainerName ..." "Cyan"

$conn = Get-BlobConnectionString -Explicit $ConnectionString -CliProject $ProjectPath
if ($conn) {
    $previewLen = [Math]::Min(24, $conn.Length)
    Write-Log "Found connection string (length=$($conn.Length), starts with '$($conn.Substring(0, $previewLen))...')." "DarkGray"
    if ($conn.Length -lt 60 -or $conn -notmatch 'AccountName=') {
        Write-Log "WARNING: connection string looks incomplete. Expected DefaultEndpointsProtocol=...;AccountName=...;AccountKey=..." "Yellow"
    }
}
else {
    Write-Log "No connection string in param/env/user-secrets; trying az account key or login." "Yellow"
}

try {
    $listJson = Get-AgendaBlobNames `
        -Container $ContainerName `
        -Account $AccountName `
        -ConnString $conn
}
catch {
    Write-Log $_.Exception.Message "Red"
    throw
}

try {
    $parsed = $listJson | ConvertFrom-Json
}
catch {
    Write-Error "Failed to parse blob list JSON: $($_.Exception.Message)"
}

$allNames = @()
foreach ($item in @($parsed)) {
    if ($null -eq $item) { continue }
    # az -o json without --query returns objects with .name
    if ($item -is [string]) {
        $allNames += $item
    }
    elseif ($item.PSObject.Properties.Name -contains "name") {
        $allNames += [string]$item.name
    }
}

# Full agendas only — never re-parse teaching slices as source
$names = @(
    $allNames |
        Where-Object {
            $_ -and
            ($_ -match '(?i)\.pdf$') -and
            ($_ -notmatch '(?i)-teaching\.pdf$')
        } |
        Sort-Object -Unique
)

if (-not [string]::IsNullOrWhiteSpace($NameContains)) {
    $names = @($names | Where-Object { $_ -like "*$NameContains*" })
}

if ($MaxCount -gt 0 -and $names.Count -gt $MaxCount) {
    Write-Log "Limiting to first $MaxCount of $($names.Count) blobs (-MaxCount)." "Yellow"
    $names = @($names | Select-Object -First $MaxCount)
}

Write-Log "Found $($names.Count) agenda PDF(s) to process." "Cyan"

if ($names.Count -eq 0) {
    Write-Log "Nothing to do." "Yellow"
    exit 0
}

if ($WhatIf) {
    Write-Log "WhatIf: would process:" "Yellow"
    foreach ($n in $names) {
        Write-Log "  $n" "DarkGray"
    }
    exit 0
}

Write-Log "Building $ProjectPath ..." "Cyan"
$prevEap = $ErrorActionPreference
$ErrorActionPreference = "Continue"
& dotnet build $ProjectPath -v q
$buildCode = $LASTEXITCODE
$ErrorActionPreference = $prevEap
if ($buildCode -ne 0) {
    Write-Error "dotnet build failed (exit $buildCode)."
}

$ok = 0
$fail = 0
$failed = [System.Collections.Generic.List[string]]::new()
$index = 0

foreach ($name in $names) {
    $index++
    Write-Log "=== [$index/$($names.Count)] $name ===" "Cyan"

    $cliArgs = @(
        "run", "--project", $ProjectPath, "--no-build", "--",
        "--blob", $name,
        "--teaching-only"
    )

    if (-not $NoSkipExisting) {
        $cliArgs += "--skip-existing"
    }
    if ($AllowNonstandardName) {
        $cliArgs += "--allow-nonstandard-name"
    }
    if ($DryRun) {
        $cliArgs += "--dry-run"
    }

    $ErrorActionPreference = "Continue"
    & dotnet @cliArgs
    $code = $LASTEXITCODE
    $ErrorActionPreference = $prevEap

    if ($code -eq 0) {
        $ok++
        Write-Log "OK: $name" "Green"
    }
    else {
        $fail++
        $failed.Add($name) | Out-Null
        Write-Log "FAILED: $name (exit $code)" "Red"
    }
}

Write-Log "========== Summary ==========" "Cyan"
Write-Log "Succeeded: $ok" "Green"
Write-Log "Failed:    $fail" $(if ($fail -gt 0) { "Red" } else { "Green" })
Write-Log "Log:       $LogPath" "DarkGray"

if ($failed.Count -gt 0) {
    Write-Log "Failed blob names:" "Red"
    foreach ($f in $failed) {
        Write-Log "  $f" "Red"
    }
    exit 1
}

exit 0
