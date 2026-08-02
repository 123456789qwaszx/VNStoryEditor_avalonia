using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Flow;

/// <param name="Line">입력 결과에서 찾은 줄. 고아면 null이다.</param>
public sealed record ResolvedPresentationBinding(
    PresentationLineBinding Binding,
    DialogueResultLine? Line,
    bool IsOrphan);

/// <summary>연출 노드가 편집 화면에서 보는 것: 읽기 전용 입력 결과와 줄별 binding.</summary>
public sealed class PresentationWorkspace
{
    public PresentationWorkspace(
        DialogueResultReference? source,
        DialogueResult? dialogue,
        bool isStale,
        IReadOnlyList<ResolvedPresentationBinding> bindings)
    {
        Source = source;
        Dialogue = dialogue;
        IsStale = isStale;
        Bindings = bindings;
    }

    /// <summary>이 노드가 읽겠다고 적어 둔 결과.</summary>
    public DialogueResultReference? Source { get; }

    /// <summary>실제로 찾은 결과. null이면 보관소에 없다.</summary>
    public DialogueResult? Dialogue { get; }

    /// <summary>결과는 찾았지만 내용 해시가 기억하는 값과 다르다.</summary>
    public bool IsStale { get; }

    public IReadOnlyList<ResolvedPresentationBinding> Bindings { get; }

    public IEnumerable<ResolvedPresentationBinding> Orphans =>
        Bindings.Where(binding => binding.IsOrphan);

    public bool HasSource => Dialogue is not null && !IsStale;
}

/// <summary>
/// 연출 노드의 LineId binding이 지금 읽는 대사 결과에서 유효한지 계산한다.
///
/// orphan 여부는 저장하지 않는다. 입력 결과를 바꿀 때마다 다시 계산한다.
/// 저장하면 결과를 바꾼 순간부터 그 값이 거짓말을 하기 시작한다.
/// </summary>
public static class PresentationBindingResolver
{
    public static DialogueResult? ResolveSource(StoryProject project, string presentationNodeId)
    {
        ArgumentNullException.ThrowIfNull(project);

        PresentationNode? node = project.FindPresentation(presentationNodeId);

        return node?.Source is { } source
            ? project.Results.FindDialogue(source.ResultId, source.Version)
            : null;
    }

    public static PresentationWorkspace Resolve(StoryProject project, PresentationNode presentation)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(presentation);

        DialogueResult? dialogue = presentation.Source is { } source
            ? project.Results.FindDialogue(source.ResultId, source.Version)
            : null;
        bool stale = dialogue is not null &&
            presentation.Source is { } reference &&
            !reference.Matches(dialogue.Identity);

        var bindings = presentation.Bindings
            .Select(binding =>
            {
                DialogueResultLine? line = dialogue?.FindLine(binding.LineId);
                return new ResolvedPresentationBinding(binding, line, IsOrphan: line is null);
            })
            .ToList();

        return new PresentationWorkspace(presentation.Source, dialogue, stale, bindings);
    }
}
