# Publish LMM-Parse-PDF to GitHub under JohnMarsing
# Run from the repo root:  powershell -File .\scripts\publish-to-github.ps1

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot\..

Write-Host "=== 1. GitHub CLI login (browser) ===" -ForegroundColor Cyan
gh auth status 2>$null
if ($LASTEXITCODE -ne 0) {
    gh auth login -h github.com -p https -w
}

Write-Host "=== 2. Create public repo and push main ===" -ForegroundColor Cyan
# --source=. links current folder; --push uploads main
gh repo create LMM-Parse-PDF `
    --public `
    --description "Parse Living Messiah Shabbat agenda PDFs to Markdown (Azure Blob)" `
    --source=. `
    --remote=origin `
    --push

Write-Host "=== Done ===" -ForegroundColor Green
gh repo view --web
