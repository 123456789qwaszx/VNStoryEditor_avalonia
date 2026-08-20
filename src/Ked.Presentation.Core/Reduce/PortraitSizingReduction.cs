namespace Ked.Presentation.Core
{
    /// <summary>
    /// CharRigImageSizingPolicy.HeightFitPreserveAspect의 리덕션.
    ///
    /// 초상은 부모(스프라이트 루트) 높이에 맞추고 가로는 종횡비로 따라간다:
    ///   폭 = 부모 rect 높이 × 종횡비,  sizeDelta = (폭, 0)
    ///
    /// y가 0인 것은 값이 없다는 뜻이 아니라 **스트레치 앵커의 증감이 0**이라는 뜻이다 —
    /// 세로는 부모를 그대로 채운다. 가로만 앵커 간격 대비 증감으로 폭을 만든다.
    ///
    /// 정렬(pivot.x·anchoredPosition.x)은 접지 않는다. 원문 경로가 쓰는 값은
    /// 언제나 Center이고, 그건 리그 초기값과 같아서 쓰나 마나 같은 상태다.
    /// (SO 저작으로 Left/Right를 쓰는 경로가 생기면 그때 축을 늘린다)
    /// </summary>
    public static class PortraitSizingReduction
    {
        public static StageNodeClaim Reduce(string imageNodeKey, float parentHeight, float aspect)
            => StageNodeClaim.SizeDelta(imageNodeKey, new Vec2(parentHeight * aspect, 0f));
    }

    /// <summary>
    /// 무대 상태에서 초상 사이징 클레임 하나를 뽑는다 —
    /// 부모 높이는 리그 트리에서, 종횡비는 초상 치수 덤프에서 온다.
    /// </summary>
    public static class PortraitSizingStageReduction
    {
        public const string ImageNodeId = "CharacterPortraitSprite_Image";
        public const string ParentNodeId = "CharacterPortraitSprite_Root";

        public static bool TryReduce(
            StageState state,
            string slotKey,
            string emotionKey,
            PortraitDimensionsFileDto dimensions,
            out StageNodeClaim claim,
            out string reason)
        {
            claim = default;

            if (dimensions == null)
            {
                reason = "초상 치수 덤프가 tuning에 없다 (portrait-dimensions.json)";
                return false;
            }

            if (!state.TryGetCharacter(slotKey, out string characterKey))
            {
                reason = $"슬롯 '{slotKey}'에 배역이 없다 — 초상 종횡비를 정할 수 없다 (cast 선행 필요)";
                return false;
            }

            if (!dimensions.TryGetAspect(characterKey, state.GetVariant(slotKey), emotionKey,
                    out float aspect, out reason))
            {
                return false;
            }

            float parentHeight = state.Nodes
                .GetRectSize(StageState.NodeKeyOf(slotKey, ParentNodeId))
                .Y;

            claim = PortraitSizingReduction.Reduce(
                StageState.NodeKeyOf(slotKey, ImageNodeId),
                parentHeight,
                aspect);

            reason = null;
            return true;
        }
    }
}
