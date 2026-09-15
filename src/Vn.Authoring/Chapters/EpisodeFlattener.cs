using System.Text;
using Vn.Authoring.Rendering;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 산출 텍스트의 대사 줄 하나가 워크북의 어느 행에서 왔는지.
///
/// <b>산출 순서 그대로다</b> — 파서(<c>ScenarioTextParser</c>)가 같은 텍스트를 읽으면 같은
/// 순서의 줄 목록이 나오므로, 이 목록과 파서 결과는 자리로 맞물린다. G5가 이 맞물림으로
/// 새로 발급된 LineId를 그 행의 <b>인덱스</b>에 매어 프로젝트 신원 맵에 기록한다(v4 —
/// 워크북에는 쓰지 않는다).
/// </summary>
/// <param name="Index">행의 A열 인덱스 — 신원 맵의 키.</param>
public sealed record EpisodeFlattenedLine(int SourceRow, int Index, string? LineId);

/// <param name="Lines">산출된 대사 줄의 출처. 텍스트의 줄 순서와 같다.</param>
public sealed record EpisodeFlattenResult(
    string Text,
    IReadOnlyList<EpisodeFlattenedLine> Lines,
    IReadOnlyList<ChapterDiagnostic> Diagnostics)
{
    /// <summary>산출된 순서대로의 LineId. 중복이 없음을 증명하는 근거다.</summary>
    public IReadOnlyList<string> EmittedLineIds =>
        Lines.Where(line => line.LineId is not null).Select(line => line.LineId!).ToList();

    public bool HasErrors =>
        Diagnostics.Any(item => item.Severity == ChapterDiagnosticSeverity.Error);

    public IEnumerable<ChapterDiagnostic> Errors =>
        Diagnostics.Where(item => item.Severity == ChapterDiagnosticSeverity.Error);
}

/// <summary>
/// 에피소드 표를 X11 문법 텍스트로 편다 (G2-b, §3.4).
///
/// <b>v10 — 표를 위에서 아래로 한 번 훑는다.</b> 예전에는 <c>IN</c>이 가리키는 구간을 그
/// 자리로 <i>옮겨</i> 넣었고, 그래서 "각 행은 정확히 한 번 나온다"를 규칙 넷(구간 대상 존재 ·
/// 재사용 금지 · 중첩 금지 · OUT 대조)으로 지켜야 했다. 블록에서는 행이 이미 제자리에
/// 있으므로 <b>한 번 나온다는 것이 걷기의 성질</b>이고 — LineId 전역 유일성(계약서 C1)도
/// 그 성질에서 바로 나온다.
///
/// <b><c>&lt;&lt;if&gt;&gt;</c> 조립은 <see cref="YarnSyntax"/>가 한다.</b> 자기 문자열 연결로
/// 만들면 Preview·이미터와 함께 규약 사본이 셋이 된다.
/// </summary>
public static class EpisodeFlattener
{
    /// <param name="conditions">챕터 `조건` 시트의 라벨 → 조건 (G-7). 식은 Yarn으로 번역돼 실린다.</param>
    /// <param name="identity">
    /// 행 신원 — 인덱스(A열) → LineId (v4). 프로젝트의 <c>ExcelLineMap</c>이 원천이고,
    /// 여기 없는 행만 B열 값을 쓴다(과거 파일의 이행 seed). null이면 B열만 본다.
    /// </param>
    public static EpisodeFlattenResult Flatten(
        EpisodeWorkbookModel model,
        IReadOnlyDictionary<string, ChapterCondition> conditions,
        IReadOnlyDictionary<int, string>? identity = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(conditions);

        var diagnostics = new List<ChapterDiagnostic>();
        var builder = new StringBuilder();
        var emitted = new List<EpisodeFlattenedLine>();

        // v15 (2026-09-16 — R-C) — <b>대본에는 조건이 없다.</b> 모든 행이 대사이므로
        // 들여쓰기도 깊이도 없고, 평평화는 말 그대로 줄을 순서대로 적는 일이 됐다.
        // 조건 분기의 주인은 챕터 `간선` 시트다(표시조건·해금조건).
        foreach (EpisodeRow row in model.Rows)
        {
            EmitDialogue(model, row, builder, emitted, diagnostics, indent: 0, identity);
        }

        return new EpisodeFlattenResult(builder.ToString(), emitted, diagnostics);
    }

    /// <summary>
    /// 행의 유효 LineId — 매핑(인덱스 키)이 우선, 없으면 D열(이행 seed).
    /// <b>대사 행에서만 부른다</b> — 매핑의 열쇠가 대사 줄의 번호이기 때문이다(v14).
    /// </summary>
    private static string? IdOf(EpisodeRow row, IReadOnlyDictionary<int, string>? identity) =>
        identity is not null && identity.TryGetValue(row.LineIndex, out string? mapped)
            ? mapped
            : row.LineId;

    /// <summary>
    /// `IF` 행이 가리키는 조건식. 라벨은 에피소드가 적고 <b>식의 원천은 챕터 `조건` 시트</b>다(G-7) —
    /// 에피소드 워크북은 식을 모른다.
    /// </summary>
    private static void EmitDialogue(
        EpisodeWorkbookModel model,
        EpisodeRow row,
        StringBuilder builder,
        List<EpisodeFlattenedLine> emitted,
        List<ChapterDiagnostic> diagnostics,
        int indent,
        IReadOnlyDictionary<int, string>? identity)
    {
        // 화자도 내용도 없는 행은 산출하지 않는다. 텍스트의 빈 줄은 파서가 건너뛰므로,
        // 여기서 내보내면 산출 목록과 파서 결과의 자리 맞물림이 어긋난다 — 그 맞물림이
        // LineId 되쓰기(G5)의 근거라 어긋나면 엉뚱한 행에 ID가 적힌다.
        if (string.IsNullOrWhiteSpace(row.Speaker) && string.IsNullOrWhiteSpace(row.Text))
        {
            // v15 — 빈 행은 조용히 넘긴다. 예전에는 `조건라벨`만 적힌 행을 "반쯤 채운 행"으로
            // 보고 알렸는데, 그 칸이 사라지면서 <b>반쯤 채울 방법 자체가 없어졌다</b>:
            // 인덱스만 있는 행은 전부 템플릿이 깔아 둔 빈자리다. 사람이 아무것도 하지
            // 않았는데 뜨는 알림은 읽을거리가 아니라 소음이다.
            return;
        }

        builder.Append(YarnSyntax.IndentOf(indent));

        if (!string.IsNullOrWhiteSpace(row.Speaker))
        {
            builder.Append(row.Speaker).Append(": ");
        }

        builder.Append(row.Text);

        string? effectiveId = IdOf(row, identity);

        if (effectiveId is { Length: > 0 } lineId)
        {
            builder.Append(" #line:").Append(lineId);
        }

        builder.Append('\n');
        emitted.Add(new EpisodeFlattenedLine(row.SourceRow, row.LineIndex, effectiveId));
    }


    private static ChapterDiagnostic Diagnostic(
        EpisodeWorkbookModel model, int row, int column, string message) =>
        new(ChapterDiagnosticSeverity.Error,
            ChapterDiagnosticCode.ColumnHeaderUnexpected,
            model.SourcePath,
            model.SheetName,
            row,
            ClosedXML.Excel.XLHelper.GetColumnLetterFromNumber(column),
            message);
}
