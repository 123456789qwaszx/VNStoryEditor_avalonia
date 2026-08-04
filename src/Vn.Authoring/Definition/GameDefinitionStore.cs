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
    public static void SaveSpeakers(string projectPath, IReadOnlyList<SpeakerSpec> speakers)
    {
        ArgumentNullException.ThrowIfNull(speakers);

        string path = GameDefinition.PathFor(projectPath);
        JsonObject root;

        try
        {
            root = File.Exists(path)
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
                $"{GameDefinition.FileName}이 올바른 JSON이 아니라 speakers를 쓸 수 없습니다. " +
                "파일을 먼저 고쳐 주세요.");
        }

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

        Serialization.JsonSupport.WriteAtomic(
            path,
            root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }) + "\n");
    }
}
