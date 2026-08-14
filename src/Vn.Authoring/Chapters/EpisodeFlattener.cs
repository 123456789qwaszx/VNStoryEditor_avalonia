using System.Text;
using Vn.Authoring.Rendering;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 산출 텍스트의 대사·옵션 줄 하나가 워크북의 어느 행에서 왔는지.
///
/// <b>산출 순서 그대로다</b> — 파서(<c>ScenarioTextParser</c>)가 같은 텍스트를 읽으면 같은
/// 순서의 줄 목록이 나오므로, 이 목록과 파서 결과는 자리로 맞물린다. G5가 이 맞물림으로
/// 새로 발급된 LineId를 그 행의 <b>인덱스</b>에 매어 프로젝트 신원 맵에 기록한다(v4 —
/// 워크북에는 쓰지 않는다).
/// </summary>
/// <param name="Index">행의 A열 인덱스 — 신원 맵의 키.</param>
public sealed record EpisodeFlattenedLine(int SourceRow, int Index, string? LineId);

/// <param name="Lines">산출된 대사·옵션 줄의 출처. 텍스트의 줄 순서와 같다.</param>
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
/// <b>구간은 복제가 아니라 이동이다.</b> `IN`을 가진 자리에 구간을 옮겨 넣고 원래 자리에서는
/// 사라진다. §3.3 규칙 4가 한 구간의 소유자를 하나로 강제하므로 각 행은 산출물에 <b>정확히 한 번</b>
/// 나온다 — 이것이 LineId 전역 유일성(계약서 C1)의 구조적 보장이고, 인라인 복제 금지의 이유다.
///
/// <b><c>&lt;&lt;if&gt;&gt;</c> 조립은 <see cref="YarnSyntax"/>가 한다.</b> 자기 문자열 연결로 만들면
/// Preview·이미터와 함께 규약 사본이 셋이 된다. 이 클래스는 그 조립기의 세 번째 소비자다.
///
/// <b><c>OUT</c>은 명령이 아니라 선언이다.</b> Yarn에 노드 안의 줄 단위 goto가 없으므로
/// `OUT=40`은 "평평화하면 자연히 40으로 흐른다"는 주장이며, 여기서 그 주장을 계산 결과와
/// 맞춰 본다(§3.3 규칙 6).
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
        IReadOnlyList<EpisodeRow> mainFlow = model.MainFlow;

        VerifyOptionConvergence(model, mainFlow, diagnostics);

        for (int position = 0; position < mainFlow.Count; position++)
        {
            EpisodeRow row = mainFlow[position];

            switch (row.Kind)
            {
                case EpisodeRowKind.If:
                    EmitCondition(model, row, position, mainFlow, conditions,
                        builder, emitted, diagnostics, identity);
                    break;

                case EpisodeRowKind.Choice:
                    ReportChoiceLineId(model, row, diagnostics, identity);
                    break;

                case EpisodeRowKind.Option:
                    EmitOption(model, row, builder, emitted, diagnostics, identity);
                    break;

                default:
                    EmitDialogue(model, row, builder, emitted, diagnostics, indent: 0, identity);
                    break;
            }
        }

        return new EpisodeFlattenResult(builder.ToString(), emitted, diagnostics);
    }

    /// <summary>행의 유효 LineId — 매핑(인덱스 키)이 우선, 없으면 B열(이행 seed).</summary>
    private static string? IdOf(EpisodeRow row, IReadOnlyDictionary<int, string>? identity) =>
        identity is not null && identity.TryGetValue(row.Index, out string? mapped)
            ? mapped
            : row.LineId;

    // ── 조건 ────────────────────────────────────────────────────────────────

    private static void EmitCondition(
        EpisodeWorkbookModel model,
        EpisodeRow row,
        int position,
        IReadOnlyList<EpisodeRow> mainFlow,
        IReadOnlyDictionary<string, ChapterCondition> conditions,
        StringBuilder builder,
        List<EpisodeFlattenedLine> emitted,
        List<ChapterDiagnostic> diagnostics,
        IReadOnlyDictionary<int, string>? identity)
    {
        string? expression = Expression(model, row, conditions, diagnostics);

        if (expression is null || row.In is null)
        {
            return;
        }

        if (!model.Sections.TryGetValue(row.In.Value, out EpisodeSection? section))
        {
            // 규칙 1이 이미 오류로 잡았다. 여기서 또 말하지 않는다.
            return;
        }

        // 조건 조립은 공유 조립기가 한다 — 화면·파일·여기가 같은 규칙 하나를 지난다.
        YarnSyntax.AppendCondition(builder, "if", expression);
        builder.Append('\n');

        foreach (EpisodeRow inner in section.Rows)
        {
            EmitDialogue(model, inner, builder, emitted, diagnostics, indent: 1, identity);
        }

        builder.Append("<<endif>>\n");

        VerifyConvergence(model, row, section, NaturalSuccessor(mainFlow, position), diagnostics);
    }

    // ── 선택지 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>`CHOICE` 행의 LineId는 평평화 텍스트에 실리지 않는다.</b>
    ///
    /// Yarn에 "선택 시작"이라는 <i>줄</i>이 없기 때문이다 — 옵션 줄들이 곧 블록이고, 블록의
    /// 머리를 가리킬 자리가 텍스트에 없다. §3.2는 `CHOICE`도 라인이라고 하지만(연출·세이브
    /// 타깃이 될 수 있다), 그 신원을 텍스트 경계 너머로 옮길 그릇이 v1에는 없다.
    ///
    /// 모델에는 그대로 읽혀 있으므로 <b>데이터가 사라지지는 않는다.</b> 다만 텍스트를 지나
    /// 대사노드로 갈 때 이 ID는 따라가지 못한다는 사실을 조용히 넘기지 않는다(규칙 14).
    /// 기존 툴이 "선택 블록을 여는 줄"을 라인으로 갖게 되면 그때 열린다.
    /// </summary>
    private static void ReportChoiceLineId(
        EpisodeWorkbookModel model,
        EpisodeRow row,
        List<ChapterDiagnostic> diagnostics,
        IReadOnlyDictionary<int, string>? identity)
    {
        if (IdOf(row, identity) is not { Length: > 0 } lineId)
        {
            return;
        }

        diagnostics.Add(new ChapterDiagnostic(
            ChapterDiagnosticSeverity.Info,
            ChapterDiagnosticCode.ColumnHeaderUnexpected,
            model.SourcePath,
            model.SheetName,
            row.SourceRow,
            ClosedXML.Excel.XLHelper.GetColumnLetterFromNumber(ColumnLineId),
            $"CHOICE 행의 LineId '{lineId}'는 평평화 텍스트에 실리지 않습니다 — Yarn에 " +
            "'선택 시작' 줄이 없어 붙일 자리가 없습니다. 옵션들의 LineId는 그대로 실립니다."));
    }

    private static void EmitOption(
        EpisodeWorkbookModel model,
        EpisodeRow row,
        StringBuilder builder,
        List<EpisodeFlattenedLine> emitted,
        List<ChapterDiagnostic> diagnostics,
        IReadOnlyDictionary<int, string>? identity)
    {
        builder.Append("-> ").Append(row.Text);

        string? effectiveId = IdOf(row, identity);

        if (effectiveId is { Length: > 0 } optionId)
        {
            builder.Append(" #line:").Append(optionId);
        }

        builder.Append('\n');
        emitted.Add(new EpisodeFlattenedLine(row.SourceRow, row.Index, effectiveId));

        if (row.In is null || !model.Sections.TryGetValue(row.In.Value, out EpisodeSection? section))
        {
            return;
        }

        foreach (EpisodeRow inner in section.Rows)
        {
            EmitDialogue(model, inner, builder, emitted, diagnostics, indent: 1, identity);
        }
    }

    /// <summary>
    /// D6 — 옵션별 <c>OUT</c>이 서로 다른 비-<c>END</c> 인덱스로 갈리면 v1 오류다.
    /// 자연 낙하로는 둘을 동시에 만족시킬 수 없다. <c>$__ch_N</c> 승격은 이번 범위가 아니다.
    /// </summary>
    private static void VerifyOptionConvergence(
        EpisodeWorkbookModel model,
        IReadOnlyList<EpisodeRow> mainFlow,
        List<ChapterDiagnostic> diagnostics)
    {
        List<EpisodeRow> options = mainFlow
            .Where(row => row.Kind == EpisodeRowKind.Option && row.In is not null)
            .ToList();

        var targets = new List<(EpisodeRow Option, string Target)>();

        foreach (EpisodeRow option in options)
        {
            if (model.Sections.TryGetValue(option.In!.Value, out EpisodeSection? section) &&
                section.OutTarget is { Length: > 0 } target)
            {
                targets.Add((option, target));
            }
        }

        var distinct = targets
            .Where(item => !string.Equals(item.Target, EpisodeFlow.EndMarker, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Target)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (distinct.Count <= 1)
        {
            return;
        }

        foreach ((EpisodeRow option, string target) in targets.Skip(1))
        {
            diagnostics.Add(Diagnostic(
                model, option.SourceRow, ColumnIn,
                $"옵션마다 OUT이 갈립니다(이 옵션은 {target}, 앞의 옵션은 {targets[0].Target}). " +
                "평평화는 자연 낙하라 둘을 동시에 만족시킬 수 없습니다 — v1에서는 오류입니다(§3.4). " +
                "모든 옵션이 같은 인덱스로 수렴하거나 전부 END여야 합니다."));
        }
    }

    // ── OUT 대조 (§3.3 규칙 6) ──────────────────────────────────────────────

    /// <summary>
    /// 주 흐름에서 이 자리 다음에 오는 행의 인덱스. 없으면 에피소드가 끝난다는 뜻이다.
    ///
    /// <b>평평화 결과 위에서 센다.</b> 원본 행 순서로 세면 구간이 옮겨진 뒤의 흐름을 못 보고,
    /// 그게 정확히 <c>OUT</c>이 막으려는 착오다. 주 흐름은 이미 구간을 빼낸 목록이므로
    /// 여기서 다음 행을 보는 것이 곧 "평평화 후 자연히 흐르는 곳"이다.
    /// </summary>
    private static string NaturalSuccessor(IReadOnlyList<EpisodeRow> mainFlow, int position) =>
        position + 1 < mainFlow.Count
            // CHOICE·OPTION도 그대로 센다 — 조건 구간이 선택지 블록으로 수렴한다고 적는 것은 정상이다.
            ? mainFlow[position + 1].Index.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : EpisodeFlow.EndMarker;

    /// <summary>
    /// `IF` 행이 가리키는 조건식. 라벨은 에피소드가 적고 <b>식의 원천은 챕터 `조건` 시트</b>다(G-7) —
    /// 에피소드 워크북은 식을 모른다.
    /// </summary>
    private static string? Expression(
        EpisodeWorkbookModel model,
        EpisodeRow row,
        IReadOnlyDictionary<string, ChapterCondition> conditions,
        List<ChapterDiagnostic> diagnostics)
    {
        if (row.ConditionLabel is not { Length: > 0 } label)
        {
            diagnostics.Add(Diagnostic(
                model, row.SourceRow, ColumnConditionLabel,
                "IF 행에 조건라벨이 없어 조건을 만들 수 없습니다. 이 구간은 산출물에 나오지 않습니다."));

            return null;
        }

        if (!conditions.TryGetValue(label, out ChapterCondition? condition))
        {
            diagnostics.Add(Diagnostic(
                model, row.SourceRow, ColumnConditionLabel,
                $"조건라벨 '{label}'의 식을 챕터 `조건` 시트에서 찾지 못해 이 구간을 펴지 못했습니다."));

            return null;
        }

        // 시트의 식은 기획자 언어(trust >= 3)이고 대사 안 <<if>>는 Yarn이 평가한다 —
        // 번역은 ConditionYarnTranslator 한 곳이 한다. 실컴파일이 이 간극을 잡았다.
        ConditionYarnTranslation translated = ConditionYarnTranslator.Translate(condition);

        if (!translated.IsTranslatable)
        {
            diagnostics.Add(Diagnostic(model, row.SourceRow, ColumnConditionLabel, translated.Problem!));
            return null;
        }

        return translated.Yarn;
    }

    private static void VerifyConvergence(
        EpisodeWorkbookModel model,
        EpisodeRow caller,
        EpisodeSection section,
        string natural,
        List<ChapterDiagnostic> diagnostics)
    {
        if (section.OutTarget is not { Length: > 0 } declared)
        {
            return;  // OUT 누락은 리더가 이미 잡았다.
        }

        if (string.Equals(declared, natural, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // 조사(로/으로)는 숫자마다 달라지므로 쓰지 않는다 — "40로"처럼 어긋난 문장이 나간다.
        diagnostics.Add(Diagnostic(
            model, section.Last.SourceRow, ColumnOut,
            $"OUT={declared}이라고 적혀 있지만, 평평화하면 실제 수렴 지점은 {Describe(natural)}입니다. " +
            "OUT은 점프 명령이 아니라 '여기로 수렴한다'는 선언이고, 검증기가 그 선언이 사실인지 " +
            $"대조합니다(§3.4). 구간을 부른 자리는 {caller.SourceRow}행(인덱스 {caller.Index})입니다."));
    }

    private static string Describe(string natural) =>
        string.Equals(natural, EpisodeFlow.EndMarker, StringComparison.OrdinalIgnoreCase)
            ? "에피소드 끝(END)"
            : natural;

    // ── 줄 산출 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>화자: 대사 #line:ln_0001</c>. LineId 표기 정본은 Yarn 접미형이다(D8) — 계약서 C1이
    /// 요구하는 형식이고 이미터가 이미 쓰는 형식이라, 정본을 여기 맞추면 표기가 늘지 않는다.
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
            // 인덱스만 있고 나머지가 전부 빈 행은 <b>아직 안 쓴 자리</b>다 — 템플릿이 스스로
            // 넣어 준 시작 행(인덱스 10)이 그렇다. 사람이 아무것도 하지 않았는데 알림이
            // 뜨면, 그 알림은 읽을거리가 아니라 소음이 된다. 반쯤 채운 행만 말해 준다.
            bool untouched =
                string.IsNullOrWhiteSpace(row.ConditionLabel) &&
                row.In is null &&
                string.IsNullOrWhiteSpace(row.OutTarget) &&
                row.Tag == EpisodeRowTag.None;

            if (!untouched)
            {
                diagnostics.Add(new ChapterDiagnostic(
                    ChapterDiagnosticSeverity.Info,
                    ChapterDiagnosticCode.ColumnHeaderUnexpected,
                    model.SourcePath,
                    model.SheetName,
                    row.SourceRow,
                    null,
                    $"인덱스 {row.Index} 행은 화자·내용이 모두 비어 있어 산출물에 나오지 않습니다."));
            }

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
        emitted.Add(new EpisodeFlattenedLine(row.SourceRow, row.Index, effectiveId));
    }

    private const int ColumnLineId = 2;
    private const int ColumnConditionLabel = 5;
    private const int ColumnIn = 6;
    private const int ColumnOut = 7;

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
