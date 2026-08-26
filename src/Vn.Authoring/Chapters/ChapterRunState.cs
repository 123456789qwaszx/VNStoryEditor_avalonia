namespace Vn.Authoring.Chapters;

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

    public ChapterRunState(ChapterGraphModel chapter)
    {
        ArgumentNullException.ThrowIfNull(chapter);
        _chapter = chapter;
        // 증명기·워커와 같은 시드 — 초기값 그대로(경계 보정 없음. 밖이면 검증이 짚을 일이다).
        _stats = chapter.Stats.Select(stat => stat.Initial).ToArray();
    }

    /// <summary>이 판이 어느 챕터의 것인가 — 챕터가 바뀌면 판도 새로 선다.</summary>
    public string ChapterId => _chapter.ChapterId;

    /// <summary>지금 스탯으로 이 관문을 지날 수 있는가 — 판정은 커밋 <b>전</b> 값으로 한다.</summary>
    public ChapterGateVerdict Judge(string? conditionLabel) =>
        ChapterGateJudge.Judge(_chapter, conditionLabel, _stats);

    /// <summary>간선을 탔다 — 그 간선의 `스탯변화`를 1회 커밋한다(최소~최대로 잘라낸다).</summary>
    public void Commit(IReadOnlyList<StatDelta> deltas)
    {
        ArgumentNullException.ThrowIfNull(deltas);
        _stats = ChapterReachabilityProver.ApplyDeltas(_chapter, _stats, deltas);
    }

    /// <summary>HUD가 그릴 현재 값 전부 — `스탯` 시트의 행 순서다.</summary>
    public IReadOnlyList<ChapterRunStatValue> Values =>
        _chapter.Stats.Select((stat, index) => new ChapterRunStatValue(stat, _stats[index])).ToList();
}
