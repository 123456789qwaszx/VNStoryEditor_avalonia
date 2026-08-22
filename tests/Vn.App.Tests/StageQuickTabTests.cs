using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;
using Vn.Authoring.Script;

namespace Vn.App.Tests;

/// <summary>
/// 무대 조절창 [★ 자주 쓰는] 탭 (2026-08-22 소유자: "비개발자가 보기에 직접 연출을
/// 추가하는 건 부담이 커 … 일종의 핫키 느낌으로").
///
/// 담기 흐름이 여기서 닫힌다: <b>[편집]이 터미널 판 전체를 활성으로 바꾸고</b>(행마다
/// 글리프를 갈지 않는다 — 줄 높이가 흔들린다) · <b>커맨드 행을 그냥 클릭하면 인자가
/// 통째로(슬롯·duration 포함) 담기고</b> · <b>담긴 칩의 수치를 그 자리에서 조절한다</b>.
/// </summary>
public sealed class StageQuickTabTests
{
    private static PresentationResultCommand Command(
        string definitionId, params (string Key, string Value)[] args)
    {
        return new PresentationResultCommand(
            Identifier.PresentationCommand(),
            definitionId,
            args.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    }

    private static void LeftClick(Control target) => target.RaiseEvent(
        new PointerPressedEventArgs(
            target, new Pointer(0, PointerType.Mouse, true), target, default, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None));

    private static void RightClick(Control target) => target.RaiseEvent(
        new PointerPressedEventArgs(
            target, new Pointer(0, PointerType.Mouse, true), target, default, 0,
            new PointerPointProperties(RawInputModifiers.RightMouseButton, PointerUpdateKind.RightButtonPressed),
            KeyModifiers.None));

    /// <summary>대사 발행 + 연출 노드가 선 세션 하나 — 칩이 실제로 커맨드를 달 수 있는 최소 무대.</summary>
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

    private static StageSceneView SceneOf(
        AuthoringSession session,
        string nodeId,
        string lineId,
        params PresentationResultCommand[] setupCommands)
    {
        var view = new StageSceneView();
        view.Attach(session);
        view.Render(new MiniStagePreviewRequest(
            "테스트",
            MiniStageFold.Fold(PresentationCommandCatalog.Default, setupCommands, []),
            HasPresentation: true,
            SelectedLineId: lineId,
            SpeakerName: null,
            LineText: "첫 줄",
            EditContext: new StageEditContext(nodeId, lineId)));

        return view;
    }

    private static Button Chip(Control tab, string label) =>
        tab.GetLogicalDescendants().OfType<Button>().First(button => Equals(button.Content, label));

    /// <summary>터미널 판의 테두리 — 담기 모드에서만 색이 선다(두께는 늘 2px 그대로다).</summary>
    private static Border FrameOf(MiniStagePreview preview) =>
        preview.GetLogicalDescendants().OfType<PresentationScriptPanel>().Single()
            .GetLogicalDescendants().OfType<Border>()
            .First(border => border.BorderThickness.Left == 2);

    private static Border CommandRowOf(MiniStagePreview preview, string containsText) =>
        preview.GetLogicalDescendants().OfType<TextBlock>()
            .Where(text => (text.Text ?? "").Contains(containsText, StringComparison.Ordinal))
            .Select(text => text.FindLogicalAncestorOfType<Border>()!)
            .First();

    private static IReadOnlyList<PresentationCommandInstance> LineCommands(
        AuthoringSession session, string nodeId, string lineId) =>
        session.Project.FindPresentation(nodeId)!.FindBinding(lineId)?.Commands
            ?? (IReadOnlyList<PresentationCommandInstance>)Array.Empty<PresentationCommandInstance>();

    [Fact]
    public void 자주_쓰는_탭이_맨_앞에_서고_기본은_샷_셋뿐이다() => HeadlessUi.Run(() =>
    {
        (AuthoringSession session, string nodeId, string lineId) = Stage();
        StageSceneView view = SceneOf(session, nodeId, lineId);

        TabControl tabs = view.BuildStagePopoverProbe()
            .GetLogicalDescendants().OfType<TabControl>().Single();

        Assert.Equal(
            ["★ 자주 쓰는", "배경", "슬롯", "캐릭터", "오디오"],
            tabs.Items.OfType<TabItem>().Select(item => ((TextBlock)item.Header!).Text!).ToArray());

        // 기본 칩 = shot_zoom · shot_to · shot_reset (2026-08-22 소유자). 나머지는 사람이 담는다.
        Assert.Equal(
            ["shot.shot_zoom", "shot.shot_to", "shot.shot_reset"],
            session.Project.EffectiveQuickCommands.Select(chip => chip.DefinitionId).ToArray());
    });

    [Fact]
    public void 칩_하나가_클릭_하나로_라인에_붙는다() => HeadlessUi.Run(() =>
    {
        (AuthoringSession session, string nodeId, string lineId) = Stage();
        StageSceneView view = SceneOf(session, nodeId, lineId);

        Chip(view.BuildQuickTabProbe(null), "카메라 이동")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        PresentationCommandInstance added = Assert.Single(LineCommands(session, nodeId, lineId));
        Assert.Equal("shot.shot_to", added.DefinitionId);
        Assert.Equal("2.5u", added.Arguments["x"]);

        // 다른 커맨드는 나란히 선다 — 같은 커맨드였다면 값만 바뀐다.
        Chip(view.BuildQuickTabProbe(null), "줌 인")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(2, LineCommands(session, nodeId, lineId).Count);
    });

    [Fact]
    public void 담긴_슬롯이_없는_칩만_조절창의_선택_슬롯을_받는다() => HeadlessUi.Run(() =>
    {
        (AuthoringSession session, string nodeId, string lineId) = Stage();

        // 대상을 요구하는데 담긴 슬롯이 없는 칩 — 대상 없이 담긴 경우다.
        session.Project.QuickCommands =
        [
            new StageQuickCommand(
                "흔들기",
                "char_rig_staging.gesture",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["xAmp"] = "0.3u" })
        ];

        StageSceneView view = SceneOf(
            session, nodeId, lineId, Command("char_rig_cast.slot", ("slotKey", "c1")));

        // 슬롯이 안 골라져 있으면 회색 — 눌러서 무효한 커맨드가 나가지 않는다.
        Assert.False(Chip(view.BuildQuickTabProbe(null), "흔들기").IsEnabled);

        Chip(view.BuildQuickTabProbe("c1"), "흔들기").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("c1", Assert.Single(LineCommands(session, nodeId, lineId)).Arguments["slot"]);
    });

