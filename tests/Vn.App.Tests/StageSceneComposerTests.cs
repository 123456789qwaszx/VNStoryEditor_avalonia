using Vn.App.Services;
using Vn.Authoring.Definition;
using Vn.Authoring.Flow;

namespace Vn.App.Tests;

/// <summary>
/// W18 무대 배치 계산. 기준 해상도 좌표계에서 boxKind별 레이아웃과 초상화 나열이
/// 비율로 고정된다 — 창 크기와 무관하게 같은 그림이 나오는 근거다.
/// </summary>
public class StageSceneComposerTests
{
    private const double W = 1920;
    private const double H = 1080;

    private static MiniStageState StateWith(
        params (string SlotKey, string CharacterId)[] slots)
    {
        var dictionary = slots.ToDictionary(
            item => item.SlotKey,
            item => new MiniStageSlot(item.CharacterId, "a", "01", Visible: true, Mirrored: false),
            StringComparer.Ordinal);

        return MiniStageState.Empty with { Slots = dictionary };
    }

    [Fact]
    public void 기준_해상도는_기본_1920x1080이고_정의가_교체한다()
    {
        Assert.Equal((1920, 1080), GameDefinition.Empty.PreviewResolution);

        GameDefinition custom = GameDefinition.Parse("""
            { "preview": { "resolution": "1280x720" } }
            """)!;
        Assert.Equal((1280, 720), custom.PreviewResolution);

        GameDefinition broken = GameDefinition.Parse("""
            { "preview": { "resolution": "정사각형" } }
            """)!;
        Assert.Equal((1920, 1080), broken.PreviewResolution);
    }

    [Fact]
    public void 초상화는_슬롯_키_순서로_중앙_균등_나열된다()
    {
        StageSceneLayout layout = StageSceneComposer.Compose(
            StateWith(("c2", "willo"), ("c1", "laru")),
            speakerName: null,
            speakerCharacterId: null,
            W,
            H);

        Assert.Equal(["c1", "c2"], layout.Portraits.Select(portrait => portrait.SlotKey));

        StageRect first = layout.Portraits[0].Rect;
        StageRect second = layout.Portraits[1].Rect;

        // 같은 크기, 같은 바닥선, 좌우 대칭 배치.
        Assert.Equal(first.Width, second.Width);
        Assert.Equal(first.Bottom, second.Bottom);
        Assert.Equal(first.X, W - second.Right, precision: 6);
        Assert.True(first.Right < second.X); // 겹치지 않는다
        Assert.InRange(first.Bottom, 0, H);
    }

    [Fact]
    public void 슬롯이_많아도_무대_밖으로_잘리지_않는다()
    {
        StageSceneLayout layout = StageSceneComposer.Compose(
            StateWith(Enumerable.Range(0, 9).Select(i => ($"c{i}", $"ch{i}")).ToArray()),
            null,
            null,
            W,
            H);

        Assert.All(layout.Portraits, portrait =>
        {
            Assert.True(portrait.Rect.X >= -0.001);
            Assert.True(portrait.Rect.Right <= W + 0.001);
        });
    }

    [Fact]
    public void 화자는_무대_위_캐릭터와_characterId로_짝지어진다()
    {
        StageSceneLayout layout = StageSceneComposer.Compose(
            StateWith(("c1", "laru"), ("c2", "willo")),
            speakerName: "라루",
            speakerCharacterId: "laru",
            W,
            H);

        Assert.True(layout.Portraits.Single(portrait => portrait.SlotKey == "c1").IsSpeaker);
        Assert.False(layout.Portraits.Single(portrait => portrait.SlotKey == "c2").IsSpeaker);
        Assert.Null(layout.OffStageSpeakerName);

        // 무대에 없는 화자는 이름만 남는다.
        StageSceneLayout offStage = StageSceneComposer.Compose(
            StateWith(("c2", "willo")), "라루", "laru", W, H);
        Assert.Equal("라루", offStage.OffStageSpeakerName);
    }

    [Fact]
    public void boxKind별_대사창_레이아웃이_다르다()
    {
        StageDialogueBoxPlacement Box(string kind, bool hasSpeaker = true)
        {
            MiniStageState state = MiniStageState.Empty with
            {
                NamedBoxKind = kind,
                ProtagonistBoxKind = kind
            };

            return StageSceneComposer.Compose(
                state, hasSpeaker ? "라루" : null, null, W, H).DialogueBox!;
        }

        // Speaker: 하단 박스 + 이름표, 근사 아님.
        StageDialogueBoxPlacement speaker = Box("Speaker");
        Assert.Equal(StageDialogueBoxStyle.Speaker, speaker.Style);
        Assert.False(speaker.Approximated);
        Assert.NotNull(speaker.BoxRect);
        Assert.NotNull(speaker.NameRect);
        Assert.True(speaker.BoxRect!.Y > H / 2); // 하단에 있다

        // OnlyText: 박스 없이 본문만.
        StageDialogueBoxPlacement onlyText = Box("OnlyText");
        Assert.Equal(StageDialogueBoxStyle.OnlyText, onlyText.Style);
        Assert.Null(onlyText.BoxRect);
        Assert.Null(onlyText.NameRect);

        // LetterBox: 상하 밴드 + 중앙 텍스트.
        StageDialogueBoxPlacement letterBox = Box("LetterBox");
        Assert.Equal(StageDialogueBoxStyle.LetterBox, letterBox.Style);
        Assert.Equal(0, letterBox.TopBand!.Y);
        Assert.Equal(H, letterBox.BottomBand!.Bottom);
        Assert.InRange(letterBox.TextRect.Y, letterBox.TopBand.Bottom, letterBox.BottomBand.Y);

        // Portrait/Surface/BlackBook은 v1에선 Speaker 근사 + 종류 뱃지.
        foreach (string kind in (string[])["Portrait", "Surface", "BlackBook"])
        {
            StageDialogueBoxPlacement approximated = Box(kind);
            Assert.Equal(StageDialogueBoxStyle.Speaker, approximated.Style);
            Assert.True(approximated.Approximated);
            Assert.Equal(kind, approximated.BoxKind);
        }
    }

    [Fact]
    public void 화자_유무가_named_protagonist_박스를_가른다()
    {
        MiniStageState state = MiniStageState.Empty; // named=Speaker, protagonist=Surface

        StageDialogueBoxPlacement named = StageSceneComposer.Compose(state, "라루", null, W, H).DialogueBox!;
        Assert.Equal("Speaker", named.BoxKind);

        StageDialogueBoxPlacement protagonist = StageSceneComposer.Compose(state, null, null, W, H).DialogueBox!;
        Assert.Equal("Surface", protagonist.BoxKind);
        Assert.True(protagonist.Approximated);
    }

    [Fact]
    public void 배치는_해상도에_비례한다()
    {
        MiniStageState state = StateWith(("c1", "laru"));

        StageSceneLayout full = StageSceneComposer.Compose(state, "라루", "laru", W, H);
        StageSceneLayout half = StageSceneComposer.Compose(state, "라루", "laru", W / 2, H / 2);

        Assert.Equal(full.Portraits[0].Rect.X / 2, half.Portraits[0].Rect.X, precision: 6);
        Assert.Equal(full.DialogueBox!.BoxRect!.Y / 2, half.DialogueBox!.BoxRect!.Y, precision: 6);
    }
}
