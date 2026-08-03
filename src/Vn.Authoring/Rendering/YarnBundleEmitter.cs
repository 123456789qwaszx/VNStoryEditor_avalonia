using System.Text;
using Vn.Authoring.Definition;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Rendering;

/// <summary>이미터가 발견한 문제 하나. 막는 문제를 안고는 파일을 쓰지 않는다.</summary>
public sealed record YarnBundleProblem(string Message, bool IsBlocking, string? LineId = null);

/// <summary>
/// 이 번들이 필요로 하는 변수 선언 하나. 선언은 번들 텍스트가 아니라
/// 폴더당 하나뿐인 선언 파일에 실린다 — 여러 번들을 한 유니티 프로젝트로 컴파일할 때
/// 같은 변수를 두 번 선언하면 컴파일 전체가 깨지기 때문이다.
/// </summary>
public sealed record YarnDeclaration(string Variable, string InitialValue);

/// <summary>
/// 합성 하나에서 나온 .yarn 트리오. 파일로 쓰기 전의 순수 문자열이다.
/// Set·Pres는 연출 결과 없이 합성했으면 null이다 — 레인이 없는 Story는 혼자 재생된다.
/// </summary>
public sealed class YarnBundle
{
    public YarnBundle(
        string bundleName,
        string storyText,
        string? setText,
        string? presText,
        IReadOnlyList<YarnDeclaration> declarations,
        IReadOnlyList<YarnBundleProblem> problems)
    {
        BundleName = bundleName;
        StoryText = storyText;
        SetText = setText;
        PresText = presText;
        Declarations = declarations;
        Problems = problems;
    }

    public string BundleName { get; }

    public string StoryText { get; }

    public string? SetText { get; }

    public string? PresText { get; }

    /// <summary>이 번들이 쓰는 변수와 초기값. 선언 파일 합집합의 재료다.</summary>
    public IReadOnlyList<YarnDeclaration> Declarations { get; }

    public IReadOnlyList<YarnBundleProblem> Problems { get; }

    public bool HasBlockingProblems => Problems.Any(problem => problem.IsBlocking);

    public string StoryFileName => $"Story_{BundleName}.yarn";

    public string SetFileName => $"Set_{BundleName}.yarn";

    public string PresFileName => $"Pres_{BundleName}.yarn";

    /// <summary>파일 이름 → 내용. 쓰는 순서도 이 순서다.</summary>
    public IEnumerable<(string FileName, string Text)> Files
    {
        get
        {
            yield return (StoryFileName, StoryText);

            if (SetText is not null)
            {
                yield return (SetFileName, SetText);
            }

            if (PresText is not null)
            {
                yield return (PresFileName, PresText);
            }
        }
    }

    public string BlockingSummary() => string.Join(
        Environment.NewLine,
        Problems.Where(problem => problem.IsBlocking).Select(problem => "• " + problem.Message));
}

/// <summary>
/// 발행 결과 조합 하나를 ked-presentation-runtime이 재생하는 .yarn 트리오
/// (Story / Set / Pres)로 조립한다. <c>runtime-contract.md</c>가 이 클래스의 사양이다.
///
/// - Story 대사 라인에만 <c>#line:</c> 태그를 단다 (C1). Pres 사본은 무태그다 (C4).
/// - 활성 레인이 있는 노드의 모든 <c>&lt;&lt;jump&gt;&gt;</c> 직전에 <c>&lt;&lt;pres_end&gt;&gt;</c>를 낸다 (A5).
/// - <c>&lt;&lt;set&gt;&gt;</c>은 Story에만 낸다 (D2). 조건 구조는 Pres에 그대로 복제한다 (D3).
/// - Set 노드는 커맨드 전용이다 (A2). 메인 레인 전용 커맨드는 Set·Pres에 내지 않는다 (E2).
/// - Pres 사본의 라인 수·순서는 Story의 대사 라인과 정확히 같다 (B) — 같은 결과에서
///   만들므로 구조적으로 보장된다.
///
/// 조립은 <see cref="ResultDocumentComposer"/>의 Runtime Full Segment 목록 위에서 한다.
/// Preview와 파일이 같은 합성기를 지나야 화면에서 본 것과 파일의 차이를 찾을 필요가 없어진다.
/// </summary>
public static class YarnBundleEmitter
{
    public static YarnBundle Emit(
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

        return Emit(
            composition.Dialogue!,
            composition.Presentation,
            project,
            definition,
            bundleName);
    }

