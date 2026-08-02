using System.Text.Json.Nodes;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Serialization;

/// <summary>
/// DialogueResult의 <b>본문</b>과 파일 표현.
///
/// 본문(<see cref="WriteBody(DialogueDraft)"/>)에는 identity와 발행 시각이 들어가지 않는다.
/// 그 표현이 그대로 내용 해시의 입력이기 때문이다. 해시 전용 정규 표현을 따로 만들면
/// 저장 형식이 바뀔 때 둘이 조용히 어긋나고, 그러면 "같은 내용"의 정의가 두 개가 된다.
/// </summary>
internal static class DialogueResultJson
{
    public static JsonObject WriteBody(DialogueDraft draft)
    {
        return WriteBody(
            draft.SourceNodeId,
            draft.SourceNodeName,
            draft.SourceScriptId,
            draft.SourceScriptRevision,
            draft.Locale,
            draft.Lines,
            draft.Assignments,
            draft.DefaultExitTargetNodeId);
    }

    public static JsonObject WriteBody(DialogueResult result)
    {
        return WriteBody(
            result.SourceNodeId,
            result.SourceNodeName,
            result.SourceScriptId,
            result.SourceScriptRevision,
            result.Locale,
            result.Lines,
            result.Assignments,
            result.DefaultExitTargetNodeId);
    }

    public static JsonObject Write(DialogueResult result)
    {
        JsonObject json = WriteBody(result);
        json["resultId"] = result.Identity.ResultId;
        json["version"] = result.Identity.Version;
        json["schemaVersion"] = result.Identity.SchemaVersion;
        json["contentHash"] = result.Identity.ContentHash;
        json["publishedAt"] = result.PublishedAt.ToUniversalTime().ToString("o");
        return json;
    }

    public static DialogueResult Read(JsonObject json)
    {
        var identity = new ResultIdentity(
            (string?)json["resultId"] ?? throw new InvalidDataException("대사 결과에 resultId가 없습니다."),
            (int?)json["version"] ?? throw new InvalidDataException("대사 결과에 version이 없습니다."),
            (int?)json["schemaVersion"] ?? 0,
            (string?)json["contentHash"] ?? throw new InvalidDataException("대사 결과에 contentHash가 없습니다."));

        var lines = new List<DialogueResultLine>();

        foreach (JsonNode? item in json["lines"]?.AsArray() ?? new JsonArray())
        {
            if (item is not JsonObject lineJson)
            {
                continue;
            }

            lines.Add(new DialogueResultLine(
                (int?)lineJson["index"] ?? lines.Count,
                (string?)lineJson["lineId"]
                    ?? throw new InvalidDataException($"대사 결과 '{identity.Label}'의 줄에 lineId가 없습니다."),
                (int?)lineJson["revision"] ?? 1,
                (string?)lineJson["speaker"] ?? string.Empty,
                (string?)lineJson["text"] ?? string.Empty,
                ReadTransition(lineJson["condition"] as JsonObject),
                (string?)lineJson["branchExit"]));
        }

        var assignments = new List<DialogueResultAssignment>();

        foreach (JsonNode? item in json["assignments"]?.AsArray() ?? new JsonArray())
        {
            if (item is JsonObject assignment)
            {
                assignments.Add(new DialogueResultAssignment(
                    (string?)assignment["variable"] ?? string.Empty,
                    (string?)assignment["value"] ?? string.Empty));
            }
        }

        return new DialogueResult(
            identity,
            (string?)json["sourceNode"] ?? string.Empty,
            (string?)json["sourceNodeName"] ?? string.Empty,
            (string?)json["sourceScript"],
            (int?)json["sourceScriptRevision"] ?? 0,
            (string?)json["locale"] ?? Script.ScriptDocument.DefaultLocale,
            lines,
            assignments,
            (string?)json["defaultExit"],
            ReadTimestamp(json["publishedAt"]));
    }

