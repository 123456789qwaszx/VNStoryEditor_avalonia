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

    public string StoryFileName => YarnBundleEmitter.FileNameOf(YarnBundleEmitter.StoryPrefix, BundleName);

    public string SetFileName => YarnBundleEmitter.FileNameOf(YarnBundleEmitter.SetPrefix, BundleName);

    public string PresFileName => YarnBundleEmitter.FileNameOf(YarnBundleEmitter.PresPrefix, BundleName);

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
        string name = bundleName is not null
            ? YarnSyntax.SanitizeNodeName(bundleName)
            : BundleNameOf(dialogue.SourceNodeName, dialogue.SourceNodeId);

        bool hasLane = presentation is not null;

        // 챕터 네임스페이스 (2026-08-17) — 작가의 아이템·능력은 챕터 단위로만 살아야 하는데
        // Yarn의 변수 저장소는 하나다. 접두가 그 틈을 막는다. A계층 스탯은 그대로 둔다.
        string tier1Prefix = Tier1Namespace.PrefixFor(project, dialogue.SourceNodeId);
        HashSet<string> statNames = Tier1Namespace.StatNames(project, dialogue.SourceNodeId);

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

        // 마커가 있는 라인의 커맨드 버퍼. 커맨드 Segment가 언제나 자기 라인보다 먼저 오므로
        // 리스트 하나면 된다 — 라인을 쓰는 순간 비운다.
        var bufferedLineCommands = new List<RenderedSegment>();

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
                    YarnSyntax.AppendSet(story, segment with
                    {
                        Variable = Tier1Namespace.Apply(segment.Variable ?? string.Empty, tier1Prefix, statNames)
                    });
                    story.Append('\n');
                    break;

                case RenderedSegmentKind.PresentationCommand:
                    // 마커가 있는 라인의 커맨드는 곧바로 쓰지 않고 모아 둔다 —
                    // 그룹 경계에 따라 사본 라인들 사이에 나뉘어 들어가야 한다.
                    if (segment.Source.LineId is { } commandLineId &&
                        presentation?.FindBinding(commandLineId)?.MarkerList.Count > 0)
                    {
                        if (!IsMainLaneOnly(segment, catalog, problems))
                        {
                            CloseStoryHeader();
                            bufferedLineCommands.Add(segment);
                        }

                        break;
                    }

                    AppendPresentationCommand(segment, setup, pres, catalog, problems, CloseStoryHeader);
                    break;

                case RenderedSegmentKind.ConditionBegin:
                case RenderedSegmentKind.ConditionElseIf:
                case RenderedSegmentKind.ConditionEnd:
                    // 조건 구조는 Pres에 그대로 복제한다 (D3). 읽기 전용이고,
                    // 서브 레인은 메인이 지나간 뒤에만 평가하므로 같은 분기를 탄다.
                    CloseStoryHeader();

                    RenderedSegment scoped = segment with
                    {
                        Expression = Tier1Namespace.ApplyToExpression(
                            segment.Expression, tier1Prefix, statNames)
                    };

                    AppendCondition(story, scoped, indent);

                    if (pres is not null)
                    {
                        AppendCondition(pres, scoped, indent);
                    }

                    break;

                case RenderedSegmentKind.ChoiceOption:
                    CloseStoryHeader();
                    AppendChoiceOption(segment, story, pres, hasLane, problems);
                    break;

                case RenderedSegmentKind.ChoiceEnd:
                    CloseStoryHeader();

                    // Story에서 선택 블록은 마지막 옵션 본문이 끝나면 저절로 닫힌다.
                    // Pres 사본의 합성 조건만 명시적으로 닫는다.
                    pres?.Append("<<endif>>\n");
                    break;

                case RenderedSegmentKind.DialogueLine:
                    CloseStoryHeader();
                    AppendDialogue(
                        segment,
                        story,
                        pres,
                        indent,
                        problems,
                        presentation?.FindBinding(segment.Source.LineId)?.MarkerList
                            ?? Array.Empty<PresentationResultMarker>(),
                        bufferedLineCommands);
                    bufferedLineCommands.Clear();
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
            CollectDeclarations(dialogue, definition, hasLane, tier1Prefix, statNames),
            problems);
    }

    /// <summary>선언 파일 이름. 폴더당 하나다.</summary>
    public const string DeclarationsFileName = "declarations.yarn";

    public const string StoryPrefix = "Story_";

    public const string SetPrefix = "Set_";

    public const string PresPrefix = "Pres_";

    /// <summary>
    /// 번들 이름을 만드는 <b>단 하나의 규칙</b>. 이 이름은 파일 이름이자 Yarn 타이틀이고
    /// 곧 세이브 키·에피소드 진입 키다(계약서 C2) — 같은 입력은 언제나 같은 이름이어야 한다.
    /// 이미터·점프 대상 재현·고아 출력 판정이 모두 여기를 지난다(규칙 사본 금지).
    /// </summary>
    public static string BundleNameOf(string? nodeName, string? nodeId)
    {
        string source = string.IsNullOrWhiteSpace(nodeName)
            ? (string.IsNullOrWhiteSpace(nodeId) ? "missing_target" : nodeId)
            : nodeName;

        return YarnSyntax.SanitizeNodeName(source);
    }

    /// <summary>번들 이름 하나가 만들 파일 이름.</summary>
    public static string FileNameOf(string prefix, string bundleName) => $"{prefix}{bundleName}.yarn";

    /// <summary>
    /// 번들 이름 하나가 만들 수 있는 파일 이름 셋. Set·Pres는 연출이 없으면 실제로는
    /// 쓰이지 않지만, "이 이름은 이 노드의 것"을 판정할 때는 셋 다 포함해야 한다.
    /// </summary>
    public static IEnumerable<string> FileNamesOf(string bundleName)
    {
        yield return FileNameOf(StoryPrefix, bundleName);
        yield return FileNameOf(SetPrefix, bundleName);
        yield return FileNameOf(PresPrefix, bundleName);
    }

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
        GameDefinition? definition,
        bool hasLane,
        string tier1Prefix,
        IReadOnlySet<string> statNames)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var declarations = new List<YarnDeclaration>();

        void Collect(string variable)
        {
            string trimmed = variable.TrimStart('$').Trim();

            if (trimmed.Length == 0)
            {
                return;
            }

            // 초기값 타입은 <b>접두 붙이기 전</b> 이름으로 찾는다 — 정의 파일은 작가가
            // 보는 이름을 안다. 선언에 나가는 것은 접두 붙은 이름이다.
            string declared = Tier1Namespace.Apply(trimmed, tier1Prefix, statNames);

            if (seen.Add(declared))
            {
                declarations.Add(new YarnDeclaration(declared, InitialValueOf(trimmed, definition)));
            }
        }

        foreach (DialogueResultAssignment assignment in dialogue.Assignments)
        {
            Collect(assignment.Variable);
        }

        int choiceBlockOrdinal = -1;

        foreach (DialogueResultLine line in dialogue.Lines)
        {
            // 합성 추적 변수도 선언이 필요하다. 블록 서수는 노드마다 0부터라 다른 노드와
            // 이름이 겹치지만, 초기값이 같아(0) 선언 합집합에서 충돌하지 않는다.
            if (hasLane && line.Transition?.Kind == ConditionTransitionKind.BeginChoice)
            {
                choiceBlockOrdinal++;
                Collect($"__ch_{choiceBlockOrdinal}");
            }

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

    /// <summary>
    /// 메인 레인 전용 커맨드인지 (계약서 E2). 서브 러너들에 등록되어 있지 않아
    /// unknown command로 즉시 깨지므로 출력 자체를 막는다.
    /// </summary>
    private static bool IsMainLaneOnly(
        RenderedSegment segment,
        PresentationCommandCatalog catalog,
        List<YarnBundleProblem> problems)
    {
        if (catalog.Find(segment.DefinitionId)?.MainLaneOnly != true)
        {
            return false;
        }

        problems.Add(new YarnBundleProblem(
            $"메인 레인 전용 커맨드 '{segment.CommandName ?? segment.DefinitionId}'는 " +
            "Set·Pres 노드에 출력할 수 없습니다.",
            IsBlocking: true,
            segment.Source.LineId));
        return true;
    }

    private static void AppendPresentationCommand(
        RenderedSegment segment,
        StringBuilder? setup,
        StringBuilder? pres,
        PresentationCommandCatalog catalog,
        List<YarnBundleProblem> problems,
        Action closeStoryHeader)
    {
        if (IsMainLaneOnly(segment, catalog, problems))
        {
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

    /// <summary>
    /// 옵션 라벨 하나를 낸다. 선택은 변수가 아니라 플레이어 입력이므로 Pres 사본이
    /// <c>&lt;&lt;if&gt;&gt;</c>로 그대로 재현할 수 없다 — 그래서 Story가 각 옵션 본문의 첫 문장으로
    /// 합성 추적 변수 <c>$__ch_N</c>을 기록하고, Pres는 그 변수로 같은 갈래를 탄다.
    /// 저장소는 공유이고(D1) 서브 레인은 메인이 본문 첫 라인에 진입한 뒤에만 평가하므로
    /// 그 시점에 set은 이미 실행되어 있다. 시킹 리플레이도 저장된 선택 기록으로 같은 옵션을
    /// 다시 고르므로 결정적이다(C5).
    /// </summary>
    private static void AppendChoiceOption(
        RenderedSegment segment,
        StringBuilder story,
        StringBuilder? pres,
        bool hasLane,
        List<YarnBundleProblem> problems)
    {
        if (segment.ChoiceBlockOrdinal is not { } ordinal || segment.ChoiceOptionIndex is not { } index)
        {
            problems.Add(new YarnBundleProblem(
                $"옵션 라벨 '{segment.Text}'에 블록 서수가 없습니다.",
                IsBlocking: true,
                segment.Source.LineId));
            return;
        }

        // 라벨은 접두 없이 순수 텍스트로 낸다 (계약서 D6 결정 — 런타임은 라벨을 원문 그대로 렌더한다).
        // 미리보기 태그(D5)는 표시 전용이고, 실제 효과는 뒤따르는 본문의 <<set>>이다.
        // 조건 갈래 안 선택지(W54)는 라벨부터 감싼 깊이만큼 들여쓴다 — 옵션 소속은
        // 들여쓰기가 문법이다.
        string labelIndent = YarnSyntax.IndentOf(segment.IndentLevel);
        story.Append(labelIndent).Append("-> ").Append(segment.Text ?? string.Empty);

        foreach (string tag in segment.Tags ?? Array.Empty<string>())
        {
            story.Append(' ').Append(tag);
        }

        if (segment.Source.LineId is { Length: > 0 } lineId)
        {
            story.Append(" #line:").Append(lineId);
        }
        else
        {
            problems.Add(new YarnBundleProblem(
                $"옵션 라벨 '{segment.Text}'에 LineId가 없어 #line: 태그를 만들 수 없습니다.",
                IsBlocking: true));
        }

        story.Append('\n');

        if (hasLane)
        {
            story.Append(labelIndent).Append(YarnSyntax.Indent)
                .Append("<<set $__ch_").Append(ordinal)
                .Append(" = ").Append(index)
                .Append(">>\n");
        }

        if (pres is not null)
        {
            // 라벨 라인은 advance를 소비하지 않으므로(계약서 B) Pres에 사본을 만들지 않는다.
            // 합성 조건만 낸다. 갈래별 본문 라인 수는 같은 결과에서 나오므로 일치한다.
            pres.Append(labelIndent)
                .Append(index == 0 ? "<<if $__ch_" : "<<elseif $__ch_")
                .Append(ordinal)
                .Append(" == ")
                .Append(index)
                .Append(">>\n");
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
        List<YarnBundleProblem> problems,
        IReadOnlyList<PresentationResultMarker> markers,
        IReadOnlyList<RenderedSegment> bufferedCommands)
    {
        string text = segment.Text ?? string.Empty;

        if (text.Contains("[adv/]", StringComparison.Ordinal))
        {
            // 본문에 직접 입력한 마커는 동기화 그룹이 없다 — 라인 예산이 어긋난다 (B).
            problems.Add(new YarnBundleProblem(
                $"LineId '{segment.Source.LineId}'의 본문에 직접 입력한 [adv/] 마커가 있습니다. " +
                "연출 바인딩의 마커 기능을 사용해야 서브 레인 예산이 맞습니다.",
                IsBlocking: false,
                segment.Source.LineId));
        }

        string[] parts = SplitByMarkers(text, markers);

        // Story 라인에는 #line: 태그가 필수다 (C1). 없으면 implicit ID가 익스포트마다
        // 바뀌고, 세이브 로드가 조용히 행에 빠진다. 마커는 본문 오프셋 위치에 삽입된다.
        // 마커명은 `adv` 고정 — InlineAdvanceManifest.DefaultMarkerName.
        story.Append(indent);

        if (!string.IsNullOrWhiteSpace(segment.Speaker))
        {
            story.Append(segment.Speaker).Append(": ");
        }

        story.Append(string.Join("[adv/]", parts));

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

        // 갈래(선택지·조건) 안의 줄은 붙여 낸다 (W47) — 옵션 본문 사이에 빈 줄이 섞이면
        // 블록이 흩어져 보인다. 숨(빈 줄)은 갈래 밖 일반 흐름에서만 준다.
        story.Append(segment.IndentLevel > 0 ? "\n" : "\n\n");

        if (pres is null)
        {
            return;
        }

        // Pres 사본은 무태그다 — Story와 같은 #line:을 내면 전역 유일성 위반으로
        // 컴파일이 깨진다 (C4). 마커가 있으면 이 라인 자리에 1 + 마커 수 개의 라인을 내
        // 라인 예산(B: 대사 라인 + [adv/] 마커 수)을 정확히 맞춘다. 그룹 k의 커맨드가
        // k번째 사본 라인 앞에 붙는다.
        for (int part = 0; part < parts.Length; part++)
        {
            int groupStart = part == 0 ? 0 : ClampIndex(markers[part - 1].FirstCommandIndex, bufferedCommands.Count);
            int groupEnd = part < markers.Count
                ? ClampIndex(markers[part].FirstCommandIndex, bufferedCommands.Count)
                : bufferedCommands.Count;

            for (int index = groupStart; index < Math.Max(groupStart, groupEnd); index++)
            {
                pres.Append(indent);
                YarnSyntax.AppendCommand(pres, bufferedCommands[index]);
                pres.Append('\n');
            }

            pres.Append(indent);

            if (part == 0 && !string.IsNullOrWhiteSpace(segment.Speaker))
            {
                pres.Append(segment.Speaker).Append(": ");
            }

            // 사본 라인의 문구는 표시되지 않는 동기화 앵커다. 빈 조각은 자리 표시로 채운다.
            pres.Append(string.IsNullOrWhiteSpace(parts[part]) ? "…" : parts[part]);
            pres.Append("\n\n");
        }
    }

    private static string[] SplitByMarkers(string text, IReadOnlyList<PresentationResultMarker> markers)
    {
        if (markers.Count == 0)
        {
            return new[] { text };
        }

        var parts = new string[markers.Count + 1];
        int previous = 0;

        for (int index = 0; index < markers.Count; index++)
        {
            int offset = Math.Clamp(markers[index].CharacterOffset, previous, text.Length);
            parts[index] = text[previous..offset];
            previous = offset;
        }

        parts[markers.Count] = text[previous..];
        return parts;
    }

    private static int ClampIndex(int value, int count) => Math.Clamp(value, 0, count);

    private static string JumpTargetOf(RenderedSegment segment)
    {
        // 타이틀은 세이브 키이자 에피소드 진입 키다 (C2). 대상 노드의 이름에서
        // 이 이미터가 만들 타이틀(Story_이름)을 그대로 재현한다 — 규칙은 BundleNameOf 하나다.
        return StoryPrefix + BundleNameOf(segment.TargetNodeName, segment.TargetNodeId);
    }
}
