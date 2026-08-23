using System.Text.Json.Nodes;
using Vn.Authoring.Flow;
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
                var entry = new JsonObject
                {
                    ["variable"] = assignment.Variable,
                    ["value"] = assignment.Value
                };

                // 기본 타입(float)은 쓰지 않는다 — 기존 프로젝트 파일이 바뀌지 않는다.
                if (!string.Equals(assignment.Type, VariableAssignment.FloatType, StringComparison.Ordinal))
                {
                    entry["type"] = assignment.Type;
                }

                // 슬라이더 범위도 등록했을 때만 쓴다(기본 -5~+5는 생략).
                if (assignment.SliderMin is { } sliderMin)
                {
                    entry["sliderMin"] = sliderMin;
                }

                if (assignment.SliderMax is { } sliderMax)
                {
                    entry["sliderMax"] = sliderMax;
                }

                assignments.Add(entry);
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

        if (node.ExcelEpisodeId is not null)
        {
            json["excelEpisode"] = node.ExcelEpisodeId;
        }

        var lines = new JsonArray();

        foreach (DialogueLineExtension extension in node.LineExtensions)
        {
            bool hasExit = extension.Transitions.Any(transition => transition.OpensBranch) &&
                node.BranchExits.ContainsKey(extension.LineId);

            // 아무것도 담지 않은 확장은 저장하지 않는다. 파일이 조용해야 diff가 읽힌다.
            if (extension.IsEmpty && !hasExit)
            {
                continue;
            }

            var lineJson = new JsonObject { ["lineId"] = extension.LineId };

            // 흔한 경우(전환 하나)는 `condition` 한 칸으로 그대로 쓴다 — 파일이 조용해야
            // diff가 읽힌다. 둘 이상이 몰린 줄(겹쳐 닫기·연달아 열기)만 `conditions` 배열로
            // 나간다. 읽을 때는 둘 다 받으므로 옛 판이 그대로 열린다.
            if (extension.Transitions.Count == 1)
            {
                lineJson["condition"] = TransitionJson(extension.Transitions[0], node, extension, hasExit);
            }
            else if (extension.Transitions.Count > 1)
            {
                var array = new JsonArray();

                foreach (LineConditionTransition transition in extension.Transitions)
                {
                    array.Add(TransitionJson(transition, node, extension, hasExit));
                }

                lineJson["conditions"] = array;
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

        // 마지막 줄 뒤의 전환 (2026-08-24) — 대사 없는 조건 블록이 대본의 끝에 있을 때.
        // 줄이 아니라 노드에 붙으므로 `lines` 밖에 선다. 없으면 칸도 안 만든다.
        if (node.TrailingTransitions.Count > 0)
        {
            var trailing = new JsonArray();

            int opened = 0;

            foreach (LineConditionTransition transition in node.TrailingTransitions)
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

                // ⚠ 앵커를 <b>글자로 적어 둔다</b> — 규칙(몇 번째인가)이 나중에 바뀌어도
                // 이미 매달린 출구가 제 갈래를 계속 찾는다. 읽는 쪽도 이 글자를 쓴다.
                if (transition.OpensBranch)
                {
                    string anchor = BranchAnchor.ForTrailing(opened++);
                    transitionJson["anchor"] = anchor;

                    if (node.BranchExits.TryGetValue(anchor, out string? exit))
                    {
                        transitionJson["exit"] = exit;
                    }
                }

                trailing.Add(transitionJson);
            }

            json["trailing"] = trailing;
        }

        // 선택지별 자유 씬 배선 (v9) — 열쇠가 대본의 줄이 아니라 <b>문구</b>다. 그래서
        // 줄 항목 안이 아니라 노드에 따로 선다(대본이 바뀌어도 이 배선은 그대로다).
        if (node.ChoiceExits.Count > 0)
        {
            var choiceExits = new JsonObject();

            foreach ((string choice, string target) in node.ChoiceExits.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                choiceExits[choice] = target;
            }

            json["choiceExits"] = choiceExits;
        }

        // 에피소드 엑셀의 행 신원 (v4) — 인덱스 → LineId. 대본 파일에 되쓰는 대신 여기 산다.
        if (node.ExcelLineMap.Count > 0)
        {
            var excelLines = new JsonObject();

            foreach ((int index, string lineId) in node.ExcelLineMap.OrderBy(pair => pair.Key))
            {
                excelLines[index.ToString(System.Globalization.CultureInfo.InvariantCulture)] = lineId;
            }

            json["excelLines"] = excelLines;
        }
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

            var bindingJson = new JsonObject
            {
                ["lineId"] = binding.LineId,
                ["commands"] = commands
            };

            if (binding.Markers.Count > 0)
            {
                var markers = new JsonArray();

                foreach (PresentationLineMarker marker in binding.Markers)
                {
                    markers.Add(new JsonObject
                    {
                        ["offset"] = marker.CharacterOffset,
                        ["firstCommand"] = marker.FirstCommandIndex
                    });
                }

                bindingJson["markers"] = markers;
            }

            bindings.Add(bindingJson);
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
                    Value = (string?)assignment["value"] ?? string.Empty,
                    Type = (string?)assignment["type"] ?? VariableAssignment.FloatType,
                    SliderMin = (double?)assignment["sliderMin"],
                    SliderMax = (double?)assignment["sliderMax"]
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
        var node = new DialogueNode(id, name)
        {
            ScriptId = (string?)json["script"],
            ExcelEpisodeId = (string?)json["excelEpisode"]
        };
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

            foreach (JsonObject transitionJson in Transitions(lineJson))
            {
                var transition = new LineConditionTransition(
                    DialogueResultJson.ParseKind((string?)transitionJson["kind"]),
                    (string?)transitionJson["condition"],
                    (string?)transitionJson["option"]);

                extension.Transitions.Add(transition);

                if ((string?)transitionJson["exit"] is { } exit && transition.OpensBranch)
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

        foreach (JsonNode? item in json["trailing"]?.AsArray() ?? new JsonArray())
        {
            if (item is not JsonObject transitionJson)
            {
                continue;
            }

            var transition = new LineConditionTransition(
                DialogueResultJson.ParseKind((string?)transitionJson["kind"]),
                (string?)transitionJson["condition"],
                (string?)transitionJson["option"]);

            node.TrailingTransitions.Add(transition);

            // 끝의 빈 갈래도 출구(detour)를 가질 수 있다 — 열쇠는 그 갈래의 앵커다.
            if ((string?)transitionJson["exit"] is { } exit &&
                (string?)transitionJson["anchor"] is { } anchor &&
                transition.OpensBranch)
            {
                node.BranchExits[anchor] = exit;
            }
        }

        if (json["choiceExits"] is JsonObject choiceExits)
        {
            foreach ((string choice, JsonNode? value) in choiceExits)
            {
                node.ChoiceExits[choice] = (string?)value
                    ?? throw new InvalidDataException(
                        $"DialogueNode '{id}'의 choiceExits['{choice}']에 대상 노드가 없습니다.");
            }
        }

        if (json["excelLines"] is JsonObject excelLines)
        {
            foreach ((string key, JsonNode? value) in excelLines)
            {
                if (!int.TryParse(key, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int index))
                {
                    throw new InvalidDataException(
                        $"DialogueNode '{id}'의 excelLines 키 '{key}'가 정수 인덱스가 아닙니다.");
                }

                node.ExcelLineMap[index] = (string?)value
                    ?? throw new InvalidDataException(
                        $"DialogueNode '{id}'의 excelLines[{key}]에 LineId가 없습니다.");
            }
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

            foreach (JsonNode? markerItem in bindingJson["markers"]?.AsArray() ?? new JsonArray())
            {
                if (markerItem is JsonObject markerJson)
                {
                    binding.Markers.Add(new PresentationLineMarker
                    {
                        CharacterOffset = (int?)markerJson["offset"] ?? 0,
                        FirstCommandIndex = (int?)markerJson["firstCommand"] ?? 0
                    });
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

    /// <summary>전환 하나를 JSON으로. 갈래 출구는 <b>여는 전환</b>에만 붙는다.</summary>
    private static JsonObject TransitionJson(
        LineConditionTransition transition, DialogueNode node, DialogueLineExtension extension, bool hasExit)
    {
        var json = new JsonObject
        {
            ["kind"] = DialogueResultJson.KindName(transition.Kind)
        };

        if (transition.ConditionId is not null)
        {
            json["condition"] = transition.ConditionId;
        }

        if (transition.OptionId is not null)
        {
            json["option"] = transition.OptionId;
        }

        if (hasExit && transition.OpensBranch)
        {
            json["exit"] = node.BranchExits[extension.LineId];
        }

        return json;
    }

    /// <summary>
    /// 줄의 전환 목록 — 새 <c>conditions</c> 배열이 있으면 그것, 없으면 옛 <c>condition</c>
    /// 한 칸. 전환이 하나뿐이던 시절의 파일이 그대로 열린다.
    /// </summary>
    private static IEnumerable<JsonObject> Transitions(JsonObject lineJson)
    {
        if (lineJson["conditions"] is JsonArray array)
        {
            return array.OfType<JsonObject>();
        }

        return lineJson["condition"] is JsonObject single ? [single] : [];
    }
}
