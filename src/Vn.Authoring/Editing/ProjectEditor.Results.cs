using Vn.Authoring.Definition;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Editing;

/// <summary>발행하려 했지만 검증에 걸린 경우. 문제 목록을 그대로 들고 있다.</summary>
public sealed class PublishRejectedException : InvalidOperationException
{
    public PublishRejectedException(string message, IReadOnlyList<PublishProblem> problems)
        : base(message)
    {
        Problems = problems;
    }

    public IReadOnlyList<PublishProblem> Problems { get; }
}

/// <summary>발행 결과와 그것이 새로 만들어졌는지.</summary>
/// <param name="Created">false면 같은 내용이라 기존 버전을 그대로 돌려준 것이다.</param>
public readonly record struct PublishOutcome<TResult>(TResult Result, bool Created);

/// <summary>
/// 연출 채널 확보의 결과. <see cref="Presentation"/>이 null이면 <see cref="Problem"/>이
/// 이유를 말한다(대사가 발행 불가 상태 등).
/// </summary>
public sealed record PresentationChannelOutcome(PresentationNode? Presentation, string? Problem)
{
    public bool Ready => Presentation is not null;
}

/// <summary>
/// 작업 상태를 불변 결과로 발행하고, 결과끼리 짝지어 두는 편집 명령.
/// </summary>
public sealed partial class ProjectEditor
{
    /// <summary>
    /// 이 대사 노드를 발행한다. 같은 노드를 여러 번 발행하면 같은 계보의 다음 버전이 된다.
    /// 내용이 바뀌지 않았으면 새 버전을 만들지 않는다.
    /// </summary>
    public PublishOutcome<DialogueResult> PublishDialogue(
        string dialogueNodeId,
        GameDefinition? definition = null,
        string? locale = null)
    {
        DialogueNode node = RequireDialogue(dialogueNodeId);
        DialogueDraft draft = DialoguePublisher.Draft(Project, node.Id, definition, locale);

        if (!draft.CanPublish)
        {
            throw new PublishRejectedException(
                $"'{node.Name}'을 발행할 수 없습니다.{Environment.NewLine}{draft.BlockingSummary()}",
                draft.Problems);
        }

        // 이 노드가 이미 발행한 적이 있으면 그 계보를 잇는다. 발행할 때마다 새 계보를 만들면
        // v1을 읽던 연출이 v2가 그것의 후속이라는 사실을 알 수 없다.
        string resultId = Project.Results.DialogueResultsOf(node.Id).LastOrDefault()
            ?.Identity.ResultId ?? Identifier.Result();

        DialogueResult result = null!;
        bool created = false;

        Mutate(ProjectChangeKind.Results, () =>
            result = DialoguePublisher.Publish(
                Project.Results,
                draft,
                resultId,
                _now(),
                out created));

        return new PublishOutcome<DialogueResult>(result, created);
    }

    /// <summary>발행 전에 무엇이 막고 있는지 본다. 아무것도 바꾸지 않는다.</summary>
    public DialogueDraft InspectDialoguePublish(
        string dialogueNodeId,
        GameDefinition? definition = null,
        string? locale = null)
    {
        return DialoguePublisher.Draft(Project, dialogueNodeId, definition, locale);
    }

    /// <summary>
    /// 연출 노드가 읽을 대사 결과를 정한다.
    /// 버전을 명시적으로 고르게 한다. "최신"으로 두면 같은 연출이 어제와 오늘 다른 대사를 읽는다.
    /// </summary>
    public void SetPresentationSource(string presentationNodeId, string? resultId, int version = 0)
    {
        PresentationNode node = RequirePresentation(presentationNodeId);

        if (resultId is null)
        {
            if (node.Source is null)
            {
                return;
            }

            Mutate(ProjectChangeKind.Connections, () => node.Source = null);
            return;
        }

        DialogueResult result = Project.Results.FindDialogue(resultId, version)
            ?? throw new InvalidOperationException($"대사 결과 '{resultId} v{version}'을 찾을 수 없습니다.");

        var reference = DialogueResultReference.Of(result);

        if (node.Source == reference)
        {
            return;
        }

        // binding은 손대지 않는다. 새 결과에 없는 LineId는 고아가 되고, 그 사실은
        // PresentationBindingResolver가 계산해서 알린다. 자동으로 지우지 않는다.
        Mutate(ProjectChangeKind.Connections, () => node.Source = reference);
    }

