using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Results;

/// <summary>identity를 붙이기 전의 연출 결과 본문과 검증 결과.</summary>
public sealed class PresentationDraft
{
    public PresentationDraft(
        string sourceNodeId,
        string sourceNodeName,
        DialogueResultReference? source,
        IReadOnlyList<PresentationResultBinding> bindings,
        IReadOnlyList<PublishProblem> problems)
    {
        SourceNodeId = sourceNodeId;
        SourceNodeName = sourceNodeName;
        Source = source;
        Bindings = bindings;
        Problems = problems;
    }

    public string SourceNodeId { get; }

    public string SourceNodeName { get; }

    public DialogueResultReference? Source { get; }

    public IReadOnlyList<PresentationResultBinding> Bindings { get; }

    public IReadOnlyList<PublishProblem> Problems { get; }

    public bool CanPublish => Source is not null && !Problems.Any(problem => problem.IsBlocking);

    public string BlockingSummary() => string.Join(
        Environment.NewLine,
        Problems.Where(problem => problem.IsBlocking).Select(problem => "• " + problem.Message));
}

/// <summary>
/// PresentationNode의 작업 상태를 불변 결과로 얼린다.
///
/// <b>정확한 대사 결과가 없으면 발행하지 않는다.</b> 어느 대사 위에서 만들어졌는지 말할 수 없는
/// 연출표는 나중에 아무 대사에나 붙을 수 있고, 그 어긋남은 최종 출력에서야 드러난다.
///
/// 대상 결과에 없는 LineId의 연출은 <b>지우지 않고</b> orphan으로 표시해 함께 발행한다.
/// 연출가가 쓴 것이 말없이 사라지는 것보다 남아서 눈에 띄는 편이 낫다.
/// </summary>
public static class PresentationPublisher
{
    public static PresentationDraft Draft(StoryProject project, string presentationNodeId)
    {
        ArgumentNullException.ThrowIfNull(project);

        PresentationNode node = project.FindPresentation(presentationNodeId)
            ?? throw new InvalidOperationException($"'{presentationNodeId}'는 연출 노드가 아닙니다.");

        var problems = new List<PublishProblem>();
        DialogueResultReference? source = node.Source;
        DialogueResult? dialogue = null;

        if (source is not { } reference)
        {
            problems.Add(new PublishProblem(
                PublishProblemKind.MissingSourceResult,
                null,
                "읽을 대사 결과를 아직 고르지 않았습니다. 발행된 DialogueResult를 먼저 선택하세요.",
                IsBlocking: true));
        }
        else
        {
            dialogue = project.Results.FindDialogue(reference.ResultId, reference.Version);

            if (dialogue is null)
            {
                problems.Add(new PublishProblem(
                    PublishProblemKind.StaleSourceResult,
                    null,
                    $"대사 결과 '{reference.Label}'을 찾을 수 없습니다.",
                    IsBlocking: true));
            }
            else if (!reference.Matches(dialogue.Identity))
            {
                problems.Add(new PublishProblem(
                    PublishProblemKind.StaleSourceResult,
                    null,
                    $"대사 결과 '{reference.Label}'의 내용 해시가 이 연출이 기억하는 값과 다릅니다.",
                    IsBlocking: true));
            }
        }

        var bindings = new List<PresentationResultBinding>();

        foreach (PresentationLineBinding binding in node.Bindings)
        {
            bool orphan = dialogue is not null && !dialogue.ContainsLine(binding.LineId);

            if (orphan)
            {
                problems.Add(new PublishProblem(
                    PublishProblemKind.OrphanData,
                    binding.LineId,
                    $"LineId '{binding.LineId}'가 대상 대사 결과에 없습니다. " +
                    "연출은 그대로 발행하되 출력에서는 빠집니다.",
                    IsBlocking: false));
            }

            bindings.Add(new PresentationResultBinding(
                binding.LineId,
                binding.Commands
                    .Where(command => command.IsEnabled)
                    .Select(command => new PresentationResultCommand(
                        command.Id,
                        command.DefinitionId,
                        new Dictionary<string, string>(command.Arguments, StringComparer.Ordinal),
                        command.Note))
                    .ToArray(),
                orphan));
        }

        return new PresentationDraft(node.Id, node.Name, source, bindings, problems);
    }

    public static PresentationResult Publish(
        ResultRepository results,
        PresentationDraft draft,
        string resultId,
        DateTimeOffset publishedAt,
        out bool created)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(draft);

        if (!draft.CanPublish)
        {
            throw new InvalidOperationException(
                $"'{draft.SourceNodeName}'을 발행할 수 없습니다.{Environment.NewLine}{draft.BlockingSummary()}");
        }

        string hash = ResultHash.Compute(PresentationResultJson.WriteBody(draft));
        PresentationResult? latest = results.LatestPresentation(resultId);

        if (latest is not null &&
            string.Equals(latest.Identity.ContentHash, hash, StringComparison.Ordinal) &&
            latest.Identity.SchemaVersion == PresentationResult.CurrentSchemaVersion)
        {
            created = false;
            return latest;
        }

        var identity = new ResultIdentity(
            resultId,
            results.NextPresentationVersion(resultId),
            PresentationResult.CurrentSchemaVersion,
            hash);

        var result = new PresentationResult(
            identity,
            draft.SourceNodeId,
            draft.SourceNodeName,
            draft.Source!.Value,
            draft.Bindings,
            publishedAt);

        results.Add(result);
        created = true;
        return result;
    }
}
