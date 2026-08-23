namespace Vn.Authoring.Chapters;

/// <summary>
/// 챕터 그래프의 배치 계산 — 깊이(depth) 기반. 챕터 그래프는 왼쪽에서 오른쪽으로 흐르는
/// 이야기이므로, 노드의 열(column)은 시작 에피소드에서 그 노드까지의 <b>가장 긴 경로</b>로
/// 정한다. 합류 노드(두 분기가 다시 만나는 곳)는 가장 깊은 부모보다 한 열 오른쪽에 서서
/// 간선이 뒤로 꺾이지 않는다.
///
/// <b>배치는 툴이 소유한다 (v3, 2026-08-12 소유자 개정).</b> 뷰는 그릴 때마다
/// <see cref="Layout"/>을 다시 계산한다 — 드래그가 없으므로 사람이 지킬 배치도 없고,
/// 흐름(간선)이 바뀌면 자리가 저절로 따라온다. 엑셀 X·Y 셀은 내보내기의 Position에나 쓰인다.
/// </summary>
public static class ChapterBranchPlanner
{
    /// <summary>열 간격. 카드 폭(190)보다 넉넉해 간선이 보인다.</summary>
    public const double ColumnWidth = 220;

    /// <summary>줄 간격. 카드 높이(74) + 선택지 포트(최대 3줄, 2026-08-15)보다 넉넉하다.</summary>
    public const double RowHeight = 150;

    /// <summary>
    /// 시작 에피소드에서 각 에피소드까지의 깊이(가장 긴 경로). 도달할 수 없는 노드는
    /// 사전에 없다. 순환이 있어도 멈춘다 — 깊이는 노드 수를 넘을 수 없기 때문이다.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Depths(ChapterGraphModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var depths = new Dictionary<string, int>(StringComparer.Ordinal);

        if (model.StartEpisode is not { } start)
        {
            return depths;
        }

        depths[start.EpisodeId] = 0;

        // 벨만-포드식 이완. 깊이 상한 = 노드 수 - 1 이므로 순환이 있어도 그 이상 자라지 않는다.
        int cap = model.Episodes.Count;
        bool changed = true;

        for (int pass = 0; changed && pass < cap; pass++)
        {
            changed = false;

            foreach (ChapterEdge edge in model.Edges)
            {
                if (!depths.TryGetValue(edge.FromEpisodeId, out int fromDepth))
                {
                    continue;
                }

                int candidate = fromDepth + 1;

                if (candidate >= cap)
                {
                    continue; // 순환 가드
                }

                if (!depths.TryGetValue(edge.ToEpisodeId, out int current) || candidate > current)
                {
                    depths[edge.ToEpisodeId] = candidate;
                    changed = true;
                }
            }
        }

