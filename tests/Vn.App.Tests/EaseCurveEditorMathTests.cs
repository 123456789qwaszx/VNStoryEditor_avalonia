using Vn.App.Views;

namespace Vn.App.Tests;

/// <summary>
/// 곡선 에디터의 기울기 ↔ 화면 팔 벡터 변환 (2026-08-20 소유자 보고의 수리) —
/// 화면 y는 값과 반대로 자라므로 부호 반전이 두 방향 모두에 있어야 한다.
/// 이게 빠지면 핸들이 곡선과 거울로 서고, 날개를 돌리면 의도와 반대로 굽는다.
/// </summary>
public class EaseCurveEditorMathTests
{
    private const double PixelsPerT = 280;     // 플롯 폭
    private const double PixelsPerValue = 81;  // 플롯 높이 / 값 범위

    [Fact]
    public void 오르는_기울기의_out_핸들은_화면에서_위를_향한다()
    {
        // slope +1(오르는 곡선), 오른쪽 팔(dx>0) → 픽셀 dy는 음수(화면 위)여야 한다.
        double dy = EaseCurveEditor.HandleDyPixels(
            slope: 1, dxPixels: 34, PixelsPerT, PixelsPerValue);
        Assert.True(dy < 0, $"오르는 곡선의 오른팔이 아래를 향했다 (dy={dy})");

        // in 핸들(dx<0)은 정확히 거울 — 두 팔이 한 직선(탄젠트)이다.
        double mirrored = EaseCurveEditor.HandleDyPixels(
            slope: 1, dxPixels: -34, PixelsPerT, PixelsPerValue);
        Assert.Equal(-dy, mirrored, 9);
    }

    [Fact]
    public void 핸들을_화면_위로_끌면_기울기가_커진다()
    {
        // 오른팔을 위로(dy<0) → 양의 기울기. 이게 반대면 "의도와 반대로 곡선이 조절"된다.
        double slope = EaseCurveEditor.SlopeFromHandleDelta(
            dxPixels: 34, dyPixels: -20, PixelsPerT, PixelsPerValue);
        Assert.True(slope > 0, $"위로 끈 날개가 음의 기울기를 냈다 (slope={slope})");
    }

    [Fact]
    public void 기울기_화면_왕복은_항등이다()
    {
        foreach (double slope in (double[])[-3.5, -1, 0, 0.4, 1, 2.6])
        {
            double dy = EaseCurveEditor.HandleDyPixels(slope, 34, PixelsPerT, PixelsPerValue);
            double roundTripped = EaseCurveEditor.SlopeFromHandleDelta(34, dy, PixelsPerT, PixelsPerValue);
            Assert.Equal(slope, roundTripped, 9);
        }
    }
}
