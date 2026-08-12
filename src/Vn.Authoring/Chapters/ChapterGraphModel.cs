namespace Vn.Authoring.Chapters;

/// <summary>
/// `에피소드` 시트 한 행. 위치는 엑셀이 소유하고 뷰는 읽기만 한다 (G-2) —
/// 그래서 <see cref="X"/>·<see cref="Y"/>에 setter가 없다.
/// </summary>
/// <param name="Index">"05"·"05A"·"★"처럼 사람이 붙이는 표시용 순번. 숫자가 아니어도 된다.</param>
/// <param name="DialogueEntry">런타임이 재생할 대사 엔트리 이름. 비면 오류다.</param>
/// <param name="SourceRow">엑셀 행 번호. 진단이 사람에게 자리를 짚어 주는 근거다.</param>
/// <param name="AllowUnreachable">
/// `도달불가 허용` 열(선택)이 켜져 있는가 (D3). 도달성 증명이 이 에피소드의 도달 불가를
/// 오류가 아니라 알림으로 낮추되, 그 사실은 그래프에 표시된다 — 조용히 넘기지 않는다.
/// </param>
public sealed record ChapterEpisode(
    string EpisodeId,
    string Title,
    string Index,
    string Kind,
    string DialogueEntry,
    double X,
    double Y,
    string? VisibleConditionLabel,
    string? UnlockConditionLabel,
    string? EndingKey,
    string? Memo,
    int SourceRow,
    bool AllowUnreachable = false)
{
    public bool IsEnding => !string.IsNullOrWhiteSpace(EndingKey);

    /// <summary>표시·해금 중 하나라도 조건이 걸려 있으면 잠금 후보다.</summary>
    public bool HasGate =>
        !string.IsNullOrWhiteSpace(VisibleConditionLabel) ||
        !string.IsNullOrWhiteSpace(UnlockConditionLabel);
}

/// <summary>
/// `간선` 시트 한 행 = 옵션 하나. 도착 에피소드의 원천은 <b>여기</b>이고
/// 에피소드 워크북의 `OPTION` 행은 라벨만 소유한다 (필드별 단일 소유권).
/// </summary>
public sealed record ChapterEdge(
    string FromEpisodeId,
    string ToEpisodeId,
    string? OptionLabel,
    string? ConditionLabel,
    bool HideWhenLocked,
    string? LockedMessage,
    int SourceRow)
{
    /// <summary>선택지 라벨이 비면 분기 없는 일반 진행이다 (§3.1).</summary>
    public bool IsPlainAdvance => string.IsNullOrWhiteSpace(OptionLabel);
}

/// <summary>
/// `조건` 시트 한 행. 라벨↔식 표이며 <c>ConditionDefinition{Name, Expression}</c>에 대응한다 (G-7).
/// <see cref="Parsed"/>는 툴이 검증에 쓰는 해석 결과일 뿐, <see cref="Expression"/> 원문이 정본이다 —
/// 게임이 평가하는 것은 원문이고 툴은 그 의미를 소유하지 않는다(무해석성, §0.5).
/// </summary>
public sealed record ChapterCondition(
    string Label,
    string Expression,
    string? Description,
    IReadOnlyList<ConditionTerm> Parsed,
    bool IsValid,
    int SourceRow);

/// <summary>
/// `스탯` 시트 한 행 = Tier 2 키 하나. <see cref="Minimum"/>·<see cref="Maximum"/>는 장식이 아니라
/// G7 도달성 증명의 <b>탐색 경계</b>다 (§7-5).
/// </summary>
public sealed record ChapterStat(
    string Key,
    string DisplayName,
    int Initial,
    int Minimum,
    int Maximum,
    int SourceRow);

/// <param name="From">고정 선택의 출발 에피소드.</param>
/// <param name="To">그 선택이 향하는 도착 에피소드.</param>
public sealed record ChapterFixtureChoice(string From, string To);

/// <summary>
/// `픽스처` 시트 한 행. <b>내보내기에 섞이지 않는다</b> (§3.1) — 재생루트를 눈으로 보기 위한
/// 테스트 데이터다. G6가 이걸로 경로를 하이라이트한다.
/// </summary>
public sealed record ChapterFixture(
    string Name,
    bool IsActive,
    IReadOnlyDictionary<string, int> Stats,
    IReadOnlyList<ChapterFixtureChoice> Choices,
    int SourceRow);

