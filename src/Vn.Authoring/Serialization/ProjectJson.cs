using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vn.Authoring.Model;

namespace Vn.Authoring.Serialization;

/// <summary>
/// 프로젝트를 <c>.vnstory.json</c>으로 읽고 쓴다.
///
/// 형식을 손으로 다루는 이유는 두 가지다.
/// 첫째, 노드가 종류마다 다른 모양이라 자동 다형 직렬화에 맡기면 파일에
/// 어셈블리 이름 같은 것이 새어 나온다. 둘째, 이 파일은 git diff에서 읽히는 것이 목적이라
/// 키 순서와 빈 값 생략을 우리가 정해야 한다.
///
/// <b>줄바꿈은 언제나 LF, 인코딩은 BOM 없는 UTF-8이다.</b> 저작 도구가 만드는 파일이므로
/// 원본 형식을 보존할 대상이 아니라 우리가 형식을 정하는 대상이다. 환경마다 달라지면
/// 같은 편집이 사람마다 다른 diff를 만든다.
/// </summary>
public static class ProjectJson
{
    public const string FileExtension = ".vnstory.json";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        // 한글 화자·대사를 \uXXXX로 바꾸면 사람이 읽을 수 없다.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Write(StoryProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var root = new JsonObject
        {
            ["formatVersion"] = project.FormatVersion,
            ["title"] = project.Title
        };

        if (project.StartNodeId is not null)
        {
            root["startNode"] = project.StartNodeId;
        }

        var nodes = new JsonArray();

        foreach (StoryNode node in project.Nodes)
        {
            nodes.Add(WriteNode(node));
        }

        root["nodes"] = nodes;

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

        // 임시 파일에 다 쓰고 나서 옮긴다. 저장 도중 죽어도 반쪽짜리 원고가 남지 않는다.
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, json, new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// 문자열을 프로젝트로 읽는다. VnTool 프로젝트가 아니면 예외를 던진다.
    ///
    /// <b>모르는 파일을 관대하게 읽지 않는다.</b> 관대하게 읽으면 다른 도구의 JSON이
    /// "노드 0개짜리 프로젝트"로 열리고, 작가가 저장을 누르는 순간 원본이 빈 프로젝트로 덮어써진다.
    /// 열리지 않는 것은 불편하지만 되돌릴 수 있고, 덮어써진 원고는 되돌릴 수 없다.
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

        if (root["formatVersion"] is null || root["nodes"] is not JsonArray)
        {
            throw new InvalidDataException(
                "VnTool 프로젝트 파일이 아닙니다. " +
                $"VnTool 프로젝트는 formatVersion과 nodes를 가진 {FileExtension} 파일입니다.");
        }

        var project = new StoryProject
        {
            FormatVersion = (int?)root["formatVersion"] ?? StoryProject.CurrentFormatVersion,
            Title = (string?)root["title"] ?? "제목 없음",
            StartNodeId = (string?)root["startNode"]
        };

        if (project.FormatVersion > StoryProject.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"이 파일은 형식 버전 {project.FormatVersion}입니다. " +
                $"이 VnTool은 {StoryProject.CurrentFormatVersion}까지 읽을 수 있습니다.");
        }

        foreach (JsonNode? item in root["nodes"]!.AsArray())
        {
            if (item is JsonObject node)
            {
                project.Nodes.Add(ReadNode(node));
            }
        }

        return project;
    }

    public static StoryProject Load(string path) =>
        Read(File.ReadAllText(path, new UTF8Encoding(false)));

    // ── 쓰기 ────────────────────────────────────────────────────────────────

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

                // 이 갈래의 출구를 여는 줄 옆에 함께 적는다. 갈래와 출구가 한 자리에 있어야
                // 파일만 읽어도 "이 갈래가 끝나면 어디로 가는가"를 알 수 있다.
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
}
