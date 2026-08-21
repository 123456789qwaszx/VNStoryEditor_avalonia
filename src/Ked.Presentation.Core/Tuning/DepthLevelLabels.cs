namespace Ked.Presentation.Core
{
    // ─────────────────────────────────────────────────────────────────
    // depth 라벨 → 레벨 수치.
    //
    // far·back·mid·front·close는 **독립 프리셋이 아니다**. 깊이의 진실은
    // level 커브(presets/depth.json의 "level") 하나뿐이고, 라벨은 그 커브
    // 위에서 "어떤 수치를 쓸지"만 정하는 이름표다.
    //
    // 그래서 재생·정지 프레임·툴 프리뷰가 전부 같은 커브 한 장을 지난다 —
    // 종전처럼 손으로 맞춘 프리셋 표를 따로 두면 세 곳이 갈린다.
    //
    // 이 표는 사다리의 눈금이지 튜닝 값이 아니다(튜닝은 커브가 진다).
    // 눈금을 옮기고 싶으면 여기 숫자 하나만 고치면 된다.
    // ─────────────────────────────────────────────────────────────────
    public static class DepthLevelLabels
    {
        /// <summary>
        /// 눈금. 커브 설계 구간은 [0,16]이고 라벨은 그중 [0,10]에 균등하게 얹혀 있다 —
        /// close 위로 6레벨이 비어 있는 것은 의도다(더 붙는 클로즈업이 필요할 때 쓴다).
        /// </summary>
        public const float Far = 0f;
        public const float Back = 2.5f;
        public const float Mid = 5f;
        public const float Front = 7.5f;
        public const float Close = 10f;

        /// <summary>실험용 눈금. close 너머 / mid와 같은 자리 — 필요하면 여기서 옮긴다.</summary>
        public const float Exp1 = 12f;
        public const float Exp2 = Mid;

        /// <summary>
        /// 라벨 토큰 → 레벨. 별칭 목록은 런타임 CharacterDepthPresetParser와 같아야 한다 —
        /// 한쪽만 알면 그 토큰에서 재생과 폴드가 갈린다.
        /// </summary>
        public static bool TryGetLevel(string token, out float level)
        {
            switch ((token ?? "").Trim().ToLowerInvariant())
            {
                case "none":
                case "n":
                    level = Mid;
                    return true;

                case "far":
                case "f":
                    level = Far;
                    return true;

                case "back":
                case "b":
                    level = Back;
                    return true;

                case "mid":
                case "middle":
                case "normal":
                case "default":
                case "m":
                    level = Mid;
                    return true;

                case "front":
                case "fore":
                case "foreground":
                    level = Front;
                    return true;

                case "close":
                case "near":
                case "c":
                    level = Close;
                    return true;

                case "exp1":
                case "experimental1":
                    level = Exp1;
                    return true;

                case "exp2":
                case "experimental2":
                    level = Exp2;
                    return true;

                default:
                    level = Mid;
                    return false;
            }
        }
    }
}
