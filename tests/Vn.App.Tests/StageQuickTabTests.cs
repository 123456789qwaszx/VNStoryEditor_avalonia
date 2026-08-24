using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
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

    /// <summary>칩 단추 하나 — 묶음이면 이름 옆에 <c>·N</c> 배지가 붙으므로 이름만 본다.</summary>
    private static Button Chip(Control tab, string label) =>
        tab.GetLogicalDescendants().OfType<Button>().First(button =>
            Equals(button.Content, label) ||
            (button.Content as Panel)?.Children.OfType<TextBlock>()
                .Any(text => string.Equals(text.Text, label, StringComparison.Ordinal)) == true);

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
            session.Project.EffectiveQuickCommands.Select(chip => chip.Steps[0].DefinitionId).ToArray());
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
            StageQuickCommand.Single(
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
        Assert.Equal("char_rig_staging.gesture", pinned.Steps[0].DefinitionId);
        Assert.Equal("c1", pinned.Steps[0].Arguments["slot"]);
        Assert.Equal("18fr", pinned.Steps[0].Arguments["duration"]);
        Assert.Equal("0.7u", pinned.Steps[0].Arguments["xAmp"]);

        // 같은 커맨드를 또 담으면 이름에 번호가 붙는다 — 값만 달리해 담는 것이 정상 쓰임이다.
        session.Editor.PinQuickCommand(StageQuickCommand.Single(
            pinned.DisplayName,
            pinned.Steps[0].DefinitionId,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["slot"] = "c2" }));
        Assert.Equal("제자리 몸짓", pinned.DisplayName);
    });


    // ── 묶음 칩 (2026-08-24 소유자: "여러개의 커맨드 단위로 커스텀") ──────────
    //
    // 담기의 규칙은 하나다: <b>펼친 칩이 있으면 거기 이어 붙고, 없으면 새 칩이다.</b>
    // 입구가 둘(터미널 행 · [＋ 이 라인 통째로])이어도 규칙은 그 하나라, 안내 줄이
    // "이번 클릭이 어디로 가는지"를 언제나 말할 수 있다.

    /// <summary>터미널이 달린 프리뷰 하나 — 담기 흐름은 여기서만 끝까지 돈다.</summary>
    private static MiniStagePreview PreviewWith(
        AuthoringSession session,
        string nodeId,
        string lineId,
        params PresentationResultCommand[] lineCommands)
    {
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
            ScriptRows: lineCommands
                .Select(command => new PresentationScriptRow(
                    PresentationScriptRowKind.Command,
                    lineId,
                    command,
                    $"<<{command.DefinitionId}>>"))
                .ToArray()));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return preview;
    }

    [Fact]
    public void 펼친_칩이_터미널_클릭을_이어받아_묶음이_된다() => HeadlessUi.Run(() =>
    {
        (AuthoringSession session, string nodeId, string lineId) = Stage();

        MiniStagePreview preview = PreviewWith(session, nodeId, lineId, Command(
            "char_rig_staging.gesture", ("slot", "c1"), ("xAmp", "0.7u"), ("duration", "18fr")));

        int before = session.Project.EffectiveQuickCommands.Count;

        preview.Scene.SetQuickEditModeProbe(true);
        preview.Scene.ExpandQuickChipProbe(0); // 첫 칩(줌 인)을 펴 둔다 = 담을 대상
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        LeftClick(CommandRowOf(preview, "gesture"));

        // 새 칩이 생기지 않는다 — 펼친 칩의 뒤에 단계로 붙는다.
        Assert.Equal(before, session.Project.EffectiveQuickCommands.Count);

        StageQuickCommand grown = session.Project.EffectiveQuickCommands[0];
        Assert.Equal(2, grown.Steps.Count);
        Assert.Equal("shot.shot_zoom", grown.Steps[0].DefinitionId);
        Assert.Equal("char_rig_staging.gesture", grown.Steps[1].DefinitionId);
        // 인자는 통째로 온다 — 한 개짜리 담기와 같은 규칙이다.
        Assert.Equal("18fr", grown.Steps[1].Arguments["duration"]);

        // 접으면 다시 새 칩으로 간다.
        preview.Scene.ExpandQuickChipProbe(null);
        LeftClick(CommandRowOf(preview, "gesture"));
        Assert.Equal(before + 1, session.Project.EffectiveQuickCommands.Count);
    });

    private static Button ButtonStarting(Control host, string prefix) =>
        host.GetLogicalDescendants().OfType<Button>().First(button =>
            (button.Content as string)?.StartsWith(prefix, StringComparison.Ordinal) == true);

    [Fact]
    public void 묶음_만들기가_빈_그릇을_세우고_담기를_시작한다() => HeadlessUi.Run(() =>
    {
        // 담기의 출발점이 그릇이다 — [＋ 묶음]이 빈 칩을 세우고, 편집을 켜고, 그 칩을
        // 펴 둔다(= 담을 대상). "만들기 → 고르기"가 한 흐름으로 이어져야 한다.
        (AuthoringSession session, string nodeId, string lineId) = Stage();

        MiniStagePreview preview = PreviewWith(session, nodeId, lineId, Command(
            "char_rig_staging.gesture", ("slot", "c1"), ("xAmp", "0.7u")));

        int before = session.Project.EffectiveQuickCommands.Count;

        ButtonStarting(preview.Scene.BuildQuickTabProbe(null), "＋ 묶음")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(before + 1, session.Project.EffectiveQuickCommands.Count);
        Assert.Empty(session.Project.EffectiveQuickCommands[^1].Steps);

        // 편집이 함께 켜지고, 만든 그릇이 담을 대상이다 — 바로 골라 담을 수 있다.
        Assert.True(preview.Scene.IsQuickPinMode);
        Assert.Equal(before, preview.Scene.QuickPinTarget);

        LeftClick(CommandRowOf(preview, "gesture"));

        StageQuickStep step = Assert.Single(session.Project.EffectiveQuickCommands[^1].Steps);
        Assert.Equal("char_rig_staging.gesture", step.DefinitionId);
        Assert.Equal("0.7u", step.Arguments["xAmp"]);
    });

    [Fact]
    public void 이_라인_전부는_펼친_그릇에_순서대로_담는다() => HeadlessUi.Run(() =>
    {
        // 편의 기능이지 담기의 정식 경로가 아니다 — 그래서 <b>펼친 칩 안에</b> 선다.
        (AuthoringSession session, string nodeId, string lineId) = Stage();

        MiniStagePreview preview = PreviewWith(
            session, nodeId, lineId,
            Command("char_rig_presentation.fade_out", ("slot", "c1")),
            Command("common_control.pause", ("seconds", "0.2")));

        ButtonStarting(preview.Scene.BuildQuickTabProbe(null), "＋ 묶음")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ButtonStarting(preview.Scene.BuildQuickTabProbe(null), "＋ 이 라인 전부")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        StageQuickCommand pinned = session.Project.EffectiveQuickCommands[^1];

        Assert.Equal(
            ["char_rig_presentation.fade_out", "common_control.pause"],
            pinned.Steps.Select(step => step.DefinitionId).ToArray());
        // 만든 그릇에 담긴다 — 새 칩이 또 생기지 않는다.
        Assert.Equal(ProjectEditor.DefaultQuickBundleName, pinned.DisplayName);
    });

    [Fact]
    public void 커맨드와_묶음이_구역으로_갈린다() => HeadlessUi.Run(() =>
    {
        // 소유자: "칩커맨드와 단일 커맨드가 구별되도록 둘의 영역을 구분."
        (AuthoringSession session, string nodeId, string lineId) = Stage();
        StageSceneView view = SceneOf(session, nodeId, lineId);

        // 기본은 한 단계짜리 셋뿐 — [묶음] 구역은 아예 서지 않는다(빈 제목은 자리만 먹는다).
        string[] Headings(Control tab) => tab.GetLogicalDescendants().OfType<TextBlock>()
            .Select(text => text.Text ?? string.Empty)
            .Where(text => text is "커맨드" or "묶음")
            .ToArray();

        Assert.Equal(["커맨드"], Headings(view.BuildQuickTabProbe(null)));

        session.Editor.PinQuickCommand(new StageQuickCommand("퇴장 한 벌",
        [
            new StageQuickStep("char_rig_presentation.fade_out",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["slot"] = "c1" }),
            new StageQuickStep("common_control.pause",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["seconds"] = "0.2" })
        ]));

        Assert.Equal(["커맨드", "묶음"], Headings(view.BuildQuickTabProbe(null)));
    });

    [Fact]
    public void 빈_묶음은_평소_모드에_안_보이고_완료가_걷는다() => HeadlessUi.Run(() =>
    {
        (AuthoringSession session, string nodeId, string lineId) = Stage();
        StageSceneView view = SceneOf(session, nodeId, lineId);

        int before = session.Project.EffectiveQuickCommands.Count;

        ButtonStarting(view.BuildQuickTabProbe(null), "＋ 묶음")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        // 편집 중에는 담는 그릇으로 서 있다.
        Assert.Contains(
            view.BuildQuickTabProbe(null).GetLogicalDescendants().OfType<TextBox>(),
            box => box.Text == ProjectEditor.DefaultQuickBundleName);

        // [완료] = 편집 끄기. 아무것도 안 담았으므로 그릇이 걷힌다.
        ButtonStarting(view.BuildQuickTabProbe(null), "완료")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(before, session.Project.EffectiveQuickCommands.Count);
        Assert.False(view.IsQuickPinMode);
    });

    [Fact]
    public void 묶음_칩은_클릭_하나로_전부_붙고_되돌리기_한_번에_사라진다() => HeadlessUi.Run(() =>
    {
        (AuthoringSession session, string nodeId, string lineId) = Stage();

        session.Project.QuickCommands =
        [
            new StageQuickCommand("퇴장 한 벌",
            [
                new StageQuickStep("char_rig_presentation.fade_out",
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["slot"] = "c1" }),
                new StageQuickStep("common_control.pause",
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["seconds"] = "0.2" })
            ])
        ];

        StageSceneView view = SceneOf(session, nodeId, lineId);

        Chip(view.BuildQuickTabProbe(null), "퇴장 한 벌")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(
            ["char_rig_presentation.fade_out", "common_control.pause"],
            LineCommands(session, nodeId, lineId).Select(command => command.DefinitionId).ToArray());

        // ⛔ 단추 하나를 누른 것은 조작 하나다 — Ctrl+Z 한 번이 두 커맨드를 함께 원복한다.
        session.Editor.Undo();
        Assert.Empty(LineCommands(session, nodeId, lineId));
    });

    [Fact]
    public void 한_단계라도_못_내면_칩이_회색으로_서고_이유를_말한다() => HeadlessUi.Run(() =>
    {
        // ⛔ 나머지만 조용히 붙이면 사람은 다 붙은 줄 안다 — 묶음은 전부 아니면 전무다.
        (AuthoringSession session, string nodeId, string lineId) = Stage();

        session.Project.QuickCommands =
        [
            new StageQuickCommand("반만 되는 묶음",
            [
                new StageQuickStep("shot.shot_reset",
                    new Dictionary<string, string>(StringComparer.Ordinal)),
                new StageQuickStep("없는.커맨드",
                    new Dictionary<string, string>(StringComparer.Ordinal))
            ])
        ];

        StageSceneView view = SceneOf(session, nodeId, lineId);
        Button chip = Chip(view.BuildQuickTabProbe(null), "반만 되는 묶음");

        Assert.False(chip.IsEnabled);
        Assert.Contains("2번째 단계", (string)ToolTip.GetTip(chip)!, StringComparison.Ordinal);

        chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(LineCommands(session, nodeId, lineId));
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

        Assert.Equal("2", session.Project.EffectiveQuickCommands[0].Steps[0].Arguments["zoom"]);
        // 손대지 않은 인자는 그대로다.
        Assert.Equal("0.45s", session.Project.EffectiveQuickCommands[0].Steps[0].Arguments["duration"]);
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
