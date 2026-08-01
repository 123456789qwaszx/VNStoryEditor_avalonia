# 이 파일은 UTF-8 BOM으로 저장해야 한다. BOM을 떼지 말 것.
# Windows PowerShell 5.1은 BOM 없는 .ps1을 시스템 ANSI(한국어 Windows에서는 949)로 읽는다.
# 그러면 아래의 한국어 메시지가 스크립트를 파싱하는 단계에서 이미 깨진다.
# 골든 비교가 실패했을 때 원인을 알려 줘야 할 문장이 먼저 못 읽게 되는 셈이다.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root

# 진단 메시지와 골든 픽스처에 한글이 들어 있다.
# Windows PowerShell 5.1은 콘솔 코드 페이지(한국어 Windows에서는 949)로 네이티브 명령의
# 출력을 해석하므로, 이것을 UTF-8로 고정하지 않으면 화면에 보이는 글자부터 깨진다.
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

<#
.SYNOPSIS
    Vn.Cli를 실행하고 표준 출력을 UTF-8로 확실하게 읽는다.

.DESCRIPTION
    출력을 PowerShell 파이프라인으로 받으면 콘솔 코드 페이지가 해석에 끼어들고,
    임시 파일에 적었다가 Get-Content로 다시 읽으면 BOM 없는 UTF-8이 ANSI로 읽힌다.
    둘 다 실제 분석 결과와 무관하게 비교를 깨뜨린다.
    그래서 자식 프로세스의 스트림 인코딩을 직접 지정해 한 번에 읽는다.
    임시 파일을 만들지 않으므로 성공·실패 어느 쪽에서도 남는 것이 없다.
#>
function Invoke-VnCli {
    param([Parameter(Mandatory = $true)][string[]] $Arguments)

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = "dotnet"
    $startInfo.Arguments = ($Arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $startInfo.WorkingDirectory = $root
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.StandardOutputEncoding = $utf8NoBom

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo

    try {
        [void] $process.Start()

        # 표준 오류는 넘겨받은 콘솔로 그대로 흘려보낸다.
        # 하나만 리다이렉트하므로 버퍼가 차서 서로 기다리는 일이 없다.
        $stdout = $process.StandardOutput.ReadToEnd()
        $process.WaitForExit()

        return [pscustomobject]@{
            Output   = $stdout
            ExitCode = $process.ExitCode
        }
    }
    finally {
        $process.Dispose()
    }
}

<#
.SYNOPSIS
    골든 파일을 UTF-8로 읽는다. BOM이 있으면 떼고, 없어도 ANSI로 넘어가지 않는다.
#>
function Read-GoldenText {
    param([Parameter(Mandatory = $true)][string] $Path)

    return [System.IO.File]::ReadAllText($Path, $utf8NoBom)
}

<#
.SYNOPSIS
    줄바꿈 정책. CRLF와 LF, 그리고 파일 끝 줄바꿈의 유무는 의미 차이로 보지 않는다.
#>
function ConvertTo-ComparableText {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string] $Text)

    return $Text.Replace("`r`n", "`n").Replace("`r", "`n").TrimEnd("`n")
}

<#
.SYNOPSIS
    두 텍스트의 첫 번째 차이를 사람이 확인할 수 있게 설명한다. 같으면 $null.

.DESCRIPTION
    "어딘가 다르다"만 알려주는 비교는 원인을 찾을 수 없게 만든다.
    몇 번째 줄인지, 그 줄이 파일의 몇 바이트째에서 시작하는지, 양쪽 내용이 무엇인지 모두 낸다.
    탭은 눈에 보이는 기호로 바꿔 공백 차이도 구분할 수 있게 한다.
#>
function Get-FirstDifference {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string] $Expected,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string] $Actual
    )

    $expectedLines = @((ConvertTo-ComparableText $Expected) -split "`n")
    $actualLines = @((ConvertTo-ComparableText $Actual) -split "`n")
    $shared = [Math]::Min($expectedLines.Count, $actualLines.Count)

    $index = 0
    while ($index -lt $shared -and $expectedLines[$index] -ceq $actualLines[$index]) {
        $index++
    }

    if ($index -eq $shared -and $expectedLines.Count -eq $actualLines.Count) {
        return $null
    }

    # 문제의 줄이 시작하는 바이트 위치. 앞선 줄들과 각 줄바꿈 한 바이트를 더한다.
    $byteOffset = 0
    for ($i = 0; $i -lt $index; $i++) {
        $byteOffset += [System.Text.Encoding]::UTF8.GetByteCount($expectedLines[$i]) + 1
    }

    $show = {
        param($lines, $at)
        if ($at -lt $lines.Count) { $lines[$at].Replace("`t", "»") } else { "<줄 없음>" }
    }

    return @(
        "expected {0}줄, actual {1}줄." -f $expectedLines.Count, $actualLines.Count
        "처음 다른 곳: {0}번째 줄 (expected 기준 {1}바이트째)" -f ($index + 1), $byteOffset
        "  expected: {0}" -f (& $show $expectedLines $index)
        "  actual  : {0}" -f (& $show $actualLines $index)
    ) -join [Environment]::NewLine
}

try {
    dotnet build .\VnTool.sln
    if ($LASTEXITCODE -ne 0) { throw "빌드에 실패했습니다." }

    dotnet test .\VnTool.sln --no-build
    if ($LASTEXITCODE -ne 0) { throw "테스트에 실패했습니다." }

    Write-Host ""
    Write-Host "=== Valid sample (exit code 0 expected) ==="
    $valid = Invoke-VnCli @(
        "run", "--project", ".\src\Vn.Cli\Vn.Cli.csproj", "--no-build", "--",
        ".\samples\Valid\Demo.yarnproject",
        ".\samples\Valid\game.schema.json",
        "--format", "text")
    Write-Host $valid.Output

    if ($valid.ExitCode -ne 0) {
        throw "Valid sample returned exit code $($valid.ExitCode)."
    }

    Write-Host ""
    Write-Host "=== Invalid sample (exit code 1 expected) ==="
    $invalid = Invoke-VnCli @(
        "run", "--project", ".\src\Vn.Cli\Vn.Cli.csproj", "--no-build", "--",
        ".\samples\Invalid\Demo.yarnproject",
        ".\samples\Invalid\game.schema.json",
        "--format", "text")
    Write-Host $invalid.Output

    if ($invalid.ExitCode -ne 1) {
        throw "Invalid sample returned exit code $($invalid.ExitCode); expected 1."
    }

    Write-Host ""
    Write-Host "=== Real sample golden fixture ==="
    $real = Invoke-VnCli @(
        "run", "--project", ".\src\Vn.Cli\Vn.Cli.csproj", "--no-build", "--",
        ".\samples\Real\Demo.yarnproject",
        ".\samples\Real\game.schema.json",
        "--format", "list")

    if ($real.ExitCode -ne 1) {
        throw "Real sample returned exit code $($real.ExitCode); current fixture expects diagnostics and exit code 1."
    }

    $expectedText = Read-GoldenText (Join-Path $root "samples\Real\expected.txt")
    $difference = Get-FirstDifference -Expected $expectedText -Actual $real.Output

    if ($null -ne $difference) {
        throw @(
            "Real sample output differs from samples/Real/expected.txt."
            $difference
            "분석 결과가 실제로 바뀐 것이라면 원인을 확인한 뒤에 expected.txt를 갱신하세요."
        ) -join [Environment]::NewLine
    }

    Write-Host "samples/Real/expected.txt와 일치합니다."
    Write-Host ""
    Write-Host "All VnTool checks passed."
}
finally {
    Pop-Location
}
