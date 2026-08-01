$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root

try {
    $publishDirectory = Join-Path $root "artifacts\VnTool-win-x64"
    $zipPath = Join-Path $root "artifacts\VnTool-win-x64.zip"

    Remove-Item $publishDirectory -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue

    dotnet publish .\src\Vn.App\Vn.App.csproj `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -o $publishDirectory

    if ($LASTEXITCODE -ne 0) {
        throw "VnTool publish failed with exit code $LASTEXITCODE."
    }

    Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $zipPath
    Write-Host "Portable Windows package: $zipPath"
}
finally {
    Pop-Location
}