    [Fact]
    public void 편집이_터미널_판을_활성으로_바꾸고_행_클릭이_인자를_통째로_담는다() => HeadlessUi.Run(() =>
    {
        (AuthoringSession session, string nodeId, string lineId) = Stage();

        // 이미 원하는 값으로 맞춰 둔 커맨드 하나 — 슬롯도 duration도 여기 적혀 있다.
        PresentationResultCommand tuned = Command(
            "char_rig_staging.gesture",
            ("slot", "c1"), ("xAmp", "0.7u"), ("yAmp", "0u"), ("duration", "18fr"));

        var preview = new MiniStagePreview();
        var window = new Window { Width = 1200, Height = 800, Content = preview };
        window.Show();
        preview.Attach(session);

        preview.Show(new MiniStagePreviewRequest(
            "연출: 테스트",
            MiniStageFold.Fold(PresentationCommandCatalog.Default, [], []),
            HasPresentation: true,
            SelectedLineId: lineId,
            SpeakerName: null,
            LineText: "첫 줄",
            EditContext: new StageEditContext(nodeId, lineId),
            ScriptRows:
            [
                new PresentationScriptRow(
                    PresentationScriptRowKind.Command, lineId, tuned, "<<gesture c1 0.7u 0u 18fr>>")
            ]));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 평소 터미널은 고요하다 — 테두리는 자리만 잡고 색이 없다.
        Border frame = FrameOf(preview);
        Assert.Equal(new Avalonia.Thickness(2), frame.BorderThickness);
        Assert.Same(Brushes.Transparent, frame.BorderBrush);

        // [편집] = 판 전체가 활성으로 바뀐다. ⚠ 행의 글리프는 안 바뀐다 — 글리프가 바뀌면
        // 줄 높이가 흔들린다(2026-08-22 소유자).
        preview.Scene.SetQuickEditModeProbe(true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotSame(Brushes.Transparent, FrameOf(preview).BorderBrush);
        Assert.DoesNotContain(
            preview.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "★");

        // 행 자체가 단추다 — 그냥 클릭하면 담긴다.
        LeftClick(CommandRowOf(preview, "gesture"));

        // 인자가 통째로 담긴다 — 슬롯도 duration도 그대로 (소유자: "그대로 복사하는게 포인트").
        StageQuickCommand pinned = session.Project.EffectiveQuickCommands[^1];
        Assert.Equal("char_rig_staging.gesture", pinned.DefinitionId);
        Assert.Equal("c1", pinned.Arguments["slot"]);
        Assert.Equal("18fr", pinned.Arguments["duration"]);
        Assert.Equal("0.7u", pinned.Arguments["xAmp"]);

        // 같은 커맨드를 또 담으면 이름에 번호가 붙는다 — 값만 달리해 담는 것이 정상 쓰임이다.
        session.Editor.PinQuickCommand(new StageQuickCommand(
            pinned.DisplayName,
            pinned.DefinitionId,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["slot"] = "c2" }));
        Assert.Equal("제자리 몸짓", pinned.DisplayName);
    });

