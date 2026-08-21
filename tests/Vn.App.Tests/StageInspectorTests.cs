using Avalonia.Controls;
using Avalonia.LogicalTree;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.App.Tests;

/// <summary>
/// 작업대(Inspector)가 커맨드 하나를 어떤 조작으로 펼치는가 (2026-08-21).
/// 여기서 지키는 것: <b>뎁스는 레벨 슬라이더</b>(소유자: "터미널 아래의 연출 편집기에서는
/// -10 ~ 10까지의 Level을 슬라이더로 직접 조절")이고, 이징 칸은 곡선 선택기다.
/// </summary>
public sealed class StageInspectorTests
{
    private static PresentationResultCommand Command(
        string definitionId, params (string Key, string Value)[] args)
    {
        return new PresentationResultCommand(
            Identifier.PresentationCommand(),
            definitionId,
            args.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    }

    /// <summary>편집 가능한 라인 화면 하나 — 작업대가 열리는 최소 조건이다.</summary>
    private static StageSceneView EditableView()
    {
        var view = new StageSceneView();
        view.Attach(new AuthoringSession());
        view.Render(new MiniStagePreviewRequest(
            "테스트",
            MiniStageFold.Fold(
                Vn.Authoring.Definition.PresentationCommandCatalog.Default, [], []),
            HasPresentation: true,
            SelectedLineId: "ln1",
            SpeakerName: null,
            LineText: "대사",
            EditContext: new StageEditContext("nd_pres", "ln1")));

        return view;
    }

    [Fact]
    public void 뎁스는_레벨_슬라이더_하나로_조절하고_라벨은_그_눈금이다() => HeadlessUi.Run(() =>
    {
        StageSceneView view = EditableView();

        // 라벨이 적혀 있으면 <b>그 라벨의 눈금</b>에 선다 (2026-08-21 런타임 개통:
        // 라벨은 커브 위의 점 이름이 됐다 — close = 레벨 20, 소유자 상향).
        Control inspector = Assert.IsAssignableFrom<Control>(
            view.BuildInspectorContent(Command(
                "char_rig_depth.size", ("slot", "c1"), ("depth", "close"), ("duration", "10fr"))));

        Slider[] sliders = inspector.GetLogicalDescendants().OfType<Slider>().ToArray();
        Slider level = Assert.Single(sliders, slider => slider.Minimum == -10);
        Assert.Equal(20, level.Maximum); // 커브 설계 구간 [0,20] (2026-08-21 확장)
        Assert.Equal(20, level.Value); // close = 눈금 20

        // 알 수 없는 토큰이면 가운데에 서고 원문을 보인다(폴드도 거부하는 값이다).
        Control unknown = Assert.IsAssignableFrom<Control>(
            view.BuildInspectorContent(Command(
                "char_rig_depth.size", ("slot", "c1"), ("depth", "헛토큰"), ("duration", "10fr"))));
        Assert.Equal(
            14, // 가운데 = mid 눈금 (2026-08-21 상향으로 14)
            Assert.Single(
                unknown.GetLogicalDescendants().OfType<Slider>(),
                slider => slider.Minimum == -10).Value);

        // 이미 레벨이 적혀 있으면 그 자리에 선다.
        Control written = Assert.IsAssignableFrom<Control>(
            view.BuildInspectorContent(Command(
                "char_rig_depth.size", ("slot", "c1"), ("depth", "-3.5"), ("duration", "10fr"))));
        Slider atLevel = Assert.Single(
            written.GetLogicalDescendants().OfType<Slider>(), slider => slider.Minimum == -10);
        Assert.Equal(-3.5, atLevel.Value, 3);
    });

    [Fact]
    public void 이징_칸은_곡선_선택기로_선다() => HeadlessUi.Run(() =>
    {
        // 2026-08-21 런타임이 place·size에도 이징을 열었다 — 이동과 같은 편집이어야 한다.
        StageSceneView view = EditableView();

        Control inspector = Assert.IsAssignableFrom<Control>(
            view.BuildInspectorContent(Command(
                "char_rig_placement.place_left",
                ("slot", "c1"), ("focus", "face"), ("duration", "12fr"), ("ease", "OutBack"))));

        // 곡선 편집 단추가 있다 = 커스텀 곡선(@이름)까지 갈 수 있는 그 선택기다.
        Assert.Contains(
            inspector.GetLogicalDescendants().OfType<Button>(),
            button => (button.Content as string) == "곡선 편집…");
    });

    [Fact]
    public void 넛지의_거리는_u_토큰_슬라이더로_선다() => HeadlessUi.Run(() =>
    {
        // 2026-08-21 소유자: "nudge의 경우 … distance slide를 추가".
        // ⚠ 값이 "3u" 같은 토큰이라 일반 숫자 슬라이더로는 안 된다 — 읽을 때도
        // 쓸 때도 u를 알아야 한다.
        StageSceneView view = EditableView();

        Control inspector = Assert.IsAssignableFrom<Control>(
            view.BuildInspectorContent(Command(
                "char_rig_entrance.left",
                ("slot", "c1"), ("distance", "3u"), ("duration", "12fr"))));

        Slider[] sliders = inspector.GetLogicalDescendants().OfType<Slider>().ToArray();

        // 거리 슬라이더 — 카탈로그가 선언한 범위(0~6)이고 적힌 3u 자리에 선다.
        Slider distance = Assert.Single(sliders, slider => slider.Maximum == 6);
        Assert.Equal(0, distance.Minimum);
        Assert.Equal(3, distance.Value, 3);

        // 시간 슬라이더도 함께 — 넛지는 원래부터 시간을 갖고 있었다.
        Assert.Contains(sliders, slider => Math.Abs(slider.Value - 12) < 0.001);

        // 거리 라벨은 u로 읽힌다(숫자만 두면 무엇의 3인지 화면이 침묵한다).
        Assert.Contains(
            inspector.GetLogicalDescendants().OfType<TextBlock>(),
            text => (text.Text ?? "").Contains("3u", StringComparison.Ordinal));
    });

    [Fact]
    public void 넛지_거리_슬라이더는_u_토큰으로_저장한다() => HeadlessUi.Run(() =>
    {
        // 손을 뗄 때 한 번 저장하는 규칙 그대로이되, 쓰는 값이 "2.5u"여야 한다 —
        // 맨 숫자를 쓰면 런타임 파서가 다른 것을 읽는다.
        var session = new AuthoringSession();
        StoryFile file = session.ActiveFile!;
        PresentationNode presentation = session.Editor.AddPresentationNode(file.Id, name: "연출");
        PresentationCommandInstance command = session.Editor.AddPresentationCommand(
            presentation.Id, "ln1", "char_rig_entrance.left",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["slot"] = "c1", ["distance"] = "3u", ["duration"] = "12fr"
            });

        session.Editor.SetPresentationCommandArgument(presentation.Id, command.Id, "distance", "2.5u");

        Assert.Equal("2.5u", session.Project.FindPresentation(presentation.Id)!
            .FindBinding("ln1")!.Commands[0].Arguments["distance"]);
    });
}
