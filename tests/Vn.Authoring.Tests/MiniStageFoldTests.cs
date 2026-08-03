using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// W14 라인 폴드. "무슨 배경에서 누가 말하는가"가 라인 경계마다 정확해야 하고,
/// 반영하지 못한 연출은 커맨드명과 라인이 보존된 채 남아야 한다.
/// </summary>
public class MiniStageFoldTests
{
    private static readonly PresentationCommandCatalog Catalog = PresentationCommandCatalog.Default;

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

    /// <summary>blank_ch01_ep00 상당의 대표 시퀀스 — 셋업, 등장, 배경 전환, 퇴장, 박스 전환.</summary>
    private static (IReadOnlyList<PresentationResultCommand> Setup, IReadOnlyList<MiniStageFoldLine> Lines) GoldenScene()
    {
        PresentationResultCommand[] setup =
        [
            Command("background.bg_spawn", ("rigKey", "bg0"), ("spriteKey", "office")),
            Command("char_rig_cast.slot", ("slotKey", "c1")),
            Command("char_rig_cast.cast", ("slot", "c1"), ("characterKey", "laru"), ("emotionKey", "1")),
            Command("char_rig_cast.slot", ("slotKey", "c2")),
            Command("char_rig_cast.cast", ("slot", "c2"), ("characterKey", "willo")),
            Command("common_control.actor", ("aliasSymbol", "@w"), ("targetKey", "c2")),
            Command("shot.shot_zoom", ("zoom", "1.4")) // 위치·크기 계열 — 미니 무대는 다루지 않는다
        ];

        MiniStageFoldLine[] lines =
        [
            Line("ln1", branch: false,
                Command("char_rig_entrance.show", ("slot", "c1"), ("face", "e2")),
                Command("dialogue_box.box_named", ("kind", "BlackBook"))),
            Line("ln2", branch: false,
                Command("char_rig_entrance.show", ("slot", "@w")),
                Command("background.bg_sprite", ("rigKey", "bg0"), ("spriteKey", "street_night"))),
            Line("ln3", branch: true,
                Command("char_rig_presentation.fade_out", ("slot", "c1")),
                Command("char_rig_acting.hop", ("slot", "c1")),
                Command("screen_effect.screen_flash", ("preset", "white"))),
            Line("ln4", branch: false,
                Command("dialogue_box.box_reset"))
        ];

        return (setup, lines);
    }

    [Fact]
    public void 골든_장면을_끝까지_접으면_마지막_값들이_남는다()
    {
        (var setup, var lines) = GoldenScene();

        MiniStageState state = MiniStageFold.Fold(Catalog, setup, lines);

        // 배경은 마지막 전환이 이긴다.
        Assert.Equal("street_night", state.BackgroundKey);

        // 캐스팅: cast의 카탈로그 기본값(variant a)이 채워지고, show가 표정을 덮는다.
        MiniStageSlot c1 = state.Slots["c1"];
        Assert.Equal("laru", c1.CharacterId);
        Assert.Equal("a", c1.VariantKey);
        Assert.Equal("2", c1.EmotionKey); // show face e2 → 2

        // fade_out 후: 상태는 남고 visible만 꺼진다.
        Assert.False(c1.Visible);

        // @w 별칭은 c2로 치환됐고 show 표정 미지정은 카탈로그 기본 e1 → 1이다.
        MiniStageSlot c2 = state.Slots["c2"];
        Assert.True(c2.Visible);
        Assert.Equal("willo", c2.CharacterId);
        Assert.Equal("1", c2.EmotionKey);
        Assert.Equal("c2", state.Aliases["@w"]);

        // 박스: box_named 후 box_reset이 기본값을 복원했다.
        Assert.Equal(MiniStageState.DefaultNamedBoxKind, state.BoxKindFor(hasSpeaker: true));
        Assert.Equal(MiniStageState.DefaultProtagonistBoxKind, state.BoxKindFor(hasSpeaker: false));

        // visible 슬롯 나열은 슬롯 키 순서다.
        Assert.Equal(["c2"], state.VisibleSlots.Select(slot => slot.Key));

        // 미반영 연출: 셋업의 shot_zoom, ln3의 hop·screen_flash — 커맨드명과 라인이 남는다.
        Assert.Equal(
            [
                new MiniStageUnhandled(null, "shot_zoom"),
                new MiniStageUnhandled("ln3", "hop"),
                new MiniStageUnhandled("ln3", "screen_flash")
            ],
            state.Unhandled);
        Assert.Equal(2, state.UnhandledCountFor("ln3"));
        Assert.Equal(1, state.UnhandledCountFor(null));

        // ln3에 갈래 전환이 있었다 — 근사 표시.
        Assert.True(state.PassedBranchApproximation);
    }

    [Fact]
    public void 중간_라인에서_멈추면_그_라인까지의_상태다()
    {
        (var setup, var lines) = GoldenScene();

        MiniStageState state = MiniStageFold.Fold(Catalog, setup, lines.Take(1).ToArray());

        Assert.Equal("office", state.BackgroundKey); // 아직 전환 전
        Assert.True(state.Slots["c1"].Visible);
        Assert.Equal("BlackBook", state.BoxKindFor(hasSpeaker: true));
        Assert.False(state.PassedBranchApproximation); // 갈래는 ln3부터다
    }