    public static YarnBundle Emit(
        DialogueResult dialogue,
        PresentationResult? presentation = null,
        StoryProject? project = null,
        GameDefinition? definition = null,
        string? bundleName = null)
    {
        ArgumentNullException.ThrowIfNull(dialogue);

        RenderedDocument document = ResultDocumentComposer.Compose(
            dialogue,
            presentation,
            project,
            definition,
            OutputPresetCatalog.RuntimeFull.Options);

        PresentationCommandCatalog catalog = PresentationCommandCatalog.For(definition);
        string name = YarnSyntax.SanitizeNodeName(
            bundleName
            ?? (string.IsNullOrWhiteSpace(dialogue.SourceNodeName)
                ? dialogue.SourceNodeId
                : dialogue.SourceNodeName));

        bool hasLane = presentation is not null;
        var problems = new List<YarnBundleProblem>();
        var story = new StringBuilder();
        var pres = hasLane ? new StringBuilder() : null;
        var setup = hasLane ? new StringBuilder() : null;

        // ── 헤더 ────────────────────────────────────────────────────────────
        story.Append("title: Story_").Append(name).Append("\n---\n");
        pres?.Append("title: Pres_").Append(name).Append("\n---\n\n");
        setup?.Append("title: Set_").Append(name).Append("\n---\n");

        // Story 서두: 변수 초기화(set) → 원샷 레인(beat) → 서브 레인(pres_start).
        // 헤더가 닫히는 시점은 첫 본문 Segment를 만났을 때다.
        bool storyHeaderClosed = false;

        void CloseStoryHeader()
        {
            if (storyHeaderClosed)
            {
                return;
            }

            if (hasLane)
            {
                // 레인이 필요한 Story 노드는 각자 자기 pres_start로 연다 (A5).
                story.Append("<<beat Set_").Append(name).Append(">>\n");
                story.Append("<<pres_start Pres_").Append(name).Append(">>\n");
            }

            story.Append('\n');
            storyHeaderClosed = true;
        }

        foreach (RenderedSegment segment in document.Segments)
        {
            string indent = YarnSyntax.IndentOf(segment.IndentLevel);

            switch (segment.Kind)
            {
                case RenderedSegmentKind.NodeHeader:
                case RenderedSegmentKind.NodeFooter:
                    break;

                case RenderedSegmentKind.SetAssignment:
                    // set은 Story에만 낸다 (D2). 저장소가 공유라 Pres에 복제하면 이중 실행된다.
                    if (segment.Source.LineId is not null)
                    {
                        CloseStoryHeader();
                    }

                    story.Append(segment.Source.LineId is null ? string.Empty : indent);
                    YarnSyntax.AppendSet(story, segment);
                    story.Append('\n');
                    break;

                case RenderedSegmentKind.PresentationCommand:
                    AppendPresentationCommand(segment, setup, pres, catalog, problems, CloseStoryHeader);
                    break;

                case RenderedSegmentKind.ConditionBegin:
                case RenderedSegmentKind.ConditionElseIf:
                case RenderedSegmentKind.ConditionEnd:
                    // 조건 구조는 Pres에 그대로 복제한다 (D3). 읽기 전용이고,
                    // 서브 레인은 메인이 지나간 뒤에만 평가하므로 같은 분기를 탄다.
                    CloseStoryHeader();
                    AppendCondition(story, segment, indent);

                    if (pres is not null)
                    {
                        AppendCondition(pres, segment, indent);
                    }

                    break;

                case RenderedSegmentKind.DialogueLine:
                    CloseStoryHeader();
                    AppendDialogue(segment, story, pres, indent, problems);
                    break;

                case RenderedSegmentKind.BranchJump:
                case RenderedSegmentKind.DefaultJump:
                    CloseStoryHeader();

                    if (hasLane)
                    {
                        // jump는 서브 레인을 정리하지 않는다 — 반드시 pres_end 선행 (A5).
                        story.Append(indent).Append("<<pres_end>>\n");
                    }

                    story.Append(indent);
                    YarnSyntax.AppendJump(story, JumpTargetOf(segment));
                    story.Append('\n');
                    break;

                case RenderedSegmentKind.Warning:
                    problems.Add(new YarnBundleProblem(
                        segment.Text ?? "알 수 없는 경고",
                        IsBlocking: false,
                        segment.Source.LineId));
                    break;
            }
        }

        CloseStoryHeader();
        story.Append("===\n");
        pres?.Append("===\n");
        setup?.Append("===\n");

        return new YarnBundle(
            name,
            story.ToString(),
            setup?.ToString(),
            pres?.ToString(),
            CollectDeclarations(dialogue, definition),
            problems);
    }

