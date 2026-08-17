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
    /// 조건 블록의 여는 줄 (v10). <b>라인이 아니다</b>(소유자 확정) — LineId가 없고
    /// 연출·세이브 타깃도 아니다. 조건라벨만 갖는다.
    /// </summary>
    If,

    /// <summary>같은 체인의 다른 갈래 — 시트 낱말은 <c>ELSEIF</c>. 깊이는 안 는다.</summary>
    ElseIf,

    /// <summary>조건 블록의 닫는 줄 — 시트 낱말은 <c>ENDIF</c> (v10). 라인이 아니다.</summary>
    End
}

/// <summary>
/// 에피소드 워크북 한 행 (§3.2의 6열 — v10, 2026-08-17 소유자 결정).
///
/// <b>조건 분기는 블록이다.</b> 예전에는 `유형 · 태그 · IN · OUT` 네 열이 한 조건을 만들었다:
/// IF의 <c>IN</c>이 딴 데 있는 구간을 가리키고, 그 구간의 첫 줄에 <c>INPUT</c>, 마지막 줄에
/// <c>OUT</c>이 붙었다. 한 개념이 네 열·세 행에 흩어져 있어서 조건 한 줄만 봐서는 그게
/// 어디까지 덮는지 알 수 없었고(§3.3의 규칙 1·4·5·6이 그 대가였다), <b>중첩은 아예
/// 금지</b>였다. 게다가 <c>OUT</c>은 흐름을 바꾸는 힘이 없는 선언이라 맞으면 아무 일도
/// 없고 틀리면 오류만 냈다.
///
/// 이제 <c>IF</c>행과 <c>ENDIF</c>행이 그 사이를 감싼다. 범위가 눈에 보이고, 중첩이
/// 자연스럽고, 세 열이 사라졌다.
/// </summary>
/// <param name="Index">A열. 10·20·30 방식(G-5). 줄의 신원이다.</param>
/// <param name="LineId">B열. 대사 행만 갖는다. 비어 있으면 아직 ID가 없는 새 행이다.</param>
/// <param name="SourceRow">엑셀 행 번호. 진단이 자리를 짚는 근거다.</param>
public sealed record EpisodeRow(
    int Index,
    string? LineId,
    EpisodeRowKind Kind,
    string? ConditionLabel,
    string Speaker,
    string Text,
    int SourceRow)
{
    /// <summary>라인인가 — LineId를 받아 연출·세이브 타깃이 될 수 있는가.</summary>
    public bool IsLine => Kind is EpisodeRowKind.Dialogue;

    /// <summary>
    /// 인덱스만 있고 아무것도 안 쓴 행 — 템플릿이 미리 깔아 둔 자리다.
    /// 표의 일부가 아니므로 검증·평평화 어디에서도 세지 않는다(리더가 걸러 낸다).
    /// </summary>
    public bool IsBlank =>
        Kind == EpisodeRowKind.Dialogue &&
        Speaker.Length == 0 &&
        Text.Length == 0 &&
        ConditionLabel is null;
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
        IReadOnlyList<ChapterDiagnostic> diagnostics)
    {
        EpisodeId = episodeId;
        SourcePath = sourcePath;
        SheetName = sheetName;
        Rows = rows;
        Diagnostics = diagnostics;
    }

    /// <summary>파일 이름에서 온다 — `episodes/{EpisodeId}.xlsx` (§3.2).</summary>
    public string EpisodeId { get; }

    public string SourcePath { get; }

    public string SheetName { get; }

    /// <summary>인덱스 오름차순. 블록 안의 행도 그 자리에 그대로 있다 (v10).</summary>
    public IReadOnlyList<EpisodeRow> Rows { get; }

    public IReadOnlyList<ChapterDiagnostic> Diagnostics { get; }

    public bool HasErrors =>
        Diagnostics.Any(item => item.Severity == ChapterDiagnosticSeverity.Error);

    public IEnumerable<ChapterDiagnostic> Errors =>
        Diagnostics.Where(item => item.Severity == ChapterDiagnosticSeverity.Error);

    public EpisodeRow? FindByIndex(int index) =>
        Rows.FirstOrDefault(row => row.Index == index);
}
