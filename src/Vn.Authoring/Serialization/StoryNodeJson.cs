using System.Text.Json.Nodes;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Serialization;

internal static class StoryNodeJson
{
    public static JsonObject Write(StoryNode node)
    {
        string kind = node switch
        {
            SetNode => "set",
            DialogueNode => "dialogue",
            PresentationNode => "presentation",
            CommandSupplyNode => "commandSupply",
            _ => throw new InvalidDataException($"지원하지 않는 노드 종류 '{node.GetType().Name}'입니다.")
        };

        var json = new JsonObject
        {
            ["id"] = node.Id,
            ["kind"] = kind,
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
            case PresentationNode presentation:
                WritePresentationNode(presentation, json);
                break;
            case CommandSupplyNode supply:
                WriteCommandSupplyNode(supply, json);
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
            "presentation" => ReadPresentationNode(json, id, name),
            "commandSupply" => ReadCommandSupplyNode(json, id, name),
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

    /// <summary>
    /// 대사 노드는 <b>대본 Id와 LineId별 논리만</b> 쓴다. 화자와 대사는 대본 파일에 있다.
    /// 조건 갈래 출구는 갈래를 여는 줄의 항목 안에 함께 적어 둔다. 별도 배열로 나누면
    /// 줄이 사라졌을 때 짝 없는 출구가 파일에 남는다.
    /// </summary>
    private static void WriteDialogueNode(DialogueNode node, JsonObject json)
    {
        if (node.ScriptId is not null)
        {
            json["script"] = node.ScriptId;
        }

        var lines = new JsonArray();

        foreach (DialogueLineExtension extension in node.LineExtensions)
        {
            bool hasExit = extension.Transition?.OpensBranch == true &&
                node.BranchExits.ContainsKey(extension.LineId);

            // 아무것도 담지 않은 확장은 저장하지 않는다. 파일이 조용해야 diff가 읽힌다.
            if (extension.IsEmpty && !hasExit)
            {
                continue;
            }

            var lineJson = new JsonObject { ["lineId"] = extension.LineId };

            if (extension.Transition is { } transition)
            {
                var transitionJson = new JsonObject
                {
                    ["kind"] = DialogueResultJson.KindName(transition.Kind)
                };

                if (transition.ConditionId is not null)
                {
                    transitionJson["condition"] = transition.ConditionId;
                }

                if (transition.OptionId is not null)
                {
                    transitionJson["option"] = transition.OptionId;
                }

                if (hasExit)
                {
                    transitionJson["exit"] = node.BranchExits[extension.LineId];
                }

                lineJson["condition"] = transitionJson;
            }

            if (extension.SetOperations.Count > 0)
            {
                var operations = new JsonArray();

                foreach (SetOperation operation in extension.SetOperations)
                {
                    operations.Add(new JsonObject
                    {
                        ["variable"] = operation.Variable,
                        ["operator"] = SetOperators.Symbol(operation.Operator),
                        ["value"] = operation.Value
                    });
                }

                lineJson["set"] = operations;
            }

            lines.Add(lineJson);
        }

        json["lines"] = lines;
    }

    private static void WritePresentationNode(PresentationNode node, JsonObject json)
    {
        if (node.Source is { } source)
        {
            json["source"] = new JsonObject
            {
                ["resultId"] = source.ResultId,
                ["version"] = source.Version,
                ["contentHash"] = source.ContentHash
            };
        }

        if (node.SetupCommands.Count > 0)
        {
            var setup = new JsonArray();

            foreach (PresentationCommandInstance command in node.SetupCommands)
            {
                setup.Add(WriteCommand(command));
            }

            json["setup"] = setup;
        }

        var bindings = new JsonArray();

        foreach (PresentationLineBinding binding in node.Bindings)
        {
            var commands = new JsonArray();

            foreach (PresentationCommandInstance command in binding.Commands)
            {
                commands.Add(WriteCommand(command));
            }

            bindings.Add(new JsonObject
            {
                ["lineId"] = binding.LineId,
                ["commands"] = commands
            });
        }

        json["bindings"] = bindings;
    }

    private static void WriteCommandSupplyNode(CommandSupplyNode node, JsonObject json)
    {
        if (node.Categories.Count > 0)
        {
            var categories = new JsonArray();

            foreach (string categoryId in node.Categories)
            {
                categories.Add(categoryId);
            }

            json["categories"] = categories;
        }

        if (node.Presets.Count > 0)
        {
            var presets = new JsonArray();

            foreach (CommandPreset preset in node.Presets)
            {
                var presetJson = new JsonObject
                {
                    ["id"] = preset.Id,
                    ["name"] = preset.DisplayName,
                    ["command"] = preset.CommandDefinitionId
                };

                if (preset.ArgumentValues.Count > 0)
                {
                    var arguments = new JsonObject();

                    foreach ((string key, string value) in preset.ArgumentValues.OrderBy(
                                 pair => pair.Key,
                                 StringComparer.Ordinal))
                    {
                        arguments[key] = value;
                    }

                    presetJson["arguments"] = arguments;
                }

                if (preset.Note is not null)
                {
                    presetJson["note"] = preset.Note;
                }

                presets.Add(presetJson);
            }

            json["presets"] = presets;
        }
    }

    private static CommandSupplyNode ReadCommandSupplyNode(JsonObject json, string id, string name)
    {
        var node = new CommandSupplyNode(id, name);

        foreach (JsonNode? item in json["categories"]?.AsArray() ?? new JsonArray())
        {
            if ((string?)item is { Length: > 0 } categoryId)
            {
                node.Categories.Add(categoryId);
            }
        }

        foreach (JsonNode? item in json["presets"]?.AsArray() ?? new JsonArray())
        {
            if (item is not JsonObject presetJson)
            {
                continue;
            }

            string presetId = (string?)presetJson["id"]
                ?? throw new InvalidDataException($"CommandSupplyNode '{id}'의 프리셋에 id가 없습니다.");
            var preset = new CommandPreset(presetId)
            {
                DisplayName = (string?)presetJson["name"] ?? string.Empty,
                CommandDefinitionId = (string?)presetJson["command"] ?? string.Empty,
                Note = (string?)presetJson["note"]
            };

            if (presetJson["arguments"] is JsonObject arguments)
            {
                foreach ((string key, JsonNode? value) in arguments)
                {
                    preset.ArgumentValues[key] = (string?)value ?? string.Empty;
                }
            }

            node.Presets.Add(preset);
        }

        return node;
    }

    private static JsonObject WriteCommand(PresentationCommandInstance command)
    {
        var commandJson = new JsonObject
        {
            ["id"] = command.Id,
            ["definitionId"] = command.DefinitionId
        };

        if (command.PresetId is not null)
        {
            commandJson["preset"] = command.PresetId;
        }

        if (!command.IsEnabled)
        {
            commandJson["enabled"] = false;
        }

        if (command.Arguments.Count > 0)
        {
            var arguments = new JsonObject();
            foreach ((string key, string value) in command.Arguments.OrderBy(
                         pair => pair.Key,
                         StringComparer.Ordinal))
            {
                arguments[key] = value;
            }

            commandJson["arguments"] = arguments;
        }

        if (command.Note is not null)
        {
            commandJson["note"] = command.Note;
        }

        return commandJson;
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
        var node = new DialogueNode(id, name) { ScriptId = (string?)json["script"] };
        HashSet<string> lineIds = new(StringComparer.Ordinal);

        foreach (JsonNode? item in json["lines"]?.AsArray() ?? new JsonArray())
        {
            if (item is not JsonObject lineJson)
            {
                continue;
            }

            string lineId = (string?)lineJson["lineId"]
                ?? throw new InvalidDataException($"DialogueNode '{id}'의 줄 항목에 lineId가 없습니다.");

            if (!lineIds.Add(lineId))
            {
                throw new InvalidDataException(
                    $"DialogueNode '{id}'에서 LineId '{lineId}' 항목이 중복됩니다.");
            }

            var extension = new DialogueLineExtension(lineId);

            if (lineJson["condition"] is JsonObject transitionJson)
            {
                extension.Transition = new LineConditionTransition(
                    DialogueResultJson.ParseKind((string?)transitionJson["kind"]),
                    (string?)transitionJson["condition"],
                    (string?)transitionJson["option"]);

                if ((string?)transitionJson["exit"] is { } exit && extension.Transition.OpensBranch)
                {
                    node.BranchExits[lineId] = exit;
                }
            }

            foreach (JsonNode? operationItem in lineJson["set"]?.AsArray() ?? new JsonArray())
            {
                if (operationItem is JsonObject operationJson)
                {
                    extension.SetOperations.Add(new SetOperation
                    {
                        Variable = (string?)operationJson["variable"] ?? string.Empty,
                        Operator = SetOperators.Parse((string?)operationJson["operator"]),
                        Value = (string?)operationJson["value"] ?? string.Empty
                    });
                }
            }

            node.LineExtensions.Add(extension);
        }

        return node;
    }

    private static PresentationNode ReadPresentationNode(JsonObject json, string id, string name)
    {
        var node = new PresentationNode(id, name);

        if (json["source"] is JsonObject sourceJson)
        {
            node.Source = new DialogueResultReference(
                (string?)sourceJson["resultId"]
                    ?? throw new InvalidDataException($"PresentationNode '{id}'의 source에 resultId가 없습니다."),
                (int?)sourceJson["version"]
                    ?? throw new InvalidDataException($"PresentationNode '{id}'의 source에 version이 없습니다."),
                (string?)sourceJson["contentHash"]
                    ?? throw new InvalidDataException($"PresentationNode '{id}'의 source에 contentHash가 없습니다."));
        }

        foreach (JsonNode? setupItem in json["setup"]?.AsArray() ?? new JsonArray())
        {
            if (setupItem is JsonObject setupJson)
            {
                node.SetupCommands.Add(ReadCommand(setupJson, id));
            }
        }

        foreach (JsonNode? item in json["bindings"]?.AsArray() ?? new JsonArray())
        {
            if (item is not JsonObject bindingJson)
            {
                continue;
            }

            string lineId = (string?)bindingJson["lineId"]
                ?? throw new InvalidDataException($"PresentationNode '{id}'의 binding에 lineId가 없습니다.");
            var binding = new PresentationLineBinding(lineId);

            foreach (JsonNode? commandItem in bindingJson["commands"]?.AsArray() ?? new JsonArray())
            {
                if (commandItem is JsonObject commandJson)
                {
                    binding.Commands.Add(ReadCommand(commandJson, id));
                }
            }

            node.Bindings.Add(binding);
        }

        return node;
    }

    private static PresentationCommandInstance ReadCommand(JsonObject commandJson, string nodeId)
    {
        string commandId = (string?)commandJson["id"]
            ?? throw new InvalidDataException(
                $"PresentationNode '{nodeId}'의 command에 id가 없습니다.");
        string definitionId = (string?)commandJson["definitionId"] ?? string.Empty;
        var command = new PresentationCommandInstance(commandId, definitionId)
        {
            PresetId = (string?)commandJson["preset"],
            IsEnabled = (bool?)commandJson["enabled"] ?? true,
            Note = (string?)commandJson["note"]
        };

        if (commandJson["arguments"] is JsonObject arguments)
        {
            foreach ((string key, JsonNode? value) in arguments)
            {
                command.Arguments[key] = (string?)value ?? string.Empty;
            }
        }

        return command;
    }
}