    /// <summary>선언 파일 이름. 폴더당 하나다.</summary>
    public const string DeclarationsFileName = "declarations.yarn";

    /// <summary>선언 전용 노드의 타이틀. 런타임은 이 노드에 진입하지 않는다.</summary>
    public const string DeclarationsNodeTitle = "_declarations";

    /// <summary>
    /// 여러 번들의 선언 합집합을 선언 파일 텍스트로 만든다. 선언이 하나도 없으면 null이다.
    ///
    /// 런타임 C#에는 <c>&lt;&lt;declare&gt;&gt;</c>도 스마트 변수도 없다 — 컴파일을 위해
    /// 이미터가 선언을 내되(D4), Story 노드마다 내면 여러 번들을 한 프로그램으로 컴파일할 때
    /// 중복 선언으로 깨진다. 그래서 선언은 전용 파일 하나에만 낸다.
    /// 같은 변수의 초기값이 번들 간에 다르면 합집합이 성립하지 않으므로 거부한다.
    /// </summary>
    public static string? ComposeDeclarationsText(IReadOnlyList<YarnBundle> bundles)
    {
        ArgumentNullException.ThrowIfNull(bundles);

        // 번들 목록의 순서와 무관하게 같은 파일이 나오도록 변수 이름순으로 정렬한다.
        var union = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (YarnBundle bundle in bundles)
        {
            foreach (YarnDeclaration declaration in bundle.Declarations)
            {
                if (union.TryGetValue(declaration.Variable, out string? existing))
                {
                    if (!string.Equals(existing, declaration.InitialValue, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"변수 '{declaration.Variable}'의 초기값이 합성 간에 다릅니다 " +
                            $"({existing} vs {declaration.InitialValue}). " +
                            "같은 폴더로 내보내는 합성들은 같은 게임 정의를 써야 합니다.");
                    }

                    continue;
                }

                union[declaration.Variable] = declaration.InitialValue;
            }
        }

        if (union.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.Append("title: ").Append(DeclarationsNodeTitle).Append("\n---\n");

        foreach ((string variable, string initialValue) in union)
        {
            builder.Append("<<declare ")
                .Append(YarnSyntax.NormalizeVariable(variable))
                .Append(" = ")
                .Append(initialValue)
                .Append(">>\n");
        }

        builder.Append("===\n");
        return builder.ToString();
    }

    /// <summary>트리오 하나를 폴더에 쓴다. 선언 파일도 함께 나온다.</summary>
    public static IReadOnlyList<string> WriteTo(YarnBundle bundle, string directory)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return WriteBundles(new[] { bundle }, directory);
    }

