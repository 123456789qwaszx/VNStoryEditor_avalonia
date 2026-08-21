using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Model;
using Vn.Authoring.Results;
using Vn.Authoring.Script;

namespace Vn.App.Tests;

/// <summary>
/// 연출 추가 콘솔 (2026-08-21 소유자: "아래쪽에서 보이게 한다는 느낌 대신에 콘솔을
/// 띄운다는 느낌으로 … 최근 사용·검색은 유지하되 종류별로 탭으로") — 검색·탭·항목
/// 클릭이 실제로 커맨드를 다는 것까지 사람 눈 없이 닫는다.
/// </summary>
public sealed class PresentationAddConsoleTests
{
    private static (AuthoringSession Session, PresentationNodeEditor Editor, string NodeId, string LineId) Stage()
    {
        var session = new AuthoringSession();
        StoryFile file = session.ActiveFile!;

        ScriptDocument script = session.Editor.AddScript("본문 대본");
        DialogueNode dialogue = session.Editor.AddDialogueNode(file.Id, name: "본문", scriptId: script.Id);
        ScriptLine line = session.Editor.InsertScriptLine(script.Id);
        session.Editor.SetScriptLineText(script.Id, line.Id, string.Empty, "첫 줄");

        DialogueResult published = session.Editor.PublishDialogue(dialogue.Id).Result;
        PresentationNode presentation = session.Editor.AddPresentationNode(file.Id, name: "연출");
        session.Editor.SetPresentationSource(
            presentation.Id, published.Identity.ResultId, published.Identity.Version);

        var editor = new PresentationNodeEditor();
        editor.Attach(session);
        editor.Show(presentation.Id);

        return (session, editor, presentation.Id, line.Id);
    }

    private static Button[] TabsOf(Control console) =>
        console.GetLogicalDescendants().OfType<WrapPanel>().Single()
            .Children.OfType<Button>().ToArray();

    private static Button[] ItemsOf(Control console) =>
        console.GetLogicalDescendants().OfType<ScrollViewer>().Single()
            .GetLogicalDescendants().OfType<Button>().ToArray();

    [Fact]
    public void 콘솔은_검색과_종류_탭으로_서고_항목_클릭이_커맨드를_달고_닫는다() => HeadlessUi.Run(() =>
    {
        (AuthoringSession session, PresentationNodeEditor editor, string nodeId, string lineId) = Stage();

        bool closed = false;
        Control console = editor.BuildAddConsole(lineId, setup: false, () => closed = true)!;

        // 검색 하나 + 직접 입력 하나 — 콘솔이 갤러리와 직접 입력의 문을 함께 진다.
        TextBox[] boxes = console.GetLogicalDescendants().OfType<TextBox>().ToArray();
        Assert.Equal(2, boxes.Length);

        // 탭: [최근] + 카테고리들 — 종류별로 찾아가는 길이 상시로 보인다.
        Button[] tabs = TabsOf(console);
        Assert.Contains(tabs, tab => Equals(tab.Content, "최근"));
        Assert.True(tabs.Length > 3, $"카테고리 탭이 서야 한다 (지금 {tabs.Length}개)");

        // 최근이 비어 있으면 첫 카테고리 탭이 기본이다 — 빈 화면으로 열리지 않는다.
        Assert.NotEmpty(ItemsOf(console));

        // [최근] 탭 클릭 = 아직 빈 안내(버튼 없음).
        tabs.First(tab => Equals(tab.Content, "최근"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(ItemsOf(console));

        // 카테고리 탭 클릭 = 그 종류의 커맨드 목록.
        Button categoryTab = tabs.First(tab => !Equals(tab.Content, "최근"));
        categoryTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.NotEmpty(ItemsOf(console));

        // 검색은 탭을 덮는다 — 전 범위 평면 결과. TextChanged는 비동기라 잡을 돌린다.
        boxes[0].Text = "size_close";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Button hit = Assert.Single(ItemsOf(console), item =>
            item.GetLogicalDescendants().OfType<TextBlock>()
                .Any(text => (text.Text ?? "").Contains("<<size_close>>", StringComparison.Ordinal)));

        // 항목 클릭 = 그 라인에 커맨드가 붙고 콘솔이 닫힌다.
        hit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(closed);
        PresentationNode node = session.Project.FindPresentation(nodeId)!;
        PresentationCommandInstance added = Assert.Single(node.FindBinding(lineId)!.Commands);
        Assert.Equal("char_rig_depth.size_close", added.DefinitionId);
    });

    [Fact]
    public void Setup_대상_콘솔은_Setup에_달고_최근_탭에_방금_쓴_것이_선다() => HeadlessUi.Run(() =>
    {
        (AuthoringSession session, PresentationNodeEditor editor, string nodeId, _) = Stage();

        Control console = editor.BuildAddConsole(null, setup: true, () => { })!;
        TextBox search = console.GetLogicalDescendants().OfType<TextBox>().First();

        search.Text = "size_far";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // TextChanged는 비동기다
        ItemsOf(console)[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        PresentationNode node = session.Project.FindPresentation(nodeId)!;
        Assert.Single(node.SetupCommands);

        // 다음 콘솔은 [최근] 탭부터 열리고 방금 쓴 커맨드가 맨 위다.
        Control next = editor.BuildAddConsole(null, setup: true, () => { })!;
        Button first = ItemsOf(next).First();
        Assert.Contains(first.GetLogicalDescendants().OfType<TextBlock>(),
            text => (text.Text ?? "").Contains("<<size_far>>", StringComparison.Ordinal));
    });
}
