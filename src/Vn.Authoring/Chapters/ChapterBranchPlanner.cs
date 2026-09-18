namespace Vn.Authoring.Chapters;

/// <summary>
/// 챕터 그래프의 배치 계산 — 깊이(depth) 기반. 챕터 그래프는 왼쪽에서 오른쪽으로 흐르는
/// 이야기이므로, 노드의 열(column)은 시작 에피소드에서 그 노드까지의 <b>가장 긴 경로</b>로
/// 정한다. 합류 노드(두 분기가 다시 만나는 곳)는 가장 깊은 부모보다 한 열 오른쪽에 서서
/// 간선이 뒤로 꺾이지 않는다.
///
/// ⛔ <b>~~배치는 툴이 소유한다 (v3)~~ — 걷혔다 (v4, 2026-09-18 소유자:</b> *"챕터그래프도
/// 연출그래프와 동일하게 자유롭게 노드의 위치를 조절할 수 있게"*). v3의 근거는 <i>"드래그가
/// 없으므로 사람이 지킬 배치도 없다"</i>였는데, 그 <b>전제가 뒤집혔다</b> — 드래그가 생겼다.
/// 자리의 주인은 이제 <see cref="ChapterEpisode.X"/>·<see cref="ChapterEpisode.Y"/>이고
/// <c>ProjectEditor.MoveEpisode</c>가 고친다(R-F가 2026-09-16에 엑셀에게서 가져온 그 값이다).
///
/// <b>그래서 이 계산은 「그리는 자리」가 아니라 「첫 자리」다.</b> 세 곳이 쓴다:
///
/// <list type="number">
///   <item><see cref="SuggestPlacement"/> — 새로 세우는 카드 하나의 자리</item>
///   <item><see cref="NeedsSeeding"/>가 참일 때 <see cref="Layout"/>로 <b>한 번 심기</b> —
///         엑셀에서 들여온 챕터는 X·Y가 전부 0이라 한 자리에 포개진다</item>
///   <item>[자동 정렬] — 사람이 흐트러뜨린 판을 부를 때 다시 세운다</item>
/// </list>
///
/// ⚠ 자동으로 <b>다시</b> 계산하지 않는다. 사람이 옮긴 자리를 그리기가 덮으면, 옮긴 일이
/// 아무 일도 아니게 된다.
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
    /// 화면 배치 전체 — 원점 (0,0) 기준. 열 = 깊이, 열 안 순서 = 시트 행 순서(사람이 정한
    /// 유일한 순서). 간선이 없어 깊이가 없는 노드는 맨 아래 줄에 따로 세운다 — 이미 ⚠로
    /// 보고되는 것을 겹쳐 숨기지 않는다.
    /// </summary>
    public static IReadOnlyDictionary<string, (double X, double Y)> Layout(ChapterGraphModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        IReadOnlyDictionary<string, int> depths = Depths(model);
        var positions = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);

        int deepestSlot = 0;

        foreach (IGrouping<int, ChapterEpisode> column in model.Episodes
                     .Where(episode => depths.ContainsKey(episode.EpisodeId))
                     .GroupBy(episode => depths[episode.EpisodeId]))
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
                     .Where(episode => !depths.ContainsKey(episode.EpisodeId)))
        {
            positions[episode.EpisodeId] = (orphanColumn * ColumnWidth, orphanY);
            orphanColumn++;
        }

        return positions;
    }

    /// <summary>
    /// <b>아직 자리가 없다</b> — 카드 둘 이상이 같은 점에 서 있다.
    ///
    /// v4에서 자리의 주인이 사람으로 넘어오면서 생긴 물음이다: 엑셀에서 들여온 챕터는 X·Y가
    /// 전부 0이라, 그냥 제 값대로 그리면 <b>한 점에 전부 포개진다</b>. 그때 한 번
    /// <see cref="Layout"/>으로 심어 준다.
    ///
    /// ⚠ <b>"전부 0"이 아니라 "겹친다"로 묻는다.</b> 에피소드 하나짜리 챕터는 (0,0)이어도
    /// 아무 문제가 없고, 반대로 사람이 둘을 정확히 포개 놓았다면 그것도 고쳐 줄 자리다.
    ///
    /// ⚠ 이 판정은 <b>한 번만</b> 참이다 — 심고 나면 자리가 서로 달라지므로 다시 심지 않는다.
    /// 그래야 그리기가 사람이 옮긴 자리를 덮는 일이 없다.
    /// </summary>
    public static bool NeedsSeeding(ChapterGraphModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var seen = new HashSet<(double X, double Y)>();

        foreach (ChapterEpisode episode in model.Episodes)
        {
            if (!seen.Add((Math.Round(episode.X, 2), Math.Round(episode.Y, 2))))
            {
                return true;
            }
        }

        return false;
    }
}