        return depths;
    }

    /// <summary>
    /// [＋ 분기]의 첫 자리 제안. 열 = 부모 깊이 + 1 (부모가 손으로 더 오른쪽에 옮겨졌으면
    /// 그 오른쪽), 줄 = 그 열에서 아직 비어 있는 첫 자리 — 다른 부모의 자식과 겹치지 않는다.
    /// </summary>
    public static (double X, double Y) SuggestPlacement(ChapterGraphModel model, string parentEpisodeId)
    {
        ArgumentNullException.ThrowIfNull(model);

        ChapterEpisode parent = model.FindEpisode(parentEpisodeId)
            ?? throw new InvalidOperationException($"부모 에피소드 '{parentEpisodeId}'가 없습니다.");

        IReadOnlyDictionary<string, int> depths = Depths(model);
        double startX = model.StartEpisode?.X ?? 0;

        // 부모가 도달 불가라 깊이가 없으면 부모 기준 상대 배치로 물러난다.
        double x = depths.TryGetValue(parent.EpisodeId, out int parentDepth)
            ? Math.Max(parent.X + ColumnWidth, startX + ((parentDepth + 1) * ColumnWidth))
            : parent.X + ColumnWidth;

        // 그 열(±반 칸)에 이미 선 노드들과 세로로 부딪치지 않는 첫 줄을 찾는다.
        List<double> occupied = model.Episodes
            .Where(episode => Math.Abs(episode.X - x) < ColumnWidth / 2)
            .Select(episode => episode.Y)
            .ToList();

        double y = parent.Y;

        while (occupied.Any(other => Math.Abs(other - y) < RowHeight))
        {
            y += RowHeight;
        }

        return (x, y);
    }

    /// <summary>
    /// 고른 에피소드에서 <b>지금 갈 수 있는 자리</b> 넷 (2026-08-24 소유자 — 위치 편집).
    ///
    /// 좌우는 <c>열보정</c>을 ±1 하는 것이고, 위아래는 <b>같은 열의 이웃과 시트 행을
    /// 맞바꾸는</b> 것이다. 그래서 이 기록이 곧 화면의 단추 넷이다 — 갈 데가 없으면
    /// 그 단추는 서지 않는다(눌러도 아무 일 없는 단추가 가장 나쁘다).
    /// </summary>
    /// <param name="Left">왼쪽으로 한 칸 — 보정이 0이면 없다(제자리보다 왼쪽은 금지).</param>
    /// <param name="Right">오른쪽으로 한 칸 — 도달 가능한 노드면 언제나 있다.</param>
    /// <param name="SwapUp">위와 자리 바꾸기 — 같은 열의 바로 위 에피소드 Id.</param>
    /// <param name="SwapDown">아래와 자리 바꾸기 — 같은 열의 바로 아래 에피소드 Id.</param>
    public sealed record ChapterMoves(
        bool Left, bool Right, string? SwapUp, string? SwapDown)
    {
        /// <summary>갈 데가 없다 — 도달 불가(고아) 노드가 이렇다.</summary>
        public static ChapterMoves None { get; } = new(false, false, null, null);

        public bool Any => Left || Right || SwapUp is not null || SwapDown is not null;
    }

    /// <summary>
    /// <see cref="ChapterMoves"/>를 센다.
    ///
    /// ⚠ 고아(간선이 없어 깊이가 없는 노드)는 <b>못 옮긴다</b>. 그것들은 판 아래 한 줄에
    /// 따로 서 있고 그 줄은 배치가 아니라 <b>보고</b>다 — 도달 불가라고 이미 ⚠가 붙어 있는
    /// 것을 옮겨 정돈하면 그 보고가 흐려진다. 잇는 것이 고치는 길이다.
    /// </summary>
    public static ChapterMoves Moves(ChapterGraphModel model, string episodeId)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (episodeId is null || model.FindEpisode(episodeId) is not { } episode)
        {
            return ChapterMoves.None;
        }

        IReadOnlyDictionary<string, int> columns = Columns(model);

        if (!columns.TryGetValue(episodeId, out int column))
        {
            return ChapterMoves.None;
        }

        // 같은 열의 이웃 — 순서는 시트 행 순서다(model.Episodes가 그 순서다).
        List<ChapterEpisode> lane = model.Episodes
            .Where(candidate =>
                columns.TryGetValue(candidate.EpisodeId, out int other) && other == column)
            .ToList();

        int slot = lane.FindIndex(candidate =>
            string.Equals(candidate.EpisodeId, episodeId, StringComparison.Ordinal));

        return new ChapterMoves(
            Left: episode.ColumnNudge > 0,
            Right: true,
            SwapUp: slot > 0 ? lane[slot - 1].EpisodeId : null,
            SwapDown: slot >= 0 && slot + 1 < lane.Count ? lane[slot + 1].EpisodeId : null);
    }

    /// <summary>
    /// 각 에피소드가 <b>실제로 설 열</b> — 깊이 + <c>열보정</c> (2026-08-24). 도달 불가라
    /// 깊이가 없는 노드는 사전에 없다.
    ///
    /// <b>배치의 주인은 여전히 깊이다.</b> 보정은 그 위에 얹는 한 칸짜리 덧셈이라, 격자는
    /// 그대로 딱딱 맞고 흐름(간선)이 바뀌면 자리가 여전히 저절로 따라온다 — 사람이 옮겨
    /// 둔 만큼만 옆으로 밀린 채로.
    ///
    /// ⛔ 보정은 <b>오른쪽으로만</b> 간다(리더가 음수를 0으로 읽는다). 제 깊이보다 왼쪽에
    /// 서면 부모가 자기 오른쪽에 놓여 간선이 뒤로 꺾인다.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Columns(ChapterGraphModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        IReadOnlyDictionary<string, int> depths = Depths(model);
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (ChapterEpisode episode in model.Episodes)
        {
            if (depths.TryGetValue(episode.EpisodeId, out int depth))
            {
                columns[episode.EpisodeId] = depth + Math.Max(0, episode.ColumnNudge);
            }
        }

        return columns;
    }

    /// <summary>
    /// 화면 배치 전체 — 원점 (0,0) 기준. 열 = 깊이 + 열보정(<see cref="Columns"/>),
    /// 열 안 순서 = 시트 행 순서(사람이 정한 유일한 순서). 간선이 없어 깊이가 없는 노드는
    /// 맨 아래 줄에 따로 세운다 — 이미 ⚠로 보고되는 것을 겹쳐 숨기지 않는다.
    /// </summary>
    public static IReadOnlyDictionary<string, (double X, double Y)> Layout(ChapterGraphModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        IReadOnlyDictionary<string, int> columns = Columns(model);
        var positions = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);

        int deepestSlot = 0;

        foreach (IGrouping<int, ChapterEpisode> column in model.Episodes
                     .Where(episode => columns.ContainsKey(episode.EpisodeId))
                     .GroupBy(episode => columns[episode.EpisodeId]))
        {
            int slot = 0;

            foreach (ChapterEpisode episode in column) // Episodes는 시트 행 순서다
            {
                positions[episode.EpisodeId] = (column.Key * ColumnWidth, slot * RowHeight);
                deepestSlot = Math.Max(deepestSlot, slot);
                slot++;
            }
        }

        // 고아(간선 없음 = 도달 불가) — 그래프 아래에 한 줄로.
        int orphanColumn = 0;
        double orphanY = (deepestSlot + 2) * RowHeight;

        foreach (ChapterEpisode episode in model.Episodes
                     .Where(episode => !columns.ContainsKey(episode.EpisodeId)))
        {
            positions[episode.EpisodeId] = (orphanColumn * ColumnWidth, orphanY);
            orphanColumn++;
        }

        return positions;
    }
}
