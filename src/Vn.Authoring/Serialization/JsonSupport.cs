using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vn.Authoring.Model;
using Vn.Authoring.Results;
using Vn.Authoring.Script;

namespace Vn.Authoring.Serialization;

internal static class JsonSupport
{
    internal static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static JsonObject ParseObject(string json, string kind)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidDataException($"{kind} 파일의 루트가 JSON 객체가 아닙니다.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{kind} 파일을 읽을 수 없습니다. {exception.Message}", exception);
        }
    }

    public static string ToDeterministicText(JsonObject root)
    {
        return root.ToJsonString(WriteOptions)
            .Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public static void WriteAtomic(string path, string text)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = fullPath + ".tmp";

        try
        {
            File.WriteAllText(temporary, EnsureTrailingLf(text), new UTF8Encoding(false));
            File.Move(temporary, fullPath, overwrite: true);
        }
        catch
        {
            // 교체에 실패한 임시 파일이 다음 저장을 방해하지 않게 정리한다.
            try
            {
                File.Delete(temporary);
            }
            catch
            {
                // 원래 저장 예외를 보존한다.
            }

            throw;
        }
    }

    public static string EnsureTrailingLf(string text)
    {
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return normalized.EndsWith('\n') ? normalized : normalized + "\n";
    }

    public static void ValidateProject(StoryProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        HashSet<string> scriptIds = new(StringComparer.Ordinal);
        HashSet<string> fileIds = new(StringComparer.Ordinal);
        HashSet<string> nodeIds = new(StringComparer.Ordinal);
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> linkIds = new(StringComparer.Ordinal);
        HashSet<(NodeLinkKind Kind, string Source, string Target)> linkPairs = new();
        HashSet<string> presentationCommandIds = new(StringComparer.Ordinal);

        foreach (ScriptDocument script in project.Scripts)
        {
            if (!scriptIds.Add(script.Id))
            {
                throw new InvalidDataException($"대본 Id '{script.Id}'가 중복됩니다.");
            }

            ValidateScript(script);
        }

        foreach (StoryFile file in project.Files)
        {
            if (!fileIds.Add(file.Id))
            {
                throw new InvalidDataException($"StoryFile Id '{file.Id}'가 중복됩니다.");
            }

            string relativePath = ProjectStore.NormalizeRelativeStoryPath(file.RelativePath);

            if (!paths.Add(relativePath))
            {
                throw new InvalidDataException($"StoryFile 경로 '{relativePath}'가 중복됩니다.");
            }

            foreach (StoryNode node in file.Nodes)
            {
                if (!nodeIds.Add(node.Id))
                {
                    throw new InvalidDataException($"노드 Id '{node.Id}'가 프로젝트 전체에서 중복됩니다.");
                }

                switch (node)
                {
                    case DialogueNode dialogue:
                        ValidateDialogueNode(project, dialogue, scriptIds);
                        break;
                    case PresentationNode presentation:
                        ValidatePresentationNode(presentation, presentationCommandIds);
                        break;
                }
            }
        }

        foreach (NodeLink link in project.Links)
        {
            if (!linkIds.Add(link.Id))
            {
                throw new InvalidDataException($"NodeLink Id '{link.Id}'가 중복됩니다.");
            }

            StoryNode? source = project.FindNode(link.SourceNodeId);
            StoryNode? target = project.FindNode(link.TargetNodeId);

            if (source is null || target is null)
            {
                throw new InvalidDataException(
                    $"NodeLink '{link.Id}'가 존재하지 않는 노드를 가리킵니다.");
            }

            if (link.Order < 0)
            {
                throw new InvalidDataException($"NodeLink '{link.Id}'의 order는 음수일 수 없습니다.");
            }

            if (link.Kind == NodeLinkKind.Settings &&
                (source is not SetNode || target is not DialogueNode))
            {
                throw new InvalidDataException(
                    $"Settings link '{link.Id}'는 SetNode에서 DialogueNode로만 연결할 수 있습니다.");
            }

            if (!linkPairs.Add((link.Kind, link.SourceNodeId, link.TargetNodeId)))
            {
                throw new InvalidDataException(
                    $"{link.Kind} link '{link.SourceNodeId}' → '{link.TargetNodeId}'가 중복됩니다.");
            }
        }

        ValidateCompositions(project);

        if (project.StartNodeId is not null && project.FindNode(project.StartNodeId) is null)
        {
            throw new InvalidDataException(
                $"시작 노드 '{project.StartNodeId}'를 프로젝트에서 찾을 수 없습니다.");
        }

        if (project.FindNode(project.StartNodeId) is PresentationNode)
        {
            throw new InvalidDataException("PresentationNode는 프로젝트 시작 노드가 될 수 없습니다.");
        }
    }

    private static void ValidateScript(ScriptDocument script)
    {
        HashSet<string> lineIds = new(StringComparer.Ordinal);

        foreach (ScriptLine line in script.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Id))
            {
                throw new InvalidDataException($"대본 '{script.Id}'에 빈 LineId가 있습니다.");
            }

            if (!lineIds.Add(line.Id))
            {
                throw new InvalidDataException($"대본 '{script.Id}'에서 LineId '{line.Id}'가 중복됩니다.");
            }
        }

        HashSet<string> locales = new(StringComparer.Ordinal);

        foreach (ScriptLocale locale in script.Locales)
        {
            if (!locales.Add(locale.Locale))
            {
                throw new InvalidDataException(
                    $"대본 '{script.Id}'에서 locale '{locale.Locale}'이 중복됩니다.");
            }

            foreach (string lineId in locale.Entries.Keys)
            {
                if (!lineIds.Contains(lineId))
                {
                    throw new InvalidDataException(
                        $"대본 '{script.Id}'의 locale '{locale.Locale}'이 없는 LineId '{lineId}'를 가리킵니다.");
                }
            }
        }
    }

    /// <summary>
    /// 대사 노드는 대본을 가리키기만 하고 본문을 소유하지 않는다. 여기서 확인하는 것은
    /// 가리키는 대상이 실제로 있는지와 같은 LineId에 확장이 두 벌 붙지 않았는지다.
    /// </summary>
    private static void ValidateDialogueNode(
        StoryProject project,
        DialogueNode node,
        HashSet<string> scriptIds)
    {
        if (node.ScriptId is { } scriptId && !scriptIds.Contains(scriptId))
        {
            throw new InvalidDataException(
                $"DialogueNode '{node.Id}'가 없는 대본 '{scriptId}'를 가리킵니다.");
        }

        HashSet<string> lineIds = new(StringComparer.Ordinal);

        foreach (DialogueLineExtension extension in node.LineExtensions)
        {
            if (!lineIds.Add(extension.LineId))
            {
                throw new InvalidDataException(
                    $"DialogueNode '{node.Id}'에서 LineId '{extension.LineId}' 항목이 중복됩니다.");
            }
        }

        foreach (string openLineId in node.BranchExits.Keys)
        {
            if (!lineIds.Contains(openLineId))
            {
                throw new InvalidDataException(
                    $"DialogueNode '{node.Id}'의 조건 출구가 항목 없는 LineId '{openLineId}'에 매달려 있습니다.");
            }
        }

        foreach (string target in node.BranchExits.Values)
        {
            if (project.FindNode(target) is null)
            {
                throw new InvalidDataException(
                    $"DialogueNode '{node.Id}'의 조건 출구가 없는 노드 '{target}'를 가리킵니다.");
            }
        }
    }

    private static void ValidatePresentationNode(
        PresentationNode node,
        HashSet<string> projectCommandIds)
    {
        if (node.DefaultExitTargetNodeId is not null)
        {
            throw new InvalidDataException(
                $"PresentationNode '{node.Id}'는 실행 기본 출구를 가질 수 없습니다.");
        }

        if (node.Source is { Version: <= 0 })
        {
            throw new InvalidDataException(
                $"PresentationNode '{node.Id}'는 발행되지 않은 대사 결과를 읽을 수 없습니다.");
        }

        HashSet<string> lineIds = new(StringComparer.Ordinal);

        foreach (PresentationLineBinding binding in node.Bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.LineId))
            {
                throw new InvalidDataException(
                    $"PresentationNode '{node.Id}'에 빈 LineId binding이 있습니다.");
            }

            if (!lineIds.Add(binding.LineId))
            {
                throw new InvalidDataException(
                    $"PresentationNode '{node.Id}'에서 LineId '{binding.LineId}' binding이 중복됩니다.");
            }

            foreach (PresentationCommandInstance command in binding.Commands)
            {
                if (!projectCommandIds.Add(command.Id))
                {
                    throw new InvalidDataException(
                        $"Presentation command Id '{command.Id}'가 프로젝트에서 중복됩니다.");
                }
            }
        }
    }

    /// <summary>
    /// 조합이 없는 결과를 가리키는 것은 저장 자체를 막는다. 합성 시점의 진단으로 미루면
    /// 파일에 이미 깨진 참조가 들어간 뒤이고, 그것을 고칠 화면이 없다.
    /// </summary>
    private static void ValidateCompositions(StoryProject project)
    {
        HashSet<string> compositionIds = new(StringComparer.Ordinal);

        foreach (RuntimeComposition composition in project.Compositions)
        {
            if (!compositionIds.Add(composition.Id))
            {
                throw new InvalidDataException(
                    $"RuntimeComposition Id '{composition.Id}'가 중복됩니다.");
            }

            if (project.Results.FindDialogue(
                    composition.DialogueResultId,
                    composition.DialogueResultVersion) is null)
            {
                throw new InvalidDataException(
                    $"RuntimeComposition '{composition.Id}'이 없는 대사 결과 " +
                    $"'{composition.DialogueResultId} v{composition.DialogueResultVersion}'을 가리킵니다.");
            }

            if (composition.HasPresentation &&
                project.Results.FindPresentation(
                    composition.PresentationResultId,
                    composition.PresentationResultVersion) is null)
            {
                throw new InvalidDataException(
                    $"RuntimeComposition '{composition.Id}'이 없는 연출 결과 " +
                    $"'{composition.PresentationResultId} v{composition.PresentationResultVersion}'을 가리킵니다.");
            }
        }
    }
}
