using Vn.Core;
using Vn.Core.Analysis;
using Vn.Core.Diagnostics;

namespace Vn.App.Services;

/// <summary>
/// 이미터가 방금 쓴 <c>.yarn</c> 산출물이 <b>실제로 컴파일되는가</b>.
///
/// <b>왜 있는가</b> — 이 검사는 2026-08-23까지 <b>테스트에만</b> 꽂혀 있었다.
/// 프로덕션 경로에서 Yarn 컴파일러를 부르는 자리가 0건이라, 툴이 컴파일 안 되는 대본을
/// 써도 유니티까지 아무도 몰랐다. 같은 날의 이름 갈림과 정확히 같은 모양이다 —
/// <b>감지기는 있는데 정작 중요한 경로에 없다.</b>
///
/// <b>무엇을 보나</b> — 문법과 <b>전역 라인 ID 유일성</b>(계약서 C4)까지다.
///
/// <b>어휘(미등록 커맨드)는 일부러 보지 않는다</b> (2026-08-23 실측으로 결정). 저작이
/// 이미 <b>입력 시점에</b> 막는다 — <see cref="Vn.Authoring.Definition.CommandText"/>가
/// *"카탈로그에 없는 커맨드입니다"*로 거부하고, 편집기는 팔레트에서만 고르게 한다.
/// 여기서 한 번 더 물으면 같은 사실에 판정 기준이 둘이 되고, 팔레트에 템플릿 항목
/// (<c>&lt;N&gt;fr</c>)이 있어 순진한 집합 검사는 <c>&lt;&lt;12fr&gt;&gt;</c>를 오탐한다.
///
/// ⚠ <c>game.schema.json</c>은 이 팔레트의 사본이 <b>아니다</b> — <see cref="Vn.Cli"/>가
/// <b>외부</b> yarn 프로젝트를 분석할 때 받는 입력 형식이고, 저작 프로젝트는 그런 파일을
/// 만들지도 읽지도 않는다(`samples/`에만 있다).
///
/// <b>규율</b> — 이 검사는 산출물을 <b>바꾸지 않는다.</b> 검증하려고 산출 폴더에
/// <c>.yarnproject</c>를 만들어 두면 유니티가 읽는 폴더가 달라지고 고아 스캔에도 걸린다.
/// 그래서 <see cref="VnProjectAnalyzer.AnalyzeFiles"/>(파일 목록을 바로 받는 입구)를
/// 쓴다 — <b>검증 때문에 산출물이 달라지면 그것은 검증이 아니다.</b>
///
/// <b>막지 않는다</b> — 실패해도 쓰기를 되돌리지 않고 사유만 알린다. 이미 디스크에 있는
/// 것을 지우면 "고치는 중"이 곧 "산출물 없음"이 되어 저작을 막는다. 막을지 말지는 이
/// 검사가 실물에서 무엇을 잡는지 본 뒤에 정한다.
/// </summary>
internal static class YarnOutputVerification
{
    /// <summary>이 검사가 보는 파일 확장자. 곡선·매니페스트 같은 동반 파일은 대본이 아니다.</summary>
    private const string YarnExtension = ".yarn";

    /// <summary>상태줄에 이름을 적는 최대 개수. 나머지는 수로 접는다.</summary>
    private const int Shown = 2;

    public static YarnOutputVerdict Verify(IEnumerable<string>? writtenPaths)
    {
        string[] sources = (writtenPaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => path.EndsWith(YarnExtension, StringComparison.OrdinalIgnoreCase))
            .Where(File.Exists)
            .ToArray();

        if (sources.Length == 0)
        {
            return YarnOutputVerdict.Skipped;
        }

        try
        {
            // 스키마 없음 = 어휘 검사 없음. 위 <summary>의 이유다.
            AnalysisReport report = new VnProjectAnalyzer().AnalyzeFiles(
                sources,
                schemaPath: null,
                originLabel: Path.GetDirectoryName(sources[0]));

            string[] errors = report.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(Describe)
                .ToArray();

            return new YarnOutputVerdict(sources.Length, errors);
        }
        catch (Exception exception)
        {
            // 검증기가 저작을 죽이지 않는다 — 못 본 것을 못 봤다고 말하고 넘어간다(규율 1).
            return new YarnOutputVerdict(
                sources.Length,
                [$"산출물 검증 자체가 실패했습니다: [{exception.GetType().Name}] {exception.Message}"]);
        }
    }

    /// <summary>진단 하나를 사람이 읽을 한 조각으로. 위치를 모르면 파일 이름만 적는다.</summary>
    private static string Describe(VnDiagnostic diagnostic)
    {
        string file = string.IsNullOrEmpty(diagnostic.FilePath)
            ? string.Empty
            : Path.GetFileName(diagnostic.FilePath);

        string where = (file.Length, diagnostic.Line) switch
        {
            (0, _) => string.Empty,
            (_, <= 0) => $"{file}: ",
            _ => $"{file}:{diagnostic.Line}: "
        };

        return $"{where}{diagnostic.Message}";
    }

    /// <summary>
    /// 상태줄 한 줄. 통과했으면 <c>null</c>이다 — <b>잘된 일은 말하지 않는다</b>
    /// (고아 보고와 같은 규칙: 상태줄은 사람이 할 일이 있을 때만 쓴다).
    /// </summary>
    internal static string? ReportOf(YarnOutputVerdict verdict)
    {
        if (verdict is null || !verdict.HasErrors)
        {
            return null;
        }

        string listed = string.Join(" / ", verdict.Errors.Take(Shown));

        if (verdict.Errors.Count > Shown)
        {
            listed += $" 외 {verdict.Errors.Count - Shown}개";
        }

        return $"⚠ 내보낸 대본이 컴파일되지 않습니다 — {listed}";
    }
}

/// <summary>
/// 산출물 검증 한 벌의 결과. <see cref="Errors"/>가 비면 통과다.
/// </summary>
/// <param name="FileCount">실제로 컴파일에 건 <c>.yarn</c> 파일 수. 0이면 검사를 건너뛴 것이다.</param>
internal sealed record YarnOutputVerdict(int FileCount, IReadOnlyList<string> Errors)
{
    /// <summary>볼 대본이 없었다 — 실패가 아니다.</summary>
    public static YarnOutputVerdict Skipped { get; } = new(0, []);

    public bool HasErrors => Errors.Count > 0;
}
