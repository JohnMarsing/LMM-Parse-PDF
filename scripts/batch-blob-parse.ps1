# Batch-parse Shabbat agenda PDFs from Azure Blob Storage via the CLI.
#
# Lists blobs in shabbat-service, skips *-teaching.pdf, and runs:
#   dotnet run ... --blob <name> --skip-existing
#
# Prerequisites:
#   - Azure CLI logged in (az login) with access to the storage account
#   - Blob:ConnectionString in user secrets (or Blob__ConnectionString env)
#   - Smoke-test one blob first:  --blob "YYYY-MM-DD-Citation.pdf"
#
# From repo root:
#   .\scripts\batch-blob-parse.ps1
#   .\scripts\batch-blob-parse.ps1 -WhatIf
#   .\scripts\batch-blob-parse.ps1 -MaxCount 5
#   .\scripts\batch-blob-parse.ps1 -EnsureContainer
#   .\scripts\batch-blob-parse.ps1 -NoSkipExisting   # reprocess / overwrite

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string] $AccountName = "livingmessiahstorage",
    [string] $ContainerName = "shabbat-service",
    [string] $ProjectPath = "src/LivingMessiah.ShabbatPdf.Cli",
    [switch] $EnsureContainer,
    [switch] $NoSkipExisting,
    [switch] $AllowNonstandardName,
    [switch] $DryRun,
    [int] $MaxCount = 0,
    [string] $NameContains = "",
    [string] $LogPath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    Write-Error "Azure CLI (az) not found. Install it and run 'az login'."
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

Write-Log "Log file: $LogPath" "DarkGray"
Write-Log "Repo: $repoRoot" "DarkGray"
Write-Log "Listing pdf blobs in $AccountName / $ContainerName ..." "Cyan"

$listJson = az storage blob list `
    --account-name $AccountName `
    --container-name $ContainerName `
    --auth-mode login `
    --query "[?ends_with(name, '.pdf')].name" `
    -o json 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Error "az storage blob list failed: $listJson"
}

$allNames = @($listJson | ConvertFrom-Json)
if ($null -eq $allNames) {
    $allNames = @()
}

# Full agendas only — never re-parse teaching slices as source
$names = @(
    $allNames |
        Where-Object { $_ -and ($_ -notmatch '(?i)-teaching\.pdf$') } |
        Sort-Object
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

if ($WhatIfPreference) {
    Write-Log "WhatIf: would process:" "Yellow"
    foreach ($n in $names) {
        Write-Log "  $n" "DarkGray"
    }
    exit 0
}

Write-Log "Building $ProjectPath ..." "Cyan"
dotnet build $ProjectPath -v q
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet build failed (exit $LASTEXITCODE)."
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
        "--blob", $name
    )

    if (-not $NoSkipExisting) {
        $cliArgs += "--skip-existing"
    }
    if ($EnsureContainer) {
        $cliArgs += "--ensure-container"
    }
    if ($AllowNonstandardName) {
        $cliArgs += "--allow-nonstandard-name"
    }
    if ($DryRun) {
        $cliArgs += "--dry-run"
    }

    & dotnet @cliArgs
    $code = $LASTEXITCODE

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
