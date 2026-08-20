namespace Ked.Presentation.Core
{
    // ─────────────────────────────────────────────────────────────────
    // 코어의 이징 어휘. DOTween Ease의 표준 항목과 이름이 1:1이다
    // (Unset · INTERNAL_* 제외 — 재생 가능한 이징만 담는다).
    //
    // 이 1:1은 우연이 아니라 계약이다: 호스트는 문자열 한 번으로 양쪽 enum을
    // 얻고, EditMode 테스트(EaseFunctionsDOTweenParityTests)가 항목 대응과
    // 값 등가를 함께 고정한다. 항목을 더하거나 빼면 그 테스트가 먼저 운다.
    // ─────────────────────────────────────────────────────────────────
    public enum EaseKind
    {
        Linear,
        InSine,
        OutSine,
        InOutSine,
        InQuad,
        OutQuad,
        InOutQuad,
        InCubic,
        OutCubic,
        InOutCubic,
        InQuart,
        OutQuart,
        InOutQuart,
        InQuint,
        OutQuint,
        InOutQuint,
        InExpo,
        OutExpo,
        InOutExpo,
        InCirc,
        OutCirc,
        InOutCirc,
        InElastic,
        OutElastic,
        InOutElastic,
        InBack,
        OutBack,
        InOutBack,
        InBounce,
        OutBounce,
        InOutBounce,
        Flash,
        InFlash,
        OutFlash,
        InOutFlash,
    }
}