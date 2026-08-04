using System.Text;
using Vn.Authoring.Definition;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Rendering;

/// <summary>
/// 합성 하나에서 나온 CSV 3종(마스터 플랜 §2.5의 2·3·4). 파일로 쓰기 전의 순수 문자열이다.
/// </summary>
public sealed class CsvBundle
{
    public CsvBundle(string bundleName, string scriptCsv, string reviewCsv, string directionCsv)
    {
        BundleName = bundleName;
        ScriptCsv = scriptCsv;
        ReviewCsv = reviewCsv;
        DirectionCsv = directionCsv;
    }

    public string BundleName { get; }

    /// <summary>(2) 번역·녹음 — LineId, 화자, 대사와 녹음용 노드·인덱스.</summary>
    public string ScriptCsv { get; }

    /// <summary>(3) 기획 검수 — 한 줄 = 대본 한 줄. 조건·선택·set·출구까지.</summary>
    public string ReviewCsv { get; }

    /// <summary>(4) 연출 테이블 — 한 줄 = 커맨드 하나. 대사는 참조용이다.</summary>
    public string DirectionCsv { get; }

    public IEnumerable<(string FileName, string Text)> Files => FilesFor(new ExportFormatSelection());

    /// <summary>선택된 양식만 (X13). 기본 선택이면 3종 전부다.</summary>
    public IEnumerable<(string FileName, string Text)> FilesFor(ExportFormatSelection formats)
    {
        ArgumentNullException.ThrowIfNull(formats);

        if (formats.ScriptCsv)
        {
            yield return ($"Script_{BundleName}.csv", ScriptCsv);
        }

        if (formats.ReviewCsv)
        {
            yield return ($"Review_{BundleName}.csv", ReviewCsv);
        }

        if (formats.DirectionCsv)
        {
            yield return ($"Direction_{BundleName}.csv", DirectionCsv);
        }
    }
}

/// <summary>
/// 발행 결과(합성)를 번역·녹음 / 기획 검수 / 연출 테이블 CSV로 펼친다.
/// .yarn 내보내기와 같은 입구(발행 결과)를 쓴다 — 작업 중 상태를 내보내지 않는다.
///
/// <b>인코딩은 UTF-8 BOM 포함이다.</b> .yarn의 no-BOM 규칙과 의도적으로 다르다 —
/// 엑셀은 BOM이 없으면 한글 CSV를 ANSI로 읽어 깨뜨린다. 줄바꿈은 CRLF, 셀 이스케이프는
/// RFC 4180(쉼표·따옴표·줄바꿈이 있으면 따옴표로 감싸고 내부 따옴표는 겹친다)을 따른다.
/// </summary>
public static class CsvBundleExporter
{
    public static CsvBundle Export(
        DialogueResult dialogue,
        PresentationResult? presentation = null,
        StoryProject? project = null,
        GameDefinition? definition = null,
        string? bundleName = null)
    {
        ArgumentNullException.ThrowIfNull(dialogue);

        string name = YarnSyntax.SanitizeNodeName(
            bundleName
            ?? (string.IsNullOrWhiteSpace(dialogue.SourceNodeName)
                ? dialogue.SourceNodeId
                : dialogue.SourceNodeName));

        return new CsvBundle(
            name,
            BuildScriptCsv(dialogue),
            BuildReviewCsv(dialogue, project),
            BuildDirectionCsv(dialogue, presentation, definition));
    }

    public static CsvBundle Export(
        ResolvedComposition composition,
        StoryProject? project = null,
        GameDefinition? definition = null,
        string? bundleName = null)
    {
        ArgumentNullException.ThrowIfNull(composition);

        if (!composition.IsCompatible)
        {
            throw new IncompatibleCompositionException(composition);
        }

        return Export(composition.Dialogue!, composition.Presentation, project, definition, bundleName);
    }

    /// <summary>UTF-8 BOM 포함으로 폴더에 쓴다. 임시 파일 교체는 .yarn 쓰기와 같다.</summary>
    public static IReadOnlyList<string> WriteTo(
        CsvBundle bundle,
        string directory,
        ExportFormatSelection? formats = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Directory.CreateDirectory(directory);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var written = new List<string>();

        foreach ((string fileName, string text) in bundle.FilesFor(formats ?? new ExportFormatSelection()))
        {
            string path = Path.Combine(directory, fileName);
            string temporary = path + ".tmp";

            File.WriteAllText(temporary, text, encoding);
            File.Move(temporary, path, overwrite: true);
            written.Add(path);
        }

        return written;
    }

    // ── (2) 번역·녹음 ──────────────────────────────────────────────────────

