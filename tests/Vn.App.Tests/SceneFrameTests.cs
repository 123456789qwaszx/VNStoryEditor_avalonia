using Avalonia.Controls;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// <b>장면 프레임</b> — 챕터 박스 안에 한 겹 더 (R6 S-3b · `docs/plans/R6-explorer.md` §5).
///
/// 같은 장면의 카드가 한 영역으로 보여야 작가가 <b>어디까지가 한 수명</b>인지 안다 —
/// 장면 안에서는 모든 게 물릴 수 있고, 장면이 끝나면 확정된다.
/// </summary>
public sealed class SceneFrameTests
{
    [Fact]
    public void 장면마다_이름표가_붙은_영역이_카드_뒤에_깔린다() => HeadlessUi.Run(() =>
    {
        (GraphEditorView graph, AuthoringSession session) = Show();

        Chapter(session, ("opening", "ep01"), ("opening", "ep02"), ("classroom", "ep03"));
        graph.Rebuild();

        Assert.Contains("장면 opening", SceneLabels(graph));
        Assert.Contains("장면 classroom", SceneLabels(graph));

        // 챕터 프레임과 같은 규율 — 카드보다 앞 인덱스(= 뒤에 깔림)다.
        var canvas = graph.FindControl<Canvas>("GraphCanvas")!;
        Border label = canvas.Children.OfType<Border>().First(border =>
            (border.Child as TextBlock)?.Text?.StartsWith("장면 ", StringComparison.Ordinal) == true);
        Border card = canvas.Children.OfType<Border>().First(border => border.Tag is string);

        Assert.True(
            canvas.Children.IndexOf(label) < canvas.Children.IndexOf(card),
            "장면 영역은 노드 카드 뒤에 깔려야 한다");
    });

    [Fact]
    public void 장면ID를_하나도_안_적은_챕터에는_안_그린다() => HeadlessUi.Run(() =>
    {
        // 접힌 판과 <b>같은 규칙</b>이다(규격 §2) — 에피소드마다 제 장면인데 그대로 그리면
        // 카드마다 테두리가 하나씩 생겨 판이 통째로 노이즈가 된다.
        (GraphEditorView graph, AuthoringSession session) = Show();

        Chapter(session, (null, "ep01"), (null, "ep02"));
        graph.Rebuild();

        Assert.Empty(SceneLabels(graph));
    });

    [Fact]
    public void 장면이_하나뿐이면_안_그린다() => HeadlessUi.Run(() =>
    {
        // 챕터 프레임과 똑같은 자리에 겹쳐 그려 봐야 테두리만 두 겹이 된다.
        (GraphEditorView graph, AuthoringSession session) = Show();

        Chapter(session, ("opening", "ep01"), ("opening", "ep02"));
        graph.Rebuild();

        Assert.Empty(SceneLabels(graph));
    });

    [Fact]
    public void 들어오는_자리가_둘인_장면은_이름표가_짚어_준다() => HeadlessUi.Run(() =>
    {
        // 거부는 내보내기 관문의 일이다 — 화면은 <b>그려 놓고 짚는다</b>(묶음 규율과 같다).
        (GraphEditorView graph, AuthoringSession session) = Show();

        ChapterDocument chapter = Chapter(
            session, ("opening", "root"), ("shared", "a"), ("shared", "b"));

        chapter.Edges.Add(new ChapterEdge("root", "a", "A로", null, null, 0));
        chapter.Edges.Add(new ChapterEdge("root", "b", "B로", null, null, 0));

        graph.Rebuild();

        Assert.Contains("장면 shared ⚠", SceneLabels(graph));
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static List<string> SceneLabels(GraphEditorView graph) =>
        graph.FindControl<Canvas>("GraphCanvas")!.Children.OfType<Border>()
            .Select(border => (border.Child as TextBlock)?.Text)
            .Where(text => text is not null && text.StartsWith("장면 ", StringComparison.Ordinal))
            .Select(text => text!)
            .ToList();

    /// <summary>챕터 하나와 그 판의 카드들 — 판 이름이 챕터 Id인 것이 둘을 잇는 규약이다.</summary>
    private static ChapterDocument Chapter(
        AuthoringSession session, params (string? SceneId, string EpisodeId)[] episodes)
    {
        ChapterDocument chapter = session.Editor.EnsureChapter("ch01");
        string fileId = session.EnsureChapterBoard("ch01");

        for (int index = 0; index < episodes.Length; index++)
        {
            (string? sceneId, string episodeId) = episodes[index];

            chapter.Episodes.Add(new ChapterEpisode(
                episodeId, episodeId, string.Empty, episodeId, index * 260, 0, null, index + 2)
            {
                SceneId = sceneId
            });

            session.Editor.AddDialogueNode(fileId, index * 260, 0, episodeId).ExcelEpisodeId = episodeId;
        }

        return chapter;
    }

    private static (GraphEditorView Graph, AuthoringSession Session) Show()
    {
        var session = new AuthoringSession();
        string directory = Path.Combine(
            Path.GetTempPath(), "vn-scene-frame", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        string manifest = Path.Combine(directory, "project" + ProjectManifestJson.FileExtension);
        ProjectStore.Save(manifest, new StoryProject { Title = "장면 박스 검증" });
        session.Open(manifest);

        var graph = new GraphEditorView();
        var window = new Window { Width = 1400, Height = 900, Content = graph };
        window.Show();
        graph.Attach(session);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return (graph, session);
    }
}
