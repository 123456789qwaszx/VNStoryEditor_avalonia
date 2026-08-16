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
    /// <summary>
    /// `선택지수` 열 (K, v7 — 2026-08-16 소유자) — 이 에피소드 끝의 Choice가 갖는 Option
    /// 칸 수. 기본 1. 동기화가 이 수만큼 `선택지` 시트에 칸을 만들어 주고, 간선은 칸과
    /// 1:1로 짝하며 잇는 순간 생긴다.
    /// </summary>
    public int ChoiceCount { get; init; } = 1;

    public bool IsEnding => !string.IsNullOrWhiteSpace(EndingKey);

    /// <summary>표시·해금 중 하나라도 조건이 걸려 있으면 잠금 후보다.</summary>
    public bool HasGate =>
        !string.IsNullOrWhiteSpace(VisibleConditionLabel) ||
        !string.IsNullOrWhiteSpace(UnlockConditionLabel);
}

/// <summary>
/// `간선` 시트 한 행 = 다음 에피소드로 가는 길 하나. <b>간선 하나에 선택지 칸 하나가
/// 1:1로 짝한다</b> (2026-08-16 v7) — `선택지` 열(D)이 짝 칸의 <b>인덱스</b>를 가리키고,
/// <b>신원은 (출발, 선택지 인덱스)</b>다. 같은 도착으로 문구 여럿도 가능하다(인덱스가 다르니
/// 다른 간선). 조건·잠금·스탯변화·도착은 전부 간선 소유.
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
    /// <summary>
    /// `선택지` 열 (D, v7) — 짝 칸의 인덱스. 비면 아직 칸과 짝하지 않은 구판·수기 행이다
    /// (검증이 짚는다).
    /// </summary>
    public string? ChoiceIndex { get; init; }

    // OptionLabel은 파생값이다 — 리더가 짝 칸(출발, 인덱스)의 대본 text를 채워 준다
    // (신원이 아니다). 그래프·내보내기가 문구를 쓸 때의 값.

    /// <summary>짝 칸의 text가 비면 참 — 보이지 않는 기본(자동 진행)이다.</summary>
    public bool IsPlainAdvance => string.IsNullOrWhiteSpace(OptionLabel);

    /// <summary>
    /// `스탯변화` 열 (2026-08-14) — 이 간선을 타는 순간 1회 커밋되는 증감. 스탯이 변하는
    /// 유일한 자리다: 에피소드 안에서는 변하지 않으므로 세이브/로드 복귀가 일관되고,
    /// 도달성 증명이 근사 없이 정확값으로 전이한다. 조건 판정은 커밋 <b>전</b> 값으로 한다
    /// (플레이어가 선택지를 보는 시점의 값).
    /// </summary>
    public IReadOnlyList<StatDelta> StatChanges { get; init; } = [];
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

/// <summary>스탯의 타입 (2026-08-16 소유자) — `타입` 열(선택, 비면 int)이 정한다.</summary>
public enum ChapterStatType
{
    /// <summary>정수. 조건은 &lt; &gt; == &gt;= &lt;= 비교, 스탯변화는 증감.</summary>
    Int,

    /// <summary>참/거짓. 값 공간은 0/1이고 조건은 <c>== true/false</c>뿐이다.</summary>
    Bool
}

/// <summary>
/// `스탯` 시트 한 행 = Tier 2 키 하나. <see cref="Minimum"/>·<see cref="Maximum"/>는 장식이 아니라
/// G7 도달성 증명의 <b>탐색 경계</b>다 (§7-5). bool 스탯은 리더가 경계를 0·1로 고정한다 —
/// 프루버·픽스처 워커는 타입을 몰라도 그대로 옳게 돈다.
/// </summary>
public sealed record ChapterStat(
    string Key,
    string DisplayName,
    int Initial,
    int Minimum,
    int Maximum,
    int SourceRow,
    ChapterStatType Type = ChapterStatType.Int);

