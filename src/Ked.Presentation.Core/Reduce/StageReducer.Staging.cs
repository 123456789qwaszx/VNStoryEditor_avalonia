namespace Ked.Presentation.Core
{
    /// <summary>
    /// nudge · move · scale · rotate — 현재 값에 얹는 상대 변형.
    ///
    /// place/size와 달리 focus를 모른다. 표적 노드와 델타만 정하면 끝이라
    /// 폴드 본문은 전부 "토큰 파싱 → 표적 노드 → 리덕션 호출" 세 줄 모양이다.
    /// </summary>
    public static partial class StageReducer
    {
        private static bool ApplyNudge(
            StageState state, in StageCommand cmd, StageReducerTuning tuning,
            float xSign, float ySign, string targetId, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            string unitToken = cmd.Arg(1);

            if (!UnitToken.TryParsePixels(unitToken, tuning.ReferenceStageWidth, out float pixels))
            {
                reason = $"거리 토큰을 읽지 못했다: '{unitToken}'";
                return false;
            }

            ApplyMoveClaim(state, slotKey, targetId, relative: true, new Vec2(pixels * xSign, pixels * ySign));
            return true;
        }

        private static bool ApplyMoveBy(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!UnitToken.TryParseSignedPixels(cmd.Arg(1, "0u"), tuning.ReferenceStageWidth, out float x) ||
                !UnitToken.TryParseSignedPixels(cmd.Arg(2, "0u"), tuning.ReferenceStageWidth, out float y))
            {
                reason = $"거리 토큰을 읽지 못했다: '{cmd.Arg(1)}', '{cmd.Arg(2)}'";
                return false;
            }

            ApplyMoveClaim(state, slotKey, "CharSlot_Track", relative: true, new Vec2(x, y));
            return true;
        }

        private static bool ApplyMoveReset(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            // 브리지는 Track과 Track_Focus 두 노드에 절대 0을 건다.
            ApplyMoveClaim(state, slotKey, "CharSlot_Track", relative: false, Vec2.Zero);
            ApplyMoveClaim(state, slotKey, "CharSlot_Track_Focus", relative: false, Vec2.Zero);
            return true;
        }

        private static void ApplyMoveClaim(
            StageState state, string slotKey, string targetId, bool relative, Vec2 delta)
        {
            string nodeKey = StageState.NodeKeyOf(slotKey, targetId);

            state.Apply(MoveByReduction.Reduce(
                nodeKey,
                new MoveByReduction.Args(!relative, delta),
                state.Nodes.GetState(nodeKey).AnchoredPosition));
        }

        private static bool ApplyScaleBy(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!NumberToken.TryParseFloat(cmd.Arg(1), out float multiplier))
            {
                reason = $"배율을 읽지 못했다: '{cmd.Arg(1)}'";
                return false;
            }

            string nodeKey = StageState.NodeKeyOf(slotKey, "CharSlot_Scale");

            state.Apply(ScaleToReduction.Reduce(
                nodeKey,
                new ScaleToReduction.Args(true, new Vec2(multiplier, multiplier)),
                state.Nodes.GetState(nodeKey).LocalScale.XY));

            return true;
        }

        private static bool ApplyScaleReset(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            string nodeKey = StageState.NodeKeyOf(slotKey, "CharSlot_Scale");

            state.Apply(ScaleToReduction.Reduce(
                nodeKey,
                new ScaleToReduction.Args(false, Vec2.One),
                state.Nodes.GetState(nodeKey).LocalScale.XY));

            return true;
        }


        // char_scale_to — 초상 축의 절대 배율(브리지: CharacterPortrait_ActingScale).
        // scale_by가 미는 CharSlot_Scale과는 다른 노드다 — 슬롯을 키우는 것과
        // 초상만 키우는 것은 다른 일이고, 겹쳐 써도 서로를 덮지 않는다.
        private static bool ApplyPortraitScaleTo(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!NumberToken.TryParseFloat(cmd.Arg(1), out float scale))
            {
                reason = $"배율을 읽지 못했다: '{cmd.Arg(1)}'";
                return false;
            }

            string nodeKey = StageState.NodeKeyOf(slotKey, "CharacterPortrait_ActingScale");

            // 브리지가 xy 하나를 두 축에 함께 넣는다(toScale = new Vector2(xy, xy)).
            state.Apply(ScaleToReduction.Reduce(
                nodeKey,
                new ScaleToReduction.Args(false, new Vec2(scale, scale)),
                state.Nodes.GetState(nodeKey).LocalScale.XY));

            return true;
        }

        // char_rotate_to — 초상 축의 절대 회전(브리지: CharacterPortrait_SwayPivot).
        // rotate_by가 미는 CharSlot_SwayPivot과는 다른 노드다.
        private static bool ApplyPortraitRotateTo(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!NumberToken.TryParseFloat(cmd.Arg(1), out float degree))
            {
                reason = $"각도를 읽지 못했다: '{cmd.Arg(1)}'";
                return false;
            }

            string nodeKey = StageState.NodeKeyOf(slotKey, "CharacterPortrait_SwayPivot");

            state.Apply(RotateToReduction.Reduce(
                nodeKey,
                new RotateToReduction.Args(false, new Vec3(0f, 0f, degree)),
                state.Nodes.GetState(nodeKey).LocalEulerAngles));

            return true;
        }
        private static bool ApplyRotateBy(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!NumberToken.TryParseFloat(cmd.Arg(1), out float degree))
            {
                reason = $"각도를 읽지 못했다: '{cmd.Arg(1)}'";
                return false;
            }

            string nodeKey = StageState.NodeKeyOf(slotKey, "CharSlot_SwayPivot");

            state.Apply(RotateToReduction.Reduce(
                nodeKey,
                new RotateToReduction.Args(true, new Vec3(0f, 0f, degree)),
                state.Nodes.GetState(nodeKey).LocalEulerAngles));

            return true;
        }

        private static bool ApplyRotateReset(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            string nodeKey = StageState.NodeKeyOf(slotKey, "CharSlot_SwayPivot");

            state.Apply(RotateToReduction.Reduce(
                nodeKey,
                new RotateToReduction.Args(false, Vec3.Zero),
                state.Nodes.GetState(nodeKey).LocalEulerAngles));

            return true;
        }
    }
}
