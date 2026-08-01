using System.Text.Json.Nodes;
using Vn.Authoring.Model;

namespace Vn.Authoring.Serialization;

/// <summary>StoryFile 하나와 그 파일이 소유하는 노드를 결정적으로 직렬화한다.</summary>
public static class StoryFileJson
{
    public const int CurrentFormatVersion = 1;
    public const string FileExtension = ".vnstory.json";

    public static string Write(StoryFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        ValidateNodeIds(file);
        return JsonSupport.ToDeterministicText(WriteObject(file));
    }

    public static StoryFile Read(string json)
    {
        JsonObject root = JsonSupport.ParseObject(json, "StoryFile");
        int version = (int?)root["formatVersion"] ?? 0;

        if (version != CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"StoryFile 형식 버전 {version}은 지원하지 않습니다. 현재 버전은 {CurrentFormatVersion}입니다.");
        }

        return ReadObject(root);
    }

    internal static JsonObject WriteObject(StoryFile file)
    {
        var nodes = new JsonArray();
        foreach (StoryNode node in file.Nodes)
        {
            nodes.Add(StoryNodeJson.Write(node));
        }

        return new JsonObject
        {
            ["formatVersion"] = CurrentFormatVersion,
            ["fileId"] = file.Id,
            ["name"] = file.Name,
            ["nodes"] = nodes
        };
    }

    internal static StoryFile ReadObject(JsonObject root)
    {
        if (root["nodes"] is not JsonArray nodes)
        {
            throw new InvalidDataException("StoryFile에 nodes 배열이 없습니다.");
        }

        string id = (string?)root["fileId"]
            ?? throw new InvalidDataException("StoryFile에 fileId가 없습니다.");
        string name = (string?)root["name"] ?? "이름 없는 파일";
        var file = new StoryFile(id, name);

        HashSet<string> nodeIds = new(StringComparer.Ordinal);
        foreach (JsonNode? item in nodes)
        {
            if (item is not JsonObject nodeJson)
            {
                continue;
            }

            StoryNode node = StoryNodeJson.Read(nodeJson);
            if (!nodeIds.Add(node.Id))
            {
                throw new InvalidDataException($"StoryFile '{id}' 안에서 노드 Id '{node.Id}'가 중복됩니다.");
            }
            file.Nodes.Add(node);
        }

        return file;
    }

    private static void ValidateNodeIds(StoryFile file)
    {
        HashSet<string> nodeIds = new(StringComparer.Ordinal);
        foreach (StoryNode node in file.Nodes)
        {
            if (!nodeIds.Add(node.Id))
            {
                throw new InvalidDataException(
                    $"StoryFile '{file.Id}' 안에서 노드 Id '{node.Id}'가 중복됩니다.");
            }
        }
    }
}
