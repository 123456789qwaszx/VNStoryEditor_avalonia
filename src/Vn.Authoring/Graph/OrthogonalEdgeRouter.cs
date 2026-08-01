namespace Vn.Authoring.Graph;

/// <summary>두 endpoint 사이를 수평·수직 선분만으로 잇는 ㄱ자 경로 계산.</summary>
public static class OrthogonalEdgeRouter
{
    public static IReadOnlyList<GraphPosition> Route(
        GraphPosition from,
        GraphPosition to,
        double detour = 48)
    {
        if (detour < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(detour));
        }

        // 대상이 오른쪽에 충분히 있으면 둘 사이 중앙을 사용한다.
        // 대상이 왼쪽이거나 너무 가까우면 두 항목 오른쪽으로 우회하여 카드 위를 가로지르지 않는다.
        double middleX = to.X >= from.X + detour
            ? (from.X + to.X) / 2
            : Math.Max(from.X, to.X) + detour;

        return new[]
        {
            from,
            new GraphPosition(middleX, from.Y),
            new GraphPosition(middleX, to.Y),
            to
        };
    }
}
