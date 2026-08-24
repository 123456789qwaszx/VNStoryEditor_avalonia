using System;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 슬라이드(등장·퇴장)의 <b>공유 계약</b> — 방향 낱말 · 스펙 기본값 · 튐 모양.
    ///
    /// 리듀서(정착 상태)와 툴의 시간 계획(트윈 모양)이 <b>같은 하나</b>를 봐야 한다.
    /// 방향 표를 두 곳에 적으면 오타 한 자에 캐릭터가 반대로 나가고, 그 어긋남은
    /// "프리뷰와 게임이 다르다"로만 드러난다.
    ///
    /// 값의 출처는 전부 런타임 실코드다:
    /// <list type="bullet">
    ///   <item>방향 낱말·별칭 — <c>CharRigDirectionParser.ParseSlideDirection</c></item>
    ///   <item>방향 벡터 — <c>SlideCommandBase.DirectionToVector</c></item>
    ///   <item>기본 방향·거리 — 브리지 <c>EnqueueSlideInSpec</c>·<c>EnqueueSlideOutSpec</c>
    ///     (스펙 필드의 기본값이 아니라 <b>브리지 시그니처</b>가 Yarn 경로의 기본값이다)</item>
    ///   <item>ease·punch — <c>SlideInCommandSpecCharR</c>·<c>SlideOutCommandSpecCharR</c>
    ///     (Yarn 인자가 없는 축이라 스펙 필드값이 언제나 쓰인다)</item>
    ///   <item>튐 모양 — <c>SlideCommandBase.BumpTowardEnd</c>·<c>BumpFromStart</c></item>
    /// </list>
    /// </summary>
    public static class SlideMotion
    {
        /// <summary>등장이냐 퇴장이냐 — 런타임의 <c>CurrentPositionIsDestination</c> 하나다.</summary>
        public enum Kind
        {
            /// <summary>현재 위치가 <b>도착점</b>. 정착 상태는 항등이다.</summary>
            In,

            /// <summary>현재 위치가 <b>출발점</b>. 정착 상태가 방향 × 거리만큼 움직인다.</summary>
            Out,
        }

        public const string InCommand = "slide_in";

        public const string OutCommand = "slide_out";

        /// <summary>이 출력 커맨드가 슬라이드인가. 이름 표의 주인은 여기 하나다.</summary>
        public static Kind? KindOf(string outputCommand)
        {
            switch (outputCommand)
            {
                case InCommand: return Kind.In;
                case OutCommand: return Kind.Out;
                default: return null;
            }
        }

        public static string DefaultDirection(Kind kind)
            => kind == Kind.In ? DefaultInDirection : DefaultOutDirection;

        public static EaseKind EaseOf(Kind kind) => kind == Kind.In ? InEase : OutEase;

        public static float PunchPixels(Kind kind) => kind == Kind.In ? InPunchPixels : OutPunchPixels;

        /// <summary>진행도 → 튐의 세기. 등장은 끝에서, 퇴장은 출발에서 튄다.</summary>
        public static float Punch(Kind kind, float eased)
            => kind == Kind.In ? PunchTowardEnd(eased) : PunchFromStart(eased);

        /// <summary>등장은 <b>왼쪽에서 들어온다</b>(direction = 온 방향).</summary>
        public const string DefaultInDirection = "left";

        /// <summary>퇴장은 <b>오른쪽으로 나간다</b>(direction = 갈 방향).</summary>
        public const string DefaultOutDirection = "right";

        public const string DefaultDistanceToken = "12u";

        /// <summary>등장의 이징. Yarn 인자가 없으므로 스펙 필드값이 언제나 쓰인다.</summary>
        public const EaseKind InEase = EaseKind.OutCubic;

        /// <summary>퇴장의 이징 — <b>등장과 다르다</b>. 물러서기(OutCubic)로 두면 반대로 흐른다.</summary>
        public const EaseKind OutEase = EaseKind.InCubic;

        /// <summary>등장의 튐(px) — 도착 직전에 살짝 지나쳤다 앉는다.</summary>
        public const float InPunchPixels = 24f;

        /// <summary>퇴장의 튐(px) — 출발 직후에 튀어 나간다.</summary>
        public const float OutPunchPixels = 14f;

        /// <summary>
        /// 방향 낱말 → 단위 벡터. 별칭 목록도, <b>모르는 낱말이 left로 물러서는 것도</b>
        /// 런타임 파서와 같아야 한다 — 여기서 갈리면 오타 한 자에 캐릭터가 반대로 나간다.
        /// </summary>
        public static Vec2 DirectionVector(string direction)
        {
            switch (direction?.Trim().ToLowerInvariant())
            {
                case "right":
                case "r":
                    return new Vec2(+1f, 0f);

                case "up":
                case "u":
                case "top":
                case "t":
                    return new Vec2(0f, +1f);

                case "down":
                case "d":
                case "bottom":
                case "b":
                    return new Vec2(0f, -1f);

                default:
                    return new Vec2(-1f, 0f);
            }
        }

        /// <summary>도착 직전에 부풀었다 사그라드는 튐 — 등장용(<c>BumpTowardEnd</c>).</summary>
        public static float PunchTowardEnd(float eased)
        {
            eased = Clamp01(eased);

            return (float)Math.Sin(Math.PI * eased) * (eased * eased);
        }

        /// <summary>출발 직후에 부풀었다 사그라드는 튐 — 퇴장용(<c>BumpFromStart</c>).</summary>
        public static float PunchFromStart(float eased)
        {
            eased = Clamp01(eased);

            float oneMinus = 1f - eased;

            return (float)Math.Sin(Math.PI * eased) * (oneMinus * oneMinus);
        }

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
