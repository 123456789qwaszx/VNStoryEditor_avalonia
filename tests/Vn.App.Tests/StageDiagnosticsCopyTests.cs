using Avalonia.Controls;
using Avalonia.LogicalTree;
using Vn.App.Views;
using Vn.Authoring.Flow;

namespace Vn.App.Tests;

/// <summary>
/// 무대 진단(알림·경고·미반영 목록)을 사람이 <b>가져갈 수 있어야 한다</b>
/// (2026-08-21 소유자: "이런 문구를 복사할 수 있도록 해줄래?").
/// 줄 하나는 드래그(SelectableTextBlock), 전부는 [진단 복사]가 들고 간다 —
/// 챕터 그래프 검증 보고의 [보고 복사]와 같은 문법이다.
/// </summary>
public sealed class StageDiagnosticsCopyTests
{
    private static MiniStagePreview ShowWithDiagnostics()
    {
        var preview = new MiniStagePreview();
        var window = new Window { Width = 1200, Height = 800, Content = preview };
        window.Show();

        MiniStageState state = MiniStageState.Empty with
        {
            Unhandled =
            [
                new MiniStageUnhandled("In_5m8shih4", "gesture", FoldedButNotDrawn: true)
            ]
        };

        preview.Show(new MiniStagePreviewRequest(
            "연출: 테스트",
            state,
            HasPresentation: true,
            SelectedLineId: "In_5m8shih4",
            SpeakerName: null,
            LineText: "대사",
            Notice: "tuning 파일이 없습니다: presets/role-anchor.json — show의 캐릭터별 앵커가 기본값으로 접힙니다."));

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return preview;
    }

    [Fact]
    public void 알림과_미반영_줄은_드래그로_집어_갈_수_있다() => HeadlessUi.Run(() =>
    {
        MiniStagePreview preview = ShowWithDiagnostics();

        // 평범한 TextBlock이면 마우스로 긁어도 아무것도 안 잡힌다.
        var notices = preview.FindControl<StackPanel>("NoticeHost")!;
        Assert.NotEmpty(notices.Children);
        Assert.All(notices.Children, child => Assert.IsType<SelectableTextBlock>(child));

        var unhandled = preview.FindControl<StackPanel>("UnhandledHost")!;
        Assert.NotEmpty(unhandled.Children);
        Assert.All(unhandled.Children, child => Assert.IsType<SelectableTextBlock>(child));
    });

    [Fact]
    public void 진단_복사는_뜬_줄을_그대로_모으고_접힌_상세도_담는다() => HeadlessUi.Run(() =>
    {
        MiniStagePreview preview = ShowWithDiagnostics();

        // 상세 목록은 뱃지를 눌러야 펼쳐진다 — 접힌 채로도 복사에는 담겨야 한다.
        Assert.False(preview.FindControl<StackPanel>("UnhandledHost")!.IsVisible);

        IReadOnlyList<string> lines = preview.DiagnosticsText();

        Assert.Contains(lines, line => line.Contains("연출: 테스트", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("미표시 1", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("role-anchor.json", StringComparison.Ordinal));
        Assert.Contains(lines, line =>
            line.Contains("gesture", StringComparison.Ordinal) &&
            line.Contains("접힘·미표시", StringComparison.Ordinal));

        // 복사할 것이 있으니 단추가 선다.
        Assert.True(preview.FindControl<Button>("CopyDiagnosticsButton")!.IsVisible);
    });

    [Fact]
    public void 진단이_없으면_복사_단추도_서지_않는다() => HeadlessUi.Run(() =>
    {
        var preview = new MiniStagePreview();
        var window = new Window { Width = 1200, Height = 800, Content = preview };
        window.Show();

        preview.Show(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Empty(preview.DiagnosticsText());
        Assert.False(preview.FindControl<Button>("CopyDiagnosticsButton")!.IsVisible);
    });
}
