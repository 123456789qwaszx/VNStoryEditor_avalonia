using System.Collections.Generic;
using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// 슬라이드(등장·퇴장)의 폴드 (2026-08-24 — 소유자: "SlideOut, SlideIn이 지금 동작하지
    /// 않는데"). 리듀서·프리뷰에 케이스가 없어 둘 다 "반영 안 된 연출"로만 남던 자리다.
    ///
    /// <b>둘은 대칭이 아니다.</b> 런타임 <c>SlideCommandBase</c>가 등장·퇴장의 차이를
    /// "현재 위치가 도착점이냐 출발점이냐" 하나로 환원하기 때문이다:
    /// <list type="bullet">
    ///   <item><c>slide_in</c> — 현재 위치가 <b>도착점</b>이므로 <b>순변위 0</b>. 화면 밖
    ///     출발점과 punch 오버슈트는 트윈 중에만 존재한다(정착 프레임은 이미 옳다).</item>
    ///   <item><c>slide_out</c> — 현재 위치가 <b>출발점</b>이므로 순변위가 방향 × 거리이고,
    ///     <b>나간 자리에 남는다</b>. 이쪽이 접히지 않으면 정착 상태가 실제로 틀린다.</item>
    /// </list>
    ///
    /// 기본값·표적·방향 별칭은 전부 브리지 실측에서 왔다
    /// (<c>CommandBridge.CharRigPresentation.cs</c> · <c>CharRigDirectionParser</c>).
    /// </summary>
    public sealed class SlideReductionTests
    {
        private const float Eps = 1e-3f;

        // 1920 / 48 = 40px. 12u = 480px — 런타임 스펙의 distance 기본값과 같은 수다.
        private const float PixelsPerUnit = 40f;

        private static Float2Dto F2(float x, float y) => new() { x = x, y = y };
        private static Float3Dto F3(float x, float y, float z) => new() { x = x, y = y, z = z };

        private static RigSchemaNodeDto Node(string id, string parent)
        {
            return new RigSchemaNodeDto
            {
                id = id,
                parent = parent,
                anchoredPosition = F2(0f, 0f),
                anchorMin = F2(0f, 0f),
                anchorMax = F2(1f, 1f),
                pivot = F2(0.5f, 0.5f),
                sizeDelta = F2(0f, 0f),
                localScale = F3(1f, 1f, 1f),
                localEulerAngles = F3(0f, 0f, 0f),
                measuredRectSize = F2(0f, 0f),
                hasCanvasGroup = false,
                canvasGroupAlpha = 1f,
            };
        }

        /// <summary>슬라이드가 만지는 사슬만 담은 최소 리그 — 표적은 CharSlot_Track이다.</summary>
        private static StageReducerTuning NewTuning()
        {
            return new StageReducerTuning
            {
                RigSchemas = new RigSchemasFileDto
                {
                    capturedUnderParentSize = F2(1920f, 1080f),
                    rigs = new List<RigSchemaRigDto>
                    {
                        new()
                        {
                            rigKind = "character",
                            sourcePrefab = "",
                            nodes = new List<RigSchemaNodeDto>
                            {
                                Node("__root", ""),
                                Node("CharSlot_Track_Idle", "__root"),
                                Node("CharSlot_Track", "CharSlot_Track_Idle"),
                                Node("CharSlot_Track_X", "CharSlot_Track"),
                                Node("CharSlot_Track_Y", "CharSlot_Track_X"),
                            },
                        },
                    },
                },
                ReferenceStageWidth = 1920f,
                BaseResolution = new Vec2(1920f, 1080f),
            };
        }

        private static StageCommand Cmd(string name, params string[] args)
            => new(name, args, "test.yarn:1");

        private static StageState Fold(params StageCommand[] commands)
        {
            StageReducerTuning tuning = NewTuning();

            return StageReducer.ApplyAll(StageReducer.CreateInitialState(tuning), commands, tuning);
        }

        private static Vec2 TrackOf(StageState state, string slotKey = "c1")
            => state.Nodes.GetState(slotKey + "/CharSlot_Track").AnchoredPosition;

        // ── 이 폴드의 이유 ───────────────────────────────────────────

        [Test]
        public void slide_out은_나간_자리에_남는다()
        {
            // ⛔ 이것이 접히지 않으면 정착 상태가 <b>틀린다</b> — 나갔는데 프리뷰는
            //    제자리에 그려 놓는다. 슬라이드 둘 중 진짜 구멍은 이쪽이었다.
            StageState state = Fold(
                Cmd("slot", "c1"),
                Cmd("slide_out", "c1", "right", "12u"));

            Assert.That(TrackOf(state).X, Is.EqualTo(12f * PixelsPerUnit).Within(Eps));
            Assert.That(TrackOf(state).Y, Is.EqualTo(0f).Within(Eps));
            Assert.That(state.Unhandled, Is.Empty);
        }

        [Test]
        public void slide_in은_자리를_바꾸지_않는다()
        {
            // 런타임에서 등장의 도착점은 클레임 시점의 현재 위치다 — 화면 밖 출발점은
            // 트윈 중에만 있다. 그래서 접는 것은 "안 움직인다"는 사실 자체다.
            StageState state = Fold(
                Cmd("slot", "c1"),
                Cmd("slide_in", "c1", "left", "12u"));

            Assert.That(TrackOf(state).X, Is.EqualTo(0f).Within(Eps));
            Assert.That(TrackOf(state).Y, Is.EqualTo(0f).Within(Eps));
        }

        [Test]
        public void 둘_다_Unhandled로_남지_않는다()
        {
            // 고칠 것이 없는 뱃지는 소음이고, 진짜 미표시를 그 안에 묻는다.
            StageState state = Fold(
                Cmd("slot", "c1"),
                Cmd("slide_in", "c1"),
                Cmd("slide_out", "c1"));

            Assert.That(state.Unhandled, Is.Empty);
        }

        [Test]
        public void slide_out은_가시성을_건드리지_않는다()
        {
            // 화면 밖으로 밀어낼 뿐이다 — 지우려면 작가가 fade_out을 함께 적는다.
            StageState state = Fold(
                Cmd("slot", "c1"),
                Cmd("slide_out", "c1"));

            Assert.That(state.GetAlpha("c1/CharacterPortraitSprite_Root"), Is.EqualTo(1f));
        }

        // ── 브리지와 같은 기본값 ─────────────────────────────────────

        [Test]
        public void 생략된_인자는_브리지의_기본값이다()
        {
            // EnqueueSlideOutSpec(direction = "right", distanceToken = "12u") —
            // 다르면 인자를 안 적은 줄에서 프리뷰가 조용히 다른 장면을 그린다.
            StageState written = Fold(Cmd("slot", "c1"), Cmd("slide_out", "c1", "right", "12u"));
            StageState omitted = Fold(Cmd("slot", "c1"), Cmd("slide_out", "c1"));

            Assert.That(TrackOf(omitted).X, Is.EqualTo(TrackOf(written).X).Within(Eps));

            // 등장의 기본 방향은 left지만 순변위가 0이라 어느 쪽이든 결과가 같다 —
            // 그래서 기본값의 증거는 퇴장 쪽에서만 선다.
            Assert.That(TrackOf(Fold(Cmd("slot", "c1"), Cmd("slide_in", "c1"))).X,
                Is.EqualTo(0f).Within(Eps));
        }

        [Test]
        public void 방향_네_낱말이_런타임과_같은_벡터다()
        {
            // CharRigDirectionParser + SlideCommandBase.DirectionToVector를 합친 것.
            Assert.That(TrackOf(Fold(Cmd("slot", "c1"), Cmd("slide_out", "c1", "left", "1u"))).X,
                Is.EqualTo(-PixelsPerUnit).Within(Eps));
            Assert.That(TrackOf(Fold(Cmd("slot", "c1"), Cmd("slide_out", "c1", "right", "1u"))).X,
                Is.EqualTo(+PixelsPerUnit).Within(Eps));
            Assert.That(TrackOf(Fold(Cmd("slot", "c1"), Cmd("slide_out", "c1", "up", "1u"))).Y,
                Is.EqualTo(+PixelsPerUnit).Within(Eps));
            Assert.That(TrackOf(Fold(Cmd("slot", "c1"), Cmd("slide_out", "c1", "down", "1u"))).Y,
                Is.EqualTo(-PixelsPerUnit).Within(Eps));
        }

        [Test]
        public void 방향_별칭과_모르는_낱말도_런타임과_같다()
        {
            // l·r·u/top/t·d/bottom/b가 별칭이고, <b>모르는 낱말은 left로 물러선다</b>
            // (파서의 default). 여기서 갈리면 오타 한 자에 캐릭터가 반대로 나간다.
            Assert.That(TrackOf(Fold(Cmd("slot", "c1"), Cmd("slide_out", "c1", "r", "1u"))).X,
                Is.EqualTo(+PixelsPerUnit).Within(Eps));
            Assert.That(TrackOf(Fold(Cmd("slot", "c1"), Cmd("slide_out", "c1", "top", "1u"))).Y,
                Is.EqualTo(+PixelsPerUnit).Within(Eps));
            Assert.That(TrackOf(Fold(Cmd("slot", "c1"), Cmd("slide_out", "c1", "bottom", "1u"))).Y,
                Is.EqualTo(-PixelsPerUnit).Within(Eps));
            Assert.That(TrackOf(Fold(Cmd("slot", "c1"), Cmd("slide_out", "c1", "왼쪽", "1u"))).X,
                Is.EqualTo(-PixelsPerUnit).Within(Eps));
        }

        [Test]
        public void 슬라이드는_move_by와_같은_노드를_민다()
        {
            // 표적이 갈리면 둘을 섞어 쓴 라인에서 자리가 어긋난다 — 런타임은 둘 다
            // CharSlot_Track이다(SlideInCommandSpecCharR.target · MoveByCommandSpecCharR).
            StageState slid = Fold(Cmd("slot", "c1"), Cmd("slide_out", "c1", "right", "3u"));
            StageState moved = Fold(Cmd("slot", "c1"), Cmd("move_by", "c1", "+3u", "0u"));

            Assert.That(TrackOf(slid).X, Is.EqualTo(TrackOf(moved).X).Within(Eps));
        }

        [Test]
        public void 퇴장은_쌓인다()
        {
            // 상대 이동이라 두 번 나가면 두 배다 — move_by와 같은 규율이다.
            StageState state = Fold(
                Cmd("slot", "c1"),
                Cmd("slide_out", "c1", "right", "2u"),
                Cmd("slide_out", "c1", "right", "3u"));

            Assert.That(TrackOf(state).X, Is.EqualTo(5f * PixelsPerUnit).Within(Eps));
        }

        // ── 조용히 넘어가지 않는다 ───────────────────────────────────

        [Test]
        public void 미스폰_슬롯은_등장도_퇴장도_Unhandled다()
        {
            // 런타임도 리그를 못 찾으면 아무것도 안 한다. 등장이 무해하다고 눈감으면
            // 오타 슬롯이 산출까지 간다.
            StageState state = Fold(
                Cmd("slide_in", "없는슬롯"),
                Cmd("slide_out", "없는슬롯"));

            Assert.That(state.Unhandled, Has.Count.EqualTo(2));
        }

        [Test]
        public void 거리_토큰이_깨지면_등장도_퇴장도_Unhandled다()
        {
            StageState state = Fold(
                Cmd("slot", "c1"),
                Cmd("slide_in", "c1", "left", "열두칸"),
                Cmd("slide_out", "c1", "right", "열두칸"));

            Assert.That(state.Unhandled, Has.Count.EqualTo(2));
            Assert.That(TrackOf(state).X, Is.EqualTo(0f).Within(Eps), "못 읽었으면 안 움직인다");
        }

        [Test]
        public void 음수_거리는_거부가_아니라_0으로_클램프다()
        {
            // 카탈로그는 "음수 불가"라고만 적어 두었는데, <b>실제 규칙은 클램프</b>다:
            // 런타임 `YarnUnitParser.Parse`도 이쪽과 같은 `UnitToken.TryParsePixels`를
            // 지나고 그 안에서 `Math.Max(0f, units)`가 걸린다("음수는 0으로 클램프").
            //
            // ⚠ 그래서 오류로 세우면 <b>런타임보다 엄해진다</b> — 게임에서는 그냥 안
            //    움직이는 줄이 툴에서만 빨간 줄이 된다. 방향은 direction 인자만이 정한다.
            StageState state = Fold(
                Cmd("slot", "c1"),
                Cmd("slide_out", "c1", "right", "-3u"));

            Assert.That(state.Unhandled, Is.Empty);
            Assert.That(TrackOf(state).X, Is.EqualTo(0f).Within(Eps));
        }
    }
}
