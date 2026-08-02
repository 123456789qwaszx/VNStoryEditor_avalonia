namespace Vn.Authoring.Results;

/// <summary>
/// 프로젝트가 발행한 모든 결과의 보관소.
///
/// <b>추가만 가능하다.</b> 이미 들어온 결과를 바꾸거나 지우는 API는 없다. Undo가 스냅샷 전체를
/// 되돌려 결과가 목록에서 빠질 수는 있지만, 남아 있는 결과의 내용이 바뀌는 일은 없다.
///
/// 결과는 그래프 노드가 아니라 별도 보관소에 둔다. 발행할 때마다 그래프에 카드가 하나씩
/// 쌓이면 몇 번만 발행해도 작업 표면이 결과 카드로 덮인다. 그래프에는 현재 어느 결과를
/// 읽고 있는지만 뱃지로 보여 준다.
/// </summary>
public sealed class ResultRepository
{
    private readonly List<DialogueResult> _dialogue = new();
    private readonly List<PresentationResult> _presentation = new();

    /// <summary>발행한 순서 그대로. 저장 파일의 순서이기도 하다.</summary>
    public IReadOnlyList<DialogueResult> DialogueResults => _dialogue;

    public IReadOnlyList<PresentationResult> PresentationResults => _presentation;

    public bool IsEmpty => _dialogue.Count == 0 && _presentation.Count == 0;

    public DialogueResult? FindDialogue(string? resultId, int version)
    {
        return resultId is null
            ? null
            : _dialogue.FirstOrDefault(result =>
                string.Equals(result.Identity.ResultId, resultId, StringComparison.Ordinal) &&
                result.Identity.Version == version);
    }

    public PresentationResult? FindPresentation(string? resultId, int version)
    {
        return resultId is null
            ? null
            : _presentation.FirstOrDefault(result =>
                string.Equals(result.Identity.ResultId, resultId, StringComparison.Ordinal) &&
                result.Identity.Version == version);
    }

    public DialogueResult? LatestDialogue(string? resultId)
    {
        return resultId is null
            ? null
            : _dialogue
                .Where(result => string.Equals(result.Identity.ResultId, resultId, StringComparison.Ordinal))
                .MaxBy(result => result.Identity.Version);
    }

    public PresentationResult? LatestPresentation(string? resultId)
    {
        return resultId is null
            ? null
            : _presentation
                .Where(result => string.Equals(result.Identity.ResultId, resultId, StringComparison.Ordinal))
                .MaxBy(result => result.Identity.Version);
    }

    /// <summary>이 DialogueNode가 지금까지 발행한 결과. 버전 오름차순이다.</summary>
    public IEnumerable<DialogueResult> DialogueResultsOf(string nodeId)
    {
        return _dialogue
            .Where(result => string.Equals(result.SourceNodeId, nodeId, StringComparison.Ordinal))
            .OrderBy(result => result.Identity.Version);
    }

    public IEnumerable<PresentationResult> PresentationResultsOf(string nodeId)
    {
        return _presentation
            .Where(result => string.Equals(result.SourceNodeId, nodeId, StringComparison.Ordinal))
            .OrderBy(result => result.Identity.Version);
    }

    /// <summary>이 계보의 다음 버전 번호. 아직 없으면 1이다.</summary>
    public int NextDialogueVersion(string resultId)
    {
        return _dialogue
            .Where(result => string.Equals(result.Identity.ResultId, resultId, StringComparison.Ordinal))
            .Select(result => result.Identity.Version)
            .DefaultIfEmpty(0)
            .Max() + 1;
    }

    public int NextPresentationVersion(string resultId)
    {
        return _presentation
            .Where(result => string.Equals(result.Identity.ResultId, resultId, StringComparison.Ordinal))
            .Select(result => result.Identity.Version)
            .DefaultIfEmpty(0)
            .Max() + 1;
    }

    public void Add(DialogueResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (FindDialogue(result.Identity.ResultId, result.Identity.Version) is not null)
        {
            throw new InvalidOperationException(
                $"DialogueResult '{result.Identity.Label}'이 이미 있습니다. 발행 결과는 덮어쓰지 않습니다.");
        }

        _dialogue.Add(result);
    }

    public void Add(PresentationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (FindPresentation(result.Identity.ResultId, result.Identity.Version) is not null)
        {
            throw new InvalidOperationException(
                $"PresentationResult '{result.Identity.Label}'이 이미 있습니다. 발행 결과는 덮어쓰지 않습니다.");
        }

        _presentation.Add(result);
    }

    /// <summary>
    /// 얕은 복사로 충분하다. 결과는 불변이므로 같은 인스턴스를 여러 스냅샷이 공유해도 안전하다.
    /// </summary>
    public ResultRepository Clone()
    {
        var clone = new ResultRepository();
        clone._dialogue.AddRange(_dialogue);
        clone._presentation.AddRange(_presentation);
        return clone;
    }
}
