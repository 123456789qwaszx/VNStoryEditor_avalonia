namespace Ked.Presentation.Core
{
    // ─────────────────────────────────────────────────────────────────
    // char_rig_placement 계열의 "스펙 → 목표 상태" 변환 (U13-b-4 본보기).
    //
    // 규약 — 51곳 이관이 전부 이 모양을 따른다:
    // - 커맨드당 static class XxxReduction 하나, 파싱된 인자는 Args 구조체.
    // - Reduce(...)는 순수 함수다: 시간·랜덤·IO·전역 상태 없음.
    //   현재 상태가 필요하면 인자로 명시해서 받는다(트랜스폼을 읽지 않는다).
    // - 출력은 StageNodeClaim — 호스트는 게시·트윈 종점으로 쓰고,
    //   리듀서는 트리에 접는다.
    //
    // 경계(호스트에 남는 것): CommandBase(코루틴 호스트)·ResolveRefs·DOKill·
    // 트윈 실행·Commit의 트랜스폼 쓰기·HasClaimedTarget 수명 관리.
    // 전문: Documentation~/reduction-boundary.md
    // ─────────────────────────────────────────────────────────────────

    /// <summary>move_by — 위치 이동. (MoveByCommandCharR / MoveByCommandBgR 공용 산수)</summary>
    public static class MoveByReduction
    {
        public readonly struct Args
        {
            /// <summary>false: delta는 현재 위치 기준 오프셋. true: delta가 목표 절대값.</summary>
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

    /// <summary>rotate_to / rotate_by / rotate_reset — 회전. 절대 또는 현재에 가산.</summary>
    public static class RotateToReduction
    {
        public readonly struct Args
        {
            /// <summary>false: ToEuler가 절대 localEulerAngles. true: 현재에 더한다.</summary>
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

    /// <summary>scale_to — 스케일 변경. 절대 또는 현재 기준 배율.</summary>
    public static class ScaleToReduction
    {
        public readonly struct Args
        {
            /// <summary>false: ToScale이 절대 스케일. true: 현재 스케일에 곱한다.</summary>
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
