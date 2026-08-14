namespace Vn.Authoring.Chapters;

/// <summary>
/// 스탯 증감의 범위 하나. 도달성 증명(G7)이 상태 전이의 경계로 쓴다.
///
/// 원래는 에피소드 워크북의 `스탯변화`(J열) 합에서 계산했지만, 2026-08-14 소유자 결정으로
/// 대사 중 A계층 직접 조작이 폐지되어 그 계산기는 사라졌다 — 지금 에피소드의 증감은 전부
/// 0이고, 수치 조정의 새 경로가 정해지면 그 원천이 이 타입으로 다시 흘러든다.
/// </summary>
public sealed record StatDeltaRange(int Minimum, int Maximum)
{
    public static StatDeltaRange Zero { get; } = new(0, 0);

    public StatDeltaRange Plus(StatDeltaRange other) =>
        new(Minimum + other.Minimum, Maximum + other.Maximum);

    /// <summary>지나가지 않을 수도 있는 구간 — 0을 범위에 포함시킨다.</summary>
    public StatDeltaRange OrSkip() => new(Math.Min(Minimum, 0), Math.Max(Maximum, 0));
}
