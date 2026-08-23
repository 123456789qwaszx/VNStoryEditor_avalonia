using System.Text.Json;
using System.Text.Json.Serialization;
using Vn.Authoring.Rendering;

// 계약 이름의 정본은 코어의 enum이다 (2026-08-23 · 로드맵 T2). 별칭을 두는 이유는
// 이름이 겹치기 때문이다 — 이쪽에도 `StatChangeKind`가 있다(저작 문법용).
using Contract = Ked.Progression;

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

        string json = JsonSerializer.Serialize(payload, Options);

        // ⛔ 마지막 관문 — **진짜 소비자에게 먹여 본다** (2026-08-23).
        //
        // 검증을 통과해도 코어가 못 싣는 챕터가 있다. 실제로 있었다: 문구 없는 간선
        // (보이지 않는 기본)에 관문이 걸리면 이쪽은 경고로 넘기는데 코어는 거부한다.
        // 그대로 내보내면 게임에서 그 챕터가 **시작되지 않는다** — "실을 수 없으면
        // 내보내지 않는다"를 이쪽이 어기고 있던 자리다.
        //
        // 심각도를 손으로 맞추지 않는 이유가 이것이다. 규칙을 둘로 적어 두면 저쪽이
        // 하나 늘릴 때마다 갈린다. 대신 저쪽 판정을 그대로 받는다 — 산출물 yarn을
        // 진짜 컴파일러에 거는 것(②-A)과 같은 수다.
        if (CoreRefusals(chapter, json) is { Count: > 0 } refusals)
        {
            return new ChapterExportResult(
                null,
                validation with { Diagnostics = [.. validation.Diagnostics, .. refusals] });
        }

        return new ChapterExportResult(json, validation);
    }

    /// <summary>
    /// 낸 JSON을 코어 로더에 실어 보고, 거부 사유를 <b>엑셀 자리로 옮겨</b> 돌려준다.
    /// 실을 수 있으면 빈 목록이다.
    ///
    /// 코어 진단은 <c>Nodes[ep_03].NextOptions[1]</c> 같은 경로를 들고 있다(그 타입의
    /// 존재 이유라고 저쪽 주석이 적어 뒀다). 그래서 "어딘가 잘못됐다"로 떨어지지 않고
    /// 기획자가 열 시트와 행까지 짚을 수 있다 — 이 레이어의 규칙이 그것이다.
    /// </summary>
    private static IReadOnlyList<ChapterDiagnostic> CoreRefusals(
        ChapterGraphModel chapter, string json)
    {
        Contract.Dto.ChapterProgressionDto? dto;

        try
        {
            dto = JsonSerializer.Deserialize<Contract.Dto.ChapterProgressionDto>(json);
        }
        catch (JsonException exception)
        {
            // 우리가 방금 쓴 글을 우리가 못 읽는다 — 조용히 넘기면 안 되는 종류다(규율 1).
            return [Refusal(chapter, null, $"낸 JSON을 다시 읽지 못했습니다: {exception.Message}")];
        }

        if (dto is null)
        {
            return [Refusal(chapter, null, "낸 JSON이 비어 있습니다.")];
        }

        Contract.ProgressionLoadResult load = Contract.ProgressionLoader.Load(dto);

        // ⚠ 경고는 막지 않는다. 코어의 Warning은 "실을 수는 있지만 봐야 한다"이고,
        // 그것까지 거부하면 이쪽이 저쪽보다 엄해져 또 다른 갈림이 된다.
        return load.Diagnostics
            .Where(item => item.Severity == Contract.ProgressionDiagnosticSeverity.Error)
            .Select(item => Refusal(chapter, item.Path, item.Message))
            .ToList();
    }

    /// <summary>코어 경로(<c>Nodes[ep].NextOptions[i]</c>)를 시트·행으로 옮긴다.</summary>
    private static ChapterDiagnostic Refusal(ChapterGraphModel chapter, string? path, string message)
    {
        (string? sheet, int? row) = LocateInWorkbook(chapter, path);

        return new ChapterDiagnostic(
            ChapterDiagnosticSeverity.Error,
            ChapterDiagnosticCode.CoreRefusedChapter,
            chapter.SourcePath,
            sheet,
            row,
            null,
            $"진행 코어가 이 챕터를 싣지 못합니다 — {message}" +
            (path is { Length: > 0 } ? $" (코어 경로: {path})" : string.Empty));
    }

    private static (string? Sheet, int? Row) LocateInWorkbook(ChapterGraphModel chapter, string? path)
    {
        if (path is null || Episode(path) is not { } episodeId)
        {
            return (null, null);
        }

        // `Nodes[X].NextOptions[i]` — i는 **그 에피소드에서 나가는 간선의 행 순서**다.
        // 내보내기가 그 순서로 싣는다(`Node`의 OrderBy SourceRow)므로 여기서도 같게 센다.
        if (OptionIndex(path) is { } index)
        {
            List<ChapterEdge> outgoing = chapter.Edges
                .Where(edge => string.Equals(edge.FromEpisodeId, episodeId, StringComparison.Ordinal))
                .OrderBy(edge => edge.SourceRow)
                .ToList();

            if (index >= 0 && index < outgoing.Count)
            {
                return (ChapterSheetNames.Edges, outgoing[index].SourceRow);
            }
        }

        return (ChapterSheetNames.Episodes, chapter.FindEpisode(episodeId)?.SourceRow);
    }

    private static string? Episode(string path) => Between(path, "Nodes[", "]");

    private static int? OptionIndex(string path) =>
        int.TryParse(Between(path, "NextOptions[", "]"), out int index) ? index : null;

    private static string? Between(string text, string open, string close)
    {
        int start = text.IndexOf(open, StringComparison.Ordinal);

        if (start < 0)
        {
            return null;
        }

        start += open.Length;
        int end = text.IndexOf(close, start, StringComparison.Ordinal);

        return end < 0 ? null : text[start..end];
    }

    /// <summary>
    /// 이 에피소드로 들어오는 <b>엔딩 간선</b>의 키 (v11). 없으면 빈 문자열이다.
    ///
    /// 규칙 자체는 <see cref="ChapterGraphModel.EndingKeyOf"/> 하나뿐이다 — 판의 ★ 배지도
    /// 같은 것을 본다. 여기서 따로 세면 한 곳만 고쳐지는 날이 온다.
    ///
    /// 리더가 "같은 도착의 엔딩키는 하나뿐"을 이미 강제하므로 그 값은 유일하다. 그 검사가
    /// 없으면 조용히 하나가 골라지고, JSON에 도착한 뒤에는 나머지가 사라졌다는 것을 아무도
    /// 모른다 — 그래서 그 검사는 <b>저작 쪽만 할 수 있는</b> 것이었다
    /// (`ked-progression`과 합의, 2026-08-18).
    /// </summary>
    private static string EndingKeyOf(ChapterGraphModel chapter, ChapterEpisode episode) =>
        chapter.EndingKeyOf(episode.EpisodeId) ?? string.Empty;

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
        Type = stat.Type == ChapterStatType.Bool
            ? nameof(Contract.StatType.Bool)
            : nameof(Contract.StatType.Number),
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
            ? nameof(Contract.EpisodeKind.Attachment)
            : nameof(Contract.EpisodeKind.Main),
        // ⚠ 이미터의 이름 규칙을 통과시킨다 (2026-08-23) — 런타임은 이 글자로 YarnProject에서
        // 노드를 찾는다. 그냥 실으면 진행 JSON은 `new01`, yarn은 `Story_new01`이 되어
        // 로드·검증·증명이 전부 통과하는데 재생만 안 된다. 규칙은 접두 하나가 아니라
        // `Story_` + SanitizeNodeName 두 단계이므로 여기서 손으로 이으면 공백 든 이름에서
        // 또 갈린다 — 규칙의 주인은 이미터 하나다.
        DialogueEntryId = YarnBundleEmitter.StoryNodeTitleOf(episode.DialogueEntry),
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
                // v12 (2026-08-24) — 저작에 `종류` 칸이 사라졌다. 모든 길이 선택지이므로
                // 물을 것이 없다. 값은 코어 enum 멤버 이름 그대로다.
                Kind = nameof(Contract.OptionKind.PlayerChoice),
                VisibleConditions = Conditions(chapter, edge.VisibleConditionLabel),
                Conditions = Conditions(chapter, edge.ConditionLabel),
                HideWhenLocked = edge.HideWhenLocked,
                LockedReasonText = edge.LockedMessage ?? string.Empty,
                // ⚠ v12 (2026-08-24) — 저작의 `연출` 칸이 폐지됐다. 계약의 칸은 남아 있고
                // 언제나 빈 문자열로 나간다(저쪽 DTO를 바꾸지 않는다 — 안 채울 뿐이다).
                ViaNodeId = string.Empty,
                StatChanges = edge.StatChanges
                    .Select(delta => new StatChangeJson
                    {
                        Key = delta.Key,
                        Amount = delta.Amount,
                        Op = delta.IsSet
                            ? nameof(Contract.StatChangeKind.Set)
                            : nameof(Contract.StatChangeKind.Add)
                    })
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
            ? new ConditionJson
            {
                Kind = nameof(Contract.ConditionKind.EpisodeCleared),
                Key = term.Key,
                Op = nameof(Contract.ComparisonOp.Exists)
            }
            : new ConditionJson
            {
                Kind = nameof(Contract.ConditionKind.Stat),
                Key = term.Key,
                Op = term.Comparison switch
                {
                    ConditionComparison.AtLeast => nameof(Contract.ComparisonOp.GreaterOrEqual),
                    ConditionComparison.AtMost => nameof(Contract.ComparisonOp.LessOrEqual),
                    ConditionComparison.Above => nameof(Contract.ComparisonOp.GreaterThan),
                    ConditionComparison.Below => nameof(Contract.ComparisonOp.LessThan),
                    _ => nameof(Contract.ComparisonOp.Equal)
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

        /// <summary><c>Number</c> 또는 <c>Bool</c> — 이쪽 <c>Int</c>가 저쪽 <c>Number</c>다.</summary>
        public string Type { get; set; } = nameof(Contract.StatType.Number);

        public int Initial { get; set; }
        public int Minimum { get; set; }
        public int Maximum { get; set; }
    }

    private sealed class NodeJson
    {
        public string EpisodeId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string IndexText { get; set; } = string.Empty;
        public string Kind { get; set; } = nameof(Contract.EpisodeKind.Main);
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
        /// 이 길을 <b>누가 고르나</b> — <c>PlayerChoice</c> 또는 <c>AutoAdvance</c>
        /// (⚠ 코어 <see cref="Contract.OptionKind"/>의 멤버 이름 그대로다. 저작 쪽 낱말인
        /// "선택지/자동"이나 <c>EdgeKind</c>의 <c>Choice/Auto</c>와 <b>다르다</b>).
        /// 저작의 `간선` 시트 `종류`(I) 열이 원천이다.
        ///
        /// <b>왜 싣는가</b> — 이 칸이 없으면 저쪽은 <c>ChoiceLabel</c>이 비었는지로 추론할
        /// 수밖에 없고, 그러면 <b>문구를 실수로 지운 것</b>과 <b>의도한 자동 진행</b>이
        /// 데이터로 구별되지 않는다(`ked-progression` D5 — 그쪽이 지금 그 경고를 낸다).
        ///
        /// ⚠ <b>저쪽 <c>EpisodeOptionDto</c>에 아직 이 칸이 없다</b>(2026-08-23 실측 · `0.2.0`).
        /// 호스트의 역직렬화기가 모르는 속성을 조용히 버리므로 <b>지금은 무해하지만 아무
        /// 일도 하지 않는다</b> — D5 경고는 저쪽이 칸을 세울 때까지 그대로 뜬다. 미리 내는
        /// 이유는 이 값의 주인이 저작이고, 저쪽이 칸을 세우는 날 이쪽을 다시 안 열어도
        /// 되게 하기 위해서다.
        /// </summary>
        public string Kind { get; set; } = nameof(Contract.OptionKind.PlayerChoice);

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
        /// 이 길을 <b>지나며 거쳐 갈</b> 연출의 Yarn 노드 이름 (v11 §6). 비면 곧장 간다.
        ///
        /// ⚠ <b>"노드"는 Yarn 노드다</b> — 에피소드 노드가 아니다. 저작 쪽 이름은
        /// <c>ChapterEdge.PresentationNodeName</c>이고 계약 쪽은 <c>ViaNodeId</c>다
        /// (계약서 §H-3의 이름을 그대로 쓴다 — 이름이 둘 도는 것이 이 모호함보다 나쁘다).
        ///
        /// ⚠ <b>여기에 파라미터를 붙이지 않는다</b> — 지속시간·이징·색이 들어오는 순간
        /// 경계면이 진짜로 넓어진다. 연출의 파라미터는 연출 쪽에서 산다
        /// (`ked-progression` 요청, 2026-08-18). 이 칸은 이름 하나다.
        /// </summary>
        public string ViaNodeId { get; set; } = string.Empty;

        /// <summary>
        /// 이 간선을 타는 순간 1회 커밋할 스탯 증감 (2026-08-14 — 스탯이 변하는 유일한 자리).
        /// 런타임은 에피소드 전환 시점에 원자적으로 반영해야 한다(스탯 시트의 최소/최대로 clamp).
        /// </summary>
        public List<StatChangeJson> StatChanges { get; set; } = new();
    }

    private sealed class StatChangeJson
    {
        public string Key { get; set; } = string.Empty;

        /// <summary><c>Op</c>가 <c>"Set"</c>이면 <b>정할 값</b>, 그 외에는 증감량이다.</summary>
        public int Amount { get; set; }

        /// <summary>
        /// 더하기인가 정하기인가 (2026-08-23 — `ked-progression` `0.2.0`의 `StatChangeDto.Op`).
        /// <c>"Add"</c> = <c>trust +2</c> · <c>"Set"</c> = <c>met_willow true</c>.
        ///
        /// 이 칸이 저쪽에 서기 전까지는 깃발을 쓰는 챕터의 내보내기를 통째로 거부하고 있었다
        /// (`BoolSetNotCarried`, 2026-08-19~2026-08-23) — 빼고 내면 깃발이 영원히 안 켜진 채로
        /// 게임이 돌아가기 때문이다. 칸이 섰으므로 그 거부는 지웠다.
        ///
        /// <c>"Add"</c>를 <b>비우지 않고 명시</b>한다. 저쪽은 빈 문자열도 더하기로 읽지만
        /// (구 JSON 호환), 적어 두면 이 값을 아무도 안 정한 것과 더하기로 정한 것이 JSON에서
        /// 구별된다 — 규율 1(침묵 금지)의 이 층 판이다.
        /// </summary>
        public string Op { get; set; } = "Add";
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
