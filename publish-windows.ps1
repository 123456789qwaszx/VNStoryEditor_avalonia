$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root

# 비개발자(시나리오 작가)에게 그대로 건네는 꾸러미를 만든다.
#
# 받는 사람에게 없는 것을 전제하지 않는다 — .NET 런타임도, 개발 도구도, 명령줄도.
# 그래서 self-contained · 단일 exe이고, 압축을 풀면 바로 실행된다.
#
# ⚠ 실행 파일 하나로 묶는 것은 csproj가 아니라 여기서 정한다. 개발 중 F5는 그 묶기를
#   치를 이유가 없고(느리다), 묶는 것은 배포의 성질이지 프로젝트의 성질이 아니다.

try {
    $publishDirectory = Join-Path $root "artifacts\VnTool-win-x64"
    $zipPath = Join-Path $root "artifacts\VnTool-win-x64.zip"

    Remove-Item $publishDirectory -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue

    dotnet publish .\src\Vn.App\Vn.App.csproj `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -o $publishDirectory

    if ($LASTEXITCODE -ne 0) {
        throw "VnTool publish failed with exit code $LASTEXITCODE."
    }

    # ⛔ 지금은 <b>실행 파일만</b> 담는다 (2026-08-26 소유자: "읽어주세요와 견본 project는
    #    일단은 없는 상태로. exe만 포함되도록").
    #
    #    걷은 것 둘 — `docs\작가에게.txt` → `먼저 읽어주세요.txt`, `dist\예제 프로젝트`.
    #    "일단은"이므로 <b>원본은 그대로 둔다</b>: 되살리려면 이 자리에 복사 두 줄을 다시
    #    세우면 된다. 규격이 며칠 사이 v14까지 굴렀고 그 둘이 옛 규격을 말하고 있어서,
    #    낡은 안내와 낡은 견본을 함께 건네느니 안 건네는 편이 낫다는 판단이다.

    Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $zipPath

    $exe = Join-Path $publishDirectory "Vn.App.exe"
    $sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)

    Write-Host ""
    Write-Host "꾸러미: $zipPath"
    Write-Host "실행 파일: $exe ($sizeMb MB)"
    Write-Host "담긴 것: 실행 파일 하나 (안내문·예제 프로젝트는 이번 꾸러미에 없습니다)"
    Write-Host "받는 사람은 압축을 풀고 Vn.App.exe를 누르면 됩니다."
}
finally {
    Pop-Location
}
