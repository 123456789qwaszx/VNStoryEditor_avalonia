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
            Stats = chapter.Stats.Select(Stat).ToList(),
            Nodes = chapter.Episodes.Select(episode => Node(chapter, episode)).ToList()
        };

        return new ChapterExportResult(JsonSerializer.Serialize(payload, Options), validation);
    }

    /// <summary>
    /// 이 에피소드로 들어오는 <b>엔딩 간선</b>의 키 (v11). 없으면 빈 문자열이다.
    ///
    /// 리더가 "같은 도착의 엔딩키는 하나뿐"을 이미 강제하므로 첫 번째가 곧 유일한 값이다.
    /// 그 검사가 없으면 여기서 조용히 하나를 고르게 되고, JSON에 도착한 뒤에는 나머지가
    /// 사라졌다는 것을 아무도 모른다 — 그래서 그 검사는 <b>저작 쪽만 할 수 있는</b>
    /// 것이었다(`ked-progression`과 합의, 2026-08-18).
    /// </summary>
    private static string EndingKeyOf(ChapterGraphModel chapter, ChapterEpisode episode) =>
        chapter.Edges
            .FirstOrDefault(edge =>
                edge.IsEnding &&
                string.Equals(edge.ToEpisodeId, episode.EpisodeId, StringComparison.Ordinal))
            ?.EndingKey?.Trim()
        ?? string.Empty;

    /// <summary>
    /// 스탯 정의 한 벌 → 런타임 <c>StatDefinition</c> (2026-08-18).
    ///
    /// <b>이 칸이 비어 있던 것이 Gate D를 막고 있었다.</b> 초기값·최소·최대가 어느 런타임
    /// 입력에도 없어서, 툴의 도달성 증명만 <c>Clamp(값, 최소, 최대)</c>로 걷고 런타임은
    /// 경계를 몰랐다 — 증명과 실제 플레이가 갈릴 수 있는 자리였다. 값의 주인은 챕터
    /// 워크북의 `스탯` 시트다(같은 <c>trust</c>라도 챕터마다 초기값이 다를 수 있어
    /// 게임 단위가 아니라 챕터에 실린다).
    ///
    /// <b>⚠ 타입 이름을 번역한다.</b> 이쪽은 <c>Int</c>, 저쪽(`Ked.Progression.StatType`)은
    /// <b><c>Number</c></b>다. enum이 이름 문자열로 나가므로 그대로 내면 수입기가 모르는
    /// 이름을 만난다 — 조건 연산자를 <c>AtLeast → "GreaterOrEqual"</c>로 옮기는 것과
    /// 같은 자리, 같은 이유다.
    ///
    /// <c>SourceRow</c>는 싣지 않는다 — 엑셀 몇 행에서 왔는지는 저작의 사정이다.
    /// </summary>
    private static StatJson Stat(ChapterStat stat) => new()
    {
        Key = stat.Key,
        DisplayName = stat.DisplayName,
        Type = stat.Type == ChapterStatType.Bool ? "Bool" : "Number",
        Initial = stat.Initial,
        Minimum = stat.Minimum,
        Maximum = stat.Maximum
    };

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
        // v11 (2026-08-18) — 엔딩키는 저작에서 **간선**의 것이고 계약에서는 **도착
        // 에피소드**의 것이다. 여기가 그 번역이 일어나는 자리다.
        //
        // 기획자는 간선 한 행에서 "이 길을 타면 이 엔딩"을 보고, 진행 패키지는 D2("엔딩은
        // 한 곳이 정한다")를 그대로 지킨다. 이 에피소드로 들어오는 엔딩 간선들의 키가
        // 서로 다르면 리더가 이미 오류로 막았으므로(EndingKeyConflict), 여기서는 첫 번째를
        // 그대로 쓴다 — 조용히 고르는 것이 아니라 하나뿐임이 보장된 것이다.
        IsChapterEndingCandidate = EndingKeyOf(chapter, episode).Length > 0,
        EndingKey = EndingKeyOf(chapter, episode),
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

        /// <summary>
        /// 이 챕터가 쓰는 스탯의 정의 (2026-08-18 신설 — 계약서 §G-1).
        /// <c>Nodes</c>보다 <b>앞에</b> 둔다: 수입기가 조건·스탯변화의 키를 검사하려면
        /// 스탯 사전이 먼저 서야 하고, 사람이 읽을 때도 어휘가 먼저 오는 것이 자연스럽다.
        /// </summary>
        public List<StatJson> Stats { get; set; } = new();

        public List<NodeJson> Nodes { get; set; } = new();
        public List<object> EndingRules { get; set; } = new();  // v1은 읽고 표시까지 (D5)
    }

    /// <summary>런타임 <c>StatDefinition</c>과 1:1. <c>Type</c>은 이름 문자열이다.</summary>
    private sealed class StatJson
    {
        public string Key { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        /// <summary><c>"Number"</c> 또는 <c>"Bool"</c> — 이쪽 <c>Int</c>가 저쪽 <c>Number</c>다.</summary>
        public string Type { get; set; } = "Number";

        public int Initial { get; set; }
        public int Minimum { get; set; }
        public int Maximum { get; set; }
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
