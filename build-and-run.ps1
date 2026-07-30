<#
.SYNOPSIS
    빌드하고 samples/ 아래 샘플을 모두 회귀 검증한다.

.DESCRIPTION
    샘플마다 CLI를 두 번 돌린다.

      --format text  사람이 읽으라고 콘솔에 찍는다. 비교하지 않는다.
      --format list  expected.txt와 그대로 비교한다.

    골든 픽스처를 사람이 읽는 문장에 걸면 메시지 문구를 다듬을 때마다 픽스처가 깨진다.
    그런데 이 도구에서 문구는 자주 다듬어야 하는 것이다 — 작가가 읽을 문장이니까.
    픽스처가 문구 수정에 저항하면 결국 문구를 안 고치게 된다.
    그래서 회귀의 본체는 문구가 빠진 list 형식에 두고, 문구 품질은 사람이 text 출력을
    눈으로 보고 판단한다.

    list 형식은 CLI가 이미 프로젝트 폴더 기준 상대 경로와 / 구분자로 내보내므로
    이 스크립트가 따로 정규화할 것이 없다. 픽스처는 머신·OS에 상관없이 그대로 통과한다.

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

function Invoke-Sample {
    param(
        [string]$Sample,
        [string[]]$ExtraArgs = @()
    )

    $arguments = @(
        "run"
        "--project"
        "src/Vn.Cli"
        "--no-build"
        "--"
        "samples/$Sample/Demo.yarnproject"
        "samples/$Sample/game.schema.json"
    ) + $ExtraArgs

    $output = & dotnet @arguments

    return [pscustomobject]@{
        Lines    = @($output)
        ExitCode = $LASTEXITCODE
    }
}

# 탭으로 구분된 줄은 그대로 보면 읽기 어렵다. diff를 보여줄 때만 눈에 띄게 바꾼다.
function Format-ForDisplay {
    param([string]$Line)
    return $Line.Replace("`t", " | ")
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

        # 사람이 읽을 출력. 메시지 문구는 여기서 눈으로 확인한다.
        $text = Invoke-Sample -Sample $sample
        $text.Lines | ForEach-Object { Write-Host $_ }

        if ($text.ExitCode -ne $expectedExitCode) {
            $failures += "${sample}: 종료 코드가 $expectedExitCode 이어야 하는데 $($text.ExitCode) 입니다."
        }

        # 회귀 비교용 출력.
        $list = Invoke-Sample -Sample $sample -ExtraArgs @("--format", "list")

        # 형식이 달라졌다고 종료 코드가 달라지면 안 된다.
        if ($list.ExitCode -ne $text.ExitCode) {
            $failures += "${sample}: --format list의 종료 코드($($list.ExitCode))가 text($($text.ExitCode))와 다릅니다."
        }

        $expectedPath = Join-Path $repoRoot "samples/$sample/expected.txt"
        $actual = ($list.Lines -join "`n").TrimEnd()

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
                    Write-Host "$marker $(Format-ForDisplay $_.InputObject)"
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
