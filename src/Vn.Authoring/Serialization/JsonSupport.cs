using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vn.Authoring.Model;

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

        HashSet<string> fileIds = new(StringComparer.Ordinal);
        HashSet<string> nodeIds = new(StringComparer.Ordinal);
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> linkIds = new(StringComparer.Ordinal);
        HashSet<(NodeLinkKind Kind, string Source, string Target)> linkPairs = new();

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

        if (project.StartNodeId is not null && project.FindNode(project.StartNodeId) is null)
        {
            throw new InvalidDataException(
                $"시작 노드 '{project.StartNodeId}'를 프로젝트에서 찾을 수 없습니다.");
        }
    }
}
