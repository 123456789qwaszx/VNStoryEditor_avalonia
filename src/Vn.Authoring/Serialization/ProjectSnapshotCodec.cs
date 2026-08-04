using System.Text.Json.Nodes;
using Vn.Authoring.Model;
using Vn.Authoring.Results;
using Vn.Authoring.Script;

namespace Vn.Authoring.Serialization;

/// <summary>
/// Undo/Redo와 dirty 비교를 위한 디스크 독립 aggregate 스냅샷.
///
/// manifest 경로를 따라 파일을 읽지 않으며 StoryProject 전체를 문자열 하나로 왕복한다.
/// 되돌리기 한 번에 여러 실제 파일을 다시 조립할 이유가 없고, 디스크 배치가 또 바뀌어도
/// 편집 기록은 이 문자열 하나로 독립되어 있어야 한다.
///
/// 발행 결과도 함께 왕복한다. 발행은 프로젝트를 바꾸는 편집이므로 되돌릴 수 있어야 한다.
/// 되돌리면 결과가 목록에서 <b>빠질</b> 뿐, 남아 있는 결과의 내용이 바뀌지는 않는다.
/// </summary>
public static class ProjectSnapshotCodec
{
    public const int CurrentSnapshotVersion = 2;

    public static string Encode(StoryProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        JsonSupport.ValidateProject(project);

        var scripts = new JsonArray();

        foreach (ScriptDocument script in project.Scripts)
        {
            JsonObject item = ScriptDocumentJson.WriteObject(script);
            item.Remove("formatVersion");
            scripts.Add(item);
        }

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

        var compositions = new JsonArray();

        foreach (RuntimeComposition composition in project.Compositions)
        {
            compositions.Add(ProjectManifestJson.WriteComposition(composition));
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

        if (ProjectManifestJson.WriteAssetRoots(project.AssetRoots) is { } assetRoots)
        {
            root["assetRoots"] = assetRoots;
        }

        if (project.RecentCommandIds.Count > 0)
        {
            root["recentCommands"] = new JsonArray(
                project.RecentCommandIds.Select(id => (JsonNode)id).ToArray());
        }

        if (ProjectManifestJson.WriteExportFormats(project.ExportFormats) is { } exportFormats)
        {
            root["exportFormats"] = exportFormats;
        }

        root["scripts"] = scripts;
        root["files"] = files;

        if (links.Count > 0)
        {
            root["links"] = links;
        }

        if (compositions.Count > 0)
        {
            root["compositions"] = compositions;
        }

        if (!project.Results.IsEmpty)
        {
            root["results"] = JsonNode.Parse(ResultStoreJson.Write(project.Results));
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
            StartNodeId = (string?)root["startNode"],
            AssetRoots = ProjectManifestJson.ReadAssetRoots(root["assetRoots"]),
            RecentCommandIds = ProjectManifestJson.ReadRecentCommands(root["recentCommands"]).ToList(),
            ExportFormats = ProjectManifestJson.ReadExportFormats(root["exportFormats"])
        };

        foreach (JsonNode? item in root["scripts"]?.AsArray() ?? new JsonArray())
        {
            if (item is not JsonObject scriptObject)
            {
                throw new InvalidDataException("프로젝트 스냅샷의 scripts 항목이 객체가 아닙니다.");
            }

            JsonObject scriptRoot = scriptObject.DeepClone().AsObject();
            scriptRoot["formatVersion"] = ScriptDocumentJson.CurrentFormatVersion;
            project.Scripts.Add(ScriptDocumentJson.ReadObject(scriptRoot));
        }

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

        foreach (JsonNode? item in root["compositions"]?.AsArray() ?? new JsonArray())
        {
            if (item is not JsonObject compositionObject)
            {
                throw new InvalidDataException("프로젝트 스냅샷의 compositions 항목이 객체가 아닙니다.");
            }

            project.Compositions.Add(ProjectManifestJson.ReadComposition(compositionObject));
        }

        if (root["results"] is JsonObject resultsObject)
        {
            ResultRepository results = ResultStoreJson.Read(resultsObject.ToJsonString());

            foreach (DialogueResult result in results.DialogueResults)
            {
                project.Results.Add(result);
            }

            foreach (PresentationResult result in results.PresentationResults)
            {
                project.Results.Add(result);
            }
        }

        JsonSupport.ValidateProject(project);
        return project;
    }
}
