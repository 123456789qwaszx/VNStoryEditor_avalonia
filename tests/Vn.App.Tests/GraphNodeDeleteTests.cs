using Avalonia.Controls;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;
using Path = System.IO.Path;

namespace Vn.App.Tests;

/// <summary>
/// <b>[연출 그래프]에서 카드를 지우는 일</b> (2026-09-18 소유자: *"난 그 둘이 같다고
/// 생각하고 있었는데"*).
///
/// ⛔ <b>만들기는 합쳤는데 지우기는 안 합쳐져 있었다.</b> 챕터 판의 카드는 에피소드인데
/// (R7 P-6 · 결정 ⑤) 여기서 지우면 <b>카드만</b> 걷혀, 대본 탭의 트리에는 줄이 남고 글만
/// 빠진 반쪽이 됐다.
///
/// ⚠ 대신 <b>되돌릴 수 있던 단추가 아니게 된다</b> — 에피소드 삭제는 원고를 <c>.bak</c>으로
/// 민다. 그래서 여기서만 한 번 더 묻고, 단추 이름이 누르기 전에 무엇이 사라지는지 말한다.
/// </summary>
public sealed class GraphNodeDeleteTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-graph-delete", Guid.NewGuid().ToString("N"));

    private string ManifestPath => Path.Combine(_directory, "p" + ProjectManifestJson.FileExtension);

    public GraphNodeDeleteTests()
    {
        Directory.CreateDirectory(_directory);
        ProjectStore.Save(ManifestPath, new StoryProject { Title = "카드 지우기" });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 챕터_판의_카드를_지우면_에피소드도_함께_사라진다() => HeadlessUi.Run(() =>
    {
        (GraphEditorView graph, AuthoringSession session) = Show();
        DialogueNode card = Card(session, "ep01");

        session.Select(card.Id);
        Click(graph);

        // 첫 걸음은 묻기만 한다 — 원고가 걸려 있고 되돌리기가 못 되돌린다.
        Assert.NotNull(graph.DeleteConfirmButton);
        Assert.Contains("ep01", session.Editor.FindChapter("ch01")!.Episodes.Select(e => e.EpisodeId));

        graph.DeleteConfirmButton!.RaiseEvent(
            new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain("ep01", session.Editor.FindChapter("ch01")!.Episodes.Select(e => e.EpisodeId));
        Assert.Null(session.Project.FindNode(card.Id));
    });

    [Fact]
    public void 챕터_판의_카드를_골랐으면_단추가_에피소드_삭제라고_말한다() => HeadlessUi.Run(() =>
    {
        // ⛔ 「노드 삭제」라고 적힌 단추가 에피소드를 지우면 그것은 <b>단추가 거짓말</b>이다.
        (GraphEditorView graph, AuthoringSession session) = Show();

        session.Select(Card(session, "ep01").Id);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("에피소드 삭제…", DeleteButton(graph).Content);
    });

    [Fact]
    public void 작가의_판에서는_카드만_지우고_묻지_않는다() => HeadlessUi.Run(() =>
    {
        // 챕터가 아닌 판의 카드는 에피소드가 아니다 — 새 프로젝트의 `기본 파일`이 그렇다.
        // 되돌리기가 살아 있으므로 물을 이유도 없다.
        (GraphEditorView graph, AuthoringSession session) = Show();

        string freeBoard = session.Editor.AddStoryFile("낙서").Id;
        DialogueNode scratch = session.Editor.AddDialogueNode(freeBoard, name: "메모");

        session.Select(scratch.Id);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("노드 삭제", DeleteButton(graph).Content);

        Click(graph);

        Assert.Null(graph.DeleteConfirmButton);
        Assert.Null(session.Project.FindNode(scratch.Id));
    });

    /// <summary>그 에피소드의 카드 — `AddEpisode`가 함께 세운 것이다.</summary>
    private static DialogueNode Card(AuthoringSession session, string episodeId)
    {
        session.Editor.AddEpisode("ch01", episodeId, title: string.Empty, 0, 0);

        return session.Project.EnumerateNodes().OfType<DialogueNode>()
            .Single(node => EpisodeNaming.EpisodeIdOf(node) == episodeId);
    }

    private static Button DeleteButton(GraphEditorView graph) =>
        graph.FindControl<Button>("DeleteNodeButton")!;

    private static void Click(GraphEditorView graph)
    {
        DeleteButton(graph).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private (GraphEditorView Graph, AuthoringSession Session) Show()
    {
        var session = new AuthoringSession();
        session.Open(ManifestPath);
        session.Editor.EnsureChapter("ch01");
        session.EnsureChapterBoard("ch01");

        var graph = new GraphEditorView();
        var window = new Window { Width = 1400, Height = 900, Content = graph };
        window.Show();
        graph.Attach(session);
        graph.Rebuild();

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (graph, session);
    }
}
