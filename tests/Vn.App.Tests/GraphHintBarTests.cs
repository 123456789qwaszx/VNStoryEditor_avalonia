using Avalonia.Controls;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// 연출 그래프 <b>아래 띠는 지금 벌어지는 일만 말한다</b> (2026-08-24 소유자: "포트를 끌어
/// 놓으면 연결, 빈곳좌클릭 드래그 범위선택 이런 설명문구도 제거해주십시오").
///
/// 늘 서 있던 조작법 설명이 사라졌다. 손이 이미 아는 것을 화면이 매번 다시 일러 주면,
/// 정작 무언가를 말해야 할 때 그 줄이 눈에 안 띈다 — 같은 날 검증 보고에서 동기화 문구를
/// 지운 것과 같은 선이다.
///
/// ⚠ 되돌아가기 가장 쉬운 길은 XAML에 <c>Text=</c> 기본값을 다시 적는 것이다. 이 파일이
/// 그것을 막는다.
/// </summary>
public sealed class GraphHintBarTests
{
    [Fact]
    public void 할_말이_없으면_띠가_아예_접힌다() => HeadlessUi.Run(() =>
    {
        // ⛔ 조작법 설명도, 빈 칸도 남지 않는다 — 할 말 없는 띠가 서 있으면 그것도 소음이다.
        (GraphEditorView graph, _, _) = ShowBoard();

        Assert.False(graph.FindControl<Border>("HintBar")!.IsVisible);
        Assert.True(string.IsNullOrEmpty(graph.FindControl<TextBlock>("HintText")!.Text));
    });

    [Fact]
    public void 노드가_있어도_조작법을_일러_주지_않는다() => HeadlessUi.Run(() =>
    {
        // 판이 비어서 조용한 것이 아니라는 것을 못 박는다.
        (GraphEditorView graph, AuthoringSession session, string fileId) = ShowBoard();

        session.Editor.AddDialogueNode(fileId, name: "첫 씬");
        session.Editor.AddDialogueNode(fileId, name: "둘째 씬");
        graph.Rebuild();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        string text = graph.FindControl<TextBlock>("HintText")!.Text ?? string.Empty;

        Assert.False(graph.FindControl<Border>("HintBar")!.IsVisible);
        Assert.DoesNotContain("범위 선택", text);
        Assert.DoesNotContain("끌어", text);
        Assert.DoesNotContain("휠", text);
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static (GraphEditorView Graph, AuthoringSession Session, string FileId) ShowBoard()
    {
        var session = new AuthoringSession();
        string directory = Path.Combine(
            Path.GetTempPath(), "vn-hint-bar", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        string manifest = Path.Combine(directory, "project" + ProjectManifestJson.FileExtension);
        ProjectStore.Save(manifest, new StoryProject { Title = "안내 띠 검증" });
        session.Open(manifest);

        string fileId = session.EnsureChapterBoard("ch01");

        var graph = new GraphEditorView();
        var window = new Window { Width = 1400, Height = 900, Content = graph };
        window.Show();
        graph.Attach(session);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return (graph, session, fileId);
    }
}
