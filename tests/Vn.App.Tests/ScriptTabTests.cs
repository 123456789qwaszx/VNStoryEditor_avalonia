using Avalonia.Controls;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Definition;
using Vn.Authoring.Model;
using Vn.Authoring.Script;
using Vn.Authoring.Serialization;
using Path = System.IO.Path;

namespace Vn.App.Tests;

/// <summary>
/// <b>[대본] 탭 — 작가의 자리</b> (R-E · 지시서 §6).
///
/// 못 박는 것: ① 챕터 → 에피소드를 고르면 그 글만 보인다 ② 고쳐 반영하면 <b>줄의 신원이
/// 보존된다</b> ③ 지우기는 두 번 눌러야 한다 ④ 화면에 `#line:` 태그가 안 보인다.
///
/// ⚠ ②가 이 탭의 존재 이유다. 작가가 글을 고칠 때마다 신원이 갈리면 그 줄에 매달린
/// 연출·세이브가 통째로 끊긴다 — 그래서 <b>전량 재생성이 아니라 diff</b>여야 한다.
/// </summary>
public sealed class ScriptTabTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-script-tab", Guid.NewGuid().ToString("N"));

    private string ManifestPath => Path.Combine(_directory, "p" + ProjectManifestJson.FileExtension);

    public ScriptTabTests()
    {
        Directory.CreateDirectory(_directory);
        ProjectStore.Save(ManifestPath, new StoryProject { Title = "작가의 자리" });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 챕터와_에피소드를_고르면_그_글이_보인다() => HeadlessUi.Run(() =>
    {
        (ScriptView view, AuthoringSession session) = Show();
        Seed(session, "ch01", "ep01", ("윌로", "복도는 조용했다."), ("라루", "같이 갈까?"));

        var chapters = view.FindControl<ListBox>("ChapterList")!;
        var episodes = view.FindControl<ListBox>("EpisodeList")!;
        var box = view.FindControl<TextBox>("ScriptBox")!;

        Assert.Equal(["ch01"], chapters.ItemsSource!.Cast<string>());
        Assert.Equal(["ep01"], episodes.ItemsSource!.Cast<string>());

        Assert.Contains("윌로: 복도는 조용했다.", box.Text!);
        Assert.Contains("라루: 같이 갈까?", box.Text!);
    });

    [Fact]
    public void 화면에_LineId_태그가_보이지_않는다() => HeadlessUi.Run(() =>
    {
        // §6.3 — `#line:` 태그가 보이면 지저분하고 작가가 지운다. 신원은 diff가 붙든다.
        (ScriptView view, AuthoringSession session) = Show();
        Seed(session, "ch01", "ep01", ("윌로", "한 줄"));

        Assert.DoesNotContain("#line:", view.FindControl<TextBox>("ScriptBox")!.Text!);
    });

    [Fact]
    public void 고쳐_반영해도_줄의_신원이_보존된다() => HeadlessUi.Run(() =>
    {
        // ⛔ 이 탭의 존재 이유. 신원이 갈리면 그 줄에 매달린 연출이 통째로 끊긴다.
        (ScriptView view, AuthoringSession session) = Show();
        DialogueNode node = Seed(session, "ch01", "ep01", ("윌로", "복도는 조용했다."));

        string before = LineIds(session, node).Single();

        var box = view.FindControl<TextBox>("ScriptBox")!;
        box.Text = "윌로: 복도는 조용하지 않았다.";
        Click(view, "ApplyButton");

        Assert.Equal(["복도는 조용하지 않았다."], Texts(session, node));
        Assert.Equal(before, LineIds(session, node).Single());
    });

    [Fact]
    public void 줄을_더하면_그_줄만_새로_선다() => HeadlessUi.Run(() =>
    {
        (ScriptView view, AuthoringSession session) = Show();
        DialogueNode node = Seed(session, "ch01", "ep01", ("윌로", "첫 줄"));

        string before = LineIds(session, node).Single();

        var box = view.FindControl<TextBox>("ScriptBox")!;
        box.Text = "윌로: 첫 줄\n라루: 새로 쓴 줄";
        Click(view, "ApplyButton");

        Assert.Equal(["첫 줄", "새로 쓴 줄"], Texts(session, node));

        // 앞 줄은 제 신원을 그대로 들고 있다 — 새 줄만 새 신원을 받았다.
        Assert.Equal(before, LineIds(session, node)[0]);
        Assert.NotEqual(before, LineIds(session, node)[1]);
    });

    [Fact]
    public void 지우기는_한_번_더_눌러야_지운다() => HeadlessUi.Run(() =>
    {
        // ⚠ 붙여넣기 한 번이 줄을 통째로 날릴 수 있고, 그 줄에는 연출이 매달려 있다.
        (ScriptView view, AuthoringSession session) = Show();
        DialogueNode node = Seed(session, "ch01", "ep01", ("윌로", "첫 줄"), ("라루", "둘째 줄"));

        var box = view.FindControl<TextBox>("ScriptBox")!;
        box.Text = "윌로: 첫 줄";

        Click(view, "ApplyButton");

        // 첫 번째는 묻기만 한다 — 둘째 줄이 아직 살아 있다.
        Assert.Equal(["첫 줄", "둘째 줄"], Texts(session, node));
        Assert.Contains("한 번 더", session.StatusMessage);

        Click(view, "ApplyButton");

        Assert.Equal(["첫 줄"], Texts(session, node));
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    private (ScriptView View, AuthoringSession Session) Show()
    {
        var session = new AuthoringSession();
        session.Open(ManifestPath);

        var view = new ScriptView();
        var window = new Window { Width = 1100, Height = 700, Content = view };
        window.Show();
        view.Attach(session);

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (view, session);
    }

    /// <summary>판 하나와 그 위의 대사노드 하나를 세우고 줄을 채운다.</summary>
    private static DialogueNode Seed(
        AuthoringSession session, string chapterId, string episodeId,
        params (string Speaker, string Text)[] lines)
    {
        string fileId = session.Editor.EnsureChapterBoard(chapterId);
        DialogueNode node = session.Editor.AddDialogueNode(fileId, name: episodeId);
        string scriptId = session.Editor.EnsureDialogueScript(node.Id).Id;

        // 노드 생성이 딸려 주는 첫 빈 줄을 첫 대사로 쓴다.
        string first = session.Project.FindScript(scriptId)!.ActiveLines.First().Id;
        session.Editor.SetScriptLineText(scriptId, first, lines[0].Speaker, lines[0].Text);

        foreach ((string speaker, string text) in lines.Skip(1))
        {
            string id = session.Editor.InsertScriptLine(scriptId).Id;
            session.Editor.SetScriptLineText(scriptId, id, speaker, text);
        }

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return node;
    }

    private static void Click(ScriptView view, string name)
    {
        view.FindControl<Button>(name)!
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static IReadOnlyList<string> LineIds(AuthoringSession session, DialogueNode node) =>
        session.Project.FindScript(node.ScriptId!)!.ActiveLines.Select(line => line.Id).ToList();

    private static IReadOnlyList<string> Texts(AuthoringSession session, DialogueNode node)
    {
        ScriptDocument script = session.Project.FindScript(node.ScriptId!)!;
        ScriptLocale primary = script.Locales.Single(locale => locale.Locale == script.PrimaryLocale);

        return script.ActiveLines.Select(line => primary.Find(line.Id).Text).ToList();
    }
}
