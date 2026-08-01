using System.Text.Json.Nodes;
using Vn.Authoring.Model;

namespace Vn.Authoring.Serialization;

/// <summary>물리 저장 v2 이전의 단일 aggregate .vnstory.json을 읽기 위한 일회성 importer.</summary>
internal static class LegacyProjectJson
{
    private const string LegacyFileId = "sf_main";

    public static StoryProject Read(string json, string? sourcePath = null)
    {
        JsonObject root = JsonSupport.ParseObject(json, "이전 VnTool 프로젝트");
        int version = (int?)root["formatVersion"] ?? 0;

        StoryProject project = version switch
        {
            1 => ReadVersion1(root, sourcePath),
            2 => ReadInlineVersion2(root),
            _ => throw new InvalidDataException("VnTool 프로젝트 manifest 또는 이전 프로젝트 파일이 아닙니다.")
        };

        JsonSupport.ValidateProject(project);
        return project;
    }

    private static StoryProject ReadVersion1(JsonObject root, string? sourcePath)
    {
        if (root["nodes"] is not JsonArray nodes)
        {
            throw new InvalidDataException("이전 formatVersion 1 프로젝트에 nodes 배열이 없습니다.");
        }

        string displayName = sourcePath is null
            ? "기본 파일"
            : RemoveKnownExtension(Path.GetFileName(sourcePath));
        var file = new StoryFile(LegacyFileId, displayName, ProjectStore.DefaultRelativePath(LegacyFileId));
        ReadNodes(nodes, file);

        var project = NewProject(root);
        project.Files.Add(file);
        return project;
    }

    private static StoryProject ReadInlineVersion2(JsonObject root)
    {
        if (root["files"] is not JsonArray files)
        {
            throw new InvalidDataException("이전 formatVersion 2 프로젝트에 files 배열이 없습니다.");
        }

        var project = NewProject(root);
        foreach (JsonNode? item in files)
        {
            if (item is not JsonObject fileObject || fileObject["nodes"] is not JsonArray nodes)
            {
                throw new InvalidDataException("이전 formatVersion 2 프로젝트의 파일 항목이 올바르지 않습니다.");
            }

            string id = (string?)fileObject["id"] ?? Identifier.File();
            string name = (string?)fileObject["name"] ?? "이름 없는 파일";
            var file = new StoryFile(id, name, ProjectStore.DefaultRelativePath(id));
            ReadNodes(nodes, file);
            project.Files.Add(file);
        }

        return project;
    }

    private static void ReadNodes(JsonArray nodes, StoryFile file)
    {
        foreach (JsonNode? item in nodes)
        {
            if (item is JsonObject node)
            {
                file.Nodes.Add(StoryNodeJson.Read(node));
            }
        }
    }

    private static StoryProject NewProject(JsonObject root)
    {
        return new StoryProject
        {
            FormatVersion = StoryProject.CurrentFormatVersion,
            Title = (string?)root["title"] ?? "제목 없음",
            StartNodeId = (string?)root["startNode"]
        };
    }

    private static string RemoveKnownExtension(string fileName)
    {
        return fileName.EndsWith(StoryFileJson.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^StoryFileJson.FileExtension.Length]
            : Path.GetFileNameWithoutExtension(fileName);
    }
}
