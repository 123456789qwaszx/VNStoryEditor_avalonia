using Vn.Authoring.Model;

namespace Vn.Authoring.Results;

/// <summary>
/// 정확한 DialogueResult 하나와 (있다면) PresentationResult 하나를 골라 둔 조합.
///
/// 대사와 연출은 서로를 소유하지 않는다. 둘을 잇는 자리는 여기 하나뿐이다.
/// <code>
/// DialogueResult        PresentationResult
///        ▲                      ▲
///        └──── RuntimeComposition ────┘
/// </code>
///
/// 이 객체가 저장되기 때문에 "그때 그 Runtime Full을 다시 만들어 줘"에 답할 수 있다.
/// 버전을 적지 않고 "최신"이라고만 두면 같은 조합이 어제와 오늘 다른 것을 뜻하게 된다.
/// </summary>
public sealed class RuntimeComposition
{
    public RuntimeComposition(string? id = null, string name = "새 합성")
    {
        Id = id ?? Identifier.Composition();
        Name = name;
    }

    public string Id { get; }

    public string Name { get; set; }

    public string DialogueResultId { get; set; } = string.Empty;

    public int DialogueResultVersion { get; set; }

    public string? PresentationResultId { get; set; }

    public int PresentationResultVersion { get; set; }

    public string Locale { get; set; } = Script.ScriptDocument.DefaultLocale;

    public bool HasPresentation => !string.IsNullOrEmpty(PresentationResultId);

    public RuntimeComposition Clone()
    {
        return new RuntimeComposition(Id, Name)
        {
            DialogueResultId = DialogueResultId,
            DialogueResultVersion = DialogueResultVersion,
            PresentationResultId = PresentationResultId,
            PresentationResultVersion = PresentationResultVersion,
            Locale = Locale
        };
    }
}

public enum CompositionProblemKind
{
    /// <summary>고른 DialogueResult가 보관소에 없다.</summary>
    MissingDialogueResult,

    /// <summary>고른 PresentationResult가 보관소에 없다.</summary>
    MissingPresentationResult,

    /// <summary>PresentationResult가 읽은 대사 결과가 지금 고른 것과 다르다.</summary>
    SourceMismatch,

    /// <summary>Id와 버전은 같은데 내용 해시가 다르다. 파일이 손으로 바뀌었을 수 있다.</summary>
    ContentHashMismatch,

    /// <summary>결과의 스키마 버전을 이 도구가 모른다.</summary>
    UnsupportedSchema,

    /// <summary>대상 결과에 없는 LineId에 연출이 붙어 있다. 합성은 막지 않는다.</summary>
    OrphanBinding
}

public sealed record CompositionProblem(
    CompositionProblemKind Kind,
    string Message,
    bool IsBlocking);

/// <summary>
/// 조합이 가리키는 실제 결과와 그 호환성 판정.
/// <see cref="IsCompatible"/>이 false인 조합은 정식 출력으로 만들지 않는다.
/// </summary>
public sealed class ResolvedComposition
{
    public ResolvedComposition(
        RuntimeComposition composition,
        DialogueResult? dialogue,
        PresentationResult? presentation,
        IReadOnlyList<CompositionProblem> problems)
    {
        Composition = composition;
        Dialogue = dialogue;
        Presentation = presentation;
        Problems = problems;
    }

    public RuntimeComposition Composition { get; }

    public DialogueResult? Dialogue { get; }

    public PresentationResult? Presentation { get; }

    public IReadOnlyList<CompositionProblem> Problems { get; }

    public IEnumerable<CompositionProblem> BlockingProblems =>
        Problems.Where(problem => problem.IsBlocking);

    public bool IsCompatible => Dialogue is not null && !Problems.Any(problem => problem.IsBlocking);

    public string ProblemSummary() =>
        string.Join(" / ", Problems.Select(problem => problem.Message));
}

/// <summary>
/// 조합이 실제로 성립하는지 확인한다.
///
/// <b>서로 다른 대사 결과 위에서 만들어진 연출을 정상 Runtime Full처럼 합성하지 않는다.</b>
/// 그렇게 만든 문서는 겉보기에 멀쩡하지만 대사와 연출이 어긋나 있고, 그 사실을 아무도 모른다.
/// </summary>
public static class RuntimeCompositionResolver
{
    public static ResolvedComposition Resolve(ResultRepository results, RuntimeComposition composition)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(composition);

