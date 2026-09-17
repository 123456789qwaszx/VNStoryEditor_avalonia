using Avalonia.Controls;
using Avalonia.VisualTree;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// <b>「조건 분기」 카드 — 줄 사이에 낀다</b> (R7 P-5 · `docs/plans/R7.md`).
///
/// ⛔ <b>내용이 없다.</b> 연출 그래프는 "어디서 갈라지는가"만 짚는다 (2026-09-17 소유자) —
/// 조건식도 선택지 문구도 여기서 정하지 않는다. 조건이 성립하든 말든 다녀오고, 다녀온
/// 에피소드가 제 첫머리에서 보고 아니면 곧바로 돌아온다.
/// </summary>
public sealed class BranchMarkerCardTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-branch-card", Guid.NewGuid().ToString("N"));

    private string ManifestPath => Path.Combine(_directory, "p" + ProjectManifestJson.FileExtension);

    public BranchMarkerCardTests()
    {
        Directory.CreateDirectory(_directory);
        ProjectStore.Save(ManifestPath, new StoryProject { Title = "조건 분기" });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 표식이_없으면_카드도_없다() => HeadlessUi.Run(() =>
    {
        (DialogueNodeEditor editor, _, _, string lineId) = Show();

        Assert.Null(Card(editor, lineId));
    });

    [Fact]
    public void 표식을_두면_그_줄_앞에_카드가_낀다() => HeadlessUi.Run(() =>
    {
        // ⚠ <b>앞</b>이다 — 그 줄에 붙은 것이 아니라 그 줄 앞에서 갈라진다는 표식이다.
        (DialogueNodeEditor editor, AuthoringSession session, string nodeId, string lineId) = Show();

        DialogueNode made = session.Editor.AddBranchMarker(nodeId, lineId);
        Rebuild(editor, nodeId);

        Border card = Assert.IsType<Border>(Card(editor, lineId));

        Assert.True(
            editor.LineHost.Children.IndexOf(card) <
            editor.LineHost.Children.IndexOf(LineCardAfter(editor, card)));

        Assert.Contains(Texts(card), text => text.Contains(made.Name, StringComparison.Ordinal));
    });

    [Fact]
    public void 카드에는_조건이_없다() => HeadlessUi.Run(() =>
    {
        // ⛔ 조건식을 여기서 적게 하면 연출 그래프가 "갈지 말지"까지 정하게 된다 —
        //    소유자가 명시적으로 뒤로 미룬 일이다.
        (DialogueNodeEditor editor, AuthoringSession session, string nodeId, string lineId) = Show();

        session.Editor.AddBranchMarker(nodeId, lineId);
        Rebuild(editor, nodeId);

        Border card = Assert.IsType<Border>(Card(editor, lineId));

        Assert.Empty(card.GetVisualDescendants().OfType<ComboBox>());
        Assert.Empty(card.GetVisualDescendants().OfType<TextBox>());
    });

    [Fact]
    public void 분기_제거는_표식만_지우고_에피소드는_남긴다() => HeadlessUi.Run(() =>
    {
        // ⛔ 함께 지우면 거기 써 둔 대사가 한 번의 실수로 사라진다.
        (DialogueNodeEditor editor, AuthoringSession session, string nodeId, string lineId) = Show();

        DialogueNode made = session.Editor.AddBranchMarker(nodeId, lineId);
        Rebuild(editor, nodeId);

        Button remove = Assert.IsType<Border>(Card(editor, lineId))
            .GetVisualDescendants().OfType<Button>()
            .Single(button => (button.Content as string)?.Contains('✕') == true);

        remove.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Null(Node(session, nodeId).FindExtension(lineId)?.DetourTargetNodeId);
        Assert.NotNull(session.Project.FindNode(made.Id));

        Rebuild(editor, nodeId);
        Assert.Null(Card(editor, lineId));
    });

    [Fact]
    public void 카드가_포트_하나를_뚫는다() => HeadlessUi.Run(() =>
    {
        // 화면과 판이 같은 것을 말해야 한다 — 카드가 보이는데 포트가 없으면 이을 자리가 없다.
        (DialogueNodeEditor editor, AuthoringSession session, string nodeId, string lineId) = Show();

        session.Editor.AddBranchMarker(nodeId, lineId);
        Rebuild(editor, nodeId);

        Assert.NotNull(Card(editor, lineId));
        Assert.Single(
            NodeConnections.PortsOf(Node(session, nodeId), session.Project),
            port => port.Kind == ExitPortKind.Detour);
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static DialogueNode Node(AuthoringSession session, string nodeId) =>
        (DialogueNode)session.Project.FindNode(nodeId)!;

    private static Control? Card(DialogueNodeEditor editor, string lineId) =>
        editor.LineHost.Children.FirstOrDefault(child =>
            Equals(child.Tag, DialogueNodeEditor.BranchMarkerTag(lineId)));

    /// <summary>카드 바로 다음에 오는 줄 카드 — 낀 자리가 <b>앞</b>인지 재는 데 쓴다.</summary>
    private static Control LineCardAfter(DialogueNodeEditor editor, Control card) =>
        editor.LineHost.Children[editor.LineHost.Children.IndexOf(card) + 1];

    private static IEnumerable<string> Texts(Control card) =>
        card.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text ?? string.Empty);

    private static void Rebuild(DialogueNodeEditor editor, string nodeId)
    {
        editor.Show(nodeId);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>대사 한 줄짜리 에피소드 노드를 편집기에 띄운다.</summary>
    private (DialogueNodeEditor Editor, AuthoringSession Session, string NodeId, string LineId) Show()
    {
        var session = new AuthoringSession();
        session.Open(ManifestPath);

        session.Editor.EnsureChapter("ch01");
        string fileId = session.EnsureChapterBoard("ch01");

        DialogueNode node = session.Editor.AddDialogueNode(fileId, 0, 0, "root");
        string lineId = session.Project.FindScript(node.ScriptId)!.ActiveLines.First().Id;

        var editor = new DialogueNodeEditor();
        var window = new Window { Width = 1200, Height = 800, Content = editor };
        window.Show();
        editor.Attach(session);
        editor.Show(node.Id);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return (editor, session, node.Id, lineId);
    }
}
