using Avalonia.Controls;
using Avalonia.LogicalTree;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;
using Vn.Authoring.Script;

namespace Vn.App.Tests;

/// <summary>
/// 무대 조절창은 <b>오른쪽 붙박이 기둥</b>이다 (2026-08-22 소유자: "화면을 클릭했을 때
/// 나오는 콘솔이 연출 프리뷰 오른쪽에 상시 표시되면 좋겠어 — 챕터그래프와
/// 연출그래프에서처럼").
///
/// 여기서 지키는 것 셋: <b>클릭 없이 서 있다</b> · <b>라인이 바뀌면 따라 그려진다</b>
/// (팝업 시절에는 다시 열 때 새로 지어져 이 문제가 없었다) · <b>잠긴 화면에서는 이유를
/// 말한다</b>(예전에는 팝업이 아예 안 열려 그 상태를 말할 자리가 없었다).
/// </summary>
public sealed class StageConsoleDockTests
{
    private static (AuthoringSession Session, string NodeId, string LineId) Stage()
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

        return (session, presentation.Id, line.Id);
    }

    private static MiniStagePreviewRequest Request(
        string nodeId,
        string lineId,
        string? disabledReason = null,
        params PresentationResultCommand[] setupCommands) =>
        new(
            "연출: 테스트",
            MiniStageFold.Fold(PresentationCommandCatalog.Default, setupCommands, []),
            HasPresentation: true,
            SelectedLineId: lineId,
            SpeakerName: null,
            LineText: "첫 줄",
            EditContext: new StageEditContext(nodeId, lineId, disabledReason));

    private static ContentControl ConsoleHostOf(MiniStagePreview preview) =>
        preview.FindControl<ContentControl>("ConsoleHost")!;

    private static (MiniStagePreview Preview, Window Window) Shown(AuthoringSession session)
    {
        var preview = new MiniStagePreview();
        var window = new Window { Width = 1400, Height = 800, Content = preview };
        window.Show();
        preview.Attach(session);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (preview, window);
    }

    [Fact]
    public void 조절창이_무대_오른쪽에_클릭_없이_서_있다() => HeadlessUi.Run(() =>
    {
        (AuthoringSession session, string nodeId, string lineId) = Stage();
        (MiniStagePreview preview, Window window) = Shown(session);

        preview.Show(Request(nodeId, lineId));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 무대를 한 번도 안 눌렀는데 다섯 탭이 서 있다.
        TabControl tabs = ConsoleHostOf(preview)
            .GetLogicalDescendants().OfType<TabControl>().Single();

        Assert.Equal(
            ["★ 자주 쓰는", "배경", "슬롯", "캐릭터", "오디오"],
            tabs.Items.OfType<TabItem>().Select(item => ((TextBlock)item.Header!).Text!).ToArray());

        window.Close();
    });

    [Fact]
    public void 라인이_바뀌면_조절창의_슬롯도_따라_바뀐다() => HeadlessUi.Run(() =>
    {
        (AuthoringSession session, string nodeId, string lineId) = Stage();
        (MiniStagePreview preview, Window window) = Shown(session);

        preview.Show(Request(nodeId, lineId));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 슬롯이 없는 라인 — 슬롯 콤보 자리에 안내가 선다.
        Assert.Contains(
            ConsoleHostOf(preview).GetLogicalDescendants().OfType<TextBlock>(),
            text => (text.Text ?? "").Contains("슬롯이 없습니다", StringComparison.Ordinal));

        // 슬롯이 선 라인으로 넘어가면 판이 스스로 다시 그려진다 — 팝업이 아니므로
        // 다시 열어 주는 사람이 없다. 무대가 그릴 때 함께 그린다.
        preview.Show(Request(
            nodeId,
            lineId,
            null,
            new PresentationResultCommand(
                Identifier.PresentationCommand(),
                "char_rig_cast.slot",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["slotKey"] = "c1" })));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ComboBox slots = ConsoleHostOf(preview)
            .GetLogicalDescendants().OfType<ComboBox>()
            .First(combo => combo.ItemsSource is IEnumerable<string>);

        Assert.Contains(
            slots.ItemsSource!.Cast<string>(),
            item => item.StartsWith("c1", StringComparison.Ordinal));

        window.Close();
    });

    [Fact]
    public void 잠긴_화면에서는_조절창이_이유를_말한다() => HeadlessUi.Run(() =>
    {
        (AuthoringSession session, string nodeId, string lineId) = Stage();
        (MiniStagePreview preview, Window window) = Shown(session);

        preview.Show(Request(nodeId, lineId, "공급된 발행 결과를 보고 있습니다."));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 탭은 안 선다 — 누를 것이 없는 판을 그리면 눌러도 안 듣는 단추가 가득 찬다.
        Assert.Empty(ConsoleHostOf(preview).GetLogicalDescendants().OfType<TabControl>());
        Assert.Contains(
            ConsoleHostOf(preview).GetLogicalDescendants().OfType<TextBlock>(),
            text => (text.Text ?? "").Contains("발행 결과", StringComparison.Ordinal));

        window.Close();
    });
}
