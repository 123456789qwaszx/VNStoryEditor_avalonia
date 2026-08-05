using Ked.Presentation.Core;
using Vn.App.Services;
using Vn.Authoring.Assets;
using Vn.Authoring.Flow;

namespace Vn.App.Tests;

/// <summary>
/// W25 — 코어 좌표 배치. 컴포저는 좌표를 계산하지 않는다(D-core-2): 코어가 접은 좌표를
/// 캔버스 좌표계로 옮기고 샷 규약을 씌울 뿐이다. 그 변환이 왜곡 없는지를 여기서 고정한다.
/// U14가 코어 좌표 자체의 유니티 등가를 보증하므로, 좌표 값의 옳음은 다시 증명하지 않는다.
/// </summary>
public class StageSceneComposerCoreTests
{
    // 실덤프 픽스처는 저장소에 한 벌만 둔다 — Vn.Authoring.Tests의 것을 그대로 읽는다.
    private static readonly string FixtureDirectory = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..",
        "Vn.Authoring.Tests", "TuningFixtures", "ExportedTuning"));

    private const double Width = 1920;
    private const double Height = 1080;

    private static StageReducerTuning Tuning { get; } =
        RuntimeTuningLibrary.Load(FixtureDirectory, (Width, Height)).Tuning!;

    private static StageState CoreState(params StageCommand[] commands)
    {
        StageState state = StageReducer.CreateInitialState(Tuning);

        foreach (StageCommand command in commands)
        {
            state = StageReducer.Apply(state, command, Tuning);
        }

        Assert.Empty(state.Unhandled); // 시나리오의 커맨드는 전부 접혀야 한다 — 침묵 방지.
        return state;
    }

    private static MiniStageState Projection(params string[] visibleSlots)
    {
        var slots = visibleSlots.ToDictionary(
            slot => slot,
            slot => new MiniStageSlot("parkeunseol", "a", "01", Visible: true, Mirrored: false),
            StringComparer.Ordinal);

        return MiniStageState.Empty with { Slots = slots };
    }

    private static StagePortraitPlacement SinglePortrait(StageState core, MiniStageState projection)
    {
        StageSceneLayout layout = StageSceneComposer.Compose(
            projection, speakerName: null, speakerCharacterId: null, Width, Height, core);

        return Assert.Single(layout.Portraits);
    }

    [Fact]
    public void place_left와_right가_실제로_좌우_자리를_가른다()
    {
        StageCommand[] common =
        [
            new("slot", ["c1"]),
            new("cast", ["c1", "parkeunseol", "a", "1"]),
            new("show", ["c1"])
        ];

        StagePortraitPlacement left = SinglePortrait(
            CoreState([.. common, new StageCommand("place", ["c1", "face", "left"])]),
            Projection("c1"));

        StagePortraitPlacement right = SinglePortrait(
            CoreState([.. common, new StageCommand("place", ["c1", "face", "right"])]),
            Projection("c1"));

        double leftCenter = left.Rect.X + left.Rect.Width / 2;
        double rightCenter = right.Rect.X + right.Rect.Width / 2;

        Assert.True(leftCenter < Width / 2, $"left 초상 중심({leftCenter})은 화면 왼쪽이어야 한다");
        Assert.True(rightCenter > Width / 2, $"right 초상 중심({rightCenter})은 화면 오른쪽이어야 한다");
        Assert.True(left.Rect.Width > 0 && left.Rect.Height > 0);

        // 같은 캐릭터·같은 뎁스 — 위치만 다르고 크기는 같다(왜곡 없음).
        Assert.Equal(left.Rect.Width, right.Rect.Width, precision: 3);
        Assert.Equal(left.Rect.Height, right.Rect.Height, precision: 3);
    }

    [Fact]
    public void shot_zoom이_배율_규약대로_초상을_키운다()
    {
        StageCommand[] common =
        [
            new("slot", ["c1"]),
            new("cast", ["c1", "parkeunseol", "a", "1"]),
            new("show", ["c1"]),
            new("place", ["c1", "face", "center"])
        ];

        StagePortraitPlacement plain = SinglePortrait(CoreState(common), Projection("c1"));
        StagePortraitPlacement zoomed = SinglePortrait(
            CoreState([.. common, new StageCommand("shot_zoom", ["4"])]),
            Projection("c1"));

        // 적용측 규약: 배율 = 1 + zoom × 0.05 (ShotIntentMath). 컴포저는 이 규약을 재구현하지
        // 않고 그대로 쓴다 — 배율만큼 커진 rect가 그 증거다.
        double expectedScale = ShotIntentMath.EvaluateCameraScale(4f);
        Assert.Equal(plain.Rect.Width * expectedScale, zoomed.Rect.Width, precision: 2);
        Assert.Equal(plain.Rect.Height * expectedScale, zoomed.Rect.Height, precision: 2);
    }

    [Fact]
    public void size_close가_초상을_더_크게_그린다()
    {
        StageCommand[] common =
        [
            new("slot", ["c1"]),
            new("cast", ["c1", "parkeunseol", "a", "1"]),
            new("show", ["c1"])
        ];

        StagePortraitPlacement mid = SinglePortrait(
            CoreState([.. common, new StageCommand("size_mid", ["c1"])]),
            Projection("c1"));
        StagePortraitPlacement close = SinglePortrait(
            CoreState([.. common, new StageCommand("size_close", ["c1"])]),
            Projection("c1"));

        Assert.True(
            close.Rect.Height > mid.Rect.Height,
            $"close({close.Rect.Height})는 mid({mid.Rect.Height})보다 커야 한다");
    }

    [Fact]
    public void 코어가_모르는_슬롯은_사라지지_않고_균등_나열로_남는다()
    {
        // 보완 폴드만 아는 슬롯(slot_tyrant·관대한 생성분) — 코어에 없다고 화면에서 지우면 침묵이다.
        StageState core = CoreState(
            new StageCommand("slot", ["c1"]),
            new StageCommand("cast", ["c1", "parkeunseol", "a", "1"]),
            new StageCommand("show", ["c1"]));

        MiniStageState projection = Projection("c1", "tyrant");

        StageSceneLayout layout = StageSceneComposer.Compose(
            projection, speakerName: null, speakerCharacterId: null, Width, Height, core);

        Assert.Equal(2, layout.Portraits.Count);
        Assert.Contains(layout.Portraits, portrait => portrait.SlotKey == "tyrant");
    }

    [Fact]
    public void 슬롯의_레이어가_그리기_앞뒤를_가른다()
    {
        // c1은 앞(close), c2는 뒤(far) — 슬롯 키 순서가 아니라 부착 레이어가 순서다 (W27).
        // 캔버스는 나중에 그린 것이 위에 오므로 far(c2)가 먼저, close(c1)가 나중이어야 한다.
        StageState core = CoreState(
            new StageCommand("slot", ["c1", "stage00", "close"]),
            new StageCommand("cast", ["c1", "parkeunseol", "a", "1"]),
            new StageCommand("show", ["c1"]),
            new StageCommand("slot", ["c2", "stage00", "far"]),
            new StageCommand("cast", ["c2", "parkeunseol", "a", "1"]),
            new StageCommand("show", ["c2"]));

        StageSceneLayout layout = StageSceneComposer.Compose(
            Projection("c1", "c2"), speakerName: null, speakerCharacterId: null, Width, Height, core);

        Assert.Equal(["c2", "c1"], layout.Portraits.Select(portrait => portrait.SlotKey));
    }

    [Fact]
    public void 무대가_다르면_뒤_무대가_먼저_그려진다()
    {
        StageState core = CoreState(
            new StageCommand("slot", ["c1", "stage01", "mid"]),
            new StageCommand("cast", ["c1", "parkeunseol", "a", "1"]),
            new StageCommand("show", ["c1"]),
            new StageCommand("slot", ["c2", "stage00", "mid"]),
            new StageCommand("cast", ["c2", "parkeunseol", "a", "1"]),
            new StageCommand("show", ["c2"]));

        StageSceneLayout layout = StageSceneComposer.Compose(
            Projection("c1", "c2"), speakerName: null, speakerCharacterId: null, Width, Height, core);

        // stage00이 stage01보다 뒤(Ordinal 오름차순) — c2가 먼저 그려진다.
        Assert.Equal(["c2", "c1"], layout.Portraits.Select(portrait => portrait.SlotKey));
    }

    [Fact]
    public void 숨긴_슬롯도_자리가_배치된다_뷰가_고스트로_그린다()
    {
        // show 없이 캐스팅만 — 숨김 슬롯. 자리는 계산되고(Visible=false), 뷰가 윤곽+태그로 그린다(W28).
        StageState core = CoreState(
            new StageCommand("slot", ["c1"]),
            new StageCommand("cast", ["c1", "parkeunseol", "a", "1"]));

        MiniStageState projection = MiniStageState.Empty with
        {
            Slots = new Dictionary<string, MiniStageSlot>(StringComparer.Ordinal)
            {
                ["c1"] = new MiniStageSlot("parkeunseol", "a", "01", Visible: false, Mirrored: false)
            }
        };

        StageSceneLayout layout = StageSceneComposer.Compose(
            projection, speakerName: null, speakerCharacterId: null, Width, Height, core);

        StagePortraitPlacement ghost = Assert.Single(layout.Portraits);
        Assert.False(ghost.Slot.Visible);
        Assert.False(ghost.IsSpeaker);
        Assert.True(ghost.Rect.Width > 0 && ghost.Rect.Height > 0);
    }

    [Fact]
    public void 코어_상태가_없으면_기존_균등_나열_그대로다()
    {
        MiniStageState projection = Projection("c1", "c2");

        StageSceneLayout withNull = StageSceneComposer.Compose(
            projection, null, null, Width, Height, coreState: null);
        StageSceneLayout legacy = StageSceneComposer.Compose(
            projection, null, null, Width, Height);

        Assert.Equal(
            legacy.Portraits.Select(portrait => portrait.Rect),
            withNull.Portraits.Select(portrait => portrait.Rect));
    }
}
