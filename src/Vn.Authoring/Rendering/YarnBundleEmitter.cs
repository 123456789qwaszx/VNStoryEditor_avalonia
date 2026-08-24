using Ked.Presentation.Core;
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
/// 합성 하나에서 나온 <b>대본 하나</b>. 파일로 쓰기 전의 순수 문자열이다.
///
/// <b>2026-08-18까지는 트리오(Story/Set/Pres)였다.</b> 런타임이 세 레인을 읽고 동기화하고
/// 그것을 다시 롤백에 반영하는 값을 치르지 않기로 하면서(소유자: "디버깅비용이 최소
/// 10배 이상"), <b>합치는 쪽이 이 도구</b>가 됐다 — 런타임은 단일 대본만 읽는다.
/// 줄에 붙던 연출은 이제 Story 본문 안에서 자기 대사 줄 바로 앞에 선다.
/// </summary>
public sealed class YarnBundle
{
    public YarnBundle(
        string bundleName,
        string storyText,
        IReadOnlyList<YarnDeclaration> declarations,
        IReadOnlyList<YarnBundleProblem> problems,
        string? sourceNodeName = null,
        string? sourceNodeId = null)
    {
        BundleName = bundleName;
        StoryText = storyText;
        Declarations = declarations;
        Problems = problems;
        SourceNodeName = sourceNodeName ?? bundleName;
        SourceNodeId = sourceNodeId ?? string.Empty;
    }

    public string BundleName { get; }

    /// <summary>
    /// 이 번들을 낸 대사 노드의 <b>화면 이름</b>. 번들 이름은 그것을 sanitize한 값이라
    /// 되짚을 수 없다(`장면 1`·`장면.1`이 둘 다 `장면_1`이 된다) — 이름이 겹쳤을 때
    /// <b>어느 노드인지</b> 말해 주려고 들고 다닌다 (2026-08-25).
    /// </summary>
    public string SourceNodeName { get; }

    /// <summary>겹친 노드가 같은 이름일 때 사람이 구별할 마지막 열쇠.</summary>
    public string SourceNodeId { get; }

    public string StoryText { get; }

    /// <summary>이 번들이 쓰는 변수와 초기값. 선언 파일 합집합의 재료다.</summary>
    public IReadOnlyList<YarnDeclaration> Declarations { get; }

    public IReadOnlyList<YarnBundleProblem> Problems { get; }

    public bool HasBlockingProblems => Problems.Any(problem => problem.IsBlocking);

    /// <summary>이 번들이 사는 챕터(판 이름). 모르면 빈 문자열이고 파일 이름에 안 붙는다.</summary>
    public string ChapterId { get; init; } = string.Empty;

    public string StoryFileName =>
        YarnBundleEmitter.FileNameOf(YarnBundleEmitter.StoryPrefix, BundleName, ChapterId);

    /// <summary>파일 이름 → 내용. 이제 하나다.</summary>
    public IEnumerable<(string FileName, string Text)> Files
    {
        get { yield return (StoryFileName, StoryText); }
    }

    public string BlockingSummary() => string.Join(
        Environment.NewLine,
        Problems.Where(problem => problem.IsBlocking).Select(problem => "• " + problem.Message));
}

