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

        if (WriteExportFormats(project.ExportFormats) is { } exportFormats)
        {
            root["exportFormats"] = exportFormats;
        }

        if (AssetRootSettings.NormalizePath(project.OutputPath) is { } outputPath)
        {
            root["outputPath"] = outputPath;
        }

        root["scripts"] = scripts;
        root["files"] = files;

        if (links.Count > 0)
        {
            root["links"] = links;
        }

        // 작가가 더한 화자 (2026-08-17) — 정의 파일이 아니라 여기 산다(정의 파일은 기획자 전용).
        // <b>이름이 빈 줄은 파일에 안 쓴다</b> — 편집 중인 빈 자리는 메모리에서는 살아 있어야
        // 하지만(안 그러면 만들자마자 사라진다) 저장물에 남길 이유는 없다.
        if (WriteWriterSpeakers(project.WriterSpeakers) is { } writerSpeakers)
        {
            root["writerSpeakers"] = writerSpeakers;
        }

        // 커스텀 이징 곡선 (W67 후속) — 커브도 작가 자산이라 정의 파일이 아니라 여기 산다.
        if (WriteEaseCurves(project.EaseCurves) is { } easeCurves)
        {
            root["easeCurves"] = easeCurves;
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

        List<WriterSpeaker> writerSpeakers = ReadWriterSpeakers(root["writerSpeakers"]);
        List<EaseCurve> easeCurves = ReadEaseCurves(root["easeCurves"]);

        return new ProjectManifest(
            (string?)root["title"] ?? "제목 없음",
            (string?)root["startNode"],
            ReadAssetRoots(root["assetRoots"]),
            ReadRecentCommands(root["recentCommands"]),
            ReadExportFormats(root["exportFormats"]),
            AssetRootSettings.NormalizePath((string?)root["outputPath"]),
            scripts,
            references,
            links,
            writerSpeakers,
            easeCurves,
            compositions,
            (string?)root["results"]);
    }

    /// <summary>기본(전부 켬)이면 null — 기존 프로젝트 파일이 바뀌지 않는다.</summary>
    internal static JsonObject? WriteExportFormats(ExportFormatSelection formats)
    {
        if (formats.IsDefault)
        {
            return null;
        }

        return new JsonObject
        {
            ["yarnTrio"] = formats.YarnTrio,
            ["scriptCsv"] = formats.ScriptCsv,
            ["reviewCsv"] = formats.ReviewCsv,
            ["directionCsv"] = formats.DirectionCsv
        };
    }

    internal static ExportFormatSelection ReadExportFormats(JsonNode? json)
    {
        if (json is not JsonObject formats)
        {
            return new ExportFormatSelection();
        }

        return new ExportFormatSelection
        {
            YarnTrio = (bool?)formats["yarnTrio"] ?? true,
            ScriptCsv = (bool?)formats["scriptCsv"] ?? true,
            ReviewCsv = (bool?)formats["reviewCsv"] ?? true,
            DirectionCsv = (bool?)formats["directionCsv"] ?? true
        };
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

        if (AssetRootSettings.NormalizePath(assetRoots.BgmPath) is { } bgm)
        {
            json["bgm"] = bgm;
        }

        if (AssetRootSettings.NormalizePath(assetRoots.SfxPath) is { } sfx)
        {
            json["sfx"] = sfx;
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
            PortraitsPath = AssetRootSettings.NormalizePath((string?)assetRoots["portraits"]),
            BgmPath = AssetRootSettings.NormalizePath((string?)assetRoots["bgm"]),
            SfxPath = AssetRootSettings.NormalizePath((string?)assetRoots["sfx"])
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

    /// <summary>
    /// 커스텀 곡선 직렬화 (W67 후속) — manifest와 undo 스냅샷 코덱이 <b>같은 이것 하나</b>를
    /// 쓴다(코덱이 따로 쓰면 undo가 곡선을 조용히 지운다). 필드명(t·v·inTangent·outTangent)은
    /// 내보내기 curves.json 스키마와 같은 낱말이다 — 두 파일을 나란히 볼 사람이 같은 것을
    /// 다른 이름으로 읽지 않게. 이름이 빈 곡선은 파일에 안 쓴다.
    /// </summary>
    internal static JsonArray? WriteEaseCurves(IReadOnlyList<EaseCurve> curves)
    {
        List<EaseCurve> named = curves
            .Where(curve => !string.IsNullOrWhiteSpace(curve.Name))
            .ToList();

        if (named.Count == 0)
        {
            return null;
        }

        var array = new JsonArray();

        foreach (EaseCurve curve in named)
        {
            var keys = new JsonArray();

            foreach (Ked.Presentation.Core.CurveKey key in curve.Keys)
            {
                keys.Add(new JsonObject
                {
                    ["t"] = key.Time,
                    ["v"] = key.Value,
                    ["inTangent"] = key.InTangent,
                    ["outTangent"] = key.OutTangent
                });
            }

            array.Add(new JsonObject { ["name"] = curve.Name, ["keys"] = keys });
        }

        return array;
    }

    internal static List<EaseCurve> ReadEaseCurves(JsonNode? node)
    {
        var curves = new List<EaseCurve>();

        foreach (JsonNode? item in node?.AsArray() ?? new JsonArray())
        {
            if (item is not JsonObject curve)
            {
                throw new InvalidDataException("easeCurves 항목이 객체가 아닙니다.");
            }

            var keys = new List<Ked.Presentation.Core.CurveKey>();

            foreach (JsonNode? keyNode in curve["keys"]?.AsArray() ?? new JsonArray())
            {
                if (keyNode is not JsonObject key)
                {
                    throw new InvalidDataException("easeCurves의 keys 항목이 객체가 아닙니다.");
                }

                keys.Add(new Ked.Presentation.Core.CurveKey(
                    (float?)key["t"] ?? 0f,
                    (float?)key["v"] ?? 0f,
                    (float?)key["inTangent"] ?? 0f,
                    (float?)key["outTangent"] ?? 0f));
            }

            curves.Add(new EaseCurve
            {
                Name = (string?)curve["name"] ?? string.Empty,
                Keys = keys
            });
        }

        return curves;
    }

    /// <summary>
    /// 작가 화자 직렬화 — manifest와 undo 스냅샷 코덱이 같은 것을 쓴다. 코덱이 이걸 안 실어
    /// <b>undo가 작가 화자를 지우는 잠복 버그</b>가 있었다(곡선 undo를 세우다 발견, W67 후속).
    /// </summary>
    internal static JsonArray? WriteWriterSpeakers(IReadOnlyList<WriterSpeaker> speakers)
    {
        List<WriterSpeaker> named = speakers
            .Where(speaker => !string.IsNullOrWhiteSpace(speaker.Name))
            .ToList();

        if (named.Count == 0)
        {
            return null;
        }

        var array = new JsonArray();

        foreach (WriterSpeaker speaker in named)
        {
            var entry = new JsonObject { ["name"] = speaker.Name };

            if (speaker.CharacterId.Length > 0)
            {
                entry["characterId"] = speaker.CharacterId;
            }

            array.Add(entry);
        }

        return array;
    }

    internal static List<WriterSpeaker> ReadWriterSpeakers(JsonNode? node)
    {
        var speakers = new List<WriterSpeaker>();

        foreach (JsonNode? item in node?.AsArray() ?? new JsonArray())
        {
            if (item is not JsonObject speaker)
            {
                throw new InvalidDataException("writerSpeakers 항목이 객체가 아닙니다.");
            }

            speakers.Add(new WriterSpeaker
            {
                Name = (string?)speaker["name"] ?? string.Empty,
                CharacterId = (string?)speaker["characterId"] ?? string.Empty
            });
        }

        return speakers;
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
    ExportFormatSelection ExportFormats,
    string? OutputPath,
    IReadOnlyList<ScriptFileReference> Scripts,
    IReadOnlyList<ProjectStoryFileReference> Files,
    IReadOnlyList<NodeLink> Links,
    IReadOnlyList<WriterSpeaker> WriterSpeakers,
    IReadOnlyList<EaseCurve> EaseCurves,
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
