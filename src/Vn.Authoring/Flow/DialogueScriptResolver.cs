using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Flow;

/// <summary>
/// 대본의 줄 하나와 그 줄에 얹힌 대사 논리를 합쳐 놓은 <b>읽기 전용</b> 투영.
///
/// 저장되지 않는다. 화면·흐름 계산·발행이 모두 이 하나를 본다. 각자 대본과 확장 데이터를
/// 따로 합치면 어느 쪽이 맞는지 알 수 없는 순간이 생긴다.
/// </summary>
public sealed record DialogueLine(
    int Index,
    string LineId,
    int Revision,
    string Speaker,
    string Text,
    LineConditionTransition? Transition);

/// <summary>대본에 더 이상 없는 LineId에 남아 있는 대사 논리.</summary>
/// <param name="IsRetired">대본에서 사라진 줄인지. false면 대본 자체에 없는 Id다.</param>
public sealed record OrphanLineExtension(
    DialogueLineExtension Extension,
    bool IsRetired,
    LocalizedLine? LastKnownText);

/// <summary>
/// DialogueNode를 열었을 때 실제로 보이는 것.
/// </summary>
public sealed class DialogueScript
{
    public static DialogueScript Empty { get; } = new(
        null,
        ScriptDocument.DefaultLocale,
        false,
        Array.Empty<DialogueLine>(),
        Array.Empty<OrphanLineExtension>());

    public DialogueScript(
        string? scriptId,
        string locale,
        bool isResolved,
        IReadOnlyList<DialogueLine> lines,
        IReadOnlyList<OrphanLineExtension> orphans)
    {
        ScriptId = scriptId;
        Locale = locale;
        IsResolved = isResolved;
        Lines = lines;
        Orphans = orphans;
    }

    /// <summary>노드가 읽겠다고 적어 둔 대본 Id. 그 대본이 실제로 있는지는 별개다.</summary>
    public string? ScriptId { get; }

    public string Locale { get; }

    /// <summary>적어 둔 대본을 프로젝트에서 실제로 찾았는지.</summary>
    public bool IsResolved { get; }

    /// <summary>대본 순서 그대로. 은퇴한 줄은 들어 있지 않다.</summary>
    public IReadOnlyList<DialogueLine> Lines { get; }

    /// <summary>대본에서 사라진 줄에 남은 대사 논리. 자동으로 지우지 않는다.</summary>
    public IReadOnlyList<OrphanLineExtension> Orphans { get; }

    public bool HasScript => ScriptId is not null && IsResolved;

    public DialogueLine? Find(string? lineId)
    {
        return lineId is null
            ? null
            : Lines.FirstOrDefault(line => string.Equals(line.LineId, lineId, StringComparison.Ordinal));
    }
}

/// <summary>
/// DialogueNode가 읽는 대본과 그 노드의 확장 데이터를 합친다.
///
/// <b>합성 방향은 언제나 대본 → 화면이다.</b> 화면이 본 것을 대본으로 되돌려 쓰지 않는다.
/// 대본에 없는 LineId의 확장 데이터는 버리지 않고 고아로 알린다.
/// </summary>
public static class DialogueScriptResolver
{
    public static DialogueScript Resolve(
        StoryProject project,
        DialogueNode node,
        string? locale = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(node);

        ScriptDocument? document = project.FindScript(node.ScriptId);

        if (document is null)
        {
            // 대본을 아직 고르지 않았거나 대본이 사라졌다. 확장 데이터는 전부 고아다.
            return new DialogueScript(
                node.ScriptId,
                locale ?? ScriptDocument.DefaultLocale,
                isResolved: false,
                Array.Empty<DialogueLine>(),
                node.LineExtensions
                    .Select(extension => new OrphanLineExtension(extension, IsRetired: false, null))
                    .ToArray());
        }

        string targetLocale = locale ?? document.PrimaryLocale;
        var extensions = new Dictionary<string, DialogueLineExtension>(StringComparer.Ordinal);

        foreach (DialogueLineExtension extension in node.LineExtensions)
        {
            extensions.TryAdd(extension.LineId, extension);
        }

        var lines = new List<DialogueLine>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        int index = 0;

        foreach (ScriptLine scriptLine in document.ActiveLines)
        {
            LocalizedLine text = document.Text(scriptLine.Id, targetLocale);
            extensions.TryGetValue(scriptLine.Id, out DialogueLineExtension? extension);
            used.Add(scriptLine.Id);

            lines.Add(new DialogueLine(
                index++,
                scriptLine.Id,
                scriptLine.Revision,
                text.Speaker,
                text.Text,
                extension?.Transition));
        }

        var orphans = new List<OrphanLineExtension>();

        foreach (DialogueLineExtension extension in node.LineExtensions)
        {
            if (used.Contains(extension.LineId))
            {
                continue;
            }

            ScriptLine? retired = document.FindLine(extension.LineId);
            orphans.Add(new OrphanLineExtension(
                extension,
                retired is not null,
                retired is null ? null : document.Text(extension.LineId, targetLocale)));
        }

        return new DialogueScript(document.Id, targetLocale, isResolved: true, lines, orphans);
    }
}
