using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// 대본 텍스트 패널의 행 모델 (2026-08-20) — 이미터가 내는 대본과 같은 순서(Setup 머리,
/// 라인마다 커맨드가 대사 앞)여야 하고, 행이 커맨드 원본을 들어 점·인라인 편집이
/// 작업 중 커맨드를 만질 수 있어야 한다.
/// </summary>
public class PresentationScriptModelTests
{
    private static readonly PresentationCommandCatalog Catalog = PresentationCommandCatalog.Default;

    private static PresentationResultCommand Command(
        string definitionId, params (string Key, string Value)[] args)
    {
        return new PresentationResultCommand(
            Identifier.PresentationCommand(),
            definitionId,
            args.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    }

    private static DialogueResult Dialogue(params DialogueResultLine[] lines)
    {
        return new DialogueResult(
            new ResultIdentity("rs_test", 1, DialogueResult.CurrentSchemaVersion, "sha256:test"),
            "nd_scene", "장면", "sc_test", 1, "ko-KR",
            lines,
            Array.Empty<DialogueResultAssignment>(),
            null,
            DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void 순서는_Setup_머리_그리고_라인마다_커맨드가_대사_앞이다()
    {
        DialogueResult dialogue = Dialogue(
            new DialogueResultLine(0, "ln_a", 1, "라루", "첫 줄"),
            new DialogueResultLine(1, "ln_b", 1, "윌로", "둘째 줄"));

        PresentationResultCommand setup = Command("char_rig_cast.slot", ("slotKey", "c1"));
        PresentationResultCommand move = Command(
            "char_rig_staging.move_by", ("slot", "c1"), ("x", "+2u"));

        PresentationResultBinding[] bindings =
        [
            new PresentationResultBinding("ln_b", [move], IsOrphan: false),
            new PresentationResultBinding("ln_zz", [Command("char_rig_cast.slot", ("slotKey", "c9"))], IsOrphan: true)
        ];

        IReadOnlyList<PresentationScriptRow> rows =
            PresentationScriptModel.Build(Catalog, dialogue, [setup], bindings);

        Assert.Equal(
            [
                PresentationScriptRowKind.SectionHeader, // ── Setup ──
                PresentationScriptRowKind.Command,       // slot c1 (Setup은 액터 선언 없음)
                PresentationScriptRowKind.Dialogue,      // 라루: 첫 줄
                PresentationScriptRowKind.Actor,         // <<actor @1 c1>> (라인 단위 선언)
                PresentationScriptRowKind.Command,       // move_by (대사 앞)
                PresentationScriptRowKind.Dialogue       // 윌로: 둘째 줄
            ],
            rows.Select(row => row.Kind));

        // 고아 바인딩(ln_zz)은 실리지 않는다 — 폴드와 같은 이유.
        Assert.DoesNotContain(rows, row => row.LineId == "ln_zz");

        // 커맨드 행은 원본과 병기 텍스트를 든다 — 무대 대상은 라인 별칭으로 표시된다
        // (저장 인자는 슬롯 그대로, 표시만 — 2026-08-21 소유자: 가독성).
        PresentationScriptRow moveRow = rows.Single(row =>
            row.Kind == PresentationScriptRowKind.Command && row.LineId == "ln_b");
        Assert.Same(move, moveRow.Command);
        Assert.Equal("<<move_by @1 +2u 0u 0.4s>>", moveRow.Text);
        Assert.Equal("c1", move.Arguments["slot"]); // 원본 인자는 불변.

        // 대사 행은 화자와 본문.
        Assert.Equal("라루: 첫 줄", rows[2].Text);
        Assert.Equal("ln_a", rows[2].LineId);
    }

    [Fact]
    public void 무대_대상이_바뀌는_곳에서_묶음이_시작된다()
    {
        // 소유자의 대본 감각 — 액터(대상) 단위로 차분하게 묶는다. 장차 beat 프리셋의 단위다.
        DialogueResult dialogue = Dialogue(new DialogueResultLine(0, "ln_a", 1, "라루", "첫 줄"));

        PresentationResultBinding[] bindings =
        [
            new PresentationResultBinding("ln_a",
            [
                Command("char_rig_cast.slot", ("slotKey", "c1")),
                Command("char_rig_staging.move_by", ("slot", "c1"), ("x", "+1u")),
                Command("char_rig_staging.move_by", ("slot", "c2"), ("x", "+1u")),
                Command("char_rig_staging.scale_by", ("slot", "c2"), ("multiplier", "1.2"))
            ], IsOrphan: false)
        ];

        IReadOnlyList<PresentationScriptRow> rows =
            PresentationScriptModel.Build(Catalog, dialogue, [], bindings);

        // slot c1(생성)과 move_by c1(이동)은 같은 대상이라 한 묶음이고,
        // c1 → c2 경계의 여백은 새 액터 선언 행이 대신 진다(겹으로 벌어지지 않게) —
        // 커맨드 행 자신은 전부 false다.
        Assert.Equal(
            [false, false, false, false],
            rows.Where(row => row.Kind == PresentationScriptRowKind.Command)
                .Select(row => row.StartsGroup));

        // 액터 선언 둘 — 첫 선언은 라인 머리(여백 없음), 둘째는 묶음 경계(여백).
        Assert.Equal(
            [("<<actor @1 c1>>", false), ("<<actor @2 c2>>", true)],
            rows.Where(row => row.Kind == PresentationScriptRowKind.Actor)
                .Select(row => (row.Text, row.StartsGroup)));
    }

    [Fact]
    public void 액터_선언은_캐스팅된_캐릭터를_말하고_커맨드_표시는_별칭을_쓴다()
    {
        // 2026-08-21 소유자: "고르는 건 슬롯이더라도 … 라인단위로 actor를 캐릭터를 지정해.
        // <<actor @2 willow>>" — 런타임이 배역 지정을 슬롯으로 되돌리므로 읽기 문법이 된다.
        DialogueResult dialogue = Dialogue(
            new DialogueResultLine(0, "ln_a", 1, "윌로", "첫 줄"),
            new DialogueResultLine(1, "ln_b", 1, "윌로", "둘째 줄"));

        PresentationResultCommand[] setup =
        [
            Command("char_rig_cast.slot", ("slotKey", "c2")),
            Command("char_rig_cast.cast", ("slot", "c2"), ("characterKey", "willow"))
        ];

        PresentationResultBinding[] bindings =
        [
            // ln_a: Setup에서 캐스팅된 배역이 선언에 온다.
            new PresentationResultBinding("ln_a",
                [Command("char_rig_staging.move_by", ("slot", "c2"), ("x", "+2u"))], IsOrphan: false),
            // ln_b: 라인 안의 재캐스팅이 같은 라인 선언에 이미 반영된다 (cast가 뒤에 있어도).
            new PresentationResultBinding("ln_b",
            [
                Command("char_rig_staging.move_by", ("slot", "c2"), ("x", "-2u")),
                Command("char_rig_cast.cast", ("slot", "c2"), ("characterKey", "larue"))
            ], IsOrphan: false)
        ];

        IReadOnlyList<PresentationScriptRow> rows =
            PresentationScriptModel.Build(Catalog, dialogue, setup, bindings);

        Assert.Equal(
            ["<<actor @2 willow>>", "<<actor @2 larue>>"],
            rows.Where(row => row.Kind == PresentationScriptRowKind.Actor).Select(row => row.Text));

        // 커맨드 표시의 무대 대상도 별칭이다 — cast 자신을 포함해서.
        Assert.All(
            rows.Where(row => row.Kind == PresentationScriptRowKind.Command && row.LineId is not null),
            row => Assert.Contains("@2", row.Text, StringComparison.Ordinal));

        // Setup 구획은 슬롯 원문 그대로다(선언은 라인 단위).
        PresentationScriptRow setupCast = rows.First(row =>
            row.Kind == PresentationScriptRowKind.Command && row.LineId is null &&
            row.Text.Contains("cast", StringComparison.Ordinal));
        Assert.Contains(" c2 ", setupCast.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void 꺼진_커맨드도_행으로_남고_IsEnabled가_거짓이_된다()
    {
        // 2026-08-21 소유자: 점 = 켜고 끄기 — 꺼진 커맨드가 행에서 사라지면 다시 켤
        // 입구가 없다. 발행·폴드에서 빠지는 것과 표시에서 남는 것은 다른 문제다.
        DialogueResult dialogue = Dialogue(new DialogueResultLine(0, "ln_a", 1, "라루", "첫 줄"));

        PresentationResultCommand on = Command("char_rig_staging.move_by", ("slot", "c1"), ("x", "+1u"));
        PresentationResultCommand off = Command("char_rig_staging.move_by", ("slot", "c1"), ("x", "-1u"));

        PresentationResultBinding[] bindings =
            [new PresentationResultBinding("ln_a", [on, off], IsOrphan: false)];

        IReadOnlyList<PresentationScriptRow> rows = PresentationScriptModel.Build(
            Catalog, dialogue, [], bindings, disabledCommandIds: new[] { off.CommandId });

        PresentationScriptRow[] commands = rows
            .Where(row => row.Kind == PresentationScriptRowKind.Command).ToArray();
        Assert.Equal(2, commands.Length); // 꺼진 것도 행으로 남는다
        Assert.Equal([true, false], commands.Select(row => row.IsEnabled));
    }
}
