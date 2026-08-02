using System.Text.Json.Nodes;
using Vn.Authoring.Script;

namespace Vn.Authoring.Serialization;

/// <summary>
/// 대본 산출물 하나를 결정적으로 직렬화한다.
///
/// 한 파일 안에 두 산출물이 함께 들어간다.
/// <code>
/// lines    줄 정체성과 현재 순서 (산출물 1)
/// locales  locale별 화자·대사     (산출물 2)
/// </code>
/// 나누어 저장하면 둘의 버전이 어긋날 수 있고, 어긋난 순간 LineId가 어느 문장을 가리키는지
/// 알 수 없게 된다. 하나의 원자적 파일로 함께 움직이게 한다.
/// </summary>
public static class ScriptDocumentJson
{
    public const int CurrentFormatVersion = 1;
    public const string FileExtension = ".vnscript.json";

    public static string Write(ScriptDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSupport.ToDeterministicText(WriteObject(document));
    }

    public static ScriptDocument Read(string json)
    {
        JsonObject root = JsonSupport.ParseObject(json, "대본");
        int version = (int?)root["formatVersion"] ?? 0;

        if (version != CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"대본 형식 버전 {version}은 지원하지 않습니다. 현재 버전은 {CurrentFormatVersion}입니다.");
        }

        return ReadObject(root);
    }

    internal static JsonObject WriteObject(ScriptDocument document)
    {
        var lines = new JsonArray();

        foreach (ScriptLine line in document.Lines)
        {
            var item = new JsonObject
            {
                ["id"] = line.Id,
                ["revision"] = line.Revision
            };

            if (line.IsRetired)
            {
                item["retired"] = true;
            }

            lines.Add(item);
        }

        var locales = new JsonArray();

        // locale 이름 순으로 쓴다. 언어를 추가한 순서가 파일 diff를 흔들지 않게 한다.
        foreach (ScriptLocale locale in document.Locales.OrderBy(item => item.Locale, StringComparer.Ordinal))
        {
            var entries = new JsonObject();

            // 줄 순서대로 쓴다. 사전 순으로 쓰면 사람이 파일을 읽을 수 없다.
            foreach (ScriptLine line in document.Lines)
            {
                if (!locale.Entries.TryGetValue(line.Id, out LocalizedLine? text))
                {
                    continue;
                }

                var entry = new JsonObject();

                if (text.Speaker.Length > 0)
                {
                    entry["speaker"] = text.Speaker;
                }

                entry["text"] = text.Text;
                entries[line.Id] = entry;
            }

            locales.Add(new JsonObject
            {
                ["locale"] = locale.Locale,
                ["entries"] = entries
            });
        }

        var root = new JsonObject
        {
            ["formatVersion"] = CurrentFormatVersion,
            ["scriptId"] = document.Id,
            ["name"] = document.Name,
            ["primaryLocale"] = document.PrimaryLocale,
            ["sourceRevision"] = document.SourceRevision
        };

        if (document.SourcePath is not null)
        {
            root["sourcePath"] = document.SourcePath;
        }

        if (document.SourceContentHash is not null)
        {
            root["sourceContentHash"] = document.SourceContentHash;
        }

        root["lines"] = lines;
        root["locales"] = locales;

        return root;
    }

    internal static ScriptDocument ReadObject(JsonObject root)
    {
        string id = (string?)root["scriptId"]
            ?? throw new InvalidDataException("대본에 scriptId가 없습니다.");

        var document = new ScriptDocument(id, (string?)root["name"] ?? "이름 없는 대본")
        {
            PrimaryLocale = (string?)root["primaryLocale"] ?? ScriptDocument.DefaultLocale,
            SourcePath = (string?)root["sourcePath"],
            SourceRevision = (int?)root["sourceRevision"] ?? 0,
            SourceContentHash = (string?)root["sourceContentHash"]
        };

        HashSet<string> lineIds = new(StringComparer.Ordinal);

        foreach (JsonNode? item in root["lines"]?.AsArray() ?? new JsonArray())
        {
            if (item is not JsonObject lineJson)
            {
                continue;
            }

            string lineId = (string?)lineJson["id"]
                ?? throw new InvalidDataException($"대본 '{id}'의 줄에 id가 없습니다.");

            if (!lineIds.Add(lineId))
            {
                throw new InvalidDataException($"대본 '{id}'에서 LineId '{lineId}'가 중복됩니다.");
            }

            document.Lines.Add(new ScriptLine(
                lineId,
                (int?)lineJson["revision"] ?? 1,
                (bool?)lineJson["retired"] ?? false));
        }

        foreach (JsonNode? item in root["locales"]?.AsArray() ?? new JsonArray())
        {
            if (item is not JsonObject localeJson)
            {
                continue;
            }

            string locale = (string?)localeJson["locale"]
                ?? throw new InvalidDataException($"대본 '{id}'의 locale에 이름이 없습니다.");

            if (document.FindLocale(locale) is not null)
            {
                throw new InvalidDataException($"대본 '{id}'에서 locale '{locale}'이 중복됩니다.");
            }

            var target = new ScriptLocale(locale);

            foreach ((string lineId, JsonNode? entry) in localeJson["entries"]?.AsObject()
                         ?? new JsonObject())
            {
                if (entry is not JsonObject text)
                {
                    continue;
                }

                if (!lineIds.Contains(lineId))
                {
                    throw new InvalidDataException(
                        $"대본 '{id}'의 locale '{locale}'이 없는 LineId '{lineId}'를 가리킵니다.");
                }

                target.Entries[lineId] = new LocalizedLine(
                    (string?)text["speaker"] ?? string.Empty,
                    (string?)text["text"] ?? string.Empty);
            }

            document.Locales.Add(target);
        }

        return document;
    }
}
