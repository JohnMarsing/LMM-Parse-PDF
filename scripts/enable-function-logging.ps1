# Create Application Insights + enable app logging for the Function app.
#
# From repo root:
#   .\scripts\enable-function-logging.ps1

param(
    [string] $AppName = "lmm-shabbat-pdf",
    [string] $ResourceGroup = "LmmWebAppGroup",
    [string] $Location = "westus",
    [string] $InsightsName = "lmm-shabbat-pdf-insights"
)

$ErrorActionPreference = "Stop"

Write-Host "Creating Application Insights '$InsightsName' (if missing) ..." -ForegroundColor Cyan
$existing = az monitor app-insights component show --app $InsightsName -g $ResourceGroup -o json 2>$null
if ($LASTEXITCODE -ne 0 -or -not $existing) {
    az extension add --name application-insights --yes 2>$null
    az monitor app-insights component create `
        --app $InsightsName `
        --location $Location `
        --resource-group $ResourceGroup `
        --application-type web `
        --kind web `
        -o none
}

$conn = az monitor app-insights component show `
    --app $InsightsName `
    -g $ResourceGroup `
    --query connectionString `
    -o tsv
$ikey = az monitor app-insights component show `
    --app $InsightsName `
    -g $ResourceGroup `
    --query instrumentationKey `
    -o tsv

if ([string]::IsNullOrWhiteSpace($conn)) {
    throw "Could not read Application Insights connection string."
}

Write-Host "Linking Application Insights to Function app ..." -ForegroundColor Cyan
az functionapp config appsettings set -g $ResourceGroup -n $AppName --settings `
    "APPLICATIONINSIGHTS_CONNECTION_STRING=$conn" `
    "APPINSIGHTS_INSTRUMENTATIONKEY=$ikey" `
    -o none

Write-Host "Enabling filesystem application logging ..." -ForegroundColor Cyan
az webapp log config `
    -g $ResourceGroup `
    -n $AppName `
    --application-logging filesystem `
    --level information `
    --web-server-logging filesystem `
    -o none

Write-Host ""
Write-Host "Done. View logs in Portal:" -ForegroundColor Green
Write-Host "  1. Function app → ProcessShabbatPdf → Monitor (invocations)"
Write-Host "  2. Function app → Log stream (live)"
Write-Host "  3. Application Insights '$InsightsName' → Transaction search / Logs"
Write-Host ""
Write-Host "Kusto sample (Application Insights → Logs):" -ForegroundColor Cyan
Write-Host "  traces | where timestamp > ago(1d) | order by timestamp desc | take 50"
Write-Host "  requests | where timestamp > ago(1d) | order by timestamp desc"
Write-Host "  exceptions | where timestamp > ago(1d) | order by timestamp desc"
