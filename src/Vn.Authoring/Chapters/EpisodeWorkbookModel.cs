namespace Vn.Authoring.Chapters;

/// <summary>
/// 에피소드 워크북 한 행의 정체 (§3.2 C열 `유형`).
///
/// <b>이 판정은 여기 하나뿐이다.</b> 평평화·검증·G5가 같은 판정을 쓴다 — 행 유형 판정이
/// 두 곳에 생기면 한쪽만 고쳐지는 날이 오고, 그날 화면과 산출물이 다른 이야기를 한다.
/// </summary>
public enum EpisodeRowKind
{
    /// <summary>빈칸 = 대사. 라인이다 — LineId를 갖고 연출·세이브 타깃이 된다.</summary>
    Dialogue,

    /// <summary>
    /// 조건 행. <b>라인이 아니다</b>(소유자 확정) — LineId가 없고 연출·세이브 타깃도 아니다.
    /// 조건라벨과 <c>IN</c>만 갖는다.
    /// </summary>
    If,

    /// <summary>선택지 블록의 머리. 라인이다.</summary>
    Choice,

    /// <summary>선택지 옵션. 라인이며 조건라벨·<c>IN</c>을 가질 수 있다 (G-6c).</summary>
    Option
}

/// <summary>§3.2 D열 `태그` — 구간의 경계를 만든다. 인덱스 범위가 아니라 이 태그가 경계다.</summary>
public enum EpisodeRowTag
{
    None,

    /// <summary>구간의 첫 줄. <c>IN</c>이 가리키는 대상이다.</summary>
    Input,

    /// <summary>구간의 마지막 줄. <c>OUT</c> 값은 나갈 목적지다.</summary>
    Out
}

/// <summary>
/// 에피소드 워크북 한 행 (§3.2의 9열 — 2026-08-14 소유자 개정으로 스탯변화·메모 폐지.
/// 대사 중의 A계층 직접 조작은 세이브/로드·도달성이 못 보는 값 변화라 설계에서 뺐다).
/// </summary>
/// <param name="Index">A열. 10·20·30 방식(G-5). <c>IN</c>/<c>OUT</c>이 이 값으로 서로를 가리킨다.</param>
/// <param name="LineId">B열. 대사·선택지 행만 갖는다. 비어 있으면 아직 ID가 없는 새 행이다.</param>
/// <param name="OutTarget">G열. 나갈 목적지 인덱스이거나 <see cref="EpisodeFlow.EndMarker"/>다.</param>
/// <param name="SourceRow">엑셀 행 번호. 진단이 자리를 짚는 근거다.</param>
public sealed record EpisodeRow(
    int Index,
    string? LineId,
    EpisodeRowKind Kind,
    EpisodeRowTag Tag,
    string? ConditionLabel,
    int? In,
    string? OutTarget,
    string Speaker,
    string Text,
    int SourceRow)
{
    /// <summary>라인인가 — LineId를 받아 연출·세이브 타깃이 될 수 있는가.</summary>
    public bool IsLine => Kind is not EpisodeRowKind.If;

    /// <summary>
    /// 인덱스만 있고 아무것도 안 쓴 행 — 템플릿이 미리 깔아 둔 자리다.
    /// 표의 일부가 아니므로 검증·평평화 어디에서도 세지 않는다(리더가 걸러 낸다).
    /// </summary>
    public bool IsBlank =>
        Kind == EpisodeRowKind.Dialogue &&
        Tag == EpisodeRowTag.None &&
        Speaker.Length == 0 &&
        Text.Length == 0 &&
        ConditionLabel is null &&
        In is null &&
        OutTarget is null;

    /// <summary>이 행이 구간을 부르는가 (조건이든 선택지 옵션이든).</summary>
    public bool CallsSection => In is not null;
}

/// <summary>
/// <c>INPUT</c>부터 <c>OUT</c>까지의 줄 묶음 (§3.3). <b>양끝을 포함한다</b> —
/// 이것은 구간의 정의이지 검사 대상이 아니다.
/// </summary>
/// <param name="StartIndex">구간의 시작 인덱스. <c>IN</c>이 이 값을 가리킨다.</param>
/// <param name="OutTarget">구간을 빠져나가 흘러갈 곳. 인덱스이거나 <c>END</c>다.</param>
/// <param name="CalledFromRow">이 구간을 가리킨 행의 엑셀 행 번호. 없으면 아무도 안 가리켰다.</param>
public sealed record EpisodeSection(
    int StartIndex,
    IReadOnlyList<EpisodeRow> Rows,
    string? OutTarget,
    int? CalledFromRow)
{
    public EpisodeRow First => Rows[0];

    public EpisodeRow Last => Rows[^1];
}

/// <summary>흐름 표기 상수.</summary>
public static class EpisodeFlow
{
    /// <summary>`OUT` 값이 이것이면 에피소드가 여기서 끝난다는 선언이다.</summary>
    public const string EndMarker = "END";
}

/// <summary>
/// 에피소드 워크북 하나를 읽은 결과.
///
/// 챕터 모델과 같은 원칙이다 — <b>오류가 있어도 모델은 만든다.</b> 읽힌 데까지 세워 두고
/// 무엇이 어디서 잘못됐는지 옆에 붙여야 기획자가 고칠 자리를 찾는다(규칙 14).
/// </summary>
public sealed class EpisodeWorkbookModel
{
    public EpisodeWorkbookModel(
        string episodeId,
        string sourcePath,
        string sheetName,
        IReadOnlyList<EpisodeRow> rows,
        IReadOnlyDictionary<int, EpisodeSection> sections,
        IReadOnlyList<ChapterDiagnostic> diagnostics)
    {
        EpisodeId = episodeId;
        SourcePath = sourcePath;
        SheetName = sheetName;
        Rows = rows;
        Sections = sections;
        Diagnostics = diagnostics;
    }

    /// <summary>파일 이름에서 온다 — `episodes/{EpisodeId}.xlsx` (§3.2).</summary>
    public string EpisodeId { get; }

    public string SourcePath { get; }

    public string SheetName { get; }

    /// <summary>인덱스 오름차순. 구간 안의 행도 여기 전부 들어 있다.</summary>
    public IReadOnlyList<EpisodeRow> Rows { get; }

    /// <summary>시작 인덱스 → 구간.</summary>
    public IReadOnlyDictionary<int, EpisodeSection> Sections { get; }

    public IReadOnlyList<ChapterDiagnostic> Diagnostics { get; }

    public bool HasErrors =>
        Diagnostics.Any(item => item.Severity == ChapterDiagnosticSeverity.Error);

    public IEnumerable<ChapterDiagnostic> Errors =>
        Diagnostics.Where(item => item.Severity == ChapterDiagnosticSeverity.Error);

    /// <summary>어떤 구간에도 속하지 않는 행들 = 주 흐름. 평평화의 뼈대다.</summary>
    public IReadOnlyList<EpisodeRow> MainFlow
    {
        get
        {
            var inSection = Sections.Values
                .SelectMany(section => section.Rows)
                .Select(row => row.Index)
                .ToHashSet();

            return Rows.Where(row => !inSection.Contains(row.Index)).ToList();
        }
    }

    public EpisodeRow? FindByIndex(int index) =>
        Rows.FirstOrDefault(row => row.Index == index);
}
