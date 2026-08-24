using Avalonia.Controls;
using Avalonia.LogicalTree;
using Vn.App.Views;
using Vn.Authoring.Flow;

namespace Vn.App.Tests;

/// <summary>
/// 무대 진단(알림·경고·미반영 목록)이 <b>화면에 서고 사람이 가져갈 수 있어야 한다</b>
/// (2026-08-21 소유자: "이런 문구를 복사할 수 있도록 해줄래?").
///
/// ⚠ <b>[진단 복사] 단추는 2026-08-24에 걷혔다</b> (소유자) — 늘 자리를 차지할 만큼 자주
/// 쓰는 일이 아니었다. 남은 통로는 <b>줄 드래그</b>이고, 그래서 알림·미반영 줄이
/// 평범한 TextBlock이 아니라 <see cref="SelectableTextBlock"/>이라는 사실이 이제
/// 복사 기능 전부다 — 이 파일의 첫 고정이 그것을 진다.
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
        // ⛔ 단추가 걷힌 뒤 <b>유일한 복사 통로</b>다 — 평범한 TextBlock이면 마우스로
        //    긁어도 아무것도 안 잡히고, 그러면 "붙여 달라"는 부탁에 답할 길이 없다.
        MiniStagePreview preview = ShowWithDiagnostics();

        var notices = preview.FindControl<StackPanel>("NoticeHost")!;
        Assert.NotEmpty(notices.Children);
        Assert.All(notices.Children, child => Assert.IsType<SelectableTextBlock>(child));

        var unhandled = preview.FindControl<StackPanel>("UnhandledHost")!;
        Assert.NotEmpty(unhandled.Children);
        Assert.All(unhandled.Children, child => Assert.IsType<SelectableTextBlock>(child));
    });

    [Fact]
    public void 뜬_진단이_화면에_그대로_선다() => HeadlessUi.Run(() =>
    {
        // 단추가 모아 주던 줄들이 <b>화면에는 그대로</b> 있어야 한다 — 복사 통로가 바뀌었을
        // 뿐 진단이 줄어든 것이 아니다. 상세는 뱃지를 눌러야 펼쳐지는 것도 그대로다.
        MiniStagePreview preview = ShowWithDiagnostics();

        Assert.Contains(
            preview.FindControl<WrapPanel>("BadgeRow")!.GetLogicalDescendants().OfType<TextBlock>(),
            block => (block.Text ?? string.Empty).Contains("미표시 1", StringComparison.Ordinal));

        Assert.Contains(
            preview.FindControl<StackPanel>("NoticeHost")!.Children.OfType<TextBlock>(),
            block => (block.Text ?? string.Empty).Contains("role-anchor.json", StringComparison.Ordinal));

        StackPanel unhandled = preview.FindControl<StackPanel>("UnhandledHost")!;

        Assert.False(unhandled.IsVisible, "상세는 뱃지를 눌러야 펼쳐진다");
        Assert.Contains(
            unhandled.Children.OfType<TextBlock>(),
            block => (block.Text ?? string.Empty).Contains("gesture", StringComparison.Ordinal) &&
                     (block.Text ?? string.Empty).Contains("접힘·미표시", StringComparison.Ordinal));
    });

    [Fact]
    public void 진단_복사_단추는_없다() => HeadlessUi.Run(() =>
    {
        // 2026-08-24 소유자 — "저 버튼은 제거해." 되살아나면 이 줄이 운다.
        MiniStagePreview preview = ShowWithDiagnostics();

        Assert.Null(preview.FindControl<Button>("CopyDiagnosticsButton"));
        Assert.DoesNotContain(
            preview.GetLogicalDescendants().OfType<Button>(),
            button => (button.Content as string ?? string.Empty)
                .Contains("진단 복사", StringComparison.Ordinal));
    });
}
