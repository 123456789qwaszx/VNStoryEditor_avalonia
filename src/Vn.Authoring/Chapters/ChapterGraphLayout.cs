namespace Vn.Authoring.Chapters;

/// <summary>
/// 엑셀 좌표를 캔버스 좌표로 옮기는 <b>평행이동 하나</b>. 그 이상은 하지 않는다.
///
/// <b>자동 레이아웃은 금지다</b>(지시서 §6). 노드가 어디 있는지는 기획자가 엑셀 X·Y로 정하고
/// 툴은 그저 보여 줄 뿐이다(G-2). 그래서 여기에는 힘 기반 배치도, 겹침 회피도, 격자 스냅도 없다 —
/// 있으면 "엑셀에 적은 자리에 그려진다"가 거짓이 된다.
///
/// 평행이동이 필요한 이유는 하나뿐이다: 엑셀 좌표는 음수일 수 있는데(견본의 Y = -120)
/// 캔버스 원점은 좌상단이라, 가장 작은 좌표가 여백 안으로 들어오게 통째로 민다.
/// <b>노드 사이의 상대 위치는 보존된다.</b>
/// </summary>
public sealed record ChapterGraphLayout(double OffsetX, double OffsetY, double Width, double Height)
{
    /// <param name="margin">캔버스 가장자리와 가장 바깥 노드 사이의 여백.</param>
    public static ChapterGraphLayout For(
        IReadOnlyList<ChapterEpisode> episodes,
        double nodeWidth,
        double nodeHeight,
        double margin)
    {
        ArgumentNullException.ThrowIfNull(episodes);

        if (episodes.Count == 0)
        {
            return new ChapterGraphLayout(margin, margin, margin * 2, margin * 2);
        }

        double minX = episodes.Min(episode => episode.X);
        double minY = episodes.Min(episode => episode.Y);
        double maxX = episodes.Max(episode => episode.X);
        double maxY = episodes.Max(episode => episode.Y);

        return new ChapterGraphLayout(
            margin - minX,
            margin - minY,
            maxX - minX + nodeWidth + (margin * 2),
            maxY - minY + nodeHeight + (margin * 2));
    }

    /// <summary>노드 카드의 좌상단 캔버스 좌표.</summary>
    public (double X, double Y) Place(ChapterEpisode episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        return (episode.X + OffsetX, episode.Y + OffsetY);
    }

    public (double X, double Y) Center(ChapterEpisode episode, double nodeWidth, double nodeHeight)
    {
        (double x, double y) = Place(episode);
        return (x + (nodeWidth / 2), y + (nodeHeight / 2));
    }
}