/// <summary>
/// `선택지` 시트 한 행 = 옵션 칸 하나 (2026-08-16 소유자, v7) — <b>선택지의 정본이 대본에서
/// 챕터로 왔다.</b> 근원(출발) 에피소드가 Choice 하나를 갖고, 그 아래 Option 칸들이 선다.
/// 칸 수는 에피소드의 `선택지수`가 정한다(기본 1칸).
///
/// <b>인덱스가 칸의 신원이다</b> — 간선의 `선택지` 열이 이 인덱스를 가리켜 1:1로 짝한다.
/// 도착·조건·잠금·스탯변화는 전부 간선의 것이고, 이 행이 갖는 것은 <b>인덱스(순서)와
/// 자유롭게 고치는 대본 text뿐</b>이다 — text는 신원이 아니라서 언제 바꿔도 배선이 안 깨진다.
///
/// <b>text가 빈 칸 = 보이지 않는 기본.</b> 어떤 선택지도 고를 수 없을 때 빠지는 방어장치로,
/// 에피소드당 하나만 허용되고 조건 없는 간선과 짝이다. text를 적는 순간 보이는 선택지가 된다.
/// </summary>
public sealed record ChapterChoiceOption(
    string EpisodeId,
    string Index,
    string Text,
    string? Memo,
    int SourceRow)
{
    /// <summary>text가 비면 보이지 않는 기본이다 — 버튼이 없고, 조건 없는 간선의 자동 진행.</summary>
    public bool IsInvisibleDefault => string.IsNullOrWhiteSpace(Text);
}

/// <summary>
/// `화자` 시트 한 행 (2026-08-16 소유자 지시). 기획자가 챕터에서 화자를 등록하면
/// 에피소드 워크북의 화자 열(H)이 이 목록의 드롭다운을 받는다.
///
/// <b>초상화 매핑의 정본은 여전히 <c>game.definition.json</c>의 speakers다</b> — 이 시트는
/// 저작 시점의 이름 목록(드롭다운 재료·동기화 경고의 등록 근거)만 소유한다. 캐릭터키는
/// 참고용으로 함께 적을 수 있으나 툴이 정의 파일에 자동으로 쓰지는 않는다
/// (시트를 자동으로 믿고 쓰면 오타까지 게임 어휘가 된다 — 변수 [등록]과 같은 원칙).
/// </summary>
public sealed record ChapterSpeaker(
    string Name,
    string? CharacterId,
    string? Memo,
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
        IReadOnlyList<ChapterDiagnostic> diagnostics,
        IReadOnlyList<ChapterSpeaker>? speakers = null,
        bool hasSpeakerSheet = false,
        IReadOnlyList<ChapterChoiceOption>? choiceOptions = null)
    {
        ChapterId = chapterId;
        SourcePath = sourcePath;
        Episodes = episodes;
        Edges = edges;
        Conditions = conditions;
        Stats = stats;
        Fixtures = fixtures;
        Diagnostics = diagnostics;
        Speakers = speakers ?? [];
        HasSpeakerSheet = hasSpeakerSheet;
        ChoiceOptions = choiceOptions ?? [];
    }

    /// <summary>파일 이름에서 온다 — `chapters/{ChapterId}.xlsx` (§3.1).</summary>
    public string ChapterId { get; }

    public string SourcePath { get; }

    public IReadOnlyList<ChapterEpisode> Episodes { get; }

    public IReadOnlyList<ChapterEdge> Edges { get; }

    public IReadOnlyList<ChapterCondition> Conditions { get; }

    public IReadOnlyList<ChapterStat> Stats { get; }

    public IReadOnlyList<ChapterFixture> Fixtures { get; }

    /// <summary>`화자` 시트의 등록 화자들. 시트가 없으면(구판 워크북) 빈 목록이다.</summary>
    public IReadOnlyList<ChapterSpeaker> Speakers { get; }

    /// <summary>
    /// `화자` 시트의 존재 여부. 이 기능(2026-08-16) 전에 만든 워크북에는 시트가 없다 —
    /// 앱이 챕터를 선택할 때 이 값을 보고 한 번 만들어 준다(마이그레이션).
    /// </summary>
    public bool HasSpeakerSheet { get; }

    /// <summary>`선택지` 시트 — 간선이 짝할 선택지의 정본 (2026-08-16, 대본 CHOICE/OPTION 폐지).</summary>
    public IReadOnlyList<ChapterChoiceOption> ChoiceOptions { get; }

    /// <summary>그 에피소드 끝의 선택지 묶음 — 시트 순서 그대로다(가지 순서 = 읽는 순서).</summary>
    public IEnumerable<ChapterChoiceOption> ChoiceOptionsFor(string episodeId) =>
        ChoiceOptions.Where(option =>
            string.Equals(option.EpisodeId, episodeId, StringComparison.Ordinal));

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
    public const string Speakers = "화자";
    public const string Choices = "선택지";

    public static IReadOnlyList<string> All { get; } =
        [Episodes, Edges, Conditions, Stats, Fixtures, Speakers, Choices];
}
