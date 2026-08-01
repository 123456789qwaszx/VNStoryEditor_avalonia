using System.Text.Json.Nodes;
using Vn.Authoring.Model;

namespace Vn.Authoring.Serialization;

/// <summary>프로젝트 메타데이터, StoryFile 경로와 파일을 넘는 typed link를 담는 manifest 형식.</summary>
public static class ProjectManifestJson
{
    public const int CurrentFormatVersion = StoryProject.CurrentFormatVersion;
    public const string FileExtension = ".vnproject.json";

    public static string Write(StoryProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        JsonSupport.ValidateProject(project);

        var files = new JsonArray();
        foreach (StoryFile file in project.Files)
        {
            files.Add(new JsonObject
            {
                ["id"] = file.Id,
                ["name"] = file.Name,
                ["path"] = ProjectStore.NormalizeRelativeStoryPath(file.RelativePath)
            });
        }

        var links = new JsonArray();
        foreach (NodeLink link in project.Links)
        {
            links.Add(WriteLink(link));
        }

        var root = new JsonObject
        {
            ["formatVersion"] = CurrentFormatVersion,
            ["title"] = project.Title
        };

        if (project.StartNodeId is not null)
        {
            root["startNode"] = project.StartNodeId;
        }

        root["files"] = files;

        if (links.Count > 0)
        {
            root["links"] = links;
        }

        return JsonSupport.ToDeterministicText(root);
    }

    public static ProjectManifest Read(string json)
    {
        JsonObject root = JsonSupport.ParseObject(json, "프로젝트 manifest");
        int version = (int?)root["formatVersion"] ?? 0;

        if (version > CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"이 프로젝트 manifest는 형식 버전 {version}입니다. " +
                $"현재 VnTool은 {CurrentFormatVersion}까지 읽을 수 있습니다.");
        }

        if (version != CurrentFormatVersion || root["files"] is not JsonArray files)
        {
            throw new InvalidDataException("VnTool 프로젝트 manifest가 아닙니다.");
        }

        var references = new List<ProjectStoryFileReference>();
        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

        foreach (JsonNode? item in files)
        {
            if (item is not JsonObject file)
            {
                throw new InvalidDataException("프로젝트 manifest의 files 항목이 객체가 아닙니다.");
            }

            string id = (string?)file["id"]
                ?? throw new InvalidDataException("프로젝트 manifest의 파일 항목에 id가 없습니다.");
            string name = (string?)file["name"] ?? "이름 없는 파일";
            string path = ProjectStore.NormalizeRelativeStoryPath(
                (string?)file["path"]
                ?? throw new InvalidDataException($"StoryFile '{id}'에 path가 없습니다."));

            if (!ids.Add(id))
            {
                throw new InvalidDataException($"StoryFile Id '{id}'가 manifest에서 중복됩니다.");
            }

            if (!paths.Add(path))
            {
                throw new InvalidDataException($"StoryFile 경로 '{path}'가 manifest에서 중복됩니다.");
            }

            references.Add(new ProjectStoryFileReference(id, name, path));
        }

        var links = new List<NodeLink>();
        HashSet<string> linkIds = new(StringComparer.Ordinal);

        foreach (JsonNode? item in root["links"]?.AsArray() ?? new JsonArray())
        {
            if (item is not JsonObject linkObject)
            {
                throw new InvalidDataException("프로젝트 manifest의 links 항목이 객체가 아닙니다.");
            }

            NodeLink link = ReadLink(linkObject);
            if (!linkIds.Add(link.Id))
            {
                throw new InvalidDataException($"NodeLink Id '{link.Id}'가 manifest에서 중복됩니다.");
            }

            links.Add(link);
        }

        return new ProjectManifest(
            (string?)root["title"] ?? "제목 없음",
            (string?)root["startNode"],
            references,
            links);
    }

    internal static JsonObject WriteLink(NodeLink link)
    {
        var result = new JsonObject
        {
            ["id"] = link.Id,
            ["kind"] = link.Kind switch
            {
                NodeLinkKind.Settings => "settings",
                _ => throw new InvalidDataException($"지원하지 않는 NodeLink 종류 '{link.Kind}'입니다.")
            },
            ["source"] = link.SourceNodeId,
            ["target"] = link.TargetNodeId,
            ["order"] = link.Order
        };

        if (!link.IsEnabled)
        {
            result["enabled"] = false;
        }

        return result;
    }

    internal static NodeLink ReadLink(JsonObject json)
    {
        string id = (string?)json["id"]
            ?? throw new InvalidDataException("NodeLink에 id가 없습니다.");
        NodeLinkKind kind = (string?)json["kind"] switch
        {
            "settings" => NodeLinkKind.Settings,
            { } unknown => throw new InvalidDataException($"지원하지 않는 NodeLink 종류 '{unknown}'입니다."),
            _ => throw new InvalidDataException($"NodeLink '{id}'에 kind가 없습니다.")
        };
        string source = (string?)json["source"]
            ?? throw new InvalidDataException($"NodeLink '{id}'에 source가 없습니다.");
        string target = (string?)json["target"]
            ?? throw new InvalidDataException($"NodeLink '{id}'에 target이 없습니다.");

        return new NodeLink(id, kind, source, target)
        {
            IsEnabled = (bool?)json["enabled"] ?? true,
            Order = (int?)json["order"] ?? 0
        };
    }
}

public sealed record ProjectManifest(
    string Title,
    string? StartNodeId,
    IReadOnlyList<ProjectStoryFileReference> Files,
    IReadOnlyList<NodeLink> Links);

public sealed record ProjectStoryFileReference(
    string Id,
    string Name,
    string RelativePath);
