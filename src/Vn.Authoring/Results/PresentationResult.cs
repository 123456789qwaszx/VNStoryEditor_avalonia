namespace Vn.Authoring.Results;

/// <summary>발행된 연출 명령 하나. 인자는 게임이 해석하는 중립 문자열이다.</summary>
public sealed record PresentationResultCommand(
    string CommandId,
    string DefinitionId,
    IReadOnlyDictionary<string, string> Arguments,
    string? Note = null);

/// <summary>
/// LineId 하나에 붙은 연출. 목록 순서가 곧 실행·출력 순서다.
/// </summary>
/// <param name="IsOrphan">
/// 대상 DialogueResult에 이 LineId가 없다. 자동으로 지우지 않고 그대로 발행한다.
/// 지워 버리면 연출가가 쓴 것이 말없이 사라지고, 왜 사라졌는지 물을 수도 없다.
/// </param>
public sealed record PresentationResultBinding(
    string LineId,
    IReadOnlyList<PresentationResultCommand> Commands,
    bool IsOrphan);

/// <summary>
/// PresentationNode의 작업 상태를 얼린 <b>불변</b> 결과.
///
/// 어떤 DialogueResult 위에서 만들어졌는지를 Id·Version·Hash로 함께 적는다.
/// 이 기록이 없으면 나중에 "이 연출표가 어느 대사에 맞는 것인가"에 답할 방법이 없다.
/// </summary>
public sealed class PresentationResult
{
    /// <remarks>v2: LineId 없는 노드 수준 Setup 커맨드가 실린다.</remarks>
    public const int CurrentSchemaVersion = 2;

    public PresentationResult(
        ResultIdentity identity,
        string sourceNodeId,
        string sourceNodeName,
        DialogueResultReference source,
        IReadOnlyList<PresentationResultCommand> setupCommands,
        IReadOnlyList<PresentationResultBinding> bindings,
        DateTimeOffset publishedAt)
    {
        Identity = identity;
        SourceNodeId = sourceNodeId;
        SourceNodeName = sourceNodeName;
        Source = source;
        SetupCommands = setupCommands;
        Bindings = bindings;
        PublishedAt = publishedAt;
    }

    public ResultIdentity Identity { get; }

    public string SourceNodeId { get; }

    public string SourceNodeName { get; }

    /// <summary>이 연출이 읽은 DialogueResult. 합성할 때 이것과 정확히 맞는지 검사한다.</summary>
    public DialogueResultReference Source { get; }

    /// <summary>장면 준비용 노드 수준 커맨드. 이미터에서 Set_ 노드 본문이 된다.</summary>
    public IReadOnlyList<PresentationResultCommand> SetupCommands { get; }

    public IReadOnlyList<PresentationResultBinding> Bindings { get; }

    public DateTimeOffset PublishedAt { get; }

    public PresentationResultBinding? FindBinding(string? lineId)
    {
        return lineId is null
            ? null
            : Bindings.FirstOrDefault(
                binding => string.Equals(binding.LineId, lineId, StringComparison.Ordinal));
    }

    public IEnumerable<PresentationResultBinding> Orphans =>
        Bindings.Where(binding => binding.IsOrphan);
}
