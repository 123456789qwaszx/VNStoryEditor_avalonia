using System.Text.Json.Nodes;
using Vn.Authoring.Model;

namespace Vn.Authoring.Serialization;

/// <summary>
/// Undo/Redo와 dirty 비교를 위한 디스크 독립 aggregate 스냅샷.
/// manifest 경로를 따라 파일을 읽지 않으며 StoryProject 전체를 문자열 하나로 왕복한다.
/// </summary>
public static class ProjectSnapshotCodec
{
    public const int CurrentSnapshotVersion = 1;

    public static string Encode(StoryProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        JsonSupport.ValidateProject(project);

        var files = new JsonArray();
        foreach (StoryFile file in project.Files)
        {
            JsonObject item = StoryFileJson.WriteObject(file);
            item.Remove("formatVersion");
            item["path"] = ProjectStore.NormalizeRelativeStoryPath(file.RelativePath);
            files.Add(item);
        }

        var links = new JsonArray();
        foreach (NodeLink link in project.Links)
        {
            links.Add(ProjectManifestJson.WriteLink(link));
        }

        var root = new JsonObject
        {
            ["snapshotVersion"] = CurrentSnapshotVersion,
            ["projectFormatVersion"] = StoryProject.CurrentFormatVersion,
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

    public static StoryProject Decode(string snapshot)
    {
        JsonObject root = JsonSupport.ParseObject(snapshot, "프로젝트 스냅샷");
        int version = (int?)root["snapshotVersion"] ?? 0;

        if (version != CurrentSnapshotVersion || root["files"] is not JsonArray files)
        {
            throw new InvalidDataException("지원하지 않는 프로젝트 스냅샷입니다.");
        }

        int projectFormatVersion = (int?)root["projectFormatVersion"] ?? StoryProject.CurrentFormatVersion;
        if (projectFormatVersion > StoryProject.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"스냅샷의 프로젝트 형식 버전 {projectFormatVersion}은 지원하지 않습니다.");
        }

        var project = new StoryProject
        {
            FormatVersion = projectFormatVersion,
            Title = (string?)root["title"] ?? "제목 없음",
            StartNodeId = (string?)root["startNode"]
        };

        foreach (JsonNode? item in files)
        {
            if (item is not JsonObject fileObject)
            {
                throw new InvalidDataException("프로젝트 스냅샷의 files 항목이 객체가 아닙니다.");
            }

            var storyRoot = new JsonObject
            {
                ["formatVersion"] = StoryFileJson.CurrentFormatVersion,
                ["fileId"] = fileObject["fileId"]?.DeepClone(),
                ["name"] = fileObject["name"]?.DeepClone(),
                ["nodes"] = fileObject["nodes"]?.DeepClone()
            };

            StoryFile file = StoryFileJson.ReadObject(storyRoot);
            file.RelativePath = ProjectStore.NormalizeRelativeStoryPath(
                (string?)fileObject["path"] ?? ProjectStore.DefaultRelativePath(file.Id));
            project.Files.Add(file);
        }

        foreach (JsonNode? item in root["links"]?.AsArray() ?? new JsonArray())
        {
            if (item is not JsonObject linkObject)
            {
                throw new InvalidDataException("프로젝트 스냅샷의 links 항목이 객체가 아닙니다.");
            }

            project.Links.Add(ProjectManifestJson.ReadLink(linkObject));
        }

        JsonSupport.ValidateProject(project);
        return project;
    }
}