    /// <summary>
    /// 2026-08-22 소유자 보고 — 담기 모드로 둔 채 무대를 다시 클릭하면 "오직 자주쓰는
    /// 메뉴탭만 콘솔에 남은 이상한 상태"가 됐다.
    ///
    /// 원인은 라이트 디스미스를 끈 뒤 생긴 길이다: 무대 클릭이 팝업을 닫지 않고 곧장
    /// 조절창 열기로 오면서 <b>열린 팝업의 Child를 갈아 끼웠다.</b> 지금은 먼저 닫고 연다.
    /// (섞여 그려지는 것 자체는 Avalonia의 렌더 동작이라 여기서 볼 수 없다 — 여기서
    /// 지키는 것은 그것을 막는 규칙이다: 다시 연 판은 <b>새 판</b>이고 탭이 온전하며
    /// 담기 모드는 꺼진다.)
    /// </summary>
    [Fact]
    public void 조절창을_다시_열면_새_판이_서고_담기_모드가_꺼진다() => HeadlessUi.Run(() =>
    {
        (AuthoringSession session, string nodeId, string lineId) = Stage();
        StageSceneView view = SceneOf(session, nodeId, lineId);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Control first = view.OpenStageConsoleProbe()!;
        Assert.True(view.IsStageConsoleOpen);

        view.SetQuickEditModeProbe(true);
        Assert.True(view.IsQuickPinMode);

        // 무대를 다시 클릭 = 조절창 다시 열기.
        Control second = view.OpenStageConsoleProbe()!;

        Assert.NotSame(first, second);
        Assert.True(view.IsStageConsoleOpen);
        Assert.False(view.IsQuickPinMode); // 새 판은 평소 모드다 — 터미널 활성 표시도 내려간다
        Assert.Equal(
            5,
            second.GetLogicalDescendants().OfType<TabControl>().Single().Items.Count);

        window.Close();
    });

    /// <summary>
    /// 2026-08-22 소유자 보고 — "연출조작 콘솔에서 우클릭 닫기 기능이 안돼."
    /// 버블 핸들러라 판 안쪽 컨트롤(탭 머리·내용 컨테이너·글자 칸)이 먼저 삼켰다.
    /// 라이트 디스미스가 있던 시절에는 바깥 클릭이 닫아 줘서 구멍이 안 보였을 뿐이다.
    /// 지금은 터널로 잡으므로 <b>판 어디를 우클릭해도</b> 닫힌다.
    /// </summary>
    [Fact]
    public void 조절창_안쪽_어디를_우클릭해도_닫힌다() => HeadlessUi.Run(() =>
    {
        (AuthoringSession session, string nodeId, string lineId) = Stage();
        StageSceneView view = SceneOf(session, nodeId, lineId);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Control console = view.OpenStageConsoleProbe()!;
        Assert.True(view.IsStageConsoleOpen);

        // 탭 안쪽 = 판 대부분을 차지하는 자리. 여기서 안 닫히면 기능이 없는 것과 같다.
        TabControl tabs = console.GetLogicalDescendants().OfType<TabControl>().Single();

        // ⚠ 결함의 정체를 그대로 세운다: 안쪽 컨트롤이 우클릭을 <b>먼저 삼킨다</b>.
        // 버블 핸들러였다면 판까지 오지 못한다 — 터널이라야 삼킴보다 먼저 지난다.
        tabs.PointerPressed += (_, swallowed) => swallowed.Handled = true;

        RightClick(tabs);

        Assert.False(view.IsStageConsoleOpen);

        window.Close();
    });

