using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.App.Tests;

/// <summary>
/// <b>무대 탭에서 넣은 조작이 그 자리에서 무대에 서는가</b> (2026-08-26 소유자 보고:
/// "duration을 0으로 설정했는데도 즉각적으로 반영이 안되고 다른 에피소드를 클릭했다
/// 돌아오면 그제서야 반영이 제대로 되는 버그").
///
/// 조절창의 조작은 <see cref="PresentationStageActions"/>를 지나 편집이 되고, 편집은
/// 세션 Changed(PresentationContent)로 셸에 닿아 무대 요청이 새로 접혀야 한다 —
/// 그 마지막 고리가 끊기면 화면은 옛 요청을 들고 있고, 씬을 다시 골라야만 보인다.
/// </summary>
public sealed class StageLiveEditTests
{
    private const int StageTab = 2;

    [Fact]
    public void 무대에서_더한_조작은_씬을_다시_고르지_않아도_무대에_선다() => HeadlessUi.Run(() =>
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        AuthoringSession session = window.SessionProbe;

        DialogueNode dialogue = session.Editor.AddDialogueNode(
            session.ActiveFile!.Id, name: "본문", scriptId: session.Editor.AddScript("본문 대본").Id);
        ScriptLine line = session.Editor.InsertScriptLine(dialogue.ScriptId!);
        session.Editor.SetScriptLineText(dialogue.ScriptId!, line.Id, "라루", "첫 줄");

        // 무대 탭 진입 = 씬 선택 (2026-08-22) — 고른 대사의 연출 채널이 선다.
        session.Select(dialogue.Id);
        window.FindControl<TabControl>("MainTabs")!.SelectedIndex = StageTab;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        PresentationNode presentation = Assert.IsType<PresentationNode>(session.SelectedNode);

        var preview = window.FindControl<MiniStagePreview>("StagePreview")!;
        Canvas canvas = preview.GetVisualDescendants().OfType<StageSceneView>().Single()
            .GetLogicalDescendants().OfType<Canvas>().First();

        int before = Positioned(canvas);

        PresentationCommandCatalog catalog = AvailablePresentationCommandResolver
            .Resolve(session.Project, presentation.Id, session.Definition)
            .Catalog;

        // 조절창이 하는 그 호출들 — 슬롯을 세우고(Setup) 캐스팅하고 duration 0으로 등장.
        PresentationStageActions.ApplyToSetup(
            session.Editor, catalog, presentation.Id, "slot", Args(("slotKey", "c1")));
        PresentationStageActions.ApplyToSetup(
            session.Editor, catalog, presentation.Id, "cast",
            Args(("slot", "c1"), ("characterKey", "laru")));
        PresentationStageActions.Apply(
            session.Editor, catalog, presentation.Id, line.Id, "fade_in",
            Args(("slot", "c1"), ("duration", "0fr")));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 씬을 다시 고르지 않았다 — 그런데도 무대에 슬롯이 서 있어야 한다.
        Assert.True(
            Positioned(canvas) > before,
            "무대 조작이 즉시 반영되지 않았다 — 씬을 다시 골라야만 보인다");

        window.Close();
    });

    private static int Positioned(Canvas canvas) => canvas.Children
        .Count(control => !double.IsNaN(Canvas.GetLeft(control)));

    private static IReadOnlyDictionary<string, string> Args(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
