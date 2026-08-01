$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root

try {
    dotnet build .\VnTool.sln
    dotnet test .\VnTool.sln --no-build

    Write-Host ""
    Write-Host "=== Valid sample (exit code 0 expected) ==="
    dotnet run --project .\src\Vn.Cli\Vn.Cli.csproj --no-build -- `
        .\samples\Valid\Demo.yarnproject `
        .\samples\Valid\game.schema.json `
        --format text

    if ($LASTEXITCODE -ne 0) {
        throw "Valid sample returned exit code $LASTEXITCODE."
    }

    Write-Host ""
    Write-Host "=== Invalid sample (exit code 1 expected) ==="
    dotnet run --project .\src\Vn.Cli\Vn.Cli.csproj --no-build -- `
        .\samples\Invalid\Demo.yarnproject `
        .\samples\Invalid\game.schema.json `
        --format text

    if ($LASTEXITCODE -ne 1) {
        throw "Invalid sample returned exit code $LASTEXITCODE; expected 1."
    }

    Write-Host ""
    Write-Host "=== Real sample golden fixture ==="
    $actual = Join-Path $env:TEMP "vntool-real-$([Guid]::NewGuid().ToString('N')).txt"

    try {
        $actualLines = dotnet run --project .\src\Vn.Cli\Vn.Cli.csproj --no-build -- `
            .\samples\Real\Demo.yarnproject `
            .\samples\Real\game.schema.json `
            --format list
        $realExitCode = $LASTEXITCODE

        if ($realExitCode -ne 1) {
            throw "Real sample returned exit code $realExitCode; current fixture expects diagnostics and exit code 1."
        }

        # Windows PowerShell 5.1에는 utf8NoBOM 인코딩 이름이 없으므로 .NET API로 기록한다.
        [System.IO.File]::WriteAllText(
            $actual,
            ($actualLines -join [Environment]::NewLine),
            (New-Object System.Text.UTF8Encoding($false)))

        $expectedText = (Get-Content .\samples\Real\expected.txt -Raw).Replace("`r`n", "`n").TrimEnd()
        $actualText = (Get-Content $actual -Raw).Replace("`r`n", "`n").TrimEnd()

        if ($expectedText -cne $actualText) {
            throw "Real sample output differs from samples/Real/expected.txt."
        }
    }
    finally {
        Remove-Item $actual -ErrorAction SilentlyContinue
    }

    Write-Host ""
    Write-Host "All VnTool checks passed."
}
finally {
    Pop-Location
}