    public PublishOutcome<PresentationResult> PublishPresentation(string presentationNodeId)
    {
        PresentationNode node = RequirePresentation(presentationNodeId);
        PresentationDraft draft = PresentationPublisher.Draft(Project, node.Id);

        if (!draft.CanPublish)
        {
            throw new PublishRejectedException(
                $"'{node.Name}'을 발행할 수 없습니다.{Environment.NewLine}{draft.BlockingSummary()}",
                draft.Problems);
        }

        string resultId = Project.Results.PresentationResultsOf(node.Id).LastOrDefault()
            ?.Identity.ResultId ?? Identifier.Result();

        PresentationResult result = null!;
        bool created = false;

        Mutate(ProjectChangeKind.Results, () =>
            result = PresentationPublisher.Publish(
                Project.Results,
                draft,
                resultId,
                _now(),
                out created));

        return new PublishOutcome<PresentationResult>(result, created);
    }

    public PresentationDraft InspectPresentationPublish(string presentationNodeId)
    {
        return PresentationPublisher.Draft(Project, presentationNodeId);
    }

    /// <summary>
    /// 대사 노드 하나의 <b>연출 채널</b>을 확보한다 (2026-08-21 소유자 — "미리 다 해둔
    /// 다음에 무대 프리뷰측에서 뭘 할지 고르도록"). 예전에 사람이 그래프에서 하던
    /// 네 단계(대사 발행 → 연출 노드 만들기 → 발행 결과 연결 → 공급 연결)를 한 번에,
    /// 몇 번을 불러도 같은 채널로 한다:
    ///
    /// ① 대사를 발행한다 — 내용이 같으면 새 버전이 안 생긴다(고정용 데이터는 그대로 산다).
    ///    발행 불가(막는 문제)인데 이전 발행본도 없으면 채널을 못 세우고 이유를 돌려준다.
    /// ② 이 노드에 공급 중인 연출 노드를 찾고, 없으면 만들어 공급 연결까지 잇는다.
    /// ③ 연출의 입력을 <b>최신 대사 발행본으로 고정</b>한다 — 낡았으면 갱신한다.
    ///    binding은 손대지 않으므로 사라진 LineId는 고아로 표시된다(자동 삭제 금지).
    /// ④ 연출도 발행해 둔다 — 내보내기 짝(NodeExportResolver)이 늘 신선하다.
    ///
    /// 연출 노드는 더 이상 그래프에 그려지지 않는다(배관) — 이 채널이 유일한 입구다.
    /// </summary>
    public PresentationChannelOutcome EnsurePresentationChannel(
        string dialogueNodeId,
        GameDefinition? definition = null)
    {
        if (Project.FindDialogue(dialogueNodeId) is not { } node)
        {
            return new PresentationChannelOutcome(null, $"'{dialogueNodeId}'는 대사 노드가 아닙니다.");
        }

        // ① 대사 고정 — 발행할 수 있으면 발행하고(같은 내용이면 no-op), 못 하면
        // 이전 발행본 위에서라도 연출을 계속한다(없으면 여기서 멈춘다).
        DialogueDraft draft = DialoguePublisher.Draft(Project, node.Id, definition);
        DialogueResult? latest;

        if (draft.CanPublish)
        {
            latest = PublishDialogue(node.Id, definition).Result;
        }
        else
        {
            latest = Project.Results.DialogueResultsOf(node.Id).LastOrDefault();

            if (latest is null)
            {
                return new PresentationChannelOutcome(
                    null,
                    $"'{node.Name}'의 대사를 고정할 수 없습니다: {draft.BlockingSummary()}");
            }
        }

        // ② 공급 중인 연출 노드 — 내보내기와 같은 규칙으로 찾는다(사본 금지).
        PresentationNode? presentation =
            NodeExportResolver.SupplyLinkOf(Project, node.Id) is { } supply
                ? Project.FindPresentation(supply.SourceNodeId)
                : null;

        if (presentation is null)
        {
            string fileId = Project.FindFileContainingNode(node.Id)!.Id;
            presentation = AddPresentationNode(
                fileId,
                node.Layout.X,
                node.Layout.Y + 240,
                name: $"{node.Name} 연출");
            SetPresentationSupplyTarget(presentation.Id, node.Id);
        }

        // ③ 최신 발행본으로 고정 — 같은 참조면 SetPresentationSource가 no-op이다.
        SetPresentationSource(presentation.Id, latest.Identity.ResultId, latest.Identity.Version);

        // ④ 연출 발행 — 내용이 같으면 새 버전이 안 생긴다. 발행 불가면 조용히 넘어간다:
        // 채널 자체는 섰고, 문제는 편집 화면의 기존 진단이 말한다.
        PresentationDraft presentationDraft = PresentationPublisher.Draft(Project, presentation.Id);

        if (presentationDraft.CanPublish)
        {
            PublishPresentation(presentation.Id);
        }

        return new PresentationChannelOutcome(presentation, null);
    }

