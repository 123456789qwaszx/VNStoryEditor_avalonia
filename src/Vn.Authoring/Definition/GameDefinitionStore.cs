using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vn.Authoring.Definition;

/// <summary>
/// <c>game.definition.json</c>에 쓰는 유일한 경로 — 지금은 <c>speakers</c>만 쓴다 (X5, D-4).
///
/// 설정노드의 Speaker 등록 UI는 편집 창구일 뿐이고 저장은 언제나 이 파일이다.
/// 도구 안에 사본을 두면 원천이 둘이 되어, 파일을 손으로 고친 날 둘 다 오류 없이
/// 어긋난다. 다른 키(변수·커맨드 카탈로그 등)는 그대로 보존하고 <c>speakers</c>만 바꾼다.
/// </summary>
public static class GameDefinitionStore
{
    /// <summary>
    /// 없으면 뼈대를 깐다 — 새 프로젝트가 정의 파일 없이 시작해, 스탯을 시트에 적자마자
    /// "정의에 없다" 경고부터 맞는 일을 막는다(실사례). 있는 파일은 불가침이다.
    /// </summary>
    public static bool EnsureBeside(string projectPath)
    {
        string path = GameDefinition.PathFor(projectPath);

        if (File.Exists(path))
        {
            return false;
        }

        Serialization.JsonSupport.WriteAtomic(path,
            """
            {
              "_안내": "게임이 아는 어휘의 원천입니다. 스탯의 존재는 variables가, 값(초기/최소/최대)은 챕터의 스탯 시트가 소유합니다. 예: { \"name\": \"trust\", \"type\": \"number\", \"description\": \"신뢰\" }",
              "variables": [],
              "speakers": []
            }
            """ + "\n");

        return true;
    }

    /// <summary>
    /// <c>variables</c>에 <b>없는 이름만</b> 더한다 — 검증 보고의 [등록] 단추가 부른다.
    /// 시트를 자동으로 믿고 쓰면 오타까지 게임 어휘가 되므로, 이 쓰기는 언제나
    /// 사람이 누른 순간에만 일어난다. 이미 있는 항목과 다른 키는 건드리지 않는다.
    /// </summary>
    /// <returns>실제로 더한 개수. 0이면 파일도 안 쓴다.</returns>
    public static int AddVariables(string projectPath, IReadOnlyList<VariableSpec> additions)
    {
        ArgumentNullException.ThrowIfNull(additions);

        string path = GameDefinition.PathFor(projectPath);
        JsonObject root = ReadRoot(path, "variables");

        if (root["variables"] is not JsonArray array)
        {
            array = new JsonArray();
            root["variables"] = array;
        }

        var existing = new HashSet<string>(
            array.OfType<JsonObject>()
                .Select(item => item["name"]?.GetValue<string>() ?? string.Empty),
            StringComparer.Ordinal);

        int added = 0;

        foreach (VariableSpec variable in additions)
        {
            if (string.IsNullOrWhiteSpace(variable.Name) || !existing.Add(variable.Name.Trim()))
            {
                continue;
            }

            var entry = new JsonObject
            {
                ["name"] = variable.Name.Trim(),
                ["type"] = string.IsNullOrWhiteSpace(variable.Type) ? "number" : variable.Type.Trim()
            };

            if (!string.IsNullOrWhiteSpace(variable.Description))
            {
                entry["description"] = variable.Description.Trim();
            }

            array.Add(entry);
            added++;
        }

        if (added > 0)
        {
            WriteRoot(path, root);
        }

        return added;
    }

    public static void SaveSpeakers(string projectPath, IReadOnlyList<SpeakerSpec> speakers)
    {
        ArgumentNullException.ThrowIfNull(speakers);

        string path = GameDefinition.PathFor(projectPath);
        JsonObject root = ReadRoot(path, "speakers");

        var array = new JsonArray();

        foreach (SpeakerSpec speaker in speakers)
        {
            if (string.IsNullOrWhiteSpace(speaker.Name))
            {
                continue;
            }

            array.Add(new JsonObject
            {
                ["name"] = speaker.Name.Trim(),
                ["characterId"] = speaker.CharacterId.Trim()
            });
        }

        root["speakers"] = array;
        WriteRoot(path, root);
    }

    private static JsonObject ReadRoot(string path, string keyBeingWritten)
    {
        try
        {
            return File.Exists(path)
                ? JsonNode.Parse(
                        File.ReadAllText(path),
                        documentOptions: new JsonDocumentOptions
                        {
                            CommentHandling = JsonCommentHandling.Skip,
                            AllowTrailingCommas = true
                        }) as JsonObject ?? new JsonObject()
                : new JsonObject();
        }
        catch (JsonException)
        {
            // 깨진 파일 위에 덮어써서 다른 키(카탈로그 등)를 잃는 것보다 거부가 낫다.
            throw new InvalidDataException(
                $"{GameDefinition.FileName}이 올바른 JSON이 아니라 {keyBeingWritten}를 쓸 수 없습니다. " +
                "파일을 먼저 고쳐 주세요.");
        }
    }

    private static void WriteRoot(string path, JsonObject root)
    {
        Serialization.JsonSupport.WriteAtomic(
            path,
            root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }) + "\n");
    }
}
