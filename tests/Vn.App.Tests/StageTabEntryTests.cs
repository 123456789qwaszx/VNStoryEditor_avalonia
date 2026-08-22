using Avalonia.Controls;
using Vn.App.Services;
using Vn.Authoring.Model;
using Vn.Authoring.Results;
using Vn.Authoring.Script;

namespace Vn.App.Tests;

/// <summary>
/// 무대 프리뷰 탭에 <b>들어오는 것</b>이 곧 씬 선택이다 (2026-08-22 소유자 보고:
/// "연출 그래프에서 특정 에피소드노드를 클릭한채로 프리뷰에 들어올 시에는, 화면을
/// 클릭해도 콘솔이 안 나옴").
///
/// 원인은 화면이 아니라 <b>무엇을 보고 있는가</b>였다: 대사 노드가 선택돼 있으면 프리뷰가
/// 공급된 발행 결과를 그리고, 발행본은 불변이라 직접 조작이 잠긴다. 잠긴 화면에서는 무대를
/// 눌러도 조절창이 안 열린다. 연출의 입구가 이 판인 이상, 여기 들어왔다는 것은 그 씬을
/// 연출하겠다는 뜻이다 — 씬 선택기와 <b>같은 함수</b>를 지난다.
/// </summary>
public sealed class StageTabEntryTests
{
    [Fact]
    public void 대사_노드로_프리뷰_탭에_들어오면_연출_채널이_서고_선택이_옮겨간다() => HeadlessUi.Run(() =>
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        AuthoringSession session = window.SessionProbe;
        StoryFile file = session.ActiveFile!;

        ScriptDocument script = session.Editor.AddScript("본문 대본");
        DialogueNode dialogue = session.Editor.AddDialogueNode(file.Id, name: "EP01", scriptId: script.Id);
        ScriptLine line = session.Editor.InsertScriptLine(script.Id);
        session.Editor.SetScriptLineText(script.Id, line.Id, string.Empty, "첫 줄");

        // 연출 그래프에서 에피소드 노드를 클릭한 상태.
        session.Select(dialogue.Id);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.IsType<DialogueNode>(session.SelectedNode);

        // 그대로 무대 프리뷰 탭으로 넘어간다.
        var tabs = window.FindControl<TabControl>("MainTabs")!;
        tabs.SelectedItem = window.FindControl<TabItem>("StageTabItem")!;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 채널이 서고(연출 노드 + 공급 연결) 선택이 그리로 옮겨간다 — 이제 편집 가능한
        // 화면이라 무대 클릭이 조절창을 연다.
        PresentationNode presentation = Assert.IsType<PresentationNode>(session.SelectedNode);
        NodeLink supply = Assert.IsType<NodeLink>(
            NodeExportResolver.SupplyLinkOf(session.Project, dialogue.Id));
        Assert.Equal(presentation.Id, supply.SourceNodeId);

        // 두 번째 진입은 멱등이다 — 같은 연출 노드가 그대로다(노드가 늘지 않는다).
        tabs.SelectedIndex = 1;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        session.Select(dialogue.Id);
        tabs.SelectedItem = window.FindControl<TabItem>("StageTabItem")!;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(presentation.Id, ((PresentationNode)session.SelectedNode!).Id);
        Assert.Single(session.Project.EnumerateNodes().OfType<PresentationNode>());

        window.Close();
    });
}
