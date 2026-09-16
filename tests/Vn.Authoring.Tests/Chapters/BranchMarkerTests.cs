using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Script;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>「조건 분기」 표식</b> — 줄 하나가 곧 포트 하나 (R7 P-5 · `docs/plans/R7.md`).
///
/// ⛔ <b>조건이 아니다.</b> 연출 그래프는 "어디서 갈라지는가"만 짚는다 — 성립하든 말든
/// 다녀오고, 다녀온 자유 씬이 제 첫머리에서 보고 아니면 곧바로 돌아온다.
///
/// ⚠ 갈래(<c>BeginIf</c>…)와 달리 <b>구역이 아니다</b>. 줄 사이에 끼는 카드 한 장이라
/// 여는 전환도 닫는 전환도 없다 — 그래서 전환이 아니라 <b>줄에 붙는 표식</b>이다.
/// </summary>
public sealed class BranchMarkerTests
{
    [Fact]
    public void 표식_하나가_포트_하나를_뚫는다()
    {
        (ProjectEditor editor, DialogueNode node, string lineId) = World();

        Assert.DoesNotContain(Ports(editor, node), port => port.Kind == ExitPortKind.Detour);

        node.RequireExtension(lineId).DetourTargetNodeId = Target(editor).Id;

        ExitPort port = Assert.Single(Ports(editor, node), item => item.Kind == ExitPortKind.Detour);

        Assert.Equal(lineId, port.BranchOpenLineId);
        Assert.Equal(Target(editor).Id, port.TargetNodeId);
        Assert.True(port.IsConnected);
    }

    [Fact]
    public void 조건이_없어도_포트가_선다()
    {
        // 갈래는 `ConditionId`가 있어야 열리지만 이 표식은 조건을 아예 안 진다 —
        // 갈지 말지는 다녀온 곳이 정한다.
        (ProjectEditor editor, DialogueNode node, string lineId) = World();
        node.RequireExtension(lineId).DetourTargetNodeId = Target(editor).Id;

        Assert.DoesNotContain(
            node.LineExtensions.SelectMany(extension => extension.Transitions),
            transition => transition.OpensBranch);

        Assert.Single(Ports(editor, node), item => item.Kind == ExitPortKind.Detour);
    }

    [Fact]
    public void 목표는_그_줄이_지고_갈래_출구_장부에_안_적힌다()
    {
        // ⛔ 갈래 출구 장부(`BranchExits`)에 적으면 <b>여는 전환이 없는 고아</b>가 되고,
        //    그 칸은 저장할 때 전환 안에 실리므로 통째로 사라진다.
        (ProjectEditor editor, DialogueNode node, string lineId) = World();

        editor.SetExitTarget(node.Id, ExitPortKind.Detour, lineId, Target(editor).Id);

        Assert.Equal(Target(editor).Id, node.FindExtension(lineId)!.DetourTargetNodeId);
        Assert.Empty(node.BranchExits);
    }

    [Fact]
    public void 끊으면_표식도_지워진다()
    {
        (ProjectEditor editor, DialogueNode node, string lineId) = World();
        editor.SetExitTarget(node.Id, ExitPortKind.Detour, lineId, Target(editor).Id);

        editor.SetExitTarget(node.Id, ExitPortKind.Detour, lineId, null);

        Assert.Null(node.FindExtension(lineId)?.DetourTargetNodeId);
        Assert.DoesNotContain(Ports(editor, node), port => port.Kind == ExitPortKind.Detour);
    }

    [Fact]
    public void 저장하고_다시_열어도_남는다()
    {
        // 저장이 안 되면 다음에 열었을 때 포트가 통째로 사라진다 — 뚫어 둔 자리가 없어진다.
        (ProjectEditor editor, DialogueNode node, string lineId) = World();
        node.RequireExtension(lineId).DetourTargetNodeId = Target(editor).Id;

        StoryProject reopened = ProjectSnapshotCodec.Decode(
            ProjectSnapshotCodec.Encode(editor.Project));

        DialogueNode again = reopened.EnumerateNodes().OfType<DialogueNode>()
            .First(item => string.Equals(item.Id, node.Id, StringComparison.Ordinal));

        Assert.Equal(Target(editor).Id, again.FindExtension(lineId)!.DetourTargetNodeId);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static IReadOnlyList<ExitPort> Ports(ProjectEditor editor, DialogueNode node) =>
        NodeConnections.PortsOf(node, editor.Project);

    private static DialogueNode Target(ProjectEditor editor) =>
        editor.Project.EnumerateNodes().OfType<DialogueNode>()
            .First(node => node.Name == "곁가지");

    /// <summary>대사 한 줄짜리 에피소드 노드와, 다녀올 자유 씬 하나.</summary>
    private static (ProjectEditor Editor, DialogueNode Node, string LineId) World()
    {
        var project = new StoryProject();
        var editor = new ProjectEditor(project);

        editor.EnsureChapter("ch01");
        string fileId = editor.EnsureChapterBoard("ch01");
        editor.AddEpisode("ch01", "root", title: "root", 0, 0);

        DialogueNode node = editor.AddDialogueNode(fileId, 0, 0, "root");
        node.ExcelEpisodeId = "root";

        editor.AddDialogueNode(fileId, 400, 0, "곁가지");

        string lineId = project.FindScript(node.ScriptId)!.ActiveLines.First().Id;

        return (editor, node, lineId);
    }
}
