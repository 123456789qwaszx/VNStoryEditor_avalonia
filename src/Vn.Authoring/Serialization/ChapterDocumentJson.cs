using System.Text.Json.Nodes;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Serialization;

/// <summary>
/// <see cref="ChapterDocument"/> ↔ 프로젝트 매니페스트의 <c>chapters</c> 배열
/// (R-F · 2026-09-16, 지시서 §1.1).
///
/// ⚠ <b>비어 있는 값은 안 쓴다.</b> 이 저장소의 매니페스트 작법 그대로다 — 기본값을 전부
/// 적으면 사람이 파일을 열었을 때 무엇이 <i>실제로 정해진 값</i>인지 안 보이고, 기본값이
/// 바뀔 때 옛 파일이 옛 기본값을 굳혀 버린다.
///
/// ⚠ <b>파생값은 안 쓴다.</b> 조건의 <c>Parsed</c>·<c>IsValid</c>는 식 원문에서 다시 나온다
/// (<see cref="ChapterDocument.ToGraphModel"/>) — 담아 두면 식을 고쳤을 때 둘이 갈린다.
/// </summary>
internal static class ChapterDocumentJson
{
    internal static JsonArray? Write(IReadOnlyList<ChapterDocument> chapters)
    {
        if (chapters.Count == 0)
        {
            return null;
        }

        var array = new JsonArray();

        foreach (ChapterDocument chapter in chapters)
        {
            var json = new JsonObject { ["chapterId"] = chapter.ChapterId };

            Put(json, "episodes", chapter.Episodes, WriteEpisode);
            Put(json, "edges", chapter.Edges, WriteEdge);
            Put(json, "conditions", chapter.Conditions, WriteCondition);
            Put(json, "stats", chapter.Stats, WriteStat);
            Put(json, "choiceOptions", chapter.ChoiceOptions, WriteChoice);
            Put(json, "fixtures", chapter.Fixtures, WriteFixture);

            array.Add(json);
        }

        return array;
    }

    internal static List<ChapterDocument> Read(JsonNode? node)
    {
        var chapters = new List<ChapterDocument>();

        foreach (JsonNode? item in node?.AsArray() ?? [])
        {
            if (item is not JsonObject json)
            {
                throw new InvalidDataException("chapters 항목이 객체가 아닙니다.");
            }

            chapters.Add(new ChapterDocument
            {
                ChapterId = Text(json["chapterId"]),
                Episodes = Take(json["episodes"], ReadEpisode),
                Edges = Take(json["edges"], ReadEdge),
                Conditions = Take(json["conditions"], ReadCondition),
                Stats = Take(json["stats"], ReadStat),
                ChoiceOptions = Take(json["choiceOptions"], ReadChoice),
                Fixtures = Take(json["fixtures"], ReadFixture)
            });
        }

        return chapters;
    }

    // ── 에피소드 ────────────────────────────────────────────────────────────

    private static JsonObject WriteEpisode(ChapterEpisode episode)
    {
        var json = new JsonObject
        {
            ["episodeId"] = episode.EpisodeId,
            ["x"] = episode.X,
            ["y"] = episode.Y
        };

        Text(json, "title", episode.Title);
        Text(json, "index", episode.Index);
        Text(json, "dialogueEntry", episode.DialogueEntry);
        Text(json, "memo", episode.Memo);
        Text(json, "eventKey", episode.EventKey);
        Text(json, "sceneId", episode.SceneId);
        Row(json, episode.SourceRow);

        if (episode.AllowUnreachable)
        {
            json["allowUnreachable"] = true;
        }

        return json;
    }

    private static ChapterEpisode ReadEpisode(JsonObject json) => new(
        Text(json["episodeId"]),
        Text(json["title"]),
        Text(json["index"]),
        Text(json["dialogueEntry"]),
        (double?)json["x"] ?? 0,
        (double?)json["y"] ?? 0,
        Optional(json["memo"]),
        (int?)json["sourceRow"] ?? 0,
        (bool?)json["allowUnreachable"] ?? false)
    {
        EventKey = Optional(json["eventKey"]),
        SceneId = Optional(json["sceneId"])
    };

    // ── 간선 ────────────────────────────────────────────────────────────────

    private static JsonObject WriteEdge(ChapterEdge edge)
    {
        var json = new JsonObject
        {
            ["from"] = edge.FromEpisodeId,
            ["to"] = edge.ToEpisodeId
        };

        Text(json, "optionLabel", edge.OptionLabel);
        Text(json, "conditionLabel", edge.ConditionLabel);
        Text(json, "visibleConditionLabel", edge.VisibleConditionLabel);
        Text(json, "lockedMessage", edge.LockedMessage);
        Row(json, edge.SourceRow);

        if (edge.Auto)
        {
            json["auto"] = true;
        }

        if (edge.StatChanges.Count > 0)
        {
            var changes = new JsonArray();

            foreach (StatDelta delta in edge.StatChanges)
            {
                var change = new JsonObject
                {
                    ["key"] = delta.Key,
                    ["amount"] = delta.Amount
                };

                // 기본은 Add — 깃발을 켜는 것만 적는다.
                if (delta.Kind != StatChangeKind.Add)
                {
                    change["kind"] = delta.Kind.ToString();
                }

                changes.Add(change);
            }

            json["statChanges"] = changes;
        }

        return json;
    }

