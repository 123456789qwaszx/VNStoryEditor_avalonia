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

        /// <summary>
        /// <c>slide_in</c> — <b>정착 상태는 항등이다.</b> 런타임 <c>SlideCommandBase</c>에서
        /// 등장의 도착점은 클레임 시점의 <b>현재 위치</b>이고(<c>CurrentPositionIsDestination
        /// = true</c>), 화면 밖 출발점과 punch 오버슈트는 <b>트윈 중에만</b> 존재한다.
        /// 그래서 접는 것은 "위치를 바꾸지 않는다"는 사실 그 자체다.
        ///
        /// 접지 않으면 프리뷰가 이 커맨드를 영원히 "반영 안 된 연출"로 짚는다 — 고칠 것이
        /// 없는 뱃지는 소음이고, 진짜 미표시를 그 안에 묻는다(규칙 14의 반대편).
        ///
        /// ⚠ 인자는 <b>읽고 검사한다.</b> 슬롯을 못 풀거나 거리 토큰이 깨졌으면 그것은
        /// 런타임에서도 안 도는 커맨드다 — 무해하다고 통째로 눈감으면 오타가 산출까지 간다.
        /// </summary>
        private static bool ApplySlideIn(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
            => TryReadSlide(state, cmd, tuning, SlideMotion.DefaultInDirection, out _, out _, out reason);

        /// <summary>
        /// <c>slide_out</c> — <b>나간 자리에 남는다.</b> 등장과 달리 현재 위치가 출발점이라
        /// (<c>CurrentPositionIsDestination = false</c>) 순변위가 <b>방향 × 거리</b>다.
        /// 표적 rect는 등장과 같은 <c>CharSlot_Track</c>이고 <c>move_by</c>와 같은 상대
        /// 이동이므로, 시간 보간(타임라인·재생)은 <b>노드 차이에서 저절로</b> 나온다.
        ///
        /// ⚠ <b>가시성은 건드리지 않는다</b> — 화면 밖으로 밀어낼 뿐 알파는 그대로다
        /// (런타임도 그렇다). 지우려면 작가가 <c>fade_out</c>을 함께 적는다.
        /// </summary>
        private static bool ApplySlideOut(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            if (!TryReadSlide(state, cmd, tuning, SlideMotion.DefaultOutDirection,
                    out string slotKey, out Vec2 offset, out reason))
                return false;

            ApplyMoveClaim(state, slotKey, "CharSlot_Track", relative: true, offset);
            return true;
        }

        /// <summary>
        /// 슬라이드 두 커맨드의 공통 인자 — 슬롯 · 방향 · 거리. 낱말 표와 기본값의 주인은
        /// <see cref="SlideMotion"/> 하나다(툴의 시간 계획도 같은 것을 본다 — 사본 금지).
        /// </summary>
        /// <param name="offset">정착 상태에 얹을 변위. 등장은 부르는 쪽이 버린다(항등).</param>
        private static bool TryReadSlide(
            StageState state, in StageCommand cmd, StageReducerTuning tuning,
            string defaultDirection, out string slotKey, out Vec2 offset, out string reason)
        {
            offset = Vec2.Zero;

            if (!TryGetSpawnedSlot(state, cmd, out slotKey, out reason))
                return false;

            string distanceToken = cmd.Arg(2, SlideMotion.DefaultDistanceToken);

            // ⚠ 부호 없는 파서다 — 런타임 `YarnUnitParser.Parse`가 지나는 것과 같은 함수이고,
            // 그 안에서 음수가 0으로 <b>클램프</b>된다(거부가 아니다). 어느 쪽으로 가는지는
            // direction 인자만이 정하므로 거리에 부호를 실을 자리가 없다.
            if (!UnitToken.TryParsePixels(distanceToken, tuning.ReferenceStageWidth, out float pixels))
            {
                reason = $"거리 토큰을 읽지 못했다: '{distanceToken}'";
                return false;
            }

            offset = SlideMotion.DirectionVector(cmd.Arg(1, defaultDirection)) * pixels;
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

        /// <summary>
        /// gesture — 무변으로 접는 것이 정답이다.
        ///
        /// 변위(t) = 진폭 × 곡선(t)이고 곡선이 (0,0)→(1,0)이라 순변위가 0이다.
        /// 라인 시작과 끝의 무대가 같으므로 정지 프레임은 손대지 않는 것이 옳다 —
        /// 그래서 리듀서가 곡선 내용을 알 필요가 없고, "이징은 종점에 관여하지 않는다"는
        /// 불변식도 지켜진다(그게 이 커맨드를 move_by 위에 얹지 않은 이유다).
        ///
        /// 슬롯 존재 검사는 한다 — 없는 슬롯이면 다른 커맨드와 같은 규약으로 사유를 남긴다.
        /// </summary>
        private static bool ApplyGesture(StageState state, in StageCommand cmd, out string reason)
            => TryGetSpawnedSlot(state, cmd, out _, out reason);

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
