using Vn.Authoring.Flow;
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
        IReadOnlyList<PresentationResultCommand> setupCommands,
        IReadOnlyList<PresentationResultBinding> bindings,
        IReadOnlyList<PublishProblem> problems)
    {
        SourceNodeId = sourceNodeId;
        SourceNodeName = sourceNodeName;
        Source = source;
        SetupCommands = setupCommands;
        Bindings = bindings;
        Problems = problems;
    }

    public string SourceNodeId { get; }

    public string SourceNodeName { get; }

    public DialogueResultReference? Source { get; }

    public IReadOnlyList<PresentationResultCommand> SetupCommands { get; }

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

        // 프리셋 범위는 연결된 공급 노드가 정한다 (AvailableConditionResolver와 같은 원칙).
        AvailablePresentationCommands availableCommands =
            AvailablePresentationCommandResolver.Resolve(project, node.Id);

        PresentationResultCommand[] setupCommands = node.SetupCommands
            .Where(command => command.IsEnabled)
            .Select(command => Freeze(command, project, availableCommands, problems))
            .ToArray();

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
                    .Select(command => Freeze(command, project, availableCommands, problems, binding.LineId))
                    .ToArray(),
                orphan));
        }

        return new PresentationDraft(node.Id, node.Name, source, setupCommands, bindings, problems);
    }

    /// <summary>
    /// 커맨드 하나를 <b>해석된 최종 값</b>으로 얼린다. 프리셋 참조는 결과에 남지 않는다 —
    /// 프리셋이 커맨드 정의와 인자를 공급하고, 인스턴스의 인자가 그 위를 덮는다.
    /// 프리셋을 나중에 고치거나 지워도 발행된 결과는 불변이다.
    /// </summary>
    private static PresentationResultCommand Freeze(
        PresentationCommandInstance command,
        StoryProject project,
        AvailablePresentationCommands available,
        List<PublishProblem> problems,
        string? lineId = null)
    {
        string definitionId = command.DefinitionId;
        var arguments = new Dictionary<string, string>(StringComparer.Ordinal);

        if (command.PresetId is { } presetId)
        {
            AvailablePreset? preset = available.FindPreset(presetId);

            if (preset is null)
            {
                AvailablePreset? known = AvailablePresentationCommandResolver.FindKnown(project, presetId);

                problems.Add(new PublishProblem(
                    PublishProblemKind.UnknownPreset,
                    lineId,
                    known is null
                        ? $"커맨드가 참조하는 프리셋 '{presetId}'를 찾을 수 없습니다. 프리셋이 삭제되었을 수 있습니다."
                        : $"프리셋 '{known.DisplayName}'은 이 연출 노드에 연결된 공급 노드에 없습니다.",
                    IsBlocking: true));
            }
            else
            {
                definitionId = preset.Preset.CommandDefinitionId;

                foreach ((string key, string value) in preset.Preset.ArgumentValues)
                {
                    arguments[key] = value;
                }
            }
        }

        foreach ((string key, string value) in command.Arguments)
        {
            arguments[key] = value;
        }

        return new PresentationResultCommand(
            command.Id,
            definitionId,
            arguments,
            command.Note);
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
            draft.SetupCommands,
            draft.Bindings,
            publishedAt);

        results.Add(result);
        created = true;
        return result;
    }
}
