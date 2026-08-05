namespace Ked.Presentation.Core
{
    /// <summary>
    /// 리그 노드 하나의 RectTransform 상태 값. 불변 구조체.
    ///
    /// RectTransform의 위치는 단순 TRS가 아니다 — anchoredPosition이 실제 localPosition이
    /// 되려면 anchorMin/anchorMax/pivot/sizeDelta와 부모 rect 크기가 계산에 들어간다.
    /// 그래서 TRS 셋이 아니라 아래 일곱을 담는다.
    ///
    /// 무엇을 담고 무엇을 뺐는가 — 근거는 실제 리그 코드 조사다
    /// (CharacterRigBuilder / BackgroundRigBuilder / OverlayRigBuilder /
    ///  CharRigImageSizingPolicy / OverlayRigOperations / CharacterPlacementTargetLedger):
    ///
    /// 담은 것
    /// - AnchoredPosition : 커맨드가 위치를 쓰는 자리 (Ledger.PublishAnchoredPosition)
    /// - AnchorMin/Max    : 리그 기본은 스트레치 (0,0)-(1,1) (빌더 StretchFull)이지만
    ///                      OverlayRig가 고정 앵커 (0.5,0.5)도 쓴다 — 상수화할 수 없어 담는다
    /// - Pivot            : 기본 (0.5,0.5). NeedsBottomPivot 노드는 (0.5,0).
    ///                      초상화 이미지 정렬이 pivot.x 0/0.5/1을 바꾼다
    /// - SizeDelta        : CharRigImageSizingPolicy(초상화 폭)·OverlayRigOperations(오버레이 크기)가 쓴다.
    ///                      스트레치 앵커면 "앵커 간격 대비 증감", 고정 앵커면 "크기 그 자체"
    /// - LocalScale       : 스케일 커맨드 (Ledger.PublishLocalScale). z는 유니티 기본값 1을 유지
    /// - LocalEulerAngles : 회전 커맨드. Ledger.PublishLocalEuler가 Vector3를 게시하므로 3축 전부 담는다
    ///
    /// 뺀 것
    /// - localPosition.z  : 리그 코드 어디에서도 쓰지 않는다. 항상 0으로 취급한다
    /// - offsetMin/Max    : anchoredPosition + sizeDelta와 같은 정보의 다른 표현이라 중복
    /// </summary>
    public readonly struct RectNodeState
    {
        public readonly Vec2 AnchoredPosition;
        public readonly Vec2 AnchorMin;
        public readonly Vec2 AnchorMax;
        public readonly Vec2 Pivot;
        public readonly Vec2 SizeDelta;
        public readonly Vec3 LocalScale;
        public readonly Vec3 LocalEulerAngles;

        public RectNodeState(
            Vec2 anchoredPosition,
            Vec2 anchorMin,
            Vec2 anchorMax,
            Vec2 pivot,
            Vec2 sizeDelta,
            Vec3 localScale,
            Vec3 localEulerAngles)
        {
            AnchoredPosition = anchoredPosition;
            AnchorMin = anchorMin;
            AnchorMax = anchorMax;
            Pivot = pivot;
            SizeDelta = sizeDelta;
            LocalScale = localScale;
            LocalEulerAngles = localEulerAngles;
        }

        /// <summary>
        /// 빌더 StretchFull과 같은 값: 앵커 (0,0)-(1,1) · pivot 가운데 · 크기 증감 0 · 스케일 1.
        /// 리그 노드의 초기 상태다. (struct default는 스케일이 0이라 쓰면 안 된다 — 이걸 쓸 것)
        /// </summary>
        public static readonly RectNodeState StretchFull = new RectNodeState(
            anchoredPosition: Vec2.Zero,
            anchorMin: Vec2.Zero,
            anchorMax: Vec2.One,
            pivot: Vec2.Half,
            sizeDelta: Vec2.Zero,
            localScale: Vec3.One,
            localEulerAngles: Vec3.Zero);

        public RectNodeState WithAnchoredPosition(Vec2 value)
            => new RectNodeState(value, AnchorMin, AnchorMax, Pivot, SizeDelta, LocalScale, LocalEulerAngles);

        public RectNodeState WithAnchors(Vec2 min, Vec2 max)
            => new RectNodeState(AnchoredPosition, min, max, Pivot, SizeDelta, LocalScale, LocalEulerAngles);

        public RectNodeState WithPivot(Vec2 value)
            => new RectNodeState(AnchoredPosition, AnchorMin, AnchorMax, value, SizeDelta, LocalScale, LocalEulerAngles);

        public RectNodeState WithSizeDelta(Vec2 value)
            => new RectNodeState(AnchoredPosition, AnchorMin, AnchorMax, Pivot, value, LocalScale, LocalEulerAngles);

        public RectNodeState WithLocalScale(Vec3 value)
            => new RectNodeState(AnchoredPosition, AnchorMin, AnchorMax, Pivot, SizeDelta, value, LocalEulerAngles);

        public RectNodeState WithLocalEuler(Vec3 value)
            => new RectNodeState(AnchoredPosition, AnchorMin, AnchorMax, Pivot, SizeDelta, LocalScale, value);
    }

    /// <summary>
    /// 체인이 딛고 서는 최상위 공간(예: RigSpaceRoot)의 rect.
    /// 코어의 "월드"는 절대 월드가 아니라 이 공간의 로컬 좌표다 —
    /// CharacterPlacementTargetLedger의 stopRoot가 하던 역할과 같다.
    /// </summary>
    public readonly struct RectSpace
    {
        public readonly Vec2 Size;
        public readonly Vec2 Pivot;

        public RectSpace(Vec2 size, Vec2 pivot)
        {
            Size = size;
            Pivot = pivot;
        }

        /// <summary>가운데 pivot의 기준 공간. 예: 1920×1080 무대.</summary>
        public static RectSpace Centered(float width, float height)
            => new RectSpace(new Vec2(width, height), Vec2.Half);
    }
}