    /// <summary>
    /// 여러 트리오를 한 폴더에 쓴다. 결정적 출력: UTF-8 BOM 없음, LF, 임시 파일 교체.
    /// 선언 파일은 <b>합집합으로 한 번만</b> 쓴다. 막는 문제나 이름 충돌, 선언 충돌이
    /// 있으면 아무 파일도 쓰지 않는다 — 어긋난 출력은 컴파일이 되어도 런타임에서 조용히 깨진다.
    /// </summary>
    public static IReadOnlyList<string> WriteBundles(
        IReadOnlyList<YarnBundle> bundles,
        string directory)
    {
        ArgumentNullException.ThrowIfNull(bundles);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var blocked = bundles.Where(bundle => bundle.HasBlockingProblems).ToArray();

        if (blocked.Length > 0)
        {
            throw new InvalidOperationException(
                string.Join(
                    Environment.NewLine,
                    blocked.Select(bundle =>
                        $"'{bundle.BundleName}'을 내보낼 수 없습니다.{Environment.NewLine}{bundle.BlockingSummary()}")));
        }

        string? duplicate = bundles
            .GroupBy(bundle => bundle.BundleName, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"번들 이름 '{duplicate}'이 겹칩니다. 같은 폴더에서 서로를 덮어쓰게 됩니다.");
        }

        // 선언 충돌은 파일을 하나라도 쓰기 전에 확인한다.
        string? declarations = ComposeDeclarationsText(bundles);

        Directory.CreateDirectory(directory);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var written = new List<string>();

        void Write(string fileName, string text)
        {
            string path = Path.Combine(directory, fileName);
            string temporary = path + ".tmp";

            File.WriteAllText(temporary, text, encoding);
            File.Move(temporary, path, overwrite: true);
            written.Add(path);
        }

        foreach (YarnBundle bundle in bundles)
        {
            foreach ((string fileName, string text) in bundle.Files)
            {
                Write(fileName, text);
            }
        }

        if (declarations is not null)
        {
            Write(DeclarationsFileName, declarations);
        }

