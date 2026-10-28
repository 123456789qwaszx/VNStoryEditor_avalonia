using Ked.Presentation.Core;
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

    [Fact]
    public void 끝점은_계약_자리로_되돌아온다_탄젠트와_중간_오버슈트는_그대로다() => HeadlessUi.Run(() =>
    {
        // 2026-08-21 소유자: "overshoot은 중간에서만 허용 … 처음과 끝은 기울기만 조정".
        // 값까지 끌 수 있던 시절의 곡선이 어긋난 끝값(0.2·0.9)을 들고 와도, 로드가
        // (0,0)·(1,1)로 되돌린다 — 리듀서가 출발·도착을 정확히 밟는 계약이다.
        var editor = new EaseCurveEditor();

        editor.Load(
        [
            new CurveKey(0f, 0.2f, 0f, 1.2f),
            new CurveKey(0.5f, 1.4f, 0f, 0f),   // 중간 오버슈트는 허용 — 건드리지 않는다
            new CurveKey(1f, 0.9f, 2.5f, 0f)
        ]);

        Assert.Equal(0f, editor.Keys[0].Time);
        Assert.Equal(0f, editor.Keys[0].Value);
        Assert.Equal(1f, editor.Keys[^1].Time);
        Assert.Equal(1f, editor.Keys[^1].Value);

        // 기울기는 사람의 것 — 로드가 빼앗지 않는다.
        Assert.Equal(1.2f, editor.Keys[0].OutTangent);
        Assert.Equal(2.5f, editor.Keys[^1].InTangent);
        Assert.Equal(1.4f, editor.Keys[1].Value);
    });

    [Fact]
    public void 진동_모드는_끝점을_0으로_잠근다() => HeadlessUi.Run(() =>
    {
        // 2026-08-21 `gesture` 개통 — 잠그는 끝값이 곡선 종류를 따른다.
        // 이동은 (1,1), 진동은 (1,0). 규칙의 정본은 코어 CurveKindRules다.
        var editor = new EaseCurveEditor();

        editor.Load(
        [
            new CurveKey(0f, 0.3f, 0f, 2f),
            new CurveKey(0.5f, 1.4f, 0f, 0f),   // 중간 오버슈트는 진동에서도 자유
            new CurveKey(1f, 0.9f, -2f, 0f)
        ],
        CurveKind.Oscillation);

        Assert.Equal(CurveKind.Oscillation, editor.Kind);
        Assert.Equal(0f, editor.Keys[0].Value);
        Assert.Equal(1f, editor.Keys[^1].Time);
        Assert.Equal(0f, editor.Keys[^1].Value); // ← 이동이면 1이었을 자리
        Assert.Equal(1.4f, editor.Keys[1].Value);

        // 기울기는 사람의 것 — 종류가 바뀌어도 안 빼앗는다.
        Assert.Equal(2f, editor.Keys[0].OutTangent);
        Assert.Equal(-2f, editor.Keys[^1].InTangent);

        // 코어가 이 곡선을 진동으로 읽는다 — 툴과 런타임이 같은 판정이다.
        Assert.True(CurveKindRules.TryClassify(editor.Keys.ToArray(), out CurveKind kind, out _));
        Assert.Equal(CurveKind.Oscillation, kind);

        // 인자 없는 Load는 이동 모드 그대로(기존 호출부 불변).
        editor.Load([new CurveKey(0f, 0f, 0f, 1f), new CurveKey(1f, 0.4f, 1f, 0f)]);
        Assert.Equal(CurveKind.Motion, editor.Kind);
        Assert.Equal(1f, editor.Keys[^1].Value);
    });
}
