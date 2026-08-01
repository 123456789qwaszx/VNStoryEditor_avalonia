using System.Text.Json.Nodes;
using Vn.Authoring.Model;

namespace Vn.Authoring.Serialization;

internal static class StoryNodeJson
{
    public static JsonObject Write(StoryNode node)
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

    public static StoryNode Read(JsonObject json)
    {
        string id = (string?)json["id"] ?? Identifier.Node();
        string name = (string?)json["name"] ?? "이름 없음";
        string kind = (string?)json["kind"] ?? "dialogue";

        StoryNode node = kind switch
        {
            "set" => ReadSetNode(json, id, name),
            "dialogue" => ReadDialogueNode(json, id, name),
            _ => throw new InvalidDataException($"지원하지 않는 노드 종류 '{kind}'입니다.")
        };

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

                if (transition.OpensBranch && node.BranchExits.TryGetValue(line.Id, out string? exit))
                {
                    transitionJson["exit"] = exit;
                }

                lineJson["condition"] = transitionJson;
            }

            lines.Add(lineJson);
        }

        json["lines"] = lines;
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

                line.Transition = new LineConditionTransition(kind, (string?)transitionJson["condition"]);

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
