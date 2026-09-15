namespace Vn.Authoring.Chapters;

// ⛔ <b>EpisodeRowKind는 2026-09-16에 사라졌다</b> (규격 v15 — R-C,
//    docs/work-orders/tool-owns-workbooks-orders.md §3).
//
//    대본 시트에 `유형`이 있던 이유는 대사 행과 블록 행(IF·ELSEIF·ENDIF)을 가르기
//    위해서였다. 블록이 폐지되자 그 열의 값은 `대사` 하나뿐이 되었고, 값이 하나인
//    칸은 묻지 않는 질문이다. `조건라벨`도 같은 날 함께 걷혔다 — 그 칸이 가리키던
//    챕터 `조건` 시트의 라벨은 [2] 진행 스탯이고, 런타임이 대사에서 그것을 읽는 것을
//    금지했다.
//
//    ⚠ <b>이제 대본의 모든 행은 대사다.</b> 행 유형을 묻는 코드가 다시 생기려 하면,
//    그것은 구조가 대본으로 돌아오려는 신호다 — 구조의 주인은 챕터 `간선` 시트다.

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
/// <param name="Index">
/// C열. 10·20·30 방식(G-5). 대사 줄의 신원이다.
///
/// ⚠ <b>대사 행만 갖는다 (v14, 2026-08-24 소유자).</b> 블록 행(IF·ELSEIF·ENDIF)에서는
/// 언제나 <c>null</c>이다 — 셀에 무엇이 적혀 있든 리더가 안 싣는다. 그 번호의 뜻이
/// <i>"플레이어에게 몇 번째로 전달되는 대사인가"</i>이기 때문이다: 구조를 그리는 행이
/// 번호를 가지면 10·20·30이 대사 순번이 아니라 행 순번이 되어 버린다.
///
/// 그래서 <c>Index</c>를 쓰는 자리는 전부 <c>IsLine</c>이 참인 줄이다. 널이면 그 코드가
/// 대사 아닌 행을 대사처럼 다루고 있다는 뜻이다.
/// </param>
/// <param name="LineId">D열. 대사 행만 갖는다. 비어 있으면 아직 ID가 없는 새 행이다.</param>
/// <param name="SourceRow">엑셀 행 번호. 진단이 자리를 짚는 근거다.</param>
public sealed record EpisodeRow(
    int? Index,
    string? LineId,
    string Speaker,
    string Text,
    int SourceRow)
{
    /// <summary>
    /// 라인인가 — LineId를 받아 연출·세이브 타깃이 될 수 있는가.
    ///
    /// ⚠ <b>v15부터 언제나 참이다</b>(블록 행이 폐지됐다). 이름을 남겨 둔 것은 부르는
    /// 자리가 많아서이고, <b>뜻이 사라진 것은 아니다</b> — 인덱스 없는 행은 여전히 표의
    /// 일부가 아니다. 그 판정이 여기 하나로 모여 있어야 다음에 또 갈리지 않는다.
    /// </summary>
    public bool IsLine => Index is not null;

    /// <summary>
    /// 인덱스만 있고 아무것도 안 쓴 행 — 템플릿이 미리 깔아 둔 자리다.
    /// 표의 일부가 아니므로 검증·평평화 어디에서도 세지 않는다(리더가 걸러 낸다).
    /// </summary>
    public bool IsBlank => Speaker.Length == 0 && Text.Length == 0;

    /// <summary>
    /// 이 줄의 번호. 인덱스 없는 행에서 부르면 터진다: 조용히 0을 내주면 그 0이
    /// <c>ExcelLineMap</c>·연출·세이브로 흘러 들어가 "어느 줄인지 모르는 줄"이 생긴다.
    /// </summary>
    public int LineIndex => Index ?? throw new InvalidOperationException(
        $"{SourceRow}행에는 인덱스가 없습니다 — 인덱스는 대사 줄의 번호입니다(v15).");
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
