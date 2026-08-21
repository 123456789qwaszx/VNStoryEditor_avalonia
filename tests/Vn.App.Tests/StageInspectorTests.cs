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
    public void 뎁스는_레벨_슬라이더로_직접_조절한다() => HeadlessUi.Run(() =>
    {
        StageSceneView view = EditableView();

        // 프리셋이 적혀 있어도 작업대는 레벨 슬라이더를 낸다 — 끌면 수치가 된다.
        Control inspector = Assert.IsAssignableFrom<Control>(
            view.BuildInspectorContent(Command(
                "char_rig_depth.size", ("slot", "c1"), ("depth", "close"), ("duration", "10fr"))));

        Slider[] sliders = inspector.GetLogicalDescendants().OfType<Slider>().ToArray();
        Slider level = Assert.Single(sliders, slider => slider.Minimum == -10);
        Assert.Equal(10, level.Maximum);
        Assert.Equal(5, level.Value); // 프리셋이면 가운데에서 출발한다(끌기 전엔 안 쓴다)

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
}
