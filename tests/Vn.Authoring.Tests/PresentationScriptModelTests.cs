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
                PresentationScriptRowKind.Command,       // slot c1
                PresentationScriptRowKind.Dialogue,      // 라루: 첫 줄
                PresentationScriptRowKind.Command,       // move_by (대사 앞)
                PresentationScriptRowKind.Dialogue       // 윌로: 둘째 줄
            ],
            rows.Select(row => row.Kind));

        // 고아 바인딩(ln_zz)은 실리지 않는다 — 폴드와 같은 이유.
        Assert.DoesNotContain(rows, row => row.LineId == "ln_zz");

        // 커맨드 행은 원본과 병기 텍스트를 든다.
        PresentationScriptRow moveRow = rows.Single(row =>
            row.Kind == PresentationScriptRowKind.Command && row.LineId == "ln_b");
        Assert.Same(move, moveRow.Command);
        Assert.Equal("<<move_by c1 +2u 0u 0.4s>>", moveRow.Text);

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
        // c1 → c2에서 경계가 선다. c2 연속 둘(move·scale)은 한 묶음이다.
        Assert.Equal(
            [false, false, true, false],
            rows.Where(row => row.Kind == PresentationScriptRowKind.Command)
                .Select(row => row.StartsGroup));
    }
}
