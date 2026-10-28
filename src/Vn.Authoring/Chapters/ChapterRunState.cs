using System.Text.Json;
using Contract = Ked.Progression;

namespace Vn.Authoring.Chapters;

public sealed record ChapterRunOption(
    ChapterEdge Edge,
    int SourceIndex,
    bool IsSelectable,
    string LockedReason);

public sealed record ChapterRunAdvance(
    Contract.ChapterAdvanceKind Kind,
    IReadOnlyList<ChapterRunOption> Options,
    int HiddenCount);

/// <summary>관문 하나의 판정 결과 — 지나갈 수 있는가만이 아니라 <b>왜 못 가는가</b>까지 가른다.</summary>
public enum ChapterGateVerdict
{
    /// <summary>지나갈 수 있다 (라벨 없음 포함 — 관문 없는 길은 언제나 열려 있다).</summary>
    Open,

    /// <summary>조건이 지금 스탯으로 서지 않는다.</summary>
    Blocked,

    /// <summary>라벨이 `조건` 시트에 없거나 식이 깨졌다 — 구조 검증의 오류이지 스탯의 문제가 아니다.</summary>
    Broken
}

/// <summary>
/// 간선 관문(표시·해금조건) 판정의 <b>단일 구현</b> (2026-08-27) — 도달성 증명·픽스처
/// 워커·무대 프리뷰 셋이 이 하나를 본다. 예전에는 증명기와 워커가 같은 판정을 각자
/// 들고 있었다(규칙이 두 벌이면 한쪽만 고쳐질 때 증명과 플레이가 갈린다).
///
/// 깨진 라벨(<see cref="ChapterGateVerdict.Broken"/>)은 "지나갈 수 없음"이다 —
/// 깨진 조건을 통과시켜 도달 가능을 부풀리지 않는다(증명기의 원래 규칙 그대로).
/// </summary>
public static class ChapterGateJudge
{
    /// <param name="stats"><paramref name="chapter"/>의 `스탯` 시트 순서와 같은 자리의 현재 값.</param>
    public static ChapterGateVerdict Judge(ChapterGraphModel chapter, string? conditionLabel, int[] stats)
    {
        ArgumentNullException.ThrowIfNull(chapter);
        ArgumentNullException.ThrowIfNull(stats);

        if (string.IsNullOrEmpty(conditionLabel))
        {
            return ChapterGateVerdict.Open;
        }

        if (chapter.FindCondition(conditionLabel) is not { IsValid: true } condition)
        {
            return ChapterGateVerdict.Broken;
        }

        foreach (ConditionTerm term in condition.Parsed)
        {
            if (!CompareStat(chapter, term, stats))
            {
                return ChapterGateVerdict.Blocked;
            }
        }

        return ChapterGateVerdict.Open;
    }

    private static bool CompareStat(ChapterGraphModel chapter, ConditionTerm term, int[] stats)
    {
        int index = -1;

        for (int position = 0; position < chapter.Stats.Count; position++)
        {
            if (string.Equals(chapter.Stats[position].Key, term.Key, StringComparison.Ordinal))
            {
                index = position;
                break;
            }
        }

        if (index < 0)
        {
            return false; // 미등록 스탯키 — 구조 검증이 이미 오류로 잡았다.
        }

        return term.Comparison switch
        {
            ConditionComparison.AtLeast => stats[index] >= term.Value,
            ConditionComparison.AtMost => stats[index] <= term.Value,
            ConditionComparison.Above => stats[index] > term.Value,
            ConditionComparison.Below => stats[index] < term.Value,
            _ => stats[index] == term.Value
        };
    }
}

/// <summary>챕터 스탯 하나의 현재 값 — HUD가 그린다. bool은 0/1이 곧 false/true다.</summary>
public sealed record ChapterRunStatValue(ChapterStat Stat, int Value)
{
    public string DisplayText =>
        Stat.Type == ChapterStatType.Bool
            ? (Value != 0 ? "true" : "false")
            : Value.ToString();
}

/// <summary>
/// 챕터 한 판의 진행 상태 (2026-08-27 소유자: 프리뷰가 "선택을 따라가며 스탯이 챕터
/// 단위로 누적"되도록) — 시작은 `스탯` 시트의 초기값이고, <b>간선을 타는 순간 1회
/// 커밋</b>이라는 전이 규칙 그대로 <see cref="Commit"/>이 유일한 변화 자리다.
/// 판정·커밋의 규칙은 증명기·워커와 같은 한 벌이다(<see cref="ChapterGateJudge"/> ·
/// <see cref="ChapterReachabilityProver.ApplyDeltas"/>).
///
/// 재생 위치와 같은 뷰 상태라 저장하지 않는다(원칙 E) — 새 전체 재생이 처음값으로 되돌린다.
/// </summary>
public sealed class ChapterRunState
{
    private readonly ChapterGraphModel _chapter;
    private int[] _stats;
    private readonly Contract.ChapterProgression? _runtime;
    private Contract.ProgressionState? _sceneEntry;
    private Contract.ProgressionState? _working;
    private readonly List<Contract.EpisodeOption> _pending = [];