        return written;
    }

    /// <summary>
    /// 이 번들이 쓰는 변수를 등장 순서대로 모은다. 초기값 타입은 게임 정의의
    /// variables가 정하고, 모르면 숫자다(런타임이 스탯을 float으로 읽는다).
    /// </summary>
    private static IReadOnlyList<YarnDeclaration> CollectDeclarations(
        DialogueResult dialogue,
        GameDefinition? definition)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var declarations = new List<YarnDeclaration>();

        void Collect(string variable)
        {
            string trimmed = variable.TrimStart('$').Trim();

            if (trimmed.Length > 0 && seen.Add(trimmed))
            {
                declarations.Add(new YarnDeclaration(trimmed, InitialValueOf(trimmed, definition)));
            }
        }

        foreach (DialogueResultAssignment assignment in dialogue.Assignments)
        {
            Collect(assignment.Variable);
        }

        foreach (DialogueResultLine line in dialogue.Lines)
        {
            foreach (DialogueResultSetOperation operation in line.Sets)
            {
                Collect(operation.Variable);
            }
        }

        return declarations;
    }

    private static string InitialValueOf(string variable, GameDefinition? definition)
    {
        VariableSpec? spec = definition?.Variables.FirstOrDefault(item =>
            string.Equals(item.Name, variable, StringComparison.Ordinal));

        return spec?.Type.ToLowerInvariant() switch
        {
            "bool" or "boolean" => "false",
            "string" => "\"\"",
            _ => "0"
        };
    }

    private static void AppendPresentationCommand(
        RenderedSegment segment,
        StringBuilder? setup,
        StringBuilder? pres,
        PresentationCommandCatalog catalog,
        List<YarnBundleProblem> problems,
        Action closeStoryHeader)
    {
        // 메인 레인 전용 커맨드는 서브 러너들에 등록되어 있지 않다 — unknown command로
        // 즉시 깨지므로 출력 자체를 막는다 (E2).
        if (catalog.Find(segment.DefinitionId)?.MainLaneOnly == true)
        {
            problems.Add(new YarnBundleProblem(
                $"메인 레인 전용 커맨드 '{segment.CommandName ?? segment.DefinitionId}'는 " +
                "Set·Pres 노드에 출력할 수 없습니다.",
                IsBlocking: true,
                segment.Source.LineId));
            return;
        }

        if (segment.Source.LineId is null)
        {
            // Setup은 Set 노드 본문이다 (A2 — 커맨드 전용).
            if (setup is not null)
            {
                YarnSyntax.AppendCommand(setup, segment);
                setup.Append('\n');
            }

            return;
        }

        closeStoryHeader();

        if (pres is not null)
        {
            pres.Append(YarnSyntax.IndentOf(segment.IndentLevel));
            YarnSyntax.AppendCommand(pres, segment);
            pres.Append('\n');
        }
    }

    private static void AppendCondition(StringBuilder builder, RenderedSegment segment, string indent)
    {
        builder.Append(indent);

        switch (segment.Kind)
        {
            case RenderedSegmentKind.ConditionBegin:
                YarnSyntax.AppendCondition(builder, "if", segment.Expression);
                break;
            case RenderedSegmentKind.ConditionElseIf:
                YarnSyntax.AppendCondition(builder, "elseif", segment.Expression);
                break;
            default:
                builder.Append("<<endif>>");
                break;
        }

        builder.Append('\n');
    }

    private static void AppendDialogue(
        RenderedSegment segment,
        StringBuilder story,
        StringBuilder? pres,
        string indent,
        List<YarnBundleProblem> problems)
    {
        if (segment.Text?.Contains("[adv/]", StringComparison.Ordinal) == true)
        {
            // 인라인 동기화 마커는 Phase 0 미지원 — 라인 예산이 어긋난다 (B).
            problems.Add(new YarnBundleProblem(
                $"LineId '{segment.Source.LineId}'의 본문에 [adv/] 마커가 있습니다. " +
                "Phase 0에서는 지원하지 않으며 서브 레인 동기화가 어긋납니다.",
                IsBlocking: false,
                segment.Source.LineId));
        }

        // Story 라인에는 #line: 태그가 필수다 (C1). 없으면 implicit ID가 익스포트마다
        // 바뀌고, 세이브 로드가 조용히 행에 빠진다.
        story.Append(indent);
        YarnSyntax.AppendDialogue(story, segment);

        if (segment.Source.LineId is { Length: > 0 } lineId)
        {
            story.Append(" #line:").Append(lineId);
        }
        else
        {
            problems.Add(new YarnBundleProblem(
                $"대사 라인 '{segment.Text}'에 LineId가 없어 #line: 태그를 만들 수 없습니다.",
                IsBlocking: true));
        }

        story.Append("\n\n");

        if (pres is not null)
        {
            // Pres 사본은 무태그다 — Story와 같은 #line:을 내면 전역 유일성 위반으로
            // 컴파일이 깨진다 (C4). 라인 수·순서는 Story와 같다 (B).
            pres.Append(indent);
            YarnSyntax.AppendDialogue(pres, segment);
            pres.Append("\n\n");
        }
    }

    private static string JumpTargetOf(RenderedSegment segment)
    {
        // 타이틀은 세이브 키이자 에피소드 진입 키다 (C2). 대상 노드의 이름에서
        // 이 이미터가 만들 타이틀(Story_이름)을 그대로 재현한다.
        return "Story_" + YarnSyntax.SanitizeNodeName(
            segment.TargetNodeName ?? segment.TargetNodeId ?? "missing_target");
    }
}
