using Vn.Authoring.Editing;
using Vn.Authoring.Rendering;
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

    // ── 대본에 나가는 모양 (R7 P-5, 이미터) ────────────────────────────────

    [Fact]
    public void 표식은_조건_없이_detour_한_줄로_나간다()
    {
        // ⛔ `<<if>>`로 감싸지 않는다 — 연출 그래프는 "어디서 갈라지는가"만 짚고, 갈지
        //    말지는 다녀온 곳이 제 첫머리에서 정한다.
        (ProjectEditor editor, DialogueNode node, string lineId) = World();
        node.RequireExtension(lineId).DetourTargetNodeId = Target(editor).Id;

        string yarn = Preview(editor, node);

        // ⚠ 미리보기는 노드 <b>Id</b>로 낸다 — 번들 이미터가 제 이름 규칙으로 옮긴다.
        Assert.Contains($"<<detour {Target(editor).Id}>>", yarn, StringComparison.Ordinal);
        Assert.DoesNotContain("<<if", yarn, StringComparison.Ordinal);
    }

    [Fact]
    public void 표식이_없으면_아무것도_안_나간다()
    {
        (ProjectEditor editor, DialogueNode node, _) = World();

        Assert.DoesNotContain("<<detour", Preview(editor, node), StringComparison.Ordinal);
    }

    private static string Preview(ProjectEditor editor, DialogueNode node) =>
        DocumentPreviewFormatter.Format(
            WorkingDialoguePreview.Compose(editor.Project, node.Id));

    // ── 「분기 추가」 (R7 P-5, 편집 명령) ───────────────────────────────────

    [Fact]
    public void 한_번에_표식과_에피소드가_함께_선다()
    {
        (ProjectEditor editor, DialogueNode node, string lineId) = World();

        DialogueNode made = editor.AddBranchMarker(node.Id, lineId);

        Assert.Equal(made.Id, node.FindExtension(lineId)!.DetourTargetNodeId);
        Assert.Single(Ports(editor, node), port => port.Kind == ExitPortKind.Detour);

        // ⚠ 뒤집힌 자리다 (R7 P-6 · 결정 ⑤ · 2026-09-17). 전에는 자유 씬이라 표식이 없었다 —
        //    이제 자유 씬이라는 종류가 없으므로 다녀오는 곳도 <b>그냥 에피소드</b>다.
        Assert.Equal(made.Name, made.ExcelEpisodeId);
        Assert.Contains(
            editor.Project.Chapters.Single().Episodes,
            episode => string.Equals(episode.EpisodeId, made.Name, StringComparison.Ordinal));

        // 같은 판에 선다 — 다녀오는 길이 판을 넘지 않는다.
        Assert.Equal(
            editor.Project.FindFileContainingNode(node.Id)!.Id,
            editor.Project.FindFileContainingNode(made.Id)!.Id);
    }

    [Fact]
    public void 다녀오는_에피소드는_출발과_같은_장면에_선다()
    {
        // 곁가지는 제 본줄 옆에 있어야 판에서 읽힌다 — 장면을 안 주면 `장면 밖`으로 밀려
        // 챕터 프레임 바깥에 혼자 선다.
        (ProjectEditor editor, DialogueNode node, string lineId) = World();
        editor.UpdateEpisode("ch01", "root", sceneId: "sc_아침");

        DialogueNode made = editor.AddBranchMarker(node.Id, lineId);

        Assert.Equal(
            "sc_아침",
            editor.Project.Chapters.Single().Episodes
                .Single(episode => string.Equals(episode.EpisodeId, made.Name, StringComparison.Ordinal))
                .SceneId);
    }

    [Fact]
    public void 다녀오는_에피소드는_도달_불가로_울지_않는다()
    {
        // ⛔ 들어오는 간선이 없는 것이 맞다 — 챕터 진행이 아니라 대본의 `<<detour>>`로
        //    들어간다. 그래서 도달성 증명의 판정은 <b>옳고, 고치지 않는다</b>: 그 증명기는
        //    저쪽 런타임의 오라클이고 코퍼스로 고정돼 있어 여기서 답을 바꾸면 둘이 조용히
        //    갈린다. 이미 있는 `도달불가 허용`이 정확히 이 자리를 위한 칸이다.
        (ProjectEditor editor, DialogueNode node, string lineId) = World();

        DialogueNode made = editor.AddBranchMarker(node.Id, lineId);

        Assert.True(editor.Project.Chapters.Single().Episodes
            .Single(episode => string.Equals(episode.EpisodeId, made.Name, StringComparison.Ordinal))
            .AllowUnreachable);
    }

    [Fact]
    public void 손으로_세운_카드는_도달_불가로_운다()
    {
        // ⚠ 「분기 추가」와 가르는 자리다. 작가가 판에 그냥 세운 카드는 <b>아직 안 이은 것</b>
        //    이므로 짚어 줘야 한다 — 허용을 기본값으로 두면 고아 에피소드가 조용히 쌓인다.
        (ProjectEditor editor, _, _) = World();

        DialogueNode made = editor.AddDialogueNode(
            editor.Project.Files[0].Id, name: "혼자선카드");

        Assert.False(editor.Project.Chapters.Single().Episodes
            .Single(episode => string.Equals(episode.EpisodeId, made.Name, StringComparison.Ordinal))
            .AllowUnreachable);
    }

    [Fact]
    public void 다녀오는_에피소드도_아래에_선택지_세_칸을_가진다()
    {
        // 소유자: "이 자유씬 역시 에피소드 노드이기에 아래쪽으로 선택지 3개가 뚫려 있는 상태" —
        // 별도 기계가 아니라 <b>에피소드 선택지 3칸 바로 그것</b>이다(결정 ⑤).
        (ProjectEditor editor, DialogueNode node, string lineId) = World();
        DialogueNode made = editor.AddBranchMarker(node.Id, lineId);

        Assert.Equal(3, Graph.GraphProjectionBuilder
            .Build(editor.Project, new HashSet<string>(editor.Project.Files.Select(file => file.Id)))
            .Items.OfType<Graph.ExpandedNodeProjection>()
            .Single(item => string.Equals(item.NodeId, made.Id, StringComparison.Ordinal))
            .OutputPorts.Count(port => port.Kind == Graph.GraphOutputPortKind.Choice));
    }

    [Fact]
    public void 되돌리기_한_번에_셋이_함께_돌아간다()
    {
        // ⛔ 갈라 두면 표식만 사라지고 <b>아무도 안 부르는 씬</b>이 판에 남는다.
        (ProjectEditor editor, DialogueNode node, string lineId) = World();

        int before = editor.Project.Files[0].Nodes.Count;
        editor.AddBranchMarker(node.Id, lineId);

        editor.Undo();

        Assert.Equal(before, editor.Project.Files[0].Nodes.Count);
        Assert.Null(editor.Project.EnumerateNodes().OfType<DialogueNode>()
            .First(item => item.Id == node.Id)
            .FindExtension(lineId)?.DetourTargetNodeId);
    }

    [Fact]
    public void 한_자리에_둘은_못_둔다()
    {
        // 표식은 줄 하나에 하나다 — 둘을 적을 칸이 없고, 있어도 어느 쪽이 먼저인지 말할 수 없다.
        (ProjectEditor editor, DialogueNode node, string lineId) = World();
        editor.AddBranchMarker(node.Id, lineId);

        Assert.Throws<InvalidOperationException>(() => editor.AddBranchMarker(node.Id, lineId));
    }

    [Fact]
    public void 그_노드의_줄이_아니면_거절한다()
    {
        (ProjectEditor editor, DialogueNode node, _) = World();

        Assert.Throws<InvalidOperationException>(
            () => editor.AddBranchMarker(node.Id, "ln_아무거나"));
    }
}
