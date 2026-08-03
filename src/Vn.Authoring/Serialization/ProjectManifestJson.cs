using System.Text.Json.Nodes;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Serialization;

/// <summary>
/// 프로젝트 메타데이터와 <b>부속 파일의 목차</b>를 담는 manifest 형식.
///
/// 본문은 어디에도 없다. 대본은 <c>scripts</c>가, 노드는 <c>files</c>가, 발행 결과는
/// <c>results</c>가 가리키는 파일에 있다. manifest가 커지지 않아야 파일을 나눈 이유가 남는다.
/// 조건 공급 link와 RuntimeComposition은 파일을 넘나드는 관계라서 여기에 둔다.
/// </summary>
public static class ProjectManifestJson
{
    public const int CurrentFormatVersion = StoryProject.CurrentFormatVersion;
    public const string FileExtension = ".vnproject.json";

    public static string Write(StoryProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        JsonSupport.ValidateProject(project);

        var scripts = new JsonArray();

        foreach (ScriptFileReference reference in ScriptReferencesOf(project))
        {
            scripts.Add(new JsonObject
            {
                ["id"] = reference.Id,
                ["name"] = reference.Name,
                ["path"] = reference.RelativePath
            });
        }

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

        var compositions = new JsonArray();

        foreach (RuntimeComposition composition in project.Compositions)
        {
            compositions.Add(WriteComposition(composition));
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

        if (WriteAssetRoots(project.AssetRoots) is { } assetRoots)
        {
            root["assetRoots"] = assetRoots;
        }

        if (project.RecentCommandIds.Count > 0)
        {
            root["recentCommands"] = new JsonArray(
                project.RecentCommandIds.Select(id => (JsonNode)id).ToArray());
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
            root["results"] = ResultStoreJson.DefaultFileName;
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

        if (version > 0 && version <= StoryProject.LastUnsupportedFormatVersion)
        {
            throw new InvalidDataException(
                $"형식 버전 {version} 프로젝트는 더 이상 열 수 없습니다. " +
                "그 형식에서는 화자·대사를 대사 노드가 직접 소유했고 연출이 편집 중인 노드를 " +
                "실시간으로 읽었습니다. 지금 구조로 자동 변환하면 어느 문장이 어느 LineId인지 " +
                "도구가 임의로 정하게 되므로, 덮어써서 원고를 잃는 대신 열지 않습니다. " +
                "새 프로젝트를 만들고 대본을 가져오세요.");
        }

        if (version != CurrentFormatVersion || root["files"] is not JsonArray files)
        {
            throw new InvalidDataException("VnTool 프로젝트 manifest가 아닙니다.");
        }

        var scripts = new List<ScriptFileReference>();
        HashSet<string> scriptIds = new(StringComparer.Ordinal);
        HashSet<string> allPaths = new(StringComparer.OrdinalIgnoreCase);

        foreach (JsonNode? item in root["scripts"]?.AsArray() ?? new JsonArray())
        {
            if (item is not JsonObject script)
            {
                throw new InvalidDataException("프로젝트 manifest의 scripts 항목이 객체가 아닙니다.");
            }

            string id = (string?)script["id"]
                ?? throw new InvalidDataException("프로젝트 manifest의 대본 항목에 id가 없습니다.");
            string path = ProjectStore.NormalizeRelativePath(
                (string?)script["path"]
                    ?? throw new InvalidDataException($"대본 '{id}'에 path가 없습니다."),
                ScriptDocumentJson.FileExtension);

            if (!scriptIds.Add(id))
            {
                throw new InvalidDataException($"대본 Id '{id}'가 manifest에서 중복됩니다.");
            }

            if (!allPaths.Add(path))
            {
                throw new InvalidDataException($"부속 파일 경로 '{path}'가 manifest에서 중복됩니다.");
            }

            scripts.Add(new ScriptFileReference(id, (string?)script["name"] ?? "이름 없는 대본", path));
        }

        var references = new List<ProjectStoryFileReference>();
        HashSet<string> ids = new(StringComparer.Ordinal);

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

            if (!allPaths.Add(path))
            {
                throw new InvalidDataException($"부속 파일 경로 '{path}'가 manifest에서 중복됩니다.");
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

        var compositions = new List<RuntimeComposition>();
        HashSet<string> compositionIds = new(StringComparer.Ordinal);

        foreach (JsonNode? item in root["compositions"]?.AsArray() ?? new JsonArray())
        {
            if (item is not JsonObject compositionObject)
            {
                throw new InvalidDataException("프로젝트 manifest의 compositions 항목이 객체가 아닙니다.");
            }

            RuntimeComposition composition = ReadComposition(compositionObject);

            if (!compositionIds.Add(composition.Id))
            {
                throw new InvalidDataException(
                    $"RuntimeComposition Id '{composition.Id}'가 manifest에서 중복됩니다.");
            }

            compositions.Add(composition);
        }

        return new ProjectManifest(
            (string?)root["title"] ?? "제목 없음",
            (string?)root["startNode"],
            ReadAssetRoots(root["assetRoots"]),
            ReadRecentCommands(root["recentCommands"]),
            scripts,
            references,
            links,
            compositions,
            (string?)root["results"]);
    }

    internal static IReadOnlyList<string> ReadRecentCommands(JsonNode? json)
    {
        if (json is not JsonArray array)
        {
            return Array.Empty<string>();
        }

        return array
            .Select(item => (string?)item)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Take(StoryProject.MaxRecentCommands)
            .ToArray();
    }

    /// <summary>비어 있으면 null — manifest에 빈 객체를 남기지 않는다.</summary>
    internal static JsonObject? WriteAssetRoots(AssetRootSettings assetRoots)
    {
        if (assetRoots.IsEmpty)
        {
            return null;
        }

        var json = new JsonObject();

        if (AssetRootSettings.NormalizePath(assetRoots.BackgroundsPath) is { } backgrounds)
        {
            json["backgrounds"] = backgrounds;
        }

        if (AssetRootSettings.NormalizePath(assetRoots.PortraitsPath) is { } portraits)
        {
            json["portraits"] = portraits;
        }

        return json.Count > 0 ? json : null;
    }

    internal static AssetRootSettings ReadAssetRoots(JsonNode? json)
    {
        if (json is not JsonObject assetRoots)
        {
            return new AssetRootSettings();
        }

        return new AssetRootSettings
        {
            BackgroundsPath = AssetRootSettings.NormalizePath((string?)assetRoots["backgrounds"]),
            PortraitsPath = AssetRootSettings.NormalizePath((string?)assetRoots["portraits"])
        };
    }

    internal static IReadOnlyList<ScriptFileReference> ScriptReferencesOf(StoryProject project)
    {
        return project.Scripts
            .Select(script => new ScriptFileReference(
                script.Id,
                script.Name,
                ProjectStore.DefaultScriptPath(script.Id)))
            .ToList();
    }

    internal static JsonObject WriteLink(NodeLink link)
    {
        var result = new JsonObject
        {
            ["id"] = link.Id,
            ["kind"] = link.Kind switch
            {
                NodeLinkKind.Settings => "settings",
                NodeLinkKind.CommandSupply => "commandSupply",
                NodeLinkKind.PresentationSupply => "presentationSupply",
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
            "commandSupply" => NodeLinkKind.CommandSupply,
            "presentationSupply" => NodeLinkKind.PresentationSupply,
            "presentation" => throw new InvalidDataException(
                $"NodeLink '{id}'는 연출 link입니다. 연출은 이제 발행된 대사 결과를 읽으므로 " +
                "link로 저장하지 않습니다."),
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

    internal static JsonObject WriteComposition(RuntimeComposition composition)
    {
        var json = new JsonObject
        {
            ["id"] = composition.Id,
            ["name"] = composition.Name,
            ["locale"] = composition.Locale,
            ["dialogueResult"] = new JsonObject
            {
                ["resultId"] = composition.DialogueResultId,
                ["version"] = composition.DialogueResultVersion
            }
        };

        if (composition.HasPresentation)
        {
            json["presentationResult"] = new JsonObject
            {
                ["resultId"] = composition.PresentationResultId,
                ["version"] = composition.PresentationResultVersion
            };
        }

        return json;
    }

    internal static RuntimeComposition ReadComposition(JsonObject json)
    {
        string id = (string?)json["id"]
            ?? throw new InvalidDataException("RuntimeComposition에 id가 없습니다.");

        if (json["dialogueResult"] is not JsonObject dialogue)
        {
            throw new InvalidDataException($"RuntimeComposition '{id}'에 dialogueResult가 없습니다.");
        }

        var composition = new RuntimeComposition(id, (string?)json["name"] ?? "이름 없는 합성")
        {
            Locale = (string?)json["locale"] ?? Script.ScriptDocument.DefaultLocale,
            DialogueResultId = (string?)dialogue["resultId"]
                ?? throw new InvalidDataException(
                    $"RuntimeComposition '{id}'의 dialogueResult에 resultId가 없습니다."),
            DialogueResultVersion = (int?)dialogue["version"]
                ?? throw new InvalidDataException(
                    $"RuntimeComposition '{id}'의 dialogueResult에 version이 없습니다.")
        };

        if (json["presentationResult"] is JsonObject presentation)
        {
            composition.PresentationResultId = (string?)presentation["resultId"]
                ?? throw new InvalidDataException(
                    $"RuntimeComposition '{id}'의 presentationResult에 resultId가 없습니다.");
            composition.PresentationResultVersion = (int?)presentation["version"]
                ?? throw new InvalidDataException(
                    $"RuntimeComposition '{id}'의 presentationResult에 version이 없습니다.");
        }

        return composition;
    }
}

public sealed record ProjectManifest(
    string Title,
    string? StartNodeId,
    AssetRootSettings AssetRoots,
    IReadOnlyList<string> RecentCommandIds,
    IReadOnlyList<ScriptFileReference> Scripts,
    IReadOnlyList<ProjectStoryFileReference> Files,
    IReadOnlyList<NodeLink> Links,
    IReadOnlyList<RuntimeComposition> Compositions,
    string? ResultsRelativePath);

public sealed record ScriptFileReference(
    string Id,
    string Name,
    string RelativePath);

public sealed record ProjectStoryFileReference(
    string Id,
    string Name,
    string RelativePath);
