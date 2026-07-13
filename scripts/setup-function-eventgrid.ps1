# Wire Event Grid blob events -> Function EventGridTrigger.
# Flex Consumption does not poll containers; without this subscription the function never runs.
#
# Prerequisites: az login, function already deployed (EventGridTrigger ProcessShabbatPdf).
#
# From repo root:
#   .\scripts\setup-function-eventgrid.ps1

param(
    [string] $AppName = "lmm-shabbat-pdf",
    [string] $ResourceGroup = "LmmWebAppGroup",
    [string] $StorageAccount = "livingmessiahstorage",
    [string] $ContainerName = "shabbat-service",
    [string] $FunctionName = "ProcessShabbatPdf",
    [string] $SubscriptionName = "lmm-shabbat-pdf-blob-created"
)

$ErrorActionPreference = "Stop"

Write-Host "Ensuring Microsoft.EventGrid provider is registered ..." -ForegroundColor Cyan
az provider register --namespace Microsoft.EventGrid --wait 2>$null

Write-Host "Getting eventgrid_extension system key ..." -ForegroundColor Cyan
$keysJson = az functionapp keys list -g $ResourceGroup -n $AppName -o json | ConvertFrom-Json
$egKey = $keysJson.systemKeys.eventgrid_extension
if ([string]::IsNullOrWhiteSpace($egKey)) {
    $egKey = $keysJson.systemKeys.PSObject.Properties |
        Where-Object { $_.Name -match 'eventgrid' } |
        Select-Object -First 1 -ExpandProperty Value
}
if ([string]::IsNullOrWhiteSpace($egKey)) {
    throw "Could not find system key 'eventgrid_extension'. Redeploy the Function app and retry."
}

# Standard Event Grid webhook for Azure Functions EventGridTrigger
$endpoint = "https://$AppName.azurewebsites.net/runtime/webhooks/eventgrid?functionName=$FunctionName&code=$egKey"
Write-Host "Endpoint (key redacted): https://$AppName.azurewebsites.net/runtime/webhooks/eventgrid?functionName=$FunctionName&code=***" -ForegroundColor DarkGray

$storageId = az storage account show -n $StorageAccount -g $ResourceGroup --query id -o tsv
$subjectBegins = "/blobServices/default/containers/$ContainerName/blobs/"

# Warm the app so validation handshake can succeed
Write-Host "Warming function app ..." -ForegroundColor Cyan
try {
    Invoke-WebRequest -Uri "https://$AppName.azurewebsites.net" -UseBasicParsing -TimeoutSec 120 | Out-Null
} catch {
    Write-Host "Warm request: $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host "Creating/updating Event Grid subscription '$SubscriptionName' ..." -ForegroundColor Cyan

$existing = az eventgrid event-subscription show `
    --source-resource-id $storageId `
    --name $SubscriptionName `
    -o json 2>$null
if ($LASTEXITCODE -eq 0 -and $existing) {
    Write-Host "Removing existing subscription ..." -ForegroundColor Yellow
    az eventgrid event-subscription delete `
        --source-resource-id $storageId `
        --name $SubscriptionName `
        2>$null
}

az eventgrid event-subscription create `
    --name $SubscriptionName `
    --source-resource-id $storageId `
    --endpoint $endpoint `
    --endpoint-type webhook `
    --included-event-types Microsoft.Storage.BlobCreated `
    --subject-begins-with $subjectBegins `
    --subject-ends-with ".pdf" `
    --max-delivery-attempts 10 `
    --event-delivery-schema EventGridSchema `
    -o table

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "CLI create failed. Create the subscription in Portal instead:" -ForegroundColor Yellow
    Write-Host "  1. Portal -> storage account livingmessiahstorage -> Events -> + Event Subscription"
    Write-Host "  2. Event Types: Blob Created"
    Write-Host "  3. Endpoint Type: Web Hook"
    Write-Host "  4. Endpoint: (copy from Function App -> App keys -> system key eventgrid_extension)"
    Write-Host "     $endpoint" -ForegroundColor DarkGray
    Write-Host "  5. Filters: Subject begins with $subjectBegins , ends with .pdf"
    throw "Event Grid subscription create failed."
}

Write-Host "Done. New PDF uploads to $ContainerName should trigger $FunctionName." -ForegroundColor Green
Write-Host "Tip: re-upload or overwrite a test PDF to fire BlobCreated." -ForegroundColor Yellow