    private static ChapterEdge ReadEdge(JsonObject json) => new(
        Text(json["from"]),
        Text(json["to"]),
        Optional(json["optionLabel"]),
        Optional(json["conditionLabel"]),
        Optional(json["lockedMessage"]),
        (int?)json["sourceRow"] ?? 0)
    {
        Auto = (bool?)json["auto"] ?? false,
        VisibleConditionLabel = Optional(json["visibleConditionLabel"]),
        StatChanges = Take(json["statChanges"], change => new StatDelta(
            Text(change["key"]),
            (int?)change["amount"] ?? 0,
            Enum.TryParse((string?)change["kind"], out StatChangeKind kind) ? kind : StatChangeKind.Add))
    };

    // ── 조건·스탯·선택지·픽스처 ────────────────────────────────────────────

    private static JsonObject WriteCondition(ChapterCondition condition)
    {
        var json = new JsonObject
        {
            ["label"] = condition.Label,
            ["expression"] = condition.Expression
        };

        Text(json, "description", condition.Description);
        Row(json, condition.SourceRow);

        return json;
    }

    /// <summary>⚠ <c>Parsed</c>·<c>IsValid</c>는 안 읽는다 — 식에서 다시 나온다.</summary>
    private static ChapterCondition ReadCondition(JsonObject json) => new(
        Text(json["label"]),
        Text(json["expression"]),
        Optional(json["description"]),
        Parsed: [],
        IsValid: false,
        (int?)json["sourceRow"] ?? 0);

    private static JsonObject WriteStat(ChapterStat stat)
    {
        var json = new JsonObject
        {
            ["key"] = stat.Key,
            ["initial"] = stat.Initial,
            ["minimum"] = stat.Minimum,
            ["maximum"] = stat.Maximum
        };

        Text(json, "displayName", stat.DisplayName);
        Row(json, stat.SourceRow);

        if (stat.Type != ChapterStatType.Int)
        {
            json["type"] = stat.Type.ToString();
        }

        return json;
    }

    private static ChapterStat ReadStat(JsonObject json) => new(
        Text(json["key"]),
        Text(json["displayName"]),
        (int?)json["initial"] ?? 0,
        (int?)json["minimum"] ?? 0,
        (int?)json["maximum"] ?? 0,
        (int?)json["sourceRow"] ?? 0,
        Enum.TryParse((string?)json["type"], out ChapterStatType type) ? type : ChapterStatType.Int);

    private static JsonObject WriteChoice(ChapterChoiceOption option)
    {
        var json = new JsonObject { ["text"] = option.Text };

        Text(json, "index", option.Index);
        Text(json, "memo", option.Memo);
        Row(json, option.SourceRow);

        return json;
    }

    private static ChapterChoiceOption ReadChoice(JsonObject json) => new(
        Text(json["index"]),
        Text(json["text"]),
        Optional(json["memo"]),
        (int?)json["sourceRow"] ?? 0);

    private static JsonObject WriteFixture(ChapterFixture fixture)
    {
        var json = new JsonObject { ["name"] = fixture.Name };

        if (fixture.IsActive)
        {
            json["active"] = true;
        }

        if (fixture.Stats.Count > 0)
        {
            var stats = new JsonObject();

            foreach ((string key, int value) in fixture.Stats.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                stats[key] = value;
            }

            json["stats"] = stats;
        }

        if (fixture.Choices.Count > 0)
        {
            var choices = new JsonArray();

            foreach (ChapterFixtureChoice choice in fixture.Choices)
            {
                choices.Add(new JsonObject { ["from"] = choice.From, ["to"] = choice.To });
            }

            json["choices"] = choices;
        }

        Row(json, fixture.SourceRow);

        return json;
    }

    private static ChapterFixture ReadFixture(JsonObject json)
    {
        var stats = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach ((string key, JsonNode? value) in json["stats"]?.AsObject() ?? [])
        {
            stats[key] = (int?)value ?? 0;
        }

        return new ChapterFixture(
            Text(json["name"]),
            (bool?)json["active"] ?? false,
            stats,
            Take(json["choices"], choice => new ChapterFixtureChoice(
                Text(choice["from"]), Text(choice["to"]))),
            (int?)json["sourceRow"] ?? 0);
    }

    // ── 잔손 ────────────────────────────────────────────────────────────────

    private static void Put<T>(JsonObject json, string name, List<T> items, Func<T, JsonObject> write)
    {
        if (items.Count == 0)
        {
            return;
        }

        var array = new JsonArray();

        foreach (T item in items)
        {
            array.Add(write(item));
        }

        json[name] = array;
    }

    private static List<T> Take<T>(JsonNode? node, Func<JsonObject, T> read)
    {
        var items = new List<T>();

        foreach (JsonNode? item in node?.AsArray() ?? [])
        {
            if (item is not JsonObject json)
            {
                throw new InvalidDataException("chapters 안의 항목이 객체가 아닙니다.");
            }

            items.Add(read(json));
        }

        return items;
    }

    /// <summary>비어 있으면 안 쓴다 — 기본값을 굳히지 않는다.</summary>
    private static void Text(JsonObject json, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            json[name] = value;
        }
    }

    /// <summary>0은 안 쓴다 — 툴에서 새로 만든 행은 원래 자리가 없다.</summary>
    private static void Row(JsonObject json, int sourceRow)
    {
        if (sourceRow != 0)
        {
            json["sourceRow"] = sourceRow;
        }
    }

    private static string Text(JsonNode? node) => (string?)node ?? string.Empty;

    private static string? Optional(JsonNode? node) => (string?)node;
}
