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

        // 자주 쓰는 칩 (2026-08-22) — 손대지 않은 프로젝트에는 <b>키 자체를 안 쓴다</b>.
        // 빈 배열은 "사람이 다 지웠다"는 뜻이라 기본 목록으로 되돌아가면 안 된다.
        if (WriteQuickCommands(project.QuickCommands) is { } quickCommands)
        {
            root["quickCommands"] = quickCommands;
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
            ReadQuickCommands(root["quickCommands"]),
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
    /// <summary>
    /// 자주 쓰는 칩 직렬화 — manifest와 undo 스냅샷 코덱이 같은 것을 쓴다.
    /// <b>null(손대지 않음)이면 null</b>을 돌려 키를 안 쓰고, 빈 목록은 빈 배열로 남긴다.
    /// </summary>
    internal static JsonArray? WriteQuickCommands(IReadOnlyList<StageQuickCommand>? chips)
    {
        if (chips is null)
        {
            return null;
        }

        var array = new JsonArray();

        foreach (StageQuickCommand chip in chips)
        {
            var steps = new JsonArray();

            foreach (StageQuickStep step in chip.Steps)
            {
                var args = new JsonObject();

                // 인자 이름 순으로 — 결정적 출력이라야 저장할 때마다 diff가 안 생긴다.
                foreach ((string key, string value) in step.Arguments.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    args[key] = value;
                }

                steps.Add(new JsonObject
                {
                    ["command"] = step.DefinitionId,
                    ["args"] = args
                });
            }

            array.Add(new JsonObject
            {
                ["name"] = chip.DisplayName,
                ["steps"] = steps
            });
        }

        return array;
    }

    internal static List<StageQuickCommand>? ReadQuickCommands(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return null; // 키가 없다 = 손대지 않았다 → 기본 목록
        }

        var chips = new List<StageQuickCommand>();

        foreach (JsonNode? item in array)
        {
            if (item is not JsonObject chip)
            {
                throw new InvalidDataException("quickCommands 항목이 객체가 아닙니다.");
            }

            // 묶음(2026-08-24)은 steps 배열이다. 그 전에 저장된 칩은 한 단계가 칩 자체에
            // 펼쳐져 있으므로(command·args) 여기서 한 단계짜리 묶음으로 읽어 올린다 —
            // <b>이미 담아 둔 칩이 조용히 사라지면 그것이 곧 데이터 손실이다.</b>
            // 쓰기는 언제나 새 모양 하나다(두 벌로 쓰면 어느 쪽이 진실인지 못 말한다).
            var steps = new List<StageQuickStep>();

            List<JsonObject> stepNodes = chip["steps"] is JsonArray stepArray
                ? stepArray
                    .Select(entry => entry as JsonObject
                        ?? throw new InvalidDataException("quickCommands 단계가 객체가 아닙니다."))
                    .ToList()
                : [chip];

            foreach (JsonObject step in stepNodes)
            {
                if ((string?)step["command"] is not { Length: > 0 } definitionId)
                {
                    continue; // 커맨드 없는 단계는 낼 것이 없다
                }

                var arguments = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (KeyValuePair<string, JsonNode?> pair in step["args"]?.AsObject() ?? new JsonObject())
                {
                    if ((string?)pair.Value is { } value)
                    {
                        arguments[pair.Key] = value;
                    }
                }

                steps.Add(new StageQuickStep(definitionId, arguments));
            }

            if (steps.Count == 0)
            {
                continue; // 단계가 하나도 없는 칩은 누를 것이 없다
            }

            chips.Add(new StageQuickCommand(
                (string?)chip["name"] ?? steps[0].DefinitionId,
                steps));
        }

        return chips;
    }

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

            var entry = new JsonObject { ["name"] = curve.Name, ["keys"] = keys };

            if (curve.OwnerCommandId is { } owner)
            {
                entry["ownerCommandId"] = owner; // 커맨드 소유 곡선 — 없으면 보관함
            }

            array.Add(entry);
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
                Keys = keys,
                OwnerCommandId = (string?)curve["ownerCommandId"]
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
    IReadOnlyList<StageQuickCommand>? QuickCommands,
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
