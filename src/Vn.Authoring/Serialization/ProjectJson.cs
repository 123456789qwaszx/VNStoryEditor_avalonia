using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vn.Authoring.Model;

namespace Vn.Authoring.Serialization;

/// <summary>
/// 프로젝트 aggregate를 <c>.vnstory.json</c>으로 읽고 쓴다.
///
/// 형식 버전 2부터 프로젝트 안에 여러 StoryFile이 있고 각 파일이 노드를 소유한다.
/// 이 단계에서는 아직 물리적인 여러 파일로 나누지 않고 한 JSON 안에 파일 경계를 저장한다.
/// 형식 버전 1의 평면 nodes는 읽을 때 하나의 StoryFile로 승격한다.
///
/// 줄바꿈은 언제나 LF, 인코딩은 BOM 없는 UTF-8이다.
/// </summary>
public static class ProjectJson
{
    public const string FileExtension = ".vnstory.json";

    private const string LegacyFileId = "sf_main";
    private const string LegacyFileName = "기본 파일";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Write(StoryProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        ValidateIdentities(project);

        var root = new JsonObject
        {
            ["formatVersion"] = StoryProject.CurrentFormatVersion,
            ["title"] = project.Title
        };

        if (project.StartNodeId is not null)
        {
            root["startNode"] = project.StartNodeId;
        }

        var files = new JsonArray();

        foreach (StoryFile file in project.Files)
        {
            files.Add(WriteFile(file));
        }

        root["files"] = files;

        return root.ToJsonString(WriteOptions).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public static void Save(string path, StoryProject project)
    {
        string json = Write(project) + "\n";
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = path + ".tmp";
        File.WriteAllText(temporary, json, new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// 문자열을 프로젝트로 읽는다. VnTool 프로젝트가 아니면 예외를 던진다.
    /// </summary>
    public static StoryProject Read(string json)
    {
        JsonNode root;

        try
        {
            root = JsonNode.Parse(json)
                ?? throw new InvalidDataException("프로젝트 파일이 비어 있습니다.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"프로젝트 파일을 읽을 수 없습니다. {exception.Message}", exception);
        }

        if (root["formatVersion"] is null)
        {
            throw NotProjectFile();
        }

        int formatVersion = (int?)root["formatVersion"] ?? 0;

        if (formatVersion > StoryProject.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"이 파일은 형식 버전 {formatVersion}입니다. " +
                $"이 VnTool은 {StoryProject.CurrentFormatVersion}까지 읽을 수 있습니다.");
        }

        StoryProject project = formatVersion switch
        {
            1 => ReadVersion1(root),
            2 => ReadVersion2(root),
            _ => throw NotProjectFile()
        };

        ValidateIdentities(project);

        if (project.StartNodeId is not null && project.FindNode(project.StartNodeId) is null)
        {
            throw new InvalidDataException(
                $"시작 노드 '{project.StartNodeId}'를 프로젝트에서 찾을 수 없습니다.");
        }

        return project;
    }

    public static StoryProject Load(string path) =>
        Read(File.ReadAllText(path, new UTF8Encoding(false)));

    // ── 프로젝트 버전 읽기 ──────────────────────────────────────────────────

    private static StoryProject ReadVersion2(JsonNode root)
    {
        if (root["files"] is not JsonArray files)
        {
            throw NotProjectFile();
        }

        var project = NewProject(root);

        foreach (JsonNode? item in files)
        {
            if (item is not JsonObject fileJson)
            {
                continue;
            }

            project.Files.Add(ReadFile(fileJson));
        }

        return project;
    }

    /// <summary>평면 nodes 형식을 하나의 StoryFile로 승격한다.</summary>
    private static StoryProject ReadVersion1(JsonNode root)
    {
        if (root["nodes"] is not JsonArray nodes)
        {
            throw NotProjectFile();
        }

        var project = NewProject(root);
        var file = new StoryFile(LegacyFileId, LegacyFileName);

        foreach (JsonNode? item in nodes)
        {
            if (item is JsonObject node)
            {
                file.Nodes.Add(ReadNode(node));
            }
        }

        project.Files.Add(file);
        return project;
    }

    private static StoryProject NewProject(JsonNode root)
    {
        return new StoryProject
        {
            FormatVersion = StoryProject.CurrentFormatVersion,
            Title = (string?)root["title"] ?? "제목 없음",
            StartNodeId = (string?)root["startNode"]
        };
    }

    private static InvalidDataException NotProjectFile()
    {
        return new InvalidDataException(
            "VnTool 프로젝트 파일이 아닙니다. " +
            $"VnTool 프로젝트는 formatVersion과 files(버전 2) 또는 nodes(버전 1)를 가진 {FileExtension} 파일입니다.");
    }

    // ── 쓰기 ────────────────────────────────────────────────────────────────

    private static JsonObject WriteFile(StoryFile file)
    {
        var nodes = new JsonArray();

        foreach (StoryNode node in file.Nodes)
        {
            nodes.Add(WriteNode(node));
        }

        return new JsonObject
        {
            ["id"] = file.Id,
            ["name"] = file.Name,
            ["nodes"] = nodes
        };
    }

    private static JsonObject WriteNode(StoryNode node)
    {
        var json = new JsonObject
        {
            ["id"] = node.Id,
            ["kind"] = node is SetNode ? "set" : "dialogue",
            ["name"] = node.Name,
            ["layout"] = new JsonObject
            {
                ["x"] = Math.Round(node.Layout.X, 2),
                ["y"] = Math.Round(node.Layout.Y, 2)
            }
        };

        if (node.DefaultExitTargetNodeId is not null)
        {
            json["defaultExit"] = node.DefaultExitTargetNodeId;
        }

        switch (node)
        {
            case SetNode setNode:
                WriteSetNode(setNode, json);
                break;

            case DialogueNode dialogue:
                WriteDialogueNode(dialogue, json);
                break;
        }

        return json;
    }

    private static void WriteSetNode(SetNode node, JsonObject json)
    {
        if (node.Assignments.Count > 0)
        {
            var assignments = new JsonArray();

            foreach (VariableAssignment assignment in node.Assignments)
            {
                assignments.Add(new JsonObject
                {
                    ["variable"] = assignment.Variable,
                    ["value"] = assignment.Value
                });
            }

            json["assignments"] = assignments;
        }

        if (node.Conditions.Count > 0)
        {
            var conditions = new JsonArray();

            foreach (ConditionDefinition condition in node.Conditions)
            {
                conditions.Add(new JsonObject
                {
                    ["id"] = condition.Id,
                    ["name"] = condition.Name,
                    ["expression"] = condition.Expression
                });
            }

            json["conditions"] = conditions;
        }
    }

    private static void WriteDialogueNode(DialogueNode node, JsonObject json)
    {
        var lines = new JsonArray();

        foreach (LineBox line in node.Lines)
        {
            var lineJson = new JsonObject { ["id"] = line.Id };

            if (!string.IsNullOrEmpty(line.Speaker))
            {
                lineJson["speaker"] = line.Speaker;
            }

            lineJson["text"] = line.Text;

            if (line.Transition is { } transition)
            {
                var transitionJson = new JsonObject
                {
                    ["kind"] = transition.Kind switch
                    {
                        ConditionTransitionKind.BeginIf => "beginIf",
                        ConditionTransitionKind.BeginElseIf => "beginElseIf",
                        _ => "endIf"
                    }
                };

                if (transition.ConditionId is not null)
                {
                    transitionJson["condition"] = transition.ConditionId;
                }

                if (transition.OpensBranch &&
                    node.BranchExits.TryGetValue(line.Id, out string? exit))
                {
                    transitionJson["exit"] = exit;
                }

                lineJson["condition"] = transitionJson;
            }

            lines.Add(lineJson);
        }

        json["lines"] = lines;
    }

    // ── 읽기 ────────────────────────────────────────────────────────────────

    private static StoryFile ReadFile(JsonObject json)
    {
        string id = (string?)json["id"] ?? Identifier.File();
        string name = (string?)json["name"] ?? "이름 없는 파일";
        var file = new StoryFile(id, name);

        foreach (JsonNode? item in json["nodes"]?.AsArray() ?? new JsonArray())
        {
            if (item is JsonObject node)
            {
                file.Nodes.Add(ReadNode(node));
            }
        }

        return file;
    }

    private static StoryNode ReadNode(JsonObject json)
    {
        string id = (string?)json["id"] ?? Identifier.Node();
        string name = (string?)json["name"] ?? "이름 없음";
        string kind = (string?)json["kind"] ?? "dialogue";

        StoryNode node = kind == "set"
            ? ReadSetNode(json, id, name)
            : ReadDialogueNode(json, id, name);

        node.DefaultExitTargetNodeId = (string?)json["defaultExit"];

        if (json["layout"] is JsonObject layout)
        {
            node.Layout = new NodeLayout
            {
                X = (double?)layout["x"] ?? 0,
                Y = (double?)layout["y"] ?? 0
            };
        }

        return node;
    }

    private static SetNode ReadSetNode(JsonObject json, string id, string name)
    {
        var node = new SetNode(id, name);

        foreach (JsonNode? item in json["assignments"]?.AsArray() ?? new JsonArray())
        {
            if (item is JsonObject assignment)
            {
                node.Assignments.Add(new VariableAssignment
                {
                    Variable = (string?)assignment["variable"] ?? string.Empty,
                    Value = (string?)assignment["value"] ?? string.Empty
                });
            }
        }

        foreach (JsonNode? item in json["conditions"]?.AsArray() ?? new JsonArray())
        {
            if (item is JsonObject condition)
            {
                node.Conditions.Add(new ConditionDefinition((string?)condition["id"])
                {
                    Name = (string?)condition["name"] ?? string.Empty,
                    Expression = (string?)condition["expression"] ?? string.Empty
                });
            }
        }

        return node;
    }

    private static DialogueNode ReadDialogueNode(JsonObject json, string id, string name)
    {
        var node = new DialogueNode(id, name);

        foreach (JsonNode? item in json["lines"]?.AsArray() ?? new JsonArray())
        {
            if (item is not JsonObject lineJson)
            {
                continue;
            }

            var line = new LineBox((string?)lineJson["id"])
            {
                Speaker = (string?)lineJson["speaker"] ?? string.Empty,
                Text = (string?)lineJson["text"] ?? string.Empty
            };

            if (lineJson["condition"] is JsonObject transitionJson)
            {
                ConditionTransitionKind kind = (string?)transitionJson["kind"] switch
                {
                    "beginIf" => ConditionTransitionKind.BeginIf,
                    "beginElseIf" => ConditionTransitionKind.BeginElseIf,
                    _ => ConditionTransitionKind.EndIf
                };

                line.Transition = new LineConditionTransition(
                    kind,
                    (string?)transitionJson["condition"]);

                if ((string?)transitionJson["exit"] is { } exit && line.Transition.OpensBranch)
                {
                    node.BranchExits[line.Id] = exit;
                }
            }

            node.Lines.Add(line);
        }

        return node;
    }

    private static void ValidateIdentities(StoryProject project)
    {
        HashSet<string> fileIds = new(StringComparer.Ordinal);
        HashSet<string> nodeIds = new(StringComparer.Ordinal);

        foreach (StoryFile file in project.Files)
        {
            if (!fileIds.Add(file.Id))
            {
                throw new InvalidDataException($"StoryFile Id '{file.Id}'가 중복됩니다.");
            }

            foreach (StoryNode node in file.Nodes)
            {
                if (!nodeIds.Add(node.Id))
                {
                    throw new InvalidDataException($"노드 Id '{node.Id}'가 프로젝트 전체에서 중복됩니다.");
                }
            }
        }
    }
}