/// <summary>
/// 챕터 워크북 하나를 읽은 결과. <b>오류가 있어도 모델은 만들어진다</b> — 읽힌 데까지 그려 놓고
/// 무엇이 잘못됐는지 옆에 세워야 기획자가 고칠 자리를 찾는다. 조용히 빈 화면을 주지 않는다(규칙 14).
/// 내보내기(G8)는 <see cref="HasErrors"/>를 보고 거부한다.
/// </summary>
public sealed class ChapterGraphModel
{
    public ChapterGraphModel(
        string chapterId,
        string sourcePath,
        IReadOnlyList<ChapterEpisode> episodes,
        IReadOnlyList<ChapterEdge> edges,
        IReadOnlyList<ChapterCondition> conditions,
        IReadOnlyList<ChapterStat> stats,
        IReadOnlyList<ChapterFixture> fixtures,
        IReadOnlyList<ChapterDiagnostic> diagnostics)
    {
        ChapterId = chapterId;
        SourcePath = sourcePath;
        Episodes = episodes;
        Edges = edges;
        Conditions = conditions;
        Stats = stats;
        Fixtures = fixtures;
        Diagnostics = diagnostics;
    }

    /// <summary>파일 이름에서 온다 — `chapters/{ChapterId}.xlsx` (§3.1).</summary>
    public string ChapterId { get; }

    public string SourcePath { get; }

    public IReadOnlyList<ChapterEpisode> Episodes { get; }

    public IReadOnlyList<ChapterEdge> Edges { get; }

    public IReadOnlyList<ChapterCondition> Conditions { get; }

    public IReadOnlyList<ChapterStat> Stats { get; }

    public IReadOnlyList<ChapterFixture> Fixtures { get; }

    public IReadOnlyList<ChapterDiagnostic> Diagnostics { get; }

    public bool HasErrors =>
        Diagnostics.Any(item => item.Severity == ChapterDiagnosticSeverity.Error);

    public IEnumerable<ChapterDiagnostic> Errors =>
        Diagnostics.Where(item => item.Severity == ChapterDiagnosticSeverity.Error);

    /// <summary>
    /// 시작 에피소드 = `에피소드` 시트의 첫 행. 규격에 시작 열이 없어 <b>읽는 순서가 곧 정의</b>다.
    /// 도달성 증명(G7)과 내보내기(G8)의 `StartEpisodeId`가 같은 이 규칙 하나를 쓴다 —
    /// 런타임은 이 값이 비면 도달 제한을 아예 걸지 않으므로, 두 곳이 갈리면 안 된다.
    /// </summary>
    public ChapterEpisode? StartEpisode => Episodes.Count > 0 ? Episodes[0] : null;

    public ChapterEpisode? FindEpisode(string episodeId) =>
        Episodes.FirstOrDefault(item => string.Equals(item.EpisodeId, episodeId, StringComparison.Ordinal));

    public ChapterCondition? FindCondition(string label) =>
        Conditions.FirstOrDefault(item => string.Equals(item.Label, label, StringComparison.Ordinal));

    /// <summary>그 에피소드에 오류 진단이 붙어 있는가. 뷰가 노드에 표식을 다는 근거다.</summary>
    public bool EpisodeHasError(ChapterEpisode episode) =>
        Diagnostics.Any(item =>
            item.Severity == ChapterDiagnosticSeverity.Error &&
            item.Row == episode.SourceRow &&
            string.Equals(item.Sheet, ChapterSheetNames.Episodes, StringComparison.Ordinal));
}

/// <summary>시트 이름 한 곳. 리더와 뷰·테스트가 같은 문자열을 본다.</summary>
public static class ChapterSheetNames
{
    public const string Episodes = "에피소드";
    public const string Edges = "간선";
    public const string Conditions = "조건";
    public const string Stats = "스탯";
    public const string Fixtures = "픽스처";

    public static IReadOnlyList<string> All { get; } =
        [Episodes, Edges, Conditions, Stats, Fixtures];
}
