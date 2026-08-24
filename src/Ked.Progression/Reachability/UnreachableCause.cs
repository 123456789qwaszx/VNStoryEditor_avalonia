namespace Ked.Progression
{
    public enum UnreachableCause
    {
        NoIncomingEdge = 0,     // 들어오는 간선이 아예 없음.
        SourcesUnreachable = 1, // 들어오는 간선의 출발점부터 도달 불가.
        BlockedByCondition = 2, // 관문 조건이 어떤 경로로도 만족되지 않음.
        Undetermined = 3,       // 기타 - 탐색이 상한에서 끊겼을 때 주로 나올 것으로 예상 됨.
    }
}
