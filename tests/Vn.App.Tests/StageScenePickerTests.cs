using Avalonia.Controls;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.App.Tests;

/// <summary>
/// 무대 프리뷰의 씬 선택기 (2026-08-21 소유자 — "미리 다 해둔 다음에 무대 프리뷰측에서
/// 뭘 할지 고르도록"). 그래프에서 발행·배선하던 길이 사라지고 이 콤보가 유일한 입구다:
/// 고르면 연출 채널이 자동으로 선다(EnsurePresentationChannel — Vn.Authoring 테스트가 진다).
/// </summary>
public sealed class StageScenePickerTests
{
    [Fact]
    public void 선택기는_에피소드를_앞에_커스텀_씬을_뒤에_나열한다() => HeadlessUi.Run(() =>
    {
        var (preview, session) = ShowPreview();
        string fileId = session.EnsureChapterBoard("ch05");

        session.Editor.AddDialogueNode(fileId, name: "커스텀A");
        DialogueNode episode = session.Editor.AddDialogueNode(fileId, name: "EP00");
        episode.ExcelEpisodeId = "EP00";

        var combo = preview.FindControl<ComboBox>("SceneCombo")!;
        combo.IsDropDownOpen = true; // 열 때마다 목록을 다시 짓는다
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var labels = ((IEnumerable<string>)combo.ItemsSource!).ToList();
        Assert.Contains("📄 EP00", labels);
        Assert.Contains("✎ 커스텀A", labels);
        Assert.True(
            labels.IndexOf("📄 EP00") < labels.IndexOf("✎ 커스텀A"),
            "에피소드(📄)가 커스텀 씬(✎)보다 앞이어야 한다");
    });

    [Fact]
    public void 씬을_고르면_대사_노드_Id로_알리고_채널이_한_번에_선다() => HeadlessUi.Run(() =>
    {
        var (preview, session) = ShowPreview();
        string fileId = session.EnsureChapterBoard("ch05");

        DialogueNode scene = session.Editor.AddDialogueNode(fileId, name: "커스텀A");
        ScriptLine line = session.Editor.InsertScriptLine(scene.ScriptId!);
        session.Editor.SetScriptLineText(scene.ScriptId!, line.Id, "라루", "한 줄");

        string? chosen = null;
        preview.SceneChosen += id => chosen = id;

        var combo = preview.FindControl<ComboBox>("SceneCombo")!;
        combo.IsDropDownOpen = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var scenes = (List<DialogueNode>)combo.Tag!;
        combo.SelectedIndex = scenes.FindIndex(node => node.Id == scene.Id);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(scene.Id, chosen);

        // MainWindow가 하는 그 호출 — 채널이 서고 연출 노드가 열릴 준비가 된다.
        PresentationChannelOutcome outcome = session.Editor.EnsurePresentationChannel(chosen!);
        Assert.True(outcome.Ready, outcome.Problem);

        // 바깥 선택이 바뀌면 콤보가 따라온다 — 이벤트는 다시 안 쏜다.
        chosen = null;
        preview.SetCurrentScene(scene.Id);
        Assert.Null(chosen);
    });

    private static (MiniStagePreview Preview, AuthoringSession Session) ShowPreview()
    {
        var session = new AuthoringSession();
        var preview = new MiniStagePreview();
        var window = new Window { Width = 1200, Height = 800, Content = preview };
        window.Show();
        preview.Attach(session);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return (preview, session);
    }
}
