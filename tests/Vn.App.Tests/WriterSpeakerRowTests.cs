using Avalonia.Controls;
using Avalonia.Interactivity;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Avalonia.VisualTree;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// [화자 추가]가 실제로 줄을 남긴다 (2026-08-17 소유자 보고: "화자 추가를 눌렀는데 그냥
/// 그래프만 움찔거릴 뿐 아무일도 안 일어나").
///
/// 정체는 <b>더해진 뒤 지워지는 것</b>이었다 — `SetWriterSpeakers`가 이름 빈 항목을 걸러
/// 냈고, 방금 만든 줄이 정확히 그것이라 첫 저장에 휩쓸렸다. 빈 줄은 "아직 안 쓴 자리"이지
/// 잘못이 아니다. 거르는 자리를 <b>파일로 나갈 때</b>로 옮겼다.
/// </summary>
public sealed class WriterSpeakerRowTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-writer-speaker", Guid.NewGuid().ToString("N"));

    public WriterSpeakerRowTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 화자_추가를_누르면_빈_줄이_남는다() => HeadlessUi.Run(() =>
    {
        (AuthoringSession session, SetNodeEditor editor) = Show();

        editor.FindControl<Button>("AddSpeakerButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Single(session.Project.WriterSpeakers);

        // 화면에도 칠 칸이 서 있다.
        Assert.Contains(
            editor.FindControl<StackPanel>("SpeakerHost")!.GetVisualDescendants().OfType<TextBox>(),
            box => box.PlaceholderText == "대본에 적히는 화자명");
    });

    [Fact]
    public void 이름을_치면_그_줄에_남는다() => HeadlessUi.Run(() =>
    {
        // 빈 줄이 걸러지던 시절에는 첫 글자를 치는 순간(=첫 저장) 줄이 사라졌다.
        (AuthoringSession session, SetNodeEditor editor) = Show();

        editor.FindControl<Button>("AddSpeakerButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        TextBox name = editor.FindControl<StackPanel>("SpeakerHost")!
            .GetVisualDescendants().OfType<TextBox>()
            .First(box => box.PlaceholderText == "대본에 적히는 화자명");

        name.Text = "행상인";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("행상인", Assert.Single(session.Project.WriterSpeakers).Name);
    });

    [Fact]
    public void 이름이_빈_줄은_파일로_나가지_않는다() => HeadlessUi.Run(() =>
    {
        // 메모리에서는 살아 있어야 하지만(안 그러면 만들자마자 사라진다) 저장물에 남길
        // 이유는 없다 — 거르는 자리가 여기다.
        (AuthoringSession session, _) = Show();

        session.Editor.SetWriterSpeakers([
            new WriterSpeaker { Name = "행상인" },
            new WriterSpeaker()
        ]);

        Assert.Equal(2, session.Project.WriterSpeakers.Count);

        string path = Path.Combine(_directory, "project" + ProjectManifestJson.FileExtension);
        ProjectStore.Save(path, session.Project);

        StoryProject reread = ProjectStore.Load(path).Project;

        Assert.Equal("행상인", Assert.Single(reread.WriterSpeakers).Name);
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static (AuthoringSession Session, SetNodeEditor Editor) Show()
    {
        var session = new AuthoringSession();
        string fileId = session.EnsureChapterBoard("ch01");
        SetNode node = session.Project.FindFile(fileId)!.Nodes.OfType<SetNode>().Single();

        var editor = new SetNodeEditor();
        editor.Attach(session);
        var window = new Window { Content = editor, Width = 900, Height = 700 };
        window.Show();
        editor.Show(node.Id);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return (session, editor);
    }
}