    private static JsonObject WriteBody(
        string sourceNodeId,
        string sourceNodeName,
        string? sourceScriptId,
        int sourceScriptRevision,
        string locale,
        IReadOnlyList<DialogueResultLine> lines,
        IReadOnlyList<DialogueResultAssignment> assignments,
        string? defaultExitTargetNodeId)
    {
        var lineArray = new JsonArray();

        foreach (DialogueResultLine line in lines)
        {
            var item = new JsonObject
            {
                ["index"] = line.Index,
                ["lineId"] = line.LineId,
                ["revision"] = line.Revision
            };

            if (line.CharacterName.Length > 0)
            {
                item["speaker"] = line.CharacterName;
            }

            item["text"] = line.Text;

            if (line.Transition is { } transition)
            {
                var conditionJson = new JsonObject { ["kind"] = KindName(transition.Kind) };

                if (transition.ConditionId is not null)
                {
                    conditionJson["condition"] = transition.ConditionId;
                }

                if (transition.ConditionName is not null)
                {
                    conditionJson["name"] = transition.ConditionName;
                }

                if (transition.Expression is not null)
                {
                    conditionJson["expression"] = transition.Expression;
                }

                item["condition"] = conditionJson;
            }

            if (line.BranchExitTargetNodeId is not null)
            {
                item["branchExit"] = line.BranchExitTargetNodeId;
            }

            lineArray.Add(item);
        }

        var assignmentArray = new JsonArray();

        foreach (DialogueResultAssignment assignment in assignments)
        {
            assignmentArray.Add(new JsonObject
            {
                ["variable"] = assignment.Variable,
                ["value"] = assignment.Value
            });
        }

        var json = new JsonObject
        {
            ["sourceNode"] = sourceNodeId,
            ["sourceNodeName"] = sourceNodeName,
            ["locale"] = locale,
            ["sourceScriptRevision"] = sourceScriptRevision
        };

        if (sourceScriptId is not null)
        {
            json["sourceScript"] = sourceScriptId;
        }

        if (defaultExitTargetNodeId is not null)
        {
            json["defaultExit"] = defaultExitTargetNodeId;
        }

        json["lines"] = lineArray;

        if (assignmentArray.Count > 0)
        {
            json["assignments"] = assignmentArray;
        }

        return json;
    }

    private static DialogueResultTransition? ReadTransition(JsonObject? json)
    {
        if (json is null)
        {
            return null;
        }

        return new DialogueResultTransition(
            ParseKind((string?)json["kind"]),
            (string?)json["condition"],
            (string?)json["name"],
            (string?)json["expression"]);
    }

    internal static string KindName(ConditionTransitionKind kind) => kind switch
    {
        ConditionTransitionKind.BeginIf => "beginIf",
        ConditionTransitionKind.BeginElseIf => "beginElseIf",
        _ => "endIf"
    };

    internal static ConditionTransitionKind ParseKind(string? kind) => kind switch
    {
        "beginIf" => ConditionTransitionKind.BeginIf,
        "beginElseIf" => ConditionTransitionKind.BeginElseIf,
        _ => ConditionTransitionKind.EndIf
    };

    internal static DateTimeOffset ReadTimestamp(JsonNode? node)
    {
        return DateTimeOffset.TryParse(
            (string?)node,
            null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out DateTimeOffset value)
            ? value
            : DateTimeOffset.UnixEpoch;
    }
}

/// <summary>PresentationResult의 본문과 파일 표현. 규칙은 대사 결과와 같다.</summary>
internal static class PresentationResultJson
{
    public static JsonObject WriteBody(PresentationDraft draft)
    {
        return WriteBody(
            draft.SourceNodeId,
            draft.SourceNodeName,
            draft.Source!.Value,
            draft.Bindings);
    }

    public static JsonObject WriteBody(PresentationResult result)
    {
        return WriteBody(result.SourceNodeId, result.SourceNodeName, result.Source, result.Bindings);
    }

    public static JsonObject Write(PresentationResult result)
    {
        JsonObject json = WriteBody(result);
        json["resultId"] = result.Identity.ResultId;
        json["version"] = result.Identity.Version;
        json["schemaVersion"] = result.Identity.SchemaVersion;
        json["contentHash"] = result.Identity.ContentHash;
        json["publishedAt"] = result.PublishedAt.ToUniversalTime().ToString("o");
        return json;
    }

