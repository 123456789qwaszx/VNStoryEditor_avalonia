namespace Vn.Authoring.Chapters;

/// <summary>
/// 분기 저작의 자리 계산 — 깊이(depth) 기반. 챕터 그래프는 왼쪽에서 오른쪽으로 흐르는
/// 이야기이므로, 노드의 열(column)은 시작 에피소드에서 그 노드까지의 <b>가장 긴 경로</b>로
/// 정한다. 합류 노드(두 분기가 다시 만나는 곳)는 가장 깊은 부모보다 한 열 오른쪽에 서서
/// 간선이 뒤로 꺾이지 않는다.
///
/// <b>여기는 계산만 한다 — 쓰지 않는다.</b> 제안은 새 노드의 첫 자리([＋ 분기])이고,
/// 정렬은 사람이 [깊이 정렬] 버튼을 눌렀을 때만 쓰인다(자동 레이아웃 금지, G-2 v2).
/// </summary>
public static class ChapterBranchPlanner
{
    /// <summary>열 간격. 카드 폭(190)보다 넉넉해 간선이 보인다.</summary>
    public const double ColumnWidth = 220;

    /// <summary>줄 간격. 카드 높이(74)보다 넉넉하다.</summary>
    public const double RowHeight = 110;

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
    /// [깊이 정렬] — 도달 가능한 노드 전부를 깊이 열로 세운다. 열 안 순서는 현재 Y 순서를
    /// 보존한다(사람이 위아래로 나눠 둔 의도가 남는다). 도달 불가 노드는 건드리지 않는다.
    /// </summary>
    public static IReadOnlyList<(string EpisodeId, double X, double Y)> AlignByDepth(
        ChapterGraphModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        IReadOnlyDictionary<string, int> depths = Depths(model);

        if (model.StartEpisode is not { } start)
        {
            return [];
        }

        var positions = new List<(string EpisodeId, double X, double Y)>();

        foreach (IGrouping<int, ChapterEpisode> column in model.Episodes
                     .Where(episode => depths.ContainsKey(episode.EpisodeId))
                     .GroupBy(episode => depths[episode.EpisodeId]))
        {
            int slot = 0;

            foreach (ChapterEpisode episode in column
                         .OrderBy(episode => episode.Y)
                         .ThenBy(episode => episode.EpisodeId, StringComparer.Ordinal))
            {
                positions.Add((
                    episode.EpisodeId,
                    start.X + (column.Key * ColumnWidth),
                    start.Y + (slot * RowHeight)));
                slot++;
            }
        }

        return positions;
    }
}