    // ── RuntimeComposition ──────────────────────────────────────────────────

    /// <summary>
    /// 대사 결과 하나와 (있다면) 연출 결과 하나를 짝짓는다.
    /// 호환되지 않는 조합은 만들지 않는다. 만들 수 있게 두면 저장 파일에 이미 어긋난 참조가
    /// 들어간 뒤이고, 그때는 어느 쪽이 잘못됐는지 말하기 어렵다.
    /// </summary>
    public RuntimeComposition AddComposition(
        string dialogueResultId,
        int dialogueResultVersion,
        string? presentationResultId = null,
        int presentationResultVersion = 0,
        string? name = null,
        string? locale = null)
    {
        DialogueResult dialogue =
            Project.Results.FindDialogue(dialogueResultId, dialogueResultVersion)
            ?? throw new InvalidOperationException(
                $"대사 결과 '{dialogueResultId} v{dialogueResultVersion}'을 찾을 수 없습니다.");

        var composition = new RuntimeComposition(name: name ?? NextName("합성"))
        {
            DialogueResultId = dialogueResultId,
            DialogueResultVersion = dialogueResultVersion,
            PresentationResultId = presentationResultId,
            PresentationResultVersion = presentationResultVersion,
            Locale = locale ?? dialogue.Locale
        };

        RequireCompatible(composition);
        Mutate(ProjectChangeKind.Results, () => Project.Compositions.Add(composition));
        return composition;
    }

    public void SetCompositionPresentation(
        string compositionId,
        string? presentationResultId,
        int presentationResultVersion = 0)
    {
        RuntimeComposition composition = Project.FindComposition(compositionId)
            ?? throw new InvalidOperationException($"합성 '{compositionId}'을 찾을 수 없습니다.");

        RuntimeComposition candidate = composition.Clone();
        candidate.PresentationResultId = presentationResultId;
        candidate.PresentationResultVersion = presentationResultVersion;
        RequireCompatible(candidate);

        Mutate(ProjectChangeKind.Results, () =>
        {
            composition.PresentationResultId = presentationResultId;
            composition.PresentationResultVersion = presentationResultVersion;
        });
    }

    public void RenameComposition(string compositionId, string name)
    {
        if (Project.FindComposition(compositionId) is { } composition &&
            !string.Equals(composition.Name, name, StringComparison.Ordinal))
        {
            Mutate(ProjectChangeKind.NodeMetadata, () => composition.Name = name);
        }
    }

    public void RemoveComposition(string compositionId)
    {
        if (Project.FindComposition(compositionId) is { } composition)
        {
            Mutate(ProjectChangeKind.Results, () => Project.Compositions.Remove(composition));
        }
    }

    private void RequireCompatible(RuntimeComposition composition)
    {
        ResolvedComposition resolved = RuntimeCompositionResolver.Resolve(Project.Results, composition);

        if (!resolved.IsCompatible)
        {
            throw new InvalidOperationException(
                "호환되지 않는 결과 조합입니다." + Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    resolved.BlockingProblems.Select(problem => "• " + problem.Message)));
        }
    }
}