    private static string BuildScriptCsv(DialogueResult dialogue)
    {
        var rows = new StringBuilder();
        AppendRow(rows, "LineId", "화자", "대사", "노드", "인덱스");

        foreach (DialogueResultLine line in dialogue.Lines)
        {
            AppendRow(
                rows,
                line.LineId,
                line.CharacterName,
                line.Text,
                dialogue.SourceNodeName,
                line.Index.ToString());
        }

        return rows.ToString();
    }

    // ── (3) 기획 검수 ──────────────────────────────────────────────────────

    private static string BuildReviewCsv(DialogueResult dialogue, StoryProject? project)
    {
        var rows = new StringBuilder();
        AppendRow(rows, "노드", "인덱스", "LineId", "화자", "대사", "조건", "선택", "Set", "출구");

        string condition = string.Empty;
        int choiceBlock = -1;
        int choiceOption = -1;
        bool inChoice = false;

        foreach (DialogueResultLine line in dialogue.Lines)
        {
            bool isLabel = false;

            switch (line.Transition?.Kind)
            {
                case ConditionTransitionKind.BeginIf:
                case ConditionTransitionKind.BeginElseIf:
                    condition = line.Transition.ConditionName
                        ?? line.Transition.Expression
                        ?? line.Transition.ConditionId
                        ?? string.Empty;
                    break;

                case ConditionTransitionKind.EndIf:
                    condition = string.Empty;
                    break;

                case ConditionTransitionKind.BeginChoice:
                    choiceBlock++;
                    choiceOption = 0;
                    inChoice = true;
                    isLabel = true;
                    break;

                case ConditionTransitionKind.BeginNextOption:
                    choiceOption++;
                    isLabel = true;
                    break;

                case ConditionTransitionKind.EndChoice:
                    inChoice = false;
                    break;
            }

            string choice = inChoice
                ? $"블록{choiceBlock + 1} 옵션{choiceOption + 1}" + (isLabel ? " 라벨" : string.Empty)
                : string.Empty;

            string sets = string.Join(
                " ; ",
                line.Sets.Select(operation =>
                    $"{operation.Variable} {SetOperators.Symbol(operation.Operator)} {operation.Value}"));

            string exit = line.BranchExitTargetNodeId is { } target
                ? project?.FindNode(target)?.Name ?? target
                : string.Empty;

            AppendRow(
                rows,
                dialogue.SourceNodeName,
                line.Index.ToString(),
                line.LineId,
                line.CharacterName,
                line.Text,
                condition,
                choice,
                sets,
                exit);
        }

        return rows.ToString();
    }

    // ── (4) 연출 테이블 ────────────────────────────────────────────────────

    private static string BuildDirectionCsv(
        DialogueResult dialogue,
        PresentationResult? presentation,
        GameDefinition? definition)
    {
        var rows = new StringBuilder();
        AppendRow(rows, "LineId", "대사", "순서", "커맨드", "인자", "메모");

        if (presentation is null)
        {
            return rows.ToString();
        }

        // 인자 해석(파라미터 순서·기본값)은 합성기와 같은 길을 지난다.
        RenderedDocument document = ResultDocumentComposer.Compose(
            dialogue,
            presentation,
            project: null,
            definition,
            OutputPresetCatalog.RuntimeFull.Options);

        var orderPerLine = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (RenderedSegment segment in document.Segments)
        {
            if (segment.Kind != RenderedSegmentKind.PresentationCommand)
            {
                continue;
            }

            string lineKey = segment.Source.LineId ?? "(setup)";
            orderPerLine.TryGetValue(lineKey, out int order);
            orderPerLine[lineKey] = ++order;

            AppendRow(
                rows,
                segment.Source.LineId ?? "(setup)",
                segment.Source.LineId is { } lineId
                    ? dialogue.FindLine(lineId)?.Text ?? string.Empty
                    : string.Empty,
                order.ToString(),
                segment.CommandName ?? segment.DefinitionId ?? string.Empty,
                string.Join(
                    " ",
                    (segment.Arguments ?? Array.Empty<RenderedArgument>())
                        .Select(argument => $"{argument.Name}={argument.Value}")),
                segment.Note ?? string.Empty);
        }

        return rows.ToString();
    }

    // ── RFC 4180 ───────────────────────────────────────────────────────────

    private static void AppendRow(StringBuilder builder, params string[] cells)
    {
        builder.Append(string.Join(',', cells.Select(Escape))).Append("\r\n");
    }

    private static string Escape(string cell)
    {
        if (cell.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
        {
            return cell;
        }

        return "\"" + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
