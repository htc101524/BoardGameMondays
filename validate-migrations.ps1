# Migration Validation Script (PowerShell)
# Run this before deploying to catch migration issues early
# Usage: .\validate-migrations.ps1

param(
    [switch]$Verbose = $false
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Migration Validation Script" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

# Get the repository root
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location "$RepoRoot\BoardGameMondays"

# 1. Check dotnet is installed
Write-Host "✓ Checking if dotnet is installed..." -ForegroundColor Green
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Host "✗ dotnet CLI not found. Please install .NET SDK." -ForegroundColor Red
    exit 1
}

# 2. Build project
Write-Host "✓ Building project..." -ForegroundColor Green
$verbosity = if ($Verbose) { "normal" } else { "quiet" }
$buildResult = & dotnet build --configuration Release --verbosity $verbosity
if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ Build failed!" -ForegroundColor Red
    exit 1
}

# 3. List migrations
Write-Host "✓ Getting list of migrations..." -ForegroundColor Green
$migrations = & dotnet ef migrations list --no-build 2>$null
$migrationCount = ($migrations | Measure-Object -Line).Lines
Write-Host "  Found $migrationCount migrations" -ForegroundColor Gray

Write-Host ""

# 4. Check for pending migrations
Write-Host "✓ Checking for pending migrations..." -ForegroundColor Green
$dryRunOutput = & dotnet ef migrations list --no-build 2>&1
$hasPending = $dryRunOutput -like "*pending*"

if ($hasPending) {
    Write-Host "✗ PENDING MODEL CHANGES DETECTED!" -ForegroundColor Red
    Write-Host "  Your entity models have changes that aren't in migrations." -ForegroundColor Red
    Write-Host "  Run: dotnet ef migrations add [MigrationName]" -ForegroundColor Yellow
    Write-Host "  Then run: .\validate-migrations.ps1" -ForegroundColor Yellow
    exit 1
} else {
    Write-Host "  No pending changes detected ✓" -ForegroundColor Gray
}

Write-Host ""

# 5. Run migration tests
Write-Host "✓ Running migration tests..." -ForegroundColor Green
$testVerbosity = if ($Verbose) { "normal" } else { "quiet" }
$testResult = & dotnet test "..\BoardGameMondays.Tests\BoardGameMondays.Tests.csproj" `
    --filter "FullyQualifiedName~MigrationTests" `
    --configuration Release `
    --verbosity $testVerbosity `
    --no-build 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ Migration tests failed!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Test output:" -ForegroundColor Red
    Write-Host $testResult -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "✓ All migration validations passed!" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Safe to deploy!" -ForegroundColor Green
