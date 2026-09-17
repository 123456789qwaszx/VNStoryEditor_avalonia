using Avalonia.Controls;
using Avalonia.Input;
using Vn.App.Views;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;
using Vn.Authoring.Script;

namespace Vn.App.Tests;

/// <summary>
/// <b>Ctrl+S가 [대본]의 글까지 저장한다</b> (2026-09-17 소유자: *"텍스트 필드에 자유롭게
/// 편집하되, ctrl+s로 저장을 해야되는 구조"*).
///
/// ⛔ <b>여기가 조용한 사고였다.</b> 저장은 <b>프로젝트</b>를 쓰고, 입력칸의 글은
/// <c>ApplyScenarioText</c>를 지나야 프로젝트에 <b>들어간다</b>. 그 둘 사이에 아무도 없어서,
/// 글을 쓰고 Ctrl+S를 누르면 <b>상태줄이 저장했다고 말하는데 글은 어디에도 없었다.</b>
///
/// ⚠ 그래서 이 클래스는 <see cref="ScriptView"/>가 아니라 <b>창</b>을 띄운다. 탭 혼자서는
/// 맞아도 둘을 잇는 자리가 비어 있으면 사람은 글을 잃는다 — 맞물림이 곧 기능이다.
/// </summary>
public sealed class ScriptSaveShellTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-script-save", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Ctrl_S가_입력칸의_글을_프로젝트에_넣는다() => HeadlessUi.Run(() =>
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        DialogueNode node = SeedScript(window, _directory);

        window.FindControl<TabControl>("MainTabs")!.SelectedItem =
            window.FindControl<TabItem>("ScriptTabItem")!;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var view = window.FindControl<ScriptView>("Script")!;
        view.TextArea.Text = "라루: Ctrl+S만 눌렀다";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 아직 프로젝트에는 없다 — 자유롭게 고치는 동안은 화면에만 있다.
        Assert.True(view.HasUnsavedText);
        Assert.Equal("첫 줄", TextOf(window, node));

        Press(window, Key.S, KeyModifiers.Control);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("Ctrl+S만 눌렀다", TextOf(window, node));
        Assert.False(view.HasUnsavedText);

        window.Close();
    });

    [Fact]
    public void 안_저장된_글이_있으면_제목이_별을_단다() => HeadlessUi.Run(() =>
    {
        // ⚠ 프로젝트의 <c>IsDirty</c>는 입력칸을 <b>모른다</b> — 글은 아직 프로젝트 밖이라
        //    그쪽에서 보면 아무 일도 없다. 그것만 보면 쓰는 내내 제목이 깨끗해 보인다.
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        SeedScript(window, _directory);

        window.FindControl<TabControl>("MainTabs")!.SelectedItem =
            window.FindControl<TabItem>("ScriptTabItem")!;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var dirtyText = window.FindControl<TextBlock>("DirtyText")!;
        var view = window.FindControl<ScriptView>("Script")!;

        view.TextArea.Text = "라루: 쓰는 중";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotEmpty(dirtyText.Text ?? string.Empty);

        window.Close();
    });

    /// <summary>
    /// 챕터 하나 · 에피소드 하나 · 줄 하나 — <b>대본 탭이 그릴 것</b>을 세운다.
    ///
    /// ⚠ 챕터가 없으면 그 탭은 "아직 챕터가 없습니다"를 띄우고 입력칸을 <b>잠근다</b>.
    /// 판에 노드만 세워서는 안 되는 이유다 — 목록의 원천은 챕터다.
    ///
    /// ⚠ 저장 경로도 준다. 없으면 저장이 [다른 이름으로]를 띄운다 — 여기서 재려는 것은
    /// 그 앞 단계, <b>글이 프로젝트로 들어가는가</b>이다.
    /// </summary>
    private static DialogueNode SeedScript(MainWindow window, string directory)
    {
        Vn.App.Services.AuthoringSession session = window.SessionProbe;

        Directory.CreateDirectory(directory);
        string manifest = Path.Combine(directory, "p" + ProjectManifestJson.FileExtension);
        ProjectStore.Save(manifest, new StoryProject { Title = "저장 검증" });
        session.Open(manifest);

        session.Editor.EnsureChapter("ch01");

        // ⚠ 카드와 대본까지 함께 선다 (2026-09-18) — 여기서 또 만들면 카드가 둘이 된다.
        session.Editor.AddEpisode("ch01", "ep01", title: "ep01", 0, 0);

        DialogueNode node = session.Project.EnumerateNodes().OfType<DialogueNode>()
            .Single(item => Vn.Authoring.Chapters.EpisodeNaming.EpisodeIdOf(item) == "ep01");

        string scriptId = session.Editor.EnsureDialogueScript(node.Id).Id;

        ScriptLine first = session.Project.FindScript(scriptId)!.ActiveLines.First();
        session.Editor.SetScriptLineText(scriptId, first.Id, "라루", "첫 줄");

        session.Select(node.Id);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return node;
    }

    private static string TextOf(MainWindow window, DialogueNode node)
    {
        ScriptDocument script = window.SessionProbe.Project.FindScript(node.ScriptId)!;

        return script.RequireLocale(script.PrimaryLocale)
            .Entries[script.ActiveLines.Single().Id].Text;
    }

    /// <summary>창이 Tunnel로 받는 그 키 — 어디에 포커스가 있어도 Ctrl+S는 저장이다.</summary>
    private static void Press(Window window, Key key, KeyModifiers modifiers) =>
        window.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers
        });
}
