using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vn.Authoring.Chapters;

/// <summary>내보내기 결과. 거부됐으면 JSON이 없고 사유가 전부 담긴다 (Gate C 3번).</summary>
public sealed record ChapterExportResult(
    string? Json,
    ChapterValidationResult Validation)
{
    public bool Refused => Json is null;
}

/// <summary>
/// G8 — 런타임 `ChapterEpisodeProgressionSO` 대응 JSON을 낸다.
///
/// <b>필드는 런타임 저작 타입과 1:1이다</b> — `EpisodeNodeDefinition`·`EpisodeNextOption`·
/// `EpisodeCondition`의 필드 이름을 그대로 쓰고, 이 레이어의 확장은 `Position`(G-2 —
/// 게임 내 그래프도 같은 구도) 하나뿐이다. enum은 이름 문자열로 낸다 — 순서 재배열에
/// 깨지지 않고, 수입기가 이름으로 맵핑한다.
///
/// <b>검증 오류가 있으면 거부한다</b>(Gate C 3번) — 내보내기 전에 무결성이 잡혀야 런타임에
/// 쓰레기가 넘어가지 않는다. <b>픽스처는 싣지 않는다</b> — 테스트 데이터다(§3.1).
/// </summary>
public static class ChapterProgressionExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static ChapterExportResult Export(ChapterGraphModel chapter, string? episodesFolder)
    {
        ArgumentNullException.ThrowIfNull(chapter);

        return ExportValidated(chapter, ChapterValidator.Validate(chapter, episodesFolder));
    }

    /// <summary>
    /// 이미 계산해 둔 검증 결과로 내보낸다 — <b>같은 증명을 두 번 돌리지 않는다</b>
    /// (2026-08-18). 화면은 검증 결과를 보고 패널에 세우려고 어차피 한 번 계산하는데,
    /// 내보내기가 안에서 또 계산하고 있었다. 검증은 에피소드 워크북을 전부 읽고 상태공간을
    /// 훑으므로 챕터 하나에 200ms 가까이 든다 — 그 값을 두 번 치르고 있었다.
    /// </summary>
    public static ChapterExportResult ExportValidated(
        ChapterGraphModel chapter, ChapterValidationResult validation)
    {
        ArgumentNullException.ThrowIfNull(chapter);
        ArgumentNullException.ThrowIfNull(validation);

        if (validation.HasErrors)
        {
            return new ChapterExportResult(null, validation);
        }

        var payload = new ChapterJson
        {
            ChapterId = chapter.ChapterId,
            DisplayName = chapter.ChapterId,
            StartEpisodeId = chapter.StartEpisode?.EpisodeId ?? string.Empty,
            Nodes = chapter.Episodes.Select(episode => Node(chapter, episode)).ToList()
        };

        return new ChapterExportResult(JsonSerializer.Serialize(payload, Options), validation);
    }

    private static NodeJson Node(ChapterGraphModel chapter, ChapterEpisode episode) => new()
    {
        EpisodeId = episode.EpisodeId,
        Title = episode.Title,
        IndexText = episode.Index,
        Kind = string.Equals(episode.Kind, "Attachment", StringComparison.OrdinalIgnoreCase)
            ? "Attachment"
            : "Main",
        DialogueEntryId = episode.DialogueEntry,
        // v8 — 관문은 에피소드가 아니라 그 길(간선)이 갖는다. 노드의 두 필드는 스키마
        // 1:1을 위해 남기되 비어 나간다(⚠ 런타임 수입기가 NextOption 쪽을 읽어야 한다).
        VisibleConditions = [],
        UnlockConditions = [],
        // v9 — 간선 하나 = NextOption 하나. 문구는 간선의 `선택지` 열 값 그대로이고, 비면
        // 빈 문자열 = 보이지 않는 기본(자동 진행). 조건·잠금·스탯변화·도착도 전부 간선 것이다.
        // 순서는 `간선` 시트의 행 순서 — 적은 순서가 곧 화면에 뜨는 순서다.
        NextOptions = chapter.Edges
            .Where(edge => string.Equals(edge.FromEpisodeId, episode.EpisodeId, StringComparison.Ordinal))
            .OrderBy(edge => edge.SourceRow)
            .Select(edge => new NextOptionJson
            {
                TargetEpisodeId = edge.ToEpisodeId,
                ChoiceLabel = edge.OptionLabel ?? string.Empty,
                VisibleConditions = Conditions(chapter, edge.VisibleConditionLabel),
                Conditions = Conditions(chapter, edge.ConditionLabel),
                HideWhenLocked = edge.HideWhenLocked,
                LockedReasonText = edge.LockedMessage ?? string.Empty,
                StatChanges = edge.StatChanges
                    .Select(delta => new StatChangeJson { Key = delta.Key, Amount = delta.Amount })
                    .ToList()
            })
            .ToList(),
        IsChapterEndingCandidate = episode.IsEnding,
        EndingKey = episode.EndingKey ?? string.Empty,
        DesignerNote = episode.Memo ?? string.Empty,
        Position = new PositionJson { X = episode.X, Y = episode.Y }
    };

    /// <summary>
    /// 챕터 조건 → 런타임 `EpisodeCondition`. 스탯 항은 <c>Stat + GreaterOrEqual/LessOrEqual/Equal</c>,
    /// <c>cleared:</c>는 <c>EpisodeCleared + Exists</c>다. `!=`(NotEqual)는 파서가 아직 닫혀
    /// 있으므로(D7 — 런타임 확인 후 개방 대기) 여기 나올 수 없다.
    /// </summary>
    private static List<ConditionJson> Conditions(ChapterGraphModel chapter, string? label)
    {
        if (string.IsNullOrEmpty(label) || chapter.FindCondition(label) is not { } condition)
        {
            return new List<ConditionJson>();
        }

        return condition.Parsed.Select(term => term.Kind == ConditionTermKind.EpisodeCleared
            ? new ConditionJson { Kind = "EpisodeCleared", Key = term.Key, Op = "Exists" }
            : new ConditionJson
            {
                Kind = "Stat",
                Key = term.Key,
                Op = term.Comparison switch
                {
                    ConditionComparison.AtLeast => "GreaterOrEqual",
                    ConditionComparison.AtMost => "LessOrEqual",
                    // 2026-08-16 소유자 개방 — 런타임 수입기(Gate D)가 아직 없으므로 이름을
                    // 계약서에 함께 추가해야 한다(run-log 참조).
                    ConditionComparison.Above => "GreaterThan",
                    ConditionComparison.Below => "LessThan",
                    _ => "Equal"
                },
                IntValue = term.Value
            }).ToList();
    }

    // ── JSON 모양 — 런타임 저작 타입과 1:1 ──────────────────────────────────

    private sealed class ChapterJson
    {
        public string ChapterId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string StartEpisodeId { get; set; } = string.Empty;
        public List<NodeJson> Nodes { get; set; } = new();
        public List<object> EndingRules { get; set; } = new();  // v1은 읽고 표시까지 (D5)
    }

    private sealed class NodeJson
    {
        public string EpisodeId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string IndexText { get; set; } = string.Empty;
        public string Kind { get; set; } = "Main";
        public string DialogueEntryId { get; set; } = string.Empty;
        public List<ConditionJson> VisibleConditions { get; set; } = new();
        public List<ConditionJson> UnlockConditions { get; set; } = new();
        public List<NextOptionJson> NextOptions { get; set; } = new();
        public List<object> Attachments { get; set; } = new();  // v1 비범위 (D5)
        public bool IsChapterEndingCandidate { get; set; }
        public string EndingKey { get; set; } = string.Empty;
        public string DesignerNote { get; set; } = string.Empty;

        /// <summary>이 레이어의 확장 (G-2) — 게임 내 그래프가 같은 구도로 그린다.</summary>
        public PositionJson Position { get; set; } = new();
    }

    private sealed class NextOptionJson
    {
        public string TargetEpisodeId { get; set; } = string.Empty;
        public string ChoiceLabel { get; set; } = string.Empty;

        /// <summary>
        /// <b>표시조건</b> — 이 선택지가 목록에 보이려면 (v8, 2026-08-16). 에피소드 노드의
        /// `VisibleConditions`가 하던 일이 길 단위로 내려왔다. ⚠ 런타임 계약에 아직 없는
        /// 필드다 — Gate D에서 수입기와 함께 확정할 것.
        /// </summary>
        public List<ConditionJson> VisibleConditions { get; set; } = new();

        /// <summary><b>해금조건</b> — 보이지만 고를 수 있으려면.</summary>
        public List<ConditionJson> Conditions { get; set; } = new();
        public bool HideWhenLocked { get; set; }
        public string LockedReasonText { get; set; } = string.Empty;

        /// <summary>
        /// 이 간선을 타는 순간 1회 커밋할 스탯 증감 (2026-08-14 — 스탯이 변하는 유일한 자리).
        /// 런타임은 에피소드 전환 시점에 원자적으로 반영해야 한다(스탯 시트의 최소/최대로 clamp).
        /// </summary>
        public List<StatChangeJson> StatChanges { get; set; } = new();
    }

    private sealed class StatChangeJson
    {
        public string Key { get; set; } = string.Empty;
        public int Amount { get; set; }
    }

    private sealed class ConditionJson
    {
        public string Kind { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string Op { get; set; } = string.Empty;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int IntValue { get; set; }
    }

    private sealed class PositionJson
    {
        public double X { get; set; }
        public double Y { get; set; }
    }
}