    [Fact]
    public void 공급이_없으면_빈_무대이고_오류가_아니다()
    {
        MiniStageState state = MiniStageFold.Fold(
            Catalog,
            Array.Empty<PresentationResultCommand>(),
            Array.Empty<MiniStageFoldLine>());

        Assert.Null(state.BackgroundKey);
        Assert.Empty(state.Slots);
        Assert.Empty(state.Unhandled);
        Assert.Equal(MiniStageState.DefaultNamedBoxKind, state.NamedBoxKind);
    }

    [Fact]
    public void slot_tyrant는_주인공_캐스팅과_표시를_함께_한다()
    {
        MiniStageState state = MiniStageFold.Fold(
            Catalog,
            [Command("char_rig_cast.slot_tyrant")],
            Array.Empty<MiniStageFoldLine>());

        // 런타임 매크로 의미: cast tyrant Tyrant a 2 + fade_in.
        MiniStageSlot tyrant = state.Slots["tyrant"];
        Assert.Equal("Tyrant", tyrant.CharacterId);
        Assert.Equal("a", tyrant.VariantKey);
        Assert.Equal("2", tyrant.EmotionKey);
        Assert.True(tyrant.Visible);
    }

    [Fact]
    public void mirror는_무인자면_토글이고_명시_토큰이_이긴다()
    {
        MiniStageFoldLine[] lines =
        [
            Line("ln1", branch: false,
                Command("char_rig_cast.slot", ("slotKey", "c1")),
                Command("char_rig_cast.mirror", ("slot", "c1"))), // 토글 → true
            Line("ln2", branch: false,
                Command("char_rig_cast.mirror", ("slot", "c1"), ("mode", "right"))) // 명시 원본
        ];

        MiniStageState afterToggle = MiniStageFold.Fold(Catalog, [], lines.Take(1).ToArray());
        Assert.True(afterToggle.Slots["c1"].Mirrored);

        MiniStageState afterExplicit = MiniStageFold.Fold(Catalog, [], lines);
        Assert.False(afterExplicit.Slots["c1"].Mirrored);
    }

    [Fact]
    public void face_crossfade는_캐릭터까지_갈아_끼운다()
    {
        MiniStageState state = MiniStageFold.Fold(
            Catalog,
            [
                Command("char_rig_cast.slot", ("slotKey", "c1")),
                Command("char_rig_cast.cast", ("slot", "c1"), ("characterKey", "laru"))
            ],
            [
                Line("ln1", branch: false,
                    Command("char_rig_presentation.face_crossfade",
                        ("slot", "c1"), ("character", "willo"), ("emotion", "3")))
            ]);

        Assert.Equal("willo", state.Slots["c1"].CharacterId);
        Assert.Equal("3", state.Slots["c1"].EmotionKey);
    }

    [Fact]
    public void 카탈로그에_없는_정의도_이름이_남는다()
    {
        MiniStageState state = MiniStageFold.Fold(
            Catalog,
            [],
            [Line("ln1", branch: false, Command("custom.mystery", ("x", "1")))]);

        Assert.Equal([new MiniStageUnhandled("ln1", "custom.mystery")], state.Unhandled);
    }

    // ── 발행 결과 쌍에서 입력 만들기 ───────────────────────────────────────

    private static DialogueResult Dialogue(params DialogueResultLine[] lines)
    {
        return new DialogueResult(
            new ResultIdentity("rs_test", 1, DialogueResult.CurrentSchemaVersion, "sha256:test"),
            "nd_scene",
            "장면",
            "sc_test",
            1,
            "ko-KR",
            lines,
            Array.Empty<DialogueResultAssignment>(),
            null,
            DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void 발행_쌍에서_선택_라인까지_문서_순서로_자른다()
    {
        DialogueResult dialogue = Dialogue(
            new DialogueResultLine(0, "ln_a", 1, "라루", "첫 줄"),
            new DialogueResultLine(1, "ln_b", 1, "윌로", "둘째 줄",
                new DialogueResultTransition(ConditionTransitionKind.BeginIf, "cd_x", "조건", "x >= 1")),
            new DialogueResultLine(2, "ln_c", 1, "라루", "셋째 줄"));

        PresentationResultBinding[] bindings =
        [
            new PresentationResultBinding("ln_c", [Command("background.bg_spawn", ("rigKey", "bg0"), ("spriteKey", "office"))], IsOrphan: false),
            new PresentationResultBinding("ln_zz", [Command("char_rig_cast.slot_tyrant")], IsOrphan: true)
        ];

        IReadOnlyList<MiniStageFoldLine> upToB = MiniStageFold.LinesUpTo(dialogue, bindings, "ln_b");
        Assert.Equal(["ln_a", "ln_b"], upToB.Select(line => line.LineId));
        Assert.True(upToB[1].HasBranchTransition);

        // 선택 라인이 결과에 없으면(또는 null) 전체를 접는다. orphan 바인딩은 문서에 없으니 빠진다.
        IReadOnlyList<MiniStageFoldLine> all = MiniStageFold.LinesUpTo(dialogue, bindings, null);
        Assert.Equal(3, all.Count);
        Assert.DoesNotContain(all, line => line.LineId == "ln_zz");

        MiniStageState state = MiniStageFold.Fold(Catalog, [], all);
        Assert.Equal("office", state.BackgroundKey);
        Assert.True(state.PassedBranchApproximation);
    }
}