    [Fact]
    public void 칩을_펴면_작업대와_같은_수치_조절이_선다() => HeadlessUi.Run(() =>
    {
        (AuthoringSession session, string nodeId, string lineId) = Stage();
        StageSceneView view = SceneOf(session, nodeId, lineId);
        view.SetQuickEditModeProbe(true);

        // ▸ 하나가 칩 하나 — 첫 칩(줌 인 = shot_zoom)을 편다.
        Control collapsed = view.BuildQuickTabProbe(null);
        Assert.Empty(collapsed.GetLogicalDescendants().OfType<Slider>());
        collapsed.GetLogicalDescendants().OfType<Button>()
            .First(button => Equals(button.Content, "▸"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Control tab = view.BuildQuickTabProbe(null);

        // zoom 슬라이더(카탈로그 선언 -10~20)가 칩이 담아 둔 1.4에 서 있다.
        Slider zoom = Assert.Single(
            tab.GetLogicalDescendants().OfType<Slider>(), slider => slider.Maximum == 20);
        Assert.Equal(1.5, zoom.Value); // 0.5 눈금에 붙는다
        // 시간 슬라이더도 함께 — duration은 프레임으로 선다(0.45s = 약 11fr).
        Assert.Contains(tab.GetLogicalDescendants().OfType<Slider>(), slider => slider.Maximum != 20);

        // 확정만 편집이다 — 값을 옮기고 키를 떼면 그때 칩에 쓰인다.
        zoom.Value = 2;
        zoom.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Right });

        Assert.Equal("2", session.Project.EffectiveQuickCommands[0].Arguments["zoom"]);
        // 손대지 않은 인자는 그대로다.
        Assert.Equal("0.45s", session.Project.EffectiveQuickCommands[0].Arguments["duration"]);
    });

    [Fact]
    public void 편집_중에는_이름을_고치고_빼기로_지운다() => HeadlessUi.Run(() =>
    {
        (AuthoringSession session, string nodeId, string lineId) = Stage();
        StageSceneView view = SceneOf(session, nodeId, lineId);
        view.SetQuickEditModeProbe(true);

        Control tab = view.BuildQuickTabProbe(null);
        var window = new Window { Width = 400, Height = 500, Content = tab };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 편집 중에는 칩이 [▸][이름 칸][✕] 줄이다 — 담기는 터미널 행 클릭이 진다.
        TextBox first = tab.GetLogicalDescendants().OfType<TextBox>().First();
        Assert.Equal("줌 인", first.Text);

        // 초점을 잃을 때 커밋한다 — 이름을 치다 터미널 ★를 눌러도 이름이 살아남는 길이다.
        first.Focus();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        first.Text = "확 당기기";

        Button remove = tab.GetLogicalDescendants().OfType<Button>()
            .First(button => Equals(button.Content, "✕"));
        remove.Focus();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("확 당기기", session.Project.EffectiveQuickCommands[0].DisplayName);

        int before = session.Project.EffectiveQuickCommands.Count;
        remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(before - 1, session.Project.EffectiveQuickCommands.Count);
        Assert.DoesNotContain(
            session.Project.EffectiveQuickCommands, chip => chip.DisplayName == "확 당기기");

        session.Editor.ResetQuickCommands();
        Assert.Equal(StageQuickCommands.Default.Count, session.Project.EffectiveQuickCommands.Count);
    });
}