    public static PresentationResult Read(JsonObject json)
    {
        var identity = new ResultIdentity(
            (string?)json["resultId"] ?? throw new InvalidDataException("연출 결과에 resultId가 없습니다."),
            (int?)json["version"] ?? throw new InvalidDataException("연출 결과에 version이 없습니다."),
            (int?)json["schemaVersion"] ?? 0,
            (string?)json["contentHash"] ?? throw new InvalidDataException("연출 결과에 contentHash가 없습니다."));

        if (json["source"] is not JsonObject sourceJson)
        {
            throw new InvalidDataException(
                $"연출 결과 '{identity.Label}'에 대상 대사 결과(source)가 없습니다.");
        }

        var source = new DialogueResultReference(
            (string?)sourceJson["resultId"]
                ?? throw new InvalidDataException($"연출 결과 '{identity.Label}'의 source에 resultId가 없습니다."),
            (int?)sourceJson["version"]
                ?? throw new InvalidDataException($"연출 결과 '{identity.Label}'의 source에 version이 없습니다."),
            (string?)sourceJson["contentHash"]
                ?? throw new InvalidDataException($"연출 결과 '{identity.Label}'의 source에 contentHash가 없습니다."));

        var bindings = new List<PresentationResultBinding>();

        foreach (JsonNode? item in json["bindings"]?.AsArray() ?? new JsonArray())
        {
            if (item is not JsonObject bindingJson)
            {
                continue;
            }

            var commands = new List<PresentationResultCommand>();

            foreach (JsonNode? commandItem in bindingJson["commands"]?.AsArray() ?? new JsonArray())
            {
                if (commandItem is not JsonObject commandJson)
                {
                    continue;
                }

                var arguments = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach ((string key, JsonNode? value) in commandJson["arguments"]?.AsObject()
                             ?? new JsonObject())
                {
                    arguments[key] = (string?)value ?? string.Empty;
                }

                commands.Add(new PresentationResultCommand(
                    (string?)commandJson["id"]
                        ?? throw new InvalidDataException($"연출 결과 '{identity.Label}'의 명령에 id가 없습니다."),
                    (string?)commandJson["definitionId"] ?? string.Empty,
                    arguments,
                    (string?)commandJson["note"]));
            }

            bindings.Add(new PresentationResultBinding(
                (string?)bindingJson["lineId"]
                    ?? throw new InvalidDataException($"연출 결과 '{identity.Label}'의 binding에 lineId가 없습니다."),
                commands,
                (bool?)bindingJson["orphan"] ?? false));
        }

        return new PresentationResult(
            identity,
            (string?)json["sourceNode"] ?? string.Empty,
            (string?)json["sourceNodeName"] ?? string.Empty,
            source,
            bindings,
            DialogueResultJson.ReadTimestamp(json["publishedAt"]));
    }

    private static JsonObject WriteBody(
        string sourceNodeId,
        string sourceNodeName,
        DialogueResultReference source,
        IReadOnlyList<PresentationResultBinding> bindings)
    {
        var bindingArray = new JsonArray();

        foreach (PresentationResultBinding binding in bindings)
        {
            var commands = new JsonArray();

            foreach (PresentationResultCommand command in binding.Commands)
            {
                var commandJson = new JsonObject
                {
                    ["id"] = command.CommandId,
                    ["definitionId"] = command.DefinitionId
                };

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

                commands.Add(commandJson);
            }

            var bindingJson = new JsonObject
            {
                ["lineId"] = binding.LineId,
                ["commands"] = commands
            };

            if (binding.IsOrphan)
            {
                bindingJson["orphan"] = true;
            }

            bindingArray.Add(bindingJson);
        }

        return new JsonObject
        {
            ["sourceNode"] = sourceNodeId,
            ["sourceNodeName"] = sourceNodeName,
            ["source"] = new JsonObject
            {
                ["resultId"] = source.ResultId,
                ["version"] = source.Version,
                ["contentHash"] = source.ContentHash
            },
            ["bindings"] = bindingArray
        };
    }
}

/// <summary>
/// 발행 결과 전체를 담는 파일.
///
/// 결과마다 파일을 하나씩 만들지 않는다. 결과는 불변이고 추가만 되므로 한 파일을 원자적으로
/// 교체하는 편이 단순하고, 부분적으로만 갱신된 결과 집합이 디스크에 남는 상태를 아예 만들지 않는다.
/// 파일이 커져 곤란해지면 그때 계보별 디렉터리로 나눈다. 그 경계는 이 파일 하나뿐이다.
/// </summary>
public static class ResultStoreJson
{
    public const int CurrentFormatVersion = 1;
    public const string FileExtension = ".vnresults.json";
    public const string DefaultFileName = "results" + FileExtension;

    public static string Write(ResultRepository results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var dialogue = new JsonArray();

        foreach (DialogueResult result in results.DialogueResults)
        {
            dialogue.Add(DialogueResultJson.Write(result));
        }

        var presentation = new JsonArray();

        foreach (PresentationResult result in results.PresentationResults)
        {
            presentation.Add(PresentationResultJson.Write(result));
        }

        return JsonSupport.ToDeterministicText(new JsonObject
        {
            ["formatVersion"] = CurrentFormatVersion,
            ["dialogueResults"] = dialogue,
            ["presentationResults"] = presentation
        });
    }

    public static ResultRepository Read(string json)
    {
        JsonObject root = JsonSupport.ParseObject(json, "발행 결과");
        int version = (int?)root["formatVersion"] ?? 0;

        if (version != CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"발행 결과 형식 버전 {version}은 지원하지 않습니다. 현재 버전은 {CurrentFormatVersion}입니다.");
        }

        var results = new ResultRepository();

        foreach (JsonNode? item in root["dialogueResults"]?.AsArray() ?? new JsonArray())
        {
            if (item is JsonObject result)
            {
                results.Add(DialogueResultJson.Read(result));
            }
        }

        foreach (JsonNode? item in root["presentationResults"]?.AsArray() ?? new JsonArray())
        {
            if (item is JsonObject result)
            {
                results.Add(PresentationResultJson.Read(result));
            }
        }

        return results;
    }
}
