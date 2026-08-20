namespace Ked.Presentation.Core
{
    // ─────────────────────────────────────────────────────────────────
    // "스펙 → 목표 상태" 변환의 본보기 둘. 이후 이관이 전부 이 모양을 따른다.
    //
    // 규약:
    // - 커맨드당 static class XxxReduction 하나, 파싱된 인자는 Args 구조체.
    // - Reduce(...)는 순수 함수다: 시간·랜덤·IO·전역 상태 없음.
    //   현재 상태가 필요하면 인자로 명시해서 받는다(트랜스폼을 읽지 않는다).
    // - 출력은 StageNodeClaim — 호스트는 게시·트윈 종점으로 쓰고, 리듀서는 트리에 접는다.
    //
    // 경계(호스트에 남는 것): CommandBase(코루틴 호스트)·ResolveRefs·DOKill·
    // 트윈 실행·Commit의 트랜스폼 쓰기·HasClaimedTarget 수명 관리.
    // 전문: Documentation~/reduction-boundary.md
    // ─────────────────────────────────────────────────────────────────

    public static class MoveByReduction
    {
        public readonly struct Args
        {
            public readonly bool UseAbsolutePosition;
            public readonly Vec2 Delta;

            public Args(bool useAbsolutePosition, Vec2 delta)
            {
                UseAbsolutePosition = useAbsolutePosition;
                Delta = delta;
            }
        }

        public static StageNodeClaim Reduce(
            string nodeKey,
            in Args args,
            Vec2 currentAnchoredPosition)
        {
            Vec2 destination = args.UseAbsolutePosition
                ? args.Delta
                : currentAnchoredPosition + args.Delta;

            return StageNodeClaim.AnchoredPosition(nodeKey, destination);
        }
    }
    
    public static class RotateToReduction
    {
        public readonly struct Args
        {
            public readonly bool RelativeToCurrent;
            public readonly Vec3 ToEuler;

            public Args(bool relativeToCurrent, Vec3 toEuler)
            {
                RelativeToCurrent = relativeToCurrent;
                ToEuler = toEuler;
            }
        }

        public static StageNodeClaim Reduce(
            string nodeKey,
            in Args args,
            Vec3 currentLocalEuler)
        {
            Vec3 target = args.RelativeToCurrent
                ? currentLocalEuler + args.ToEuler
                : args.ToEuler;

            return StageNodeClaim.LocalEuler(nodeKey, target);
        }
    }

    public static class ScaleToReduction
    {
        public readonly struct Args
        {
            public readonly bool RelativeToCurrent;
            public readonly Vec2 ToScale;

            public Args(bool relativeToCurrent, Vec2 toScale)
            {
                RelativeToCurrent = relativeToCurrent;
                ToScale = toScale;
            }
        }

        public static StageNodeClaim Reduce(
            string nodeKey,
            in Args args,
            Vec2 currentLocalScaleXY)
        {
            Vec2 target = args.RelativeToCurrent
                ? Vec2.Scale(currentLocalScaleXY, args.ToScale)
                : args.ToScale;

            return StageNodeClaim.LocalScaleXY(nodeKey, target);
        }
    }
}