/// <summary>
/// 발행 결과 조합 하나를 ked-presentation-runtime이 재생하는 <b>대본 하나</b>로 조립한다.
/// <c>runtime-contract.md</c>가 이 클래스의 사양이다.
///
/// - 레인은 없다 (2026-08-18) — Story 노드 하나에 대사·조건·선택지·연출이 모두 선다.
/// - 연출 커맨드는 자기 대사 줄 <b>바로 앞</b>에 인라인으로 놓인다.
/// - <c>#line:</c> 태그는 계속 단다 — 런타임은 요구하지 않지만(§C1) 이쪽의 열쇠다.
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

        // 카탈로그는 이제 이미터가 보지 않는다 (2026-08-18) — 레인이 하나라
        // `mainLaneOnly` 검사가 가릴 대상을 잃었고, 그 검사가 유일한 소비자였다.
        string name = bundleName is not null
            ? YarnSyntax.SanitizeNodeName(bundleName)
            : BundleNameOf(dialogue.SourceNodeName, dialogue.SourceNodeId);

        // 챕터 네임스페이스 (2026-08-17) — 작가의 아이템·능력은 챕터 단위로만 살아야 하는데
        // Yarn의 변수 저장소는 하나다. 접두가 그 틈을 막는다. A계층 스탯은 그대로 둔다.
        string tier1Prefix = Tier1Namespace.PrefixFor(project, dialogue.SourceNodeId);
        HashSet<string> statNames = Tier1Namespace.StatNames(project, dialogue.SourceNodeId);

        var problems = new List<YarnBundleProblem>();
        var story = new StringBuilder();

        // 커스텀 곡선 참조 검증 (W67 후속) — `@이름`이 프로젝트에 없으면 막는다.
        // 런타임은 모르는 곡선을 로그만 남기고 OutCubic으로 굴리므로(조용한 어긋남),
        // 저작이 유일한 방어다. "채우면 반드시 동작한다"의 곡선판이다.
        ValidateEaseCurveReferences(presentation, project, definition, problems);

        // ── 헤더 ────────────────────────────────────────────────────────────
        story.Append("title: ").Append(StoryTitleOf(name)).Append("\n---\n");

        // Story 서두: 변수 초기화(set) → 노드 셋업 커맨드.
        // 헤더가 닫히는 시점은 첫 본문 Segment를 만났을 때다.
        bool storyHeaderClosed = false;

        void CloseStoryHeader()
        {
            if (storyHeaderClosed)
            {
                return;
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
                    AppendPresentationCommand(segment, story, indent, CloseStoryHeader);
                    break;

                case RenderedSegmentKind.ConditionBegin:
                case RenderedSegmentKind.ConditionElseIf:
                case RenderedSegmentKind.ConditionEnd:
                    CloseStoryHeader();

                    AppendCondition(story, segment with
                    {
                        Expression = Tier1Namespace.ApplyToExpression(
                            segment.Expression, tier1Prefix, statNames)
                    }, indent);

                    break;

                case RenderedSegmentKind.ChoiceOption:
                    CloseStoryHeader();
                    AppendChoiceOption(segment, story, problems);
                    break;

                case RenderedSegmentKind.ChoiceEnd:
                    // 선택 블록은 마지막 옵션 본문이 끝나면 저절로 닫힌다 — 낼 것이 없다.
                    CloseStoryHeader();
                    break;

                case RenderedSegmentKind.DialogueLine:
                    CloseStoryHeader();
                    AppendDialogue(segment, story, indent, problems);
                    break;

                case RenderedSegmentKind.BranchJump:
                case RenderedSegmentKind.DefaultJump:
                    CloseStoryHeader();
                    story.Append(indent);
                    YarnSyntax.AppendJump(story, JumpTargetOf(segment));
                    story.Append('\n');
                    break;

                case RenderedSegmentKind.BranchDetour:
                    // 조건 갈래의 커스텀 씬 — 재생하고 갈래로 돌아온다.
                    CloseStoryHeader();
                    story.Append(indent);
                    YarnSyntax.AppendDetour(story, JumpTargetOf(segment));
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

        return new YarnBundle(
            name,
            story.ToString(),
            CollectDeclarations(dialogue, definition, tier1Prefix, statNames),
            problems,
            dialogue.SourceNodeName,
            dialogue.SourceNodeId)
        {
            ChapterId = ChapterOf(project, dialogue.SourceNodeId)
        };
    }

    /// <summary>
    /// 겹친 번들 하나를 사람이 판에서 찾을 수 있게 적는다 — 노드 이름 · Id, 그리고
    /// <b>비었는지</b>. 대개 한쪽이 빈 노드(개명·재동기화가 남긴 것)라 그 표시가 곧 답이다.
    /// </summary>
    private static string Describe(YarnBundle bundle) =>
        $"  · 노드 '{bundle.SourceNodeName}' ({bundle.SourceNodeId})" +
        (bundle.StoryText.Contains("#line:", StringComparison.Ordinal)
            ? string.Empty
            : "  ← 재생할 줄이 없습니다(빈 노드)");

    /// <summary>선언 파일 이름. 폴더당 하나다.</summary>
    public const string DeclarationsFileName = "declarations.yarn";

    /// <summary>
    /// ⛔ <b><c>Story_</c> 접두는 2026-08-24에 폐지됐다</b> (소유자, 런타임과 함께 —
    /// 저쪽도 같은 날 <c>Story_*</c> 필터를 걷었다).
    ///
    /// 접두가 <b>두 번 붙고 있었다</b>: 기획자가 챕터 시트의 `대사엔트리`에 이미
    /// <c>Story_ch05_01</c>이라 적는데 이미터가 또 붙여 <c>Story_Story_ch05_01</c>이 나갔다.
    /// 견본 여섯 줄이 전부 그랬다. 이제 <b>타이틀은 곧 대사엔트리</b>다(정규화만 거친다).
    ///
    /// 이 상수를 되살릴 일이 있으면 <see cref="StoryTitleOf"/> 한 곳만 고치면 된다 —
    /// 파일 이름·타이틀·점프 대상·진행 JSON의 <c>DialogueEntryId</c>가 전부 그 길을 지난다.
    /// </summary>
    public const string StoryPrefix = "";

    /// <summary>
    /// 본문이 빈 대사 줄의 자리를 채우는 <b>보이지 않는 한 글자</b> (U+00A0, 2026-08-25).
    ///
    /// 빈 본문을 그대로 내면 <c>#line:</c> 태그만 남은 줄이 되어 Yarn이 파싱하지 못하고,
    /// 줄을 빼면 그 줄에 매달린 연출 커맨드가 박스를 닫지 못해 <b>조용히 버려진다</b>.
    /// 보통 공백이 아닌 <b>no-break space</b>인 이유가 그것이다 — 일반 공백은 트림에
    /// 걷혀 다시 빈 줄이 된다.
    /// </summary>
    public const string EmptyLineBody = " ";

    // ⚠ Set·Pres는 <b>남은 이름</b>이다 — 지금은 그 파일을 만들지 않는다. 옛 폴더에
    // 남아 있는 파일을 고아로 알아보는 데만 쓴다(`FileNamesOf`·`LooksLikeOutput`).
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

    /// <summary>
    /// 번들 이름 하나가 가질 Story 타이틀.
    ///
    /// <b>접두를 붙이는 자리는 여기 한 곳이다</b> — 지금은 붙이는 것이 없다(2026-08-24에
    /// <c>Story_</c>가 폐지됐다, <see cref="StoryPrefix"/>). 함수를 남겨 두는 이유는
    /// 그 결정이 <b>한 자리에 머물게</b> 하기 위해서다: 접두가 다시 필요해지면 여기만
    /// 고치고, 바깥에서 문자열을 손으로 잇는 자리는 새로 만들지 않는다.
    /// </summary>
    public static string StoryTitleOf(string bundleName) => StoryPrefix + bundleName;

    /// <summary>
    /// 이 이미터가 <b>그 이름의 노드에 붙일 Story 타이틀</b>. 이름 → 번들 이름 → 타이틀
    /// 두 단계를 한 번에 통과한다.
    ///
    /// ⚠ <b>규칙은 접두 하나가 아니다</b> — <c>Story_</c> 앞에
    /// <see cref="YarnSyntax.SanitizeNodeName"/>가 있다(`장면 1` → `Story_장면_1`). 그래서
    /// 바깥에서 <c>"Story_" + 이름</c>으로 흉내 내면 공백·점·하이픈이 든 이름에서 갈린다.
    ///
    /// <b>진행 내보내기의 <c>DialogueEntryId</c>가 이 함수를 지나야 한다</b> — 런타임은 이
    /// 글자로 <c>YarnProject</c>에서 노드를 찾는다. 2026-08-23에 그 자리만 이 규칙 밖에
    /// 있어서 진행 JSON은 `new01`, yarn은 `Story_new01`로 갈렸다
    /// (`docs/work-orders/dialogue-entry-naming-orders.md`).
    /// </summary>
    public static string StoryNodeTitleOf(string? nodeName, string? nodeId = null) =>
        StoryTitleOf(BundleNameOf(nodeName, nodeId));

    /// <summary>
    /// 번들 하나가 만들 파일 이름.
    ///
    /// <b>챕터를 아는 자리면 파일 이름 앞에 붙인다</b> (2026-08-25 소유자: "각 에피소드가
    /// 어떤 챕터의 것인지 구분이 불가능한데"). 산출 폴더는 챕터를 섞어 담으므로, 이름에
    /// 없으면 사람이 파일을 열어 보기 전에는 알 방법이 없다.
    ///
    /// ⚠ <b>Yarn 노드 타이틀은 안 건드린다.</b> 런타임은 그 글자로 노드를 찾고 진행 JSON의
    /// <c>DialogueEntryId</c>가 같은 것을 부르므로, 파일 이름만 바꿔야 유니티 쪽이 그대로
    /// 돌아간다 — 폴더를 나누지 않는 이유도 같다(YarnProject의 포함 규칙을 안 건드린다).
    /// </summary>
    public static string FileNameOf(string prefix, string bundleName, string? chapterId = null) =>
        chapterId is { Length: > 0 } chapter
            ? $"{prefix}{YarnSyntax.SanitizeNodeName(chapter)}_{bundleName}.yarn"
            : $"{prefix}{bundleName}.yarn";

    /// <summary>
    /// 이 노드가 사는 판의 이름 = 챕터 Id (챕터=판 1:1). 판을 못 찾으면 빈 문자열이다 —
    /// 그때는 챕터를 모르는 것이므로 이름에 아무것도 안 붙인다.
    /// </summary>
    public static string ChapterOf(StoryProject? project, string? nodeId) =>
        project is null || nodeId is null
            ? string.Empty
            : project.Files.FirstOrDefault(file =>
                file.Nodes.Any(node => string.Equals(node.Id, nodeId, StringComparison.Ordinal)))
                ?.Name ?? string.Empty;

    /// <summary>
    /// 번들 이름 하나가 만들 수 있는 파일 이름 셋. Set·Pres는 연출이 없으면 실제로는
    /// 쓰이지 않지만, "이 이름은 이 노드의 것"을 판정할 때는 셋 다 포함해야 한다.
    /// </summary>
    public static IEnumerable<string> FileNamesOf(string bundleName, string? chapterId = null)
    {
        foreach (string prefix in new[] { StoryPrefix, SetPrefix, PresPrefix })
        {
            yield return FileNameOf(prefix, bundleName, chapterId);

            // ⚠ 챕터 없는 이름도 함께 센다 (2026-08-25). 이름에 챕터를 붙이기 전에 나간
            //    파일들이 폴더에 남아 있는데, 그것을 고아로 세면 <b>멀쩡한 산출물이
            //    갑자기 지울 것 목록에 오른다</b>. 옛 이름은 옛 이름대로 그 노드의 것이다.
            if (chapterId is { Length: > 0 })
            {
                yield return FileNameOf(prefix, bundleName);
            }
        }
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

    /// <summary>
    /// 연출 커맨드의 ease 인자 중 <c>@이름</c> 참조를 프로젝트 곡선 목록과 대조한다.
    /// 어느 인자가 ease인지는 카탈로그가 정한다 — 이름으로 추측하지 않는다.
    /// </summary>
    private static void ValidateEaseCurveReferences(
        PresentationResult? presentation,
        StoryProject? project,
        GameDefinition? definition,
        List<YarnBundleProblem> problems)
    {
        if (presentation is null)
        {
            return;
        }

        PresentationCommandCatalog catalog = PresentationCommandCatalog.For(definition);

        // 이름 → 곡선. <b>종류(이동/진동)까지 봐야 한다</b> — 판정 규칙은 코어
        // CurveKindRules 하나이고 런타임 로더도 그것을 쓴다(2026-08-21 회신).
        // 한쪽만 알면 "툴에서는 저장되는데 재생에서는 조용히 사라지는" 곡선이 생긴다.
        Dictionary<string, Model.EaseCurve> known =
            (project?.EaseCurves ?? Enumerable.Empty<Model.EaseCurve>())
                .GroupBy(curve => curve.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        void Check(string? lineId, PresentationResultCommand command)
        {
            if (catalog.Find(command.DefinitionId) is not { } commandDefinition)
            {
                return;
            }

            foreach (PresentationCommandParameter parameter in commandDefinition.Parameters)
            {
                if (KindFor(parameter.Type) is not { } wanted ||
                    !command.Arguments.TryGetValue(parameter.Name, out string? value) ||
                    !value.StartsWith('@'))
                {
                    continue;
                }

                if (!known.TryGetValue(value[1..], out Model.EaseCurve? curve))
                {
                    problems.Add(new YarnBundleProblem(
                        $"곡선 '{value}'이 프로젝트에 없습니다 — 런타임은 이 커맨드를 기본값으로 조용히 재생하게 됩니다. " +
                        "곡선을 만들거나 커맨드의 곡선을 고치세요.",
                        IsBlocking: true,
                        lineId));
                    continue;
                }

                if (!CurveKindRules.TryClassify(curve.Keys.ToArray(), out CurveKind kind, out string? why))
                {
                    problems.Add(new YarnBundleProblem(
                        $"곡선 '{value}'은 런타임이 받지 않습니다({why}) — " +
                        "이동 곡선은 1로, 진동 곡선은 0으로 끝나야 합니다.",
                        IsBlocking: true,
                        lineId));
                    continue;
                }

                if (kind != wanted)
                {
                    problems.Add(new YarnBundleProblem(
                        $"곡선 '{value}'은 {KindName(kind)} 곡선인데 " +
                        $"{commandDefinition.OutputCommandName}의 '{parameter.Name}'은 " +
                        $"{KindName(wanted)} 곡선을 받습니다 — " +
                        "런타임은 이 곡선을 못 찾은 것으로 치고 기본값으로 재생합니다.",
                        IsBlocking: true,
                        lineId));
                }
            }
        }

        foreach (PresentationResultCommand command in presentation.SetupCommands)
        {
            Check(lineId: null, command);
        }

        foreach (PresentationResultBinding binding in presentation.Bindings)
        {
            foreach (PresentationResultCommand command in binding.Commands)
            {
                Check(binding.LineId, command);
            }
        }
    }

    /// <summary>
    /// 이 파라미터가 요구하는 곡선 종류. <c>ease</c> = 이동 · <c>oscillation</c> = 진동 ·
    /// 그 밖 = null(곡선 칸이 아니다).
    /// </summary>
    private static CurveKind? KindFor(string parameterType) =>
        string.Equals(parameterType, "ease", StringComparison.Ordinal) ? CurveKind.Motion
        : string.Equals(parameterType, "oscillation", StringComparison.Ordinal) ? CurveKind.Oscillation
        : null;

    private static string KindName(CurveKind kind) =>
        kind == CurveKind.Oscillation ? "진동" : "이동";

    /// <summary>
    /// 프로젝트의 커스텀 곡선을 런타임 스키마(ease-curves/1 — 배열 + name, 그쪽 확정 회신)로
    /// 번들 폴더에 쓴다. 곡선이 없으면 파일을 만들지 않는다 — 없는 파일은 런타임이
    /// 무음으로 커브 0개 처리하는 정상 경로다. 결정적 출력: BOM 없는 UTF-8 · LF.
    /// </summary>
    public static string? WriteCurves(IReadOnlyList<Model.EaseCurve> curves, string directory)
    {
        ArgumentNullException.ThrowIfNull(curves);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        List<Model.EaseCurve> named = curves
            .Where(curve => Model.EaseCurve.IsValidName(curve.Name))
            .OrderBy(curve => curve.Name, StringComparer.Ordinal)
            .ToList();

        if (named.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.Append("{\n  \"schema\": \"ease-curves/1\",\n  \"curves\": [\n");

        for (int index = 0; index < named.Count; index++)
        {
            Model.EaseCurve curve = named[index];
            builder.Append("    { \"name\": \"").Append(curve.Name).Append("\",\n      \"keys\": [\n");

            for (int keyIndex = 0; keyIndex < curve.Keys.Count; keyIndex++)
            {
                Ked.Presentation.Core.CurveKey key = curve.Keys[keyIndex];
                builder.Append("        { \"t\": ").Append(Compact(key.Time))
                    .Append(", \"v\": ").Append(Compact(key.Value))
                    .Append(", \"inTangent\": ").Append(Compact(key.InTangent))
                    .Append(", \"outTangent\": ").Append(Compact(key.OutTangent))
                    .Append(" }")
                    .Append(keyIndex < curve.Keys.Count - 1 ? ",\n" : "\n");
            }

            builder.Append("      ] }").Append(index < named.Count - 1 ? ",\n" : "\n");
        }

        builder.Append("  ]\n}\n");

        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "curves.json");
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, builder.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
        return path;

        // 재덤프 결정성: 지수 표기 없이 최단 왕복 표기(G9와 같은 성질의 "R" 대신,
        // .NET Core의 최단 왕복이 기본이라 그대로 쓴다).
        static string Compact(float value) =>
            value.ToString("0.####################", System.Globalization.CultureInfo.InvariantCulture);
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

        // ⚠ 겹친 <b>노드를 지목한다</b> (2026-08-25). 이름만 알려 주면 찾을 수가 없다 —
        //    번들 이름은 노드 이름을 sanitize한 값이라 되짚을 수 없고(`장면 1`과 `장면.1`이
        //    둘 다 `장면_1`이 된다), 애초에 노드 이름은 식별자가 아니라 겹칠 수 있다.
        //
        //    이 자리가 갑자기 터지기 시작한 사정이 있다: 재생할 줄이 없는 노드는 예전에
        //    발행에서 막혀 산출 목록에서 통째로 빠졌고(2026-08-25에 완화했다), 그래서
        //    그 노드가 낀 중복은 <b>보이지 않았다</b>. 개명이나 재동기화가 남긴 빈 노드가
        //    판에 하나 더 있으면 여기서 처음 드러난다.
        IGrouping<string, YarnBundle>? duplicate = bundles
            .GroupBy(bundle => bundle.BundleName, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"번들 이름 '{duplicate.Key}'이 겹칩니다. 같은 폴더에서 서로를 덮어쓰고, " +
                "Yarn 타이틀도 겹쳐 런타임이 둘을 구별하지 못합니다." + Environment.NewLine +
                string.Join(Environment.NewLine, duplicate.Select(Describe)) +
                Environment.NewLine +
                "판에서 한쪽의 이름을 바꾸거나, 안 쓰는 노드면 지워 주세요.");
        }

        // ⛔ 접두가 없어진 뒤로(2026-08-24) 번들 파일이 <b>선언 파일과 같은 이름</b>이 될
        // 수 있다 — 대사엔트리를 `declarations`라고 적으면 그렇다. 예전에는 `Story_`가
        // 막아 주던 자리다. 덮어쓰면 선언이 통째로 사라지고, 런타임은 되돌릴 초기값을
        // 잃는다(그 사고는 조용하다).
        string? collision = bundles
            .SelectMany(bundle => bundle.Files.Select(file => file.FileName))
            .FirstOrDefault(name =>
                string.Equals(name, DeclarationsFileName, StringComparison.OrdinalIgnoreCase));

        if (collision is not null)
        {
            throw new InvalidOperationException(
                $"대사엔트리 이름이 선언 파일과 겹칩니다: '{collision}'. " +
                "그 노드의 `대사엔트리`를 다른 이름으로 바꿔 주세요.");
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

        // 합성 추적 변수(`__ch_N`) 선언은 2026-08-18에 사라졌다 — 서브 레인이 없다.
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

    /// <summary>
    /// 연출 커맨드 하나를 Story 본문에 낸다 (2026-08-18 — 단일 대본).
    ///
    /// <b>레인이 사라져 갈 곳이 하나가 됐다.</b> 예전에는 LineId 없는 커맨드는 Set 노드
    /// (원샷 레인), 줄에 붙은 커맨드는 Pres 노드(서브 레인)로 갈렸다. 런타임이 세 레인을
    /// 읽고 동기화하고 그것을 다시 롤백에 반영하는 값을 치르지 않기로 하면서
    /// (소유자: "디버깅비용이 최소 10배"), <b>합치는 쪽이 이 도구</b>가 됐다.
    ///
    /// - LineId 없음 = 노드 셋업 → 헤더 자리(첫 본문 앞)에 그대로 선다
    /// - LineId 있음 = 그 줄의 연출 → 세그먼트 순서상 자기 대사 줄 <b>바로 앞</b>에 선다
    ///
    /// 메인 레인 전용 검사는 없앴다 — 이제 전부 메인 레인이라 가릴 것이 없다.
    /// </summary>
    private static void AppendPresentationCommand(
        RenderedSegment segment,
        StringBuilder story,
        string indent,
        Action closeStoryHeader)
    {
        if (segment.Source.LineId is not null)
        {
            closeStoryHeader();
            story.Append(indent);
        }

        YarnSyntax.AppendCommand(story, segment);
        story.Append('\n');
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
        List<YarnBundleProblem> problems)
    {

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

        // 합성 추적 변수(`$__ch_N`)는 2026-08-18에 사라졌다 — 서브 레인 사본이 같은
        // 갈래를 타게 하려고 두었던 것이라, 레인이 없어지자 쓸 곳이 없다.
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
        string indent,
        List<YarnBundleProblem> problems)
    {
        string text = segment.Text ?? string.Empty;

        // ⛔ 본문이 비면 `#line:` 태그만 남은 줄이 나가고, <b>Yarn이 그것을 파싱하지 못한다</b>
        //    (2026-08-25). 그렇다고 줄을 빼면 더 나쁘다 — 박스는 <b>재생되는 라인이
        //    나타나야 닫히므로</b>, 그 줄에 매달린 연출 커맨드가 어느 박스도 닫지 못하고
        //    런타임에서 통째로 버려진다. 화면에는 아무 일도 안 일어나고 로그도 없다.
        //
        //    그래서 줄은 남기되 본문에 <b>보이지 않는 한 글자</b>(U+00A0)를 세운다. 순수
        //    연출 노드(대사박스를 끄고 커맨드만 돌리는 씬)가 바로 이 모양이라, 이것이
        //    막아야 할 오류가 아니라 <b>정상 경로</b>다.
        //
        //    ⚠ 다만 박스를 켜 둔 채로 본문을 비우면 빈 말풍선이 뜬다 — 그건 저작 실수일
        //    수 있으므로 아래에서 경고로 짚는다(막지는 않는다).
        bool empty = text.Length == 0 && string.IsNullOrWhiteSpace(segment.Speaker);

        story.Append(indent);

        if (!string.IsNullOrWhiteSpace(segment.Speaker))
        {
            story.Append(segment.Speaker).Append(": ");
        }

        if (empty)
        {
            story.Append(EmptyLineBody);

            problems.Add(new YarnBundleProblem(
                "대사가 비어 있는 줄이 있습니다 — 보이지 않는 한 글자로 채워 냅니다. " +
                "연출만 돌리는 줄이면 정상이고(대사박스를 꺼 두세요), 아니라면 대사를 적어 주세요.",
                IsBlocking: false,
                segment.Source.LineId));
        }
        else
        {
            story.Append(text);
        }

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
        // 이 이미터가 만들 타이틀(Story_이름)을 그대로 재현한다 — 규칙은 StoryNodeTitleOf 하나다.
        return StoryNodeTitleOf(segment.TargetNodeName, segment.TargetNodeId);
    }
}