    public ChapterRunState(ChapterGraphModel chapter)
    {
        ArgumentNullException.ThrowIfNull(chapter);
        _chapter = chapter;
        // 증명기·워커와 같은 시드 — 초기값 그대로(경계 보정 없음. 밖이면 검증이 짚을 일이다).
        _stats = chapter.Stats.Select(stat => stat.Initial).ToArray();

        Contract.ChapterProgressionDto? dto =
            JsonSerializer.Deserialize<Contract.ChapterProgressionDto>(
                ChapterProgressionExporter.SerializeUnchecked(chapter));
        Contract.ProgressionLoadResult loaded = dto is null
            ? new Contract.ProgressionLoadResult(null, null)
            : Contract.ProgressionLoader.Load(dto);

        if (loaded.IsValid)
        {
            _runtime = loaded.Chapter;
            _sceneEntry = _runtime.CreateEntryState();
            _working = _sceneEntry;
        }
    }

    /// <summary>이 판이 어느 챕터의 것인가 — 챕터가 바뀌면 판도 새로 선다.</summary>
    public string ChapterId => _chapter.ChapterId;

    /// <summary>지금 스탯으로 이 관문을 지날 수 있는가 — 판정은 커밋 <b>전</b> 값으로 한다.</summary>
    public ChapterGateVerdict Judge(string? conditionLabel) =>
        ChapterGateJudge.Judge(_chapter, conditionLabel, _stats);

    /// <summary>
    /// 현재 에피소드의 표시·잠금·종료·Auto 판정을 런타임 코어에 직접 묻는다.
    /// SourceIndex를 간선 SourceRow 순서로 되돌려 UI가 같은 선택지를 가리키게 한다.
    /// </summary>
    public ChapterRunAdvance? Resolve(string episodeId)
    {
        // 편집 중 깨진 라벨은 JSON 투영에서 빈 관문으로 변환되어서는 안 된다.
        // null을 돌려 UI가 저작 오류 표시 경로를 쓰게 한다.
        if (Outgoing(episodeId).Any(edge =>
                !string.IsNullOrEmpty(edge.ConditionLabel) &&
                _chapter.FindCondition(edge.ConditionLabel) is not { IsValid: true }))
        {
            return null;
        }

        if (_runtime is null || _working is null)
        {
            return null;
        }

        Contract.ProgressionState atEpisode = string.Equals(
                _working.CurrentEpisodeId, episodeId, StringComparison.Ordinal)
            ? _working
            : Contract.ProgressionState.Restore(_runtime, episodeId, _working.Stats);
        Contract.ChapterAdvance resolved = Contract.ChapterTransition.Resolve(_runtime, atEpisode);
        List<ChapterEdge> edges = Outgoing(episodeId);

        return new ChapterRunAdvance(
            resolved.Kind,
            resolved.Options.Select(option => new ChapterRunOption(
                edges[option.SourceIndex], option.SourceIndex, option.IsSelectable, option.LockedReason)).ToList(),
            resolved.HiddenCount);
    }

    /// <summary>
    /// 장면 안에서는 선택을 pending에 쌓아 entry에서 다시 fold한다. 장면을 나갈 때만
    /// working이 다음 장면의 확정 entry가 된다. 롤백 UI는 아직 없지만 상태 수명은 런타임과 같다.
    /// </summary>
    public void Commit(ChapterEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        if (_runtime is null || _sceneEntry is null || _working is null)
        {
            Commit(edge.StatChanges);
            return;
        }

        List<ChapterEdge> edges = Outgoing(edge.FromEpisodeId);
        int index = edges.FindIndex(candidate => ReferenceEquals(candidate, edge) || candidate == edge);
        if (index < 0 || !_runtime.TryGetNode(edge.FromEpisodeId, out Contract.EpisodeNode? node))
        {
            throw new ArgumentException("현재 챕터에 없는 간선입니다.", nameof(edge));
        }

        // 프리뷰가 씬 선택기로 진입했을 수 있으므로 그 에피소드를 현재 위치로 맞춘다.
        if (!string.Equals(_working.CurrentEpisodeId, edge.FromEpisodeId, StringComparison.Ordinal))
        {
            _sceneEntry = Contract.ProgressionState.Restore(_runtime, edge.FromEpisodeId, _working.Stats);
            _pending.Clear();
        }

        _pending.Add(node.NextOptions[index]);
        _working = _sceneEntry.FoldChoices(_runtime, _pending);
        SyncStatsFromRuntime();

        if (!_runtime.IsSameScene(edge.FromEpisodeId, edge.ToEpisodeId))
        {
            _sceneEntry = _working;
            _pending.Clear();
        }
    }

    public int PendingChoiceCount => _pending.Count;

    /// <summary>간선을 탔다 — 그 간선의 `스탯변화`를 1회 커밋한다(최소~최대로 잘라낸다).</summary>
    public void Commit(IReadOnlyList<StatDelta> deltas)
    {
        ArgumentNullException.ThrowIfNull(deltas);
        _stats = ChapterReachabilityProver.ApplyDeltas(_chapter, _stats, deltas);
    }

    private List<ChapterEdge> Outgoing(string episodeId) => _chapter.Edges
        .Where(edge => string.Equals(edge.FromEpisodeId, episodeId, StringComparison.Ordinal))
        .OrderBy(edge => edge.SourceRow)
        .ThenBy(edge => edge.ToEpisodeId, StringComparer.Ordinal)
        .ToList();

    private void SyncStatsFromRuntime()
    {
        for (int i = 0; i < _chapter.Stats.Count; i++)
        {
            _stats[i] = _working!.GetStat(_chapter.Stats[i].Key);
        }
    }

    /// <summary>HUD가 그릴 현재 값 전부 — `스탯` 시트의 행 순서다.</summary>
    public IReadOnlyList<ChapterRunStatValue> Values =>
        _chapter.Stats.Select((stat, index) => new ChapterRunStatValue(stat, _stats[index])).ToList();
}
