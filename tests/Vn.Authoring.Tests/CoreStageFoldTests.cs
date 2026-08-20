using Ked.Presentation.Core;
using Vn.Authoring.Assets;
using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// W24 — 합성 폴드의 골든: 코어 리듀서로 갈아 끼워도 <b>화면이 그리는 정보가 기존 폴드와
/// 같아야 한다.</b> 표정·variant 키는 코어가 정규화 어휘("2"→"02")를 쓰므로, 비교는
/// 초상 해석(<c>PortraitKey.Normalize</c>)과 같은 정규화 위에서 한다 — 화면 결과(해석되는
/// 초상 파일·가시성·나열)는 동일하다.
/// </summary>
public class CoreStageFoldTests
{
    private static readonly PresentationCommandCatalog Catalog = PresentationCommandCatalog.Default;

    private static readonly string FixtureDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TuningFixtures", "ExportedTuning"));

    private static StageReducerTuning LoadTuning() =>
        RuntimeTuningLibrary.Load(FixtureDirectory, (1920, 1080)).Tuning!;

    private static PresentationResultCommand Command(string definitionId, params (string Key, string Value)[] args)
    {
        return new PresentationResultCommand(
            Identifier.PresentationCommand(),
            definitionId,
            args.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    }

    private static MiniStageFoldLine Line(
        string lineId,
        bool branch = false,
        params PresentationResultCommand[] commands)
    {
        return new MiniStageFoldLine(lineId, branch, commands);
    }

    // ── 골든 비교 ──────────────────────────────────────────────────────────

    private static string? NormalizeVariant(string? variant) =>
        string.IsNullOrWhiteSpace(variant) ? null : variant.Trim().ToLowerInvariant()[^1..];

    private static string? NormalizeEmotion(string? emotion)
    {
        if (emotion is null)
        {
            return null;
        }

        // 코어 사본 최신화(W64)로 표정 정규화의 유일한 자리가 PortraitKeyNormalizer가 됐다.
        return PortraitKeyNormalizer.EmotionCode(emotion);
    }

    private static void AssertGoldenEquivalent(MiniStageState legacy, MiniStageState composite)
    {
        Assert.Equal(legacy.BackgroundKey, composite.BackgroundKey);
        Assert.Equal(legacy.BackgroundRigKey, composite.BackgroundRigKey);
        Assert.Equal(legacy.NamedBoxKind, composite.NamedBoxKind);
        Assert.Equal(legacy.ProtagonistBoxKind, composite.ProtagonistBoxKind);
        Assert.Equal(legacy.PassedBranchApproximation, composite.PassedBranchApproximation);

        Assert.Equal(
            legacy.Aliases.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            composite.Aliases.OrderBy(pair => pair.Key, StringComparer.Ordinal));

        // 뱃지 목록은 항목·순서까지 그대로다 — 단, W25부터 코어가 그리는 좌표 축(place·size·샷)은
        // 실제 배치로 반영되므로 기존 폴드가 뱃지에 남기던 그 항목들만 목록에서 내려가고,
        // W26의 "미표시" 분류 플래그는 비교에서 제외한다(기존 폴드에는 없는 축이다).
        // 그 외의 뱃지가 달라지면 화면이 달라진 것이다.
        Assert.Equal(
            legacy.Unhandled
                .Where(entry => !CoreStageFold.DrawnCoreCommands.Contains(entry.CommandName))
                .Select(entry => (entry.LineId, entry.CommandName)),
            composite.Unhandled
                .Where(entry => !CoreStageFold.DrawnCoreCommands.Contains(entry.CommandName))
                .Select(entry => (entry.LineId, entry.CommandName)));

        Assert.Equal(
            legacy.Slots.Keys.OrderBy(key => key, StringComparer.Ordinal),
            composite.Slots.Keys.OrderBy(key => key, StringComparer.Ordinal));

        foreach ((string slotKey, MiniStageSlot legacySlot) in legacy.Slots)
        {
            MiniStageSlot compositeSlot = composite.Slots[slotKey];

            Assert.Equal(legacySlot.CharacterId, compositeSlot.CharacterId);
            Assert.Equal(legacySlot.Visible, compositeSlot.Visible);
            Assert.Equal(legacySlot.Mirrored, compositeSlot.Mirrored);
            Assert.Equal(NormalizeVariant(legacySlot.VariantKey), NormalizeVariant(compositeSlot.VariantKey));
            Assert.Equal(NormalizeEmotion(legacySlot.EmotionKey), NormalizeEmotion(compositeSlot.EmotionKey));
        }

        // 나열 순서(=화면의 초상 순서)도 같다.
        Assert.Equal(
            legacy.VisibleSlots.Select(slot => slot.Key),
            composite.VisibleSlots.Select(slot => slot.Key));
    }

    /// <summary>치수 픽스처에 있는 캐릭터(parkeunseol·yoonsaea)로 코어 경로를 실제로 태우는 장면.</summary>
    private static (IReadOnlyList<PresentationResultCommand> Setup, IReadOnlyList<MiniStageFoldLine> Lines) CorePathScene()
    {
        PresentationResultCommand[] setup =
        [
            Command("background.bg_spawn", ("rigKey", "bg0"), ("spriteKey", "office")),
            Command("char_rig_cast.slot", ("slotKey", "c1")),
            Command("char_rig_cast.cast", ("slot", "c1"), ("characterKey", "parkeunseol"), ("emotionKey", "1")),
            Command("char_rig_cast.slot", ("slotKey", "c2")),
            Command("char_rig_cast.cast", ("slot", "c2"), ("characterKey", "yoonsaea")),
            Command("common_control.actor", ("aliasSymbol", "@y"), ("targetKey", "c2")),
            Command("shot.shot_zoom", ("zoom", "1.4"))
        ];

        MiniStageFoldLine[] lines =
        [
            Line("ln1", branch: false,
                Command("char_rig_entrance.show", ("slot", "c1"), ("face", "e2")),
                Command("dialogue_box.box_named", ("kind", "BlackBook"))),
            Line("ln2", branch: false,
                Command("char_rig_entrance.show", ("slot", "@y")),
                Command("char_rig_cast.pose", ("slot", "@y"), ("variantKey", "b")),
                Command("background.bg_sprite", ("rigKey", "bg0"), ("spriteKey", "street_night"))),
            Line("ln3", branch: true,
                Command("char_rig_placement.place", ("slot", "c1"), ("screenPoint", "left")),
                Command("char_rig_presentation.fade_out", ("slot", "c1")),
                Command("char_rig_cast.mirror", ("slot", "@y"))),
            Line("ln4", branch: false,
                Command("char_rig_presentation.face_swap", ("slot", "c2"), ("emotion", "3")),
                Command("dialogue_box.box_reset"))
        ];

        return (setup, lines);
    }

    [Fact]
    public void 골든_코어_경로_장면이_기존_폴드와_같은_화면_정보를_낸다()
    {
        (var setup, var lines) = CorePathScene();

        MiniStageState legacy = MiniStageFold.Fold(Catalog, setup, lines);
        CoreStageFoldResult composite = CoreStageFold.Fold(Catalog, setup, lines, LoadTuning());

        AssertGoldenEquivalent(legacy, composite.State);
    }

    [Fact]
    public void 골든_중간_라인마다_같다()
    {
        (var setup, var lines) = CorePathScene();
        StageReducerTuning tuning = LoadTuning();

        for (int count = 0; count <= lines.Count; count++)
        {
            MiniStageFoldLine[] slice = lines.Take(count).ToArray();

            AssertGoldenEquivalent(
                MiniStageFold.Fold(Catalog, setup, slice),
                CoreStageFold.Fold(Catalog, setup, slice, tuning).State);
        }
    }

    [Fact]
    public void 골든_tuning이_없으면_기존_폴드_그대로다()
    {
        (var setup, var lines) = CorePathScene();

        CoreStageFoldResult composite = CoreStageFold.Fold(Catalog, setup, lines, tuning: null);
        MiniStageState legacy = MiniStageFold.Fold(Catalog, setup, lines);

        // 폴백은 기존 폴드 자신이다 — 정규화 없이도 슬롯 값이 문자 그대로 같다. 코어 상태는 없다.
        AssertGoldenEquivalent(legacy, composite.State);

        foreach ((string slotKey, MiniStageSlot legacySlot) in legacy.Slots)
        {
            Assert.Equal(legacySlot, composite.State.Slots[slotKey]);
        }

        Assert.Null(composite.CoreState);
    }

    [Fact]
    public void 골든_치수_없는_캐릭터는_보완_폴드가_기존_의미로_접는다()
    {
        // "laru"는 치수 덤프에 없다 — 코어는 사이징 진단을 남기고, 화면 정보는 보완 폴드가
        // 기존 의미(관대한 표시)로 접는다. 골든이 그 동등성을 고정한다.
        PresentationResultCommand[] setup =
        [
            Command("char_rig_cast.slot", ("slotKey", "c1")),
            Command("char_rig_cast.cast", ("slot", "c1"), ("characterKey", "laru"), ("emotionKey", "2"))
        ];

        MiniStageFoldLine[] lines =
        [
            Line("ln1", branch: false, Command("char_rig_entrance.show", ("slot", "c1")))
        ];

        MiniStageState legacy = MiniStageFold.Fold(Catalog, setup, lines);
        CoreStageFoldResult composite = CoreStageFold.Fold(Catalog, setup, lines, LoadTuning());

        AssertGoldenEquivalent(legacy, composite.State);

        // 코어 쪽 기록은 사라지지 않는다 — 진단이 CoreState.Unhandled에 남아 W26의 원료가 된다.
        Assert.Contains(
            composite.CoreState!.Unhandled,
            unhandled => unhandled.Reason.Contains("초상", StringComparison.Ordinal));
    }

    [Fact]
    public void 골든_슬롯_없이_캐스팅하면_기존의_관대한_생성이_유지된다()
    {
        // 코어는 spawn 없는 슬롯을 거부하지만, v1 프리뷰의 관대한 표시는 보완 폴드가 이어받는다.
        PresentationResultCommand[] setup =
        [
            Command("char_rig_cast.cast", ("slot", "c9"), ("characterKey", "parkeunseol"))
        ];

        MiniStageState legacy = MiniStageFold.Fold(Catalog, setup, Array.Empty<MiniStageFoldLine>());
        CoreStageFoldResult composite = CoreStageFold.Fold(
            Catalog, setup, Array.Empty<MiniStageFoldLine>(), LoadTuning());

        AssertGoldenEquivalent(legacy, composite.State);
        Assert.Equal("parkeunseol", composite.State.Slots["c9"].CharacterId);
    }

    [Fact]
    public void 골든_slot_tyrant_매크로와_후속_코어_커맨드가_섞여도_같다()
    {
        PresentationResultCommand[] setup =
        [
            Command("char_rig_cast.slot_tyrant"),
            Command("char_rig_cast.slot", ("slotKey", "c1")),
            Command("char_rig_cast.cast", ("slot", "c1"), ("characterKey", "parkeunseol"))
        ];

        MiniStageFoldLine[] lines =
        [
            Line("ln1", branch: false,
                Command("char_rig_entrance.show", ("slot", "c1")),
                Command("char_rig_cast.face", ("slot", "tyrant"), ("emotionKey", "3")))
        ];

        MiniStageState legacy = MiniStageFold.Fold(Catalog, setup, lines);
        CoreStageFoldResult composite = CoreStageFold.Fold(Catalog, setup, lines, LoadTuning());

        AssertGoldenEquivalent(legacy, composite.State);

        // 보완 폴드만 아는 슬롯(tyrant)이 코어 슬롯 투영에 지워지지 않았다.
        Assert.True(composite.State.Slots.ContainsKey("tyrant"));
    }

    [Fact]
    public void 좌표_축은_코어에_접혀_그려지므로_뱃지에서_내려간다()
    {
        (var setup, var lines) = CorePathScene();

        CoreStageFoldResult composite = CoreStageFold.Fold(Catalog, setup, lines, LoadTuning());

        // W25: place·shot은 코어 좌표로 실제 배치가 되므로 "반영 안 된 연출"이 아니다.
        Assert.DoesNotContain(new MiniStageUnhandled(null, "shot_zoom"), composite.State.Unhandled);
        Assert.DoesNotContain(new MiniStageUnhandled("ln3", "place"), composite.State.Unhandled);

        // 코어 상태에 실제로 접혔다 — StageSceneComposer가 이 좌표를 그린다.
        StageState core = composite.CoreState!;
        Assert.DoesNotContain(core.Unhandled, unhandled => unhandled.Command.Name == "place");
        Assert.DoesNotContain(core.Unhandled, unhandled => unhandled.Command.Name == "shot_zoom");
        Assert.True(core.HasSlot("c1"));
        Assert.NotEqual(ShotIntentState.Default.Zoom, core.Shot.Zoom);

        // 규칙 14는 그대로다: 코어도 툴도 못 접는 커맨드는 여전히 뱃지에 남는다.
        // (tuning 없이 접으면 좌표 축도 도로 뱃지로 돌아온다 — 폴백 골든이 그 경로를 지킨다.)
        CoreStageFoldResult withoutTuning = CoreStageFold.Fold(Catalog, setup, lines, tuning: null);
        Assert.Contains(new MiniStageUnhandled(null, "shot_zoom"), withoutTuning.State.Unhandled);
        Assert.Contains(new MiniStageUnhandled("ln3", "place"), withoutTuning.State.Unhandled);
    }

    [Fact]
    public void 미표시_분류_접혔지만_안_그리는_축은_플래그로_갈린다()
    {
        // char_to는 코어가 구조 축으로 접지만(v1 컨테이너 항등 가정) 화면에 그릴 것이 없다 —
        // H-3 "미표시". 코어도 툴도 모르는 커맨드는 "반영 안 됨"(플래그 false)이다.
        PresentationResultCommand[] setup =
        [
            Command("char_rig_cast.slot", ("slotKey", "c1")),
            Command("char_rig_staging.char_to", ("slot", "c1"), ("stage", "stage01")),
            Command("custom.mystery", ("x", "1"))
        ];

        CoreStageFoldResult composite = CoreStageFold.Fold(
            Catalog, setup, Array.Empty<MiniStageFoldLine>(), LoadTuning());

        MiniStageUnhandled charTo = Assert.Single(
            composite.State.Unhandled, entry => entry.CommandName == "char_to");
        Assert.True(charTo.FoldedButNotDrawn);

        MiniStageUnhandled mystery = Assert.Single(
            composite.State.Unhandled, entry => entry.CommandName == "custom.mystery");
        Assert.False(mystery.FoldedButNotDrawn);
    }

    [Fact]
    public void 코어가_접은_show_뒤에도_mirror는_보존된다()
    {
        PresentationResultCommand[] setup =
        [
            Command("char_rig_cast.slot", ("slotKey", "c1")),
            Command("char_rig_cast.cast", ("slot", "c1"), ("characterKey", "parkeunseol"))
        ];

        MiniStageFoldLine[] lines =
        [
            Line("ln1", branch: false,
                Command("char_rig_cast.mirror", ("slot", "c1"), ("mode", "left")),
                Command("char_rig_entrance.show", ("slot", "c1")))
        ];

        CoreStageFoldResult composite = CoreStageFold.Fold(Catalog, setup, lines, LoadTuning());

        Assert.True(composite.State.Slots["c1"].Mirrored);
        Assert.True(composite.State.Slots["c1"].Visible);

        AssertGoldenEquivalent(MiniStageFold.Fold(Catalog, setup, lines), composite.State);
    }
}
