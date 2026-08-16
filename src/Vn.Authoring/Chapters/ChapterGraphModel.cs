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
    string? EndingKey,
    string? Memo,
    int SourceRow,
    bool AllowUnreachable = false)
{
    // v9 (2026-08-17 소유자) — `선택지수` 열 폐지. 칸이라는 개념이 사라졌으니 셀 것도
    // 없다: 이 에피소드에서 나가는 간선 하나가 곧 선택지 하나다.

    public bool IsEnding => !string.IsNullOrWhiteSpace(EndingKey);
}

/// <summary>
/// `간선` 시트 한 행 = 다음 에피소드로 가는 길 하나. <b>길 하나가 곧 선택지 하나</b>이고,
/// `선택지` 열(D)은 그 길에 붙는 <b>문구 그 자체</b>다 (v9, 2026-08-17 소유자: "인덱스를
/// 가져오는 게 아니라 그냥 깡으로 대사만"). 문구는 `선택지` 시트의 전역 사전에서 고르지만
/// 셀에 들어가는 값은 문구이고, 사전은 <b>고르기 편하라고 있는 어휘집</b>이다.
///
/// <b>신원은 (출발, 도착, 문구)</b> — 같은 곳으로 가되 스탯변화·관문이 다른 선택지 둘을
/// 여럿 둘 수 있다(흔한 패턴). 관문·잠금·스탯변화·도착은 전부 간선 소유.
/// </summary>
/// <param name="ConditionLabel">
/// <b>해금조건</b> — 이 선택지를 고를 수 있으려면 (v8에서 열 이름이 `조건`→`해금조건`).
/// 미달이면 잠긴 채 보이고, <see cref="HideWhenLocked"/>가 켜져 있으면 숨는다.
/// </param>
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
    /// <b>표시조건</b> — 이 선택지가 목록에 보이려면 (v8, 2026-08-16 소유자: "보일지 말지는
    /// 이제 간선이 정한다"). 에피소드 시트에 있던 관문 둘이 간선으로 옮겨 온 것이고, 비면
    /// 언제나 보인다. 해금조건(<see cref="ConditionLabel"/>)과는 축이 다르다.
    /// </summary>
    public string? VisibleConditionLabel { get; init; }

    /// <summary>표시·해금 중 하나라도 걸려 있으면 관문 있는 길이다 — 그래프가 색으로 알린다.</summary>
    public bool HasGate =>
        !string.IsNullOrWhiteSpace(VisibleConditionLabel) ||
        !string.IsNullOrWhiteSpace(ConditionLabel);

    // OptionLabel = `선택지` 열(D)의 값 그대로다 (v9). 파생도 참조도 아니다 —
    // 문구를 고쳐도 배선이 안 깨지는 대신, 같은 문구를 어느 에피소드에서든 쓸 수 있다.

    /// <summary>문구가 비면 참 — 보이지 않는 기본(버튼 없이 자동 진행)이다.</summary>
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
/// `선택지` 시트 한 행 = <b>선택지 문구 하나</b> (v9, 2026-08-17 소유자). 이 시트는 어느
/// 에피소드의 소유물이 아니라 <b>챕터 전체가 함께 쓰는 문구 사전</b>이다 — `조건` 시트가
/// 라벨↔식의 사전인 것과 같은 자리다. 여기서 한 번 적어 두면 <b>어떤 에피소드의 어떤
/// 간선에서든 가져다 쓴다.</b>
///
/// 행이 갖는 것은 <b>인덱스(사전 안의 순서)와 대본 text뿐</b>이다. 도착·조건·잠금·
/// 스탯변화는 전부 간선의 것이고, 간선은 이 인덱스가 아니라 <b>문구 자체</b>를 적는다 —
/// 그래서 이 사전에서 행을 지워도 이미 쓰인 문구는 살아 있다(사전은 어휘집이지 배선이 아니다).
/// </summary>
public sealed record ChapterChoiceOption(
    string Index,
    string Text,
    string? Memo,
    int SourceRow);

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

    /// <summary>`선택지` 시트 — 챕터가 함께 쓰는 문구 사전 (v9). 인덱스 순으로 선다.</summary>
    public IReadOnlyList<ChapterChoiceOption> ChoiceOptions { get; }

    /// <summary>
    /// 고를 수 있는 문구들 — 사전에 적힌 것 + 간선이 이미 쓰고 있는 것의 합집합(중복 제거).
    /// 사전에 없는 문구를 손으로 적은 워크북에서도 툴의 드롭다운이 그 값을 잃지 않는다.
    /// </summary>
    public IReadOnlyList<string> ChoiceLabels =>
        field ??= ChoiceOptions.Select(option => option.Text)
            .Concat(Edges.Select(edge => edge.OptionLabel ?? string.Empty))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.Ordinal)
            .ToList();

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
