$ErrorActionPreference = "Stop"

dotnet restore
dotnet build

Write-Host ""
Write-Host "=== Valid sample ===" -ForegroundColor Cyan
dotnet run --project src/Vn.Cli -- `
    samples/Valid/Demo.yarnproject `
    samples/Valid/game.schema.json

Write-Host ""
Write-Host "=== Invalid sample (exit code 1 is expected) ===" -ForegroundColor Cyan
dotnet run --project src/Vn.Cli -- `
    samples/Invalid/Demo.yarnproject `
    samples/Invalid/game.schema.json

if ($LASTEXITCODE -ne 1) {
    throw "Invalid sample should return exit code 1, but returned $LASTEXITCODE."
}

Write-Host ""
Write-Host "Sample verification completed." -ForegroundColor Green
