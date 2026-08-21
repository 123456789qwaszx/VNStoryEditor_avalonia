using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// 커맨드 자리 이동 (2026-08-20 소유자: "드래그해서 위치를 자유롭게") — 같은 라인 안,
/// 라인 사이, Setup 왕복 전부 편집 통로 하나이고 undo 한 번이 이동 하나를 원복한다.
/// 호출자는 "제거 전 화면"의 자리를 말한다 — 같은 목록 안 아래로 이동의 한 칸 보정은
/// 통로가 진다.
/// </summary>
public class PresentationCommandMoveTests
{
    private static (Sample Sample, PresentationNode Node, string LineA, string LineB) Stage()
    {
        var sample = new Sample();
        string lineA = sample.Line("첫 줄");
        string lineB = sample.Line("둘째 줄");
        DialogueResult dialogue = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        PresentationNode node = sample.Editor.AddPresentationNode(sample.File.Id, name: "연출");
        sample.Editor.SetPresentationSource(node.Id, dialogue.Identity.ResultId, dialogue.Identity.Version);
        return (sample, node, lineA, lineB);
    }

    private static PresentationCommandInstance Add(
        Sample sample, PresentationNode node, string lineId, string slot)
    {
        return sample.Editor.AddPresentationCommand(node.Id, lineId, "char_rig_staging.move_by",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["slot"] = slot });
    }

    private static string[] SlotsOf(PresentationNode node, string lineId) =>
        node.FindBinding(lineId)!.Commands.Select(command => command.Arguments["slot"]).ToArray();

    [Fact]
    public void 같은_라인_안_이동은_제거_전_자리를_말해도_맞게_꽂힌다()
    {
        (Sample sample, PresentationNode node, string lineA, _) = Stage();
        Add(sample, node, lineA, "c1");
        PresentationCommandInstance b = Add(sample, node, lineA, "c2");
        Add(sample, node, lineA, "c3");

        // c2를 c3 뒤(화면 기준 index 3)로 — 제거 후엔 한 칸 당겨지는 자리다.
        sample.Editor.MovePresentationCommand(node.Id, b.Id, lineA, 3);
        Assert.Equal(["c1", "c3", "c2"], SlotsOf(node, lineA));

        // 맨 앞으로.
        sample.Editor.MovePresentationCommand(node.Id, b.Id, lineA, 0);
        Assert.Equal(["c2", "c1", "c3"], SlotsOf(node, lineA));
    }

    [Fact]
    public void 라인_사이와_Setup_왕복이_되고_undo_한_번이_이동_하나다()
    {
        (Sample sample, PresentationNode node, string lineA, string lineB) = Stage();
        PresentationCommandInstance moving = Add(sample, node, lineA, "c1");
        Add(sample, node, lineB, "c9");

        // 라인 A → 라인 B의 머리.
        sample.Editor.MovePresentationCommand(node.Id, moving.Id, lineB, 0);
        Assert.Empty(node.FindBinding(lineA)!.Commands);
        Assert.Equal(["c1", "c9"], SlotsOf(node, lineB));

        // 라인 B → Setup.
        sample.Editor.MovePresentationCommand(node.Id, moving.Id, targetLineId: null, 0);
        Assert.Equal(["c9"], SlotsOf(node, lineB));
        Assert.Single(node.SetupCommands);

        // undo는 스냅샷 복원이라 프로젝트 객체가 갈린다 — 노드를 다시 찾아 본다.
        sample.Editor.Undo(); // Setup 이동 원복
        PresentationNode restored = sample.Project.FindPresentation(node.Id)!;
        Assert.Equal(["c1", "c9"], SlotsOf(restored, lineB));

        sample.Editor.Undo(); // 라인 이동 원복
        restored = sample.Project.FindPresentation(node.Id)!;
        Assert.Equal(["c1"], SlotsOf(restored, lineA));
    }

    [Fact]
    public void 없는_커맨드_이동은_거부한다()
    {
        (Sample sample, PresentationNode node, string lineA, _) = Stage();
        Add(sample, node, lineA, "c1");

        Assert.Throws<InvalidOperationException>(() =>
            sample.Editor.MovePresentationCommand(node.Id, "pc_없음", lineA, 0));
    }

    // ── 복제 (2026-08-21 소유자: 터미널 Ctrl+D) ─────────────────────────────

    [Fact]
    public void 복제는_원본_바로_뒤에_서고_새_Id로_독립이며_undo_한_번이_원복한다()
    {
        (Sample sample, PresentationNode node, string lineA, _) = Stage();
        PresentationCommandInstance first = Add(sample, node, lineA, "c1");
        Add(sample, node, lineA, "c2");

        PresentationCommandInstance copy =
            sample.Editor.DuplicatePresentationCommand(node.Id, first.Id);

        // 목록 끝이 아니라 원본 바로 뒤다.
        Assert.Equal(["c1", "c1", "c2"], SlotsOf(node, lineA));
        Assert.NotEqual(first.Id, copy.Id);

        // 인자 사전이 복사본이다 — 복제를 고쳐도 원본은 그대로.
        copy.Arguments["slot"] = "c9";
        Assert.Equal("c1", first.Arguments["slot"]);

        sample.Editor.Undo();
        PresentationNode restored = sample.Project.FindPresentation(node.Id)!;
        Assert.Equal(["c1", "c2"], SlotsOf(restored, lineA));
    }

    [Fact]
    public void Setup_커맨드도_같은_통로로_복제된다()
    {
        (Sample sample, PresentationNode node, _, _) = Stage();
        PresentationCommandInstance setup = sample.Editor.AddPresentationSetupCommand(
            node.Id, "char_rig_cast.slot",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["slot"] = "c1" });

        sample.Editor.DuplicatePresentationCommand(node.Id, setup.Id);

        Assert.Equal(2, node.SetupCommands.Count);
        Assert.Equal("c1", node.SetupCommands[1].Arguments["slot"]);
    }

    [Fact]
    public void 없는_커맨드_복제는_거부한다()
    {
        (Sample sample, PresentationNode node, _, _) = Stage();

        Assert.Throws<InvalidOperationException>(() =>
            sample.Editor.DuplicatePresentationCommand(node.Id, "pc_없음"));
    }
}
