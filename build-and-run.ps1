<#
.SYNOPSIS
    빌드하고 samples/ 아래 샘플을 모두 회귀 검증한다.

.DESCRIPTION
    각 샘플마다 종료 코드와 전체 출력을 확인한다.
    출력은 samples/<이름>/expected.txt에 고정해 두고 그대로 비교한다.
    테스트 프로젝트가 없어도 이 골든 픽스처가 "진단이 조용히 바뀌는 일"을 막아준다.
    나중에 Unity 쪽과 진단 일치를 검증할 때도 이 파일이 기준이 된다.

.PARAMETER Update
    기대 출력을 현재 출력으로 덮어쓴다.
    진단을 의도적으로 바꾼 뒤에만 쓰고, 그 diff는 반드시 눈으로 확인할 것.

.EXAMPLE
    ./build-and-run.ps1
    ./build-and-run.ps1 -Update
#>
[CmdletBinding()]
param(
    [switch]$Update
)

$ErrorActionPreference = "Stop"

# 자식 프로세스가 UTF-8로 쓴 한국어 진단을 그대로 받아오기 위해 필요하다.
$previousEncoding = [Console]::OutputEncoding
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = $PSScriptRoot
$samples = @("Valid", "Invalid", "Malformed")
$expectedExitCodes = @{ Valid = 0; Invalid = 1; Malformed = 1 }

function Get-NormalizedOutput {
    param([string[]]$Lines)

    # 절대 경로와 경로 구분자를 지워야 다른 머신에서도 같은 픽스처가 통한다.
    $text = ($Lines -join "`n")
    $text = $text.Replace($repoRoot, "<root>")
    $text = $text.Replace("\", "/")
    return $text.TrimEnd()
}

try {
    dotnet restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

    dotnet build --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

    $failures = @()

    foreach ($sample in $samples) {
        $expectedExitCode = $expectedExitCodes[$sample]

        Write-Host ""
        Write-Host "=== $sample sample (exit code $expectedExitCode is expected) ===" -ForegroundColor Cyan

        $output = dotnet run --project src/Vn.Cli --no-build -- `
            "samples/$sample/Demo.yarnproject" `
            "samples/$sample/game.schema.json"

        $actualExitCode = $LASTEXITCODE

        $output | ForEach-Object { Write-Host $_ }

        if ($actualExitCode -ne $expectedExitCode) {
            $failures += "${sample}: 종료 코드가 $expectedExitCode 이어야 하는데 $actualExitCode 입니다."
        }

        $expectedPath = Join-Path $repoRoot "samples/$sample/expected.txt"
        $actual = Get-NormalizedOutput -Lines $output

        if ($Update) {
            Set-Content -Path $expectedPath -Value $actual -Encoding utf8 -NoNewline
            Write-Host "기대 출력을 갱신했습니다: samples/$sample/expected.txt" -ForegroundColor Yellow
            continue
        }

        if (-not (Test-Path $expectedPath)) {
            $failures += "${sample}: 기대 출력 파일이 없습니다. -Update 로 만드세요."
            continue
        }

        $expected = (Get-Content -Path $expectedPath -Raw -Encoding UTF8).Replace("`r`n", "`n").TrimEnd()

        if ($actual -ne $expected) {
            $failures += "${sample}: 출력이 기대와 다릅니다."

            Write-Host ""
            Write-Host "--- $sample diff (기대 vs 실제) ---" -ForegroundColor Yellow

            Compare-Object `
                -ReferenceObject ($expected -split "`n") `
                -DifferenceObject ($actual -split "`n") |
                ForEach-Object {
                    $marker = if ($_.SideIndicator -eq "<=") { "기대만:" } else { "실제만:" }
                    Write-Host "$marker $($_.InputObject)"
                }
        }
    }

    Write-Host ""

    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        throw "샘플 검증에 실패했습니다. ($($failures.Count)건)"
    }

    Write-Host "Sample verification completed." -ForegroundColor Green

    # 마지막 dotnet run이 남긴 $LASTEXITCODE가 그대로 새어나가지 않게 한다.
    # Invalid 샘플은 성공해도 1을 반환하므로, 명시하지 않으면 CI가 실패로 읽는다.
    exit 0
}
finally {
    [Console]::OutputEncoding = $previousEncoding
}
