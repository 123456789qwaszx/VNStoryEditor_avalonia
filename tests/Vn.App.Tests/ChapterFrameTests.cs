using Avalonia.Controls;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// 2단계 무대 3번 — 시나리오 그래프의 챕터 박스 (PDF 3·9장). 펼친 판마다 노드들을 감싸는
/// 프레임과 이름표가 카드 뒤에 깔린다. 작가가 노드의 챕터 소속을 보고 붙일 곳을 정하는 무대다.
/// </summary>
public sealed class ChapterFrameTests
{
    [Fact]
    public void 챕터마다_이름표가_붙은_박스가_카드_뒤에_깔린다() => HeadlessUi.Run(() =>
    {
        (GraphEditorView graph, AuthoringSession session) = Show();

        // 챕터 판 두 개, 각각 노드 하나씩.
        string ch01 = session.EnsureChapterBoard("ch01");
        string ch02 = session.EnsureChapterBoard("ch02");
        session.Editor.AddDialogueNode(ch01, name: "ep_a");
        session.Editor.AddDialogueNode(ch02, name: "ep_b");

        graph.Rebuild();

        var canvas = graph.FindControl<Canvas>("GraphCanvas")!;

        // 이름표 — 챕터 이름이 판 위에 보인다.
        List<string> labels = canvas.Children.OfType<Border>()
            .Select(border => (border.Child as TextBlock)?.Text)
            .Where(text => text is not null && text.StartsWith("챕터 ", StringComparison.Ordinal))
            .Select(text => text!)
            .ToList();

        Assert.Contains(labels, label => label.StartsWith("챕터 ch01", StringComparison.Ordinal));
        Assert.Contains(labels, label => label.StartsWith("챕터 ch02", StringComparison.Ordinal));

        // 프레임은 히트 대상이 아니고(클릭·드래그 무영향), 카드보다 앞 인덱스(= 뒤에 깔림)다.
        Border frame = canvas.Children.OfType<Border>()
            .First(border => !border.IsHitTestVisible && border.Child is null);
        Border card = canvas.Children.OfType<Border>()
            .First(border => border.Tag is string);

        Assert.True(
            canvas.Children.IndexOf(frame) < canvas.Children.IndexOf(card),
            "챕터 박스는 노드 카드 뒤에 깔려야 한다");
    });

    [Fact]
    public void 이름표를_클릭하면_접히고_표를_펼침으로_되돌린다() => HeadlessUi.Run(() =>
    {
        // PDF 9장 — "특정 챕터를 체크해제하면 표형태로 접힘". 손잡이는 이름표 클릭이다.
        (GraphEditorView graph, AuthoringSession session) = Show();

        string ch01 = session.EnsureChapterBoard("ch01");
        session.Editor.AddDialogueNode(ch01, name: "ep_a");
        graph.Rebuild();

        var canvas = graph.FindControl<Canvas>("GraphCanvas")!;
        Border label = canvas.Children.OfType<Border>().First(border =>
            (border.Child as TextBlock)?.Text?.StartsWith("챕터 ch01") == true);

        label.RaiseEvent(new Avalonia.Input.PointerPressedEventArgs(
            label, new Avalonia.Input.Pointer(0, Avalonia.Input.PointerType.Mouse, true),
            label, default, 0,
            new Avalonia.Input.PointerPointProperties(
                Avalonia.Input.RawInputModifiers.LeftMouseButton,
                Avalonia.Input.PointerUpdateKind.LeftButtonPressed),
            Avalonia.Input.KeyModifiers.None));

        // 세션의 펼침 상태가 접힘으로 바뀌었고, 다시 그리면 표 프록시가 선다.
        Assert.DoesNotContain(ch01, session.ExpandedFileIds);

        graph.Rebuild();
        Assert.Contains(canvas.Children.OfType<Border>(),
            border => (border.Tag as string) == ch01); // 표 프록시 (Tag = FileId)

        // 펼침 기계의 역방향 — 표가 다시 카드가 된다.
        session.SetFileExpanded(ch01, expanded: true);
        graph.Rebuild();
        Assert.Contains(canvas.Children.OfType<Border>(),
            border => (border.Child as TextBlock)?.Text?.StartsWith("챕터 ch01") == true);
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static (GraphEditorView Graph, AuthoringSession Session) Show()
    {
        var session = new AuthoringSession();
        string directory = Path.Combine(
            Path.GetTempPath(), "vn-chapter-frame", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        string manifest = Path.Combine(directory, "project" + ProjectManifestJson.FileExtension);
        ProjectStore.Save(manifest, new StoryProject { Title = "박스 검증" });
        session.Open(manifest);

        var graph = new GraphEditorView();
        var window = new Window { Width = 1400, Height = 900, Content = graph };
        window.Show();
        graph.Attach(session);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return (graph, session);
    }
}