        var problems = new List<CompositionProblem>();

        DialogueResult? dialogue = results.FindDialogue(
            composition.DialogueResultId,
            composition.DialogueResultVersion);

        if (dialogue is null)
        {
            problems.Add(new CompositionProblem(
                CompositionProblemKind.MissingDialogueResult,
                $"대사 결과 '{composition.DialogueResultId} v{composition.DialogueResultVersion}'을 찾을 수 없습니다.",
                IsBlocking: true));
        }
        else if (dialogue.Identity.SchemaVersion > DialogueResult.CurrentSchemaVersion)
        {
            problems.Add(new CompositionProblem(
                CompositionProblemKind.UnsupportedSchema,
                $"대사 결과 '{dialogue.Identity.Label}'의 스키마 버전 " +
                $"{dialogue.Identity.SchemaVersion}은 이 도구가 읽을 수 없습니다.",
                IsBlocking: true));
        }

        PresentationResult? presentation = null;

        if (composition.HasPresentation)
        {
            presentation = results.FindPresentation(
                composition.PresentationResultId,
                composition.PresentationResultVersion);

            if (presentation is null)
            {
                problems.Add(new CompositionProblem(
                    CompositionProblemKind.MissingPresentationResult,
                    $"연출 결과 '{composition.PresentationResultId} v{composition.PresentationResultVersion}'을 " +
                    "찾을 수 없습니다.",
                    IsBlocking: true));
            }
            else if (presentation.Identity.SchemaVersion > PresentationResult.CurrentSchemaVersion)
            {
                problems.Add(new CompositionProblem(
                    CompositionProblemKind.UnsupportedSchema,
                    $"연출 결과 '{presentation.Identity.Label}'의 스키마 버전 " +
                    $"{presentation.Identity.SchemaVersion}은 이 도구가 읽을 수 없습니다.",
                    IsBlocking: true));
            }
        }

        if (dialogue is not null && presentation is not null)
        {
            problems.AddRange(CheckCompatibility(dialogue, presentation));
        }

        return new ResolvedComposition(composition, dialogue, presentation, problems);
    }

    /// <summary>연출 결과가 이 대사 결과 위에서 만들어진 것인지.</summary>
    public static IReadOnlyList<CompositionProblem> CheckCompatibility(
        DialogueResult dialogue,
        PresentationResult presentation)
    {
        ArgumentNullException.ThrowIfNull(dialogue);
        ArgumentNullException.ThrowIfNull(presentation);

        var problems = new List<CompositionProblem>();
        DialogueResultReference source = presentation.Source;

        bool sameLineage =
            string.Equals(source.ResultId, dialogue.Identity.ResultId, StringComparison.Ordinal) &&
            source.Version == dialogue.Identity.Version;

        if (!sameLineage)
        {
            problems.Add(new CompositionProblem(
                CompositionProblemKind.SourceMismatch,
                $"연출 결과 '{presentation.Identity.Label}'은 대사 결과 '{source.Label}' 위에서 만들어졌습니다. " +
                $"지금 고른 것은 '{dialogue.Identity.Label}'입니다.",
                IsBlocking: true));
        }
        else if (!string.Equals(source.ContentHash, dialogue.Identity.ContentHash, StringComparison.Ordinal))
        {
            problems.Add(new CompositionProblem(
                CompositionProblemKind.ContentHashMismatch,
                $"대사 결과 '{dialogue.Identity.Label}'의 내용 해시가 연출이 기억하는 값과 다릅니다. " +
                "결과 파일이 도구 밖에서 바뀌었을 수 있습니다.",
                IsBlocking: true));
        }

        foreach (PresentationResultBinding binding in presentation.Bindings)
        {
            if (!dialogue.ContainsLine(binding.LineId))
            {
                problems.Add(new CompositionProblem(
                    CompositionProblemKind.OrphanBinding,
                    $"연출이 붙은 LineId '{binding.LineId}'가 대사 결과에 없습니다. " +
                    "그 줄의 연출은 출력에서 빠지지만 데이터는 지우지 않습니다.",
                    IsBlocking: false));
            }
        }

        return problems;
    }
}
