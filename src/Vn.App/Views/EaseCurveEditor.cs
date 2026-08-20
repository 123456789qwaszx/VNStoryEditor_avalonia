using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Ked.Presentation.Core;
using Vn.Authoring.Model;

namespace Vn.App.Views;

/// <summary>
/// 이징 곡선 그래프 에디터 (W67 후속) — 마야 그래프 에디터의 감각으로 키를 만진다.
///
/// - 키 드래그: 자리(t)와 값(v). 첫/끝 키는 t가 0/1에 잠긴다(런타임 로더 규칙)
/// - 탄젠트 핸들 드래그: 선택 키의 in/out 기울기. Shift를 누르면 한쪽만 꺾인다
/// - 곡선 빈 자리 클릭: 그 t에 키 추가(값은 지금 곡선 위)
/// - 키 삭제는 바깥의 [키 삭제] 버튼 — 우클릭은 소유자 보류라 여기서 쓰지 않는다
///
/// 그리는 곡선은 코어 <see cref="CurveFunctions"/> 그대로다 — 에디터가 보여 주는 모양 =
/// 프리뷰가 재생하는 모양 = 유니티가 재생하는 모양. 편집은 뷰 안의 작업 사본에서 일어나고,
/// 프로젝트에 닿는 것은 바깥의 [저장]뿐이다(확정만 편집).
/// </summary>
internal sealed class EaseCurveEditor : Control
{
    private const double PadX = 10;
    private const double PadY = 14;
    private const double ValueMin = -0.5;  // Back·Elastic·오버슈트 편집 여유
    private const double ValueMax = 1.5;
    private const double HitRadius = 7;
    private const double TangentArm = 34;  // 탄젠트 핸들 팔 길이(px)

    private readonly List<CurveKey> _keys = new();
    private int _selected = -1;
    private DragTarget _drag = DragTarget.None;

    private enum DragTarget { None, Key, InTangent, OutTangent }

    /// <summary>작업 사본이 바뀌었다 — 값 라벨·미리보기를 따라 그린다(저장 아님).</summary>
    public event Action? CurveChanged;

    public IReadOnlyList<CurveKey> Keys => _keys;

    public int SelectedIndex => _selected;

    /// <summary>첫/끝 키는 t 잠금이라 삭제도 안 된다.</summary>
    public bool CanDeleteSelected => _selected > 0 && _selected < _keys.Count - 1;

    public EaseCurveEditor()
    {
        // 크기는 호스트가 정한다 — 별도 창에서는 창을 따라 늘어난다(좌표 계산이 전부
        // Bounds 기반이라 그대로 통한다).
        MinWidth = 300;
        MinHeight = 190;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public void Load(IReadOnlyList<CurveKey> keys)
    {
        _keys.Clear();
        _keys.AddRange(keys);
        _selected = -1;
        _drag = DragTarget.None;
        InvalidateVisual();
        CurveChanged?.Invoke();
    }

    public void DeleteSelected()
    {
        if (!CanDeleteSelected)
        {
            return;
        }

        _keys.RemoveAt(_selected);
        _selected = -1;
        InvalidateVisual();
        CurveChanged?.Invoke();
    }

    // ── 좌표 변환 — t·v 곡선 공간 ↔ 픽셀 ─────────────────────────────────

    private double PlotWidth => Bounds.Width - PadX * 2;

    private double PlotHeight => Bounds.Height - PadY * 2;

    private Point ToPixel(double t, double v) => new(
        PadX + t * PlotWidth,
        PadY + (ValueMax - v) / (ValueMax - ValueMin) * PlotHeight);

    private (double T, double V) ToCurve(Point pixel) => (
        Math.Clamp((pixel.X - PadX) / PlotWidth, 0, 1),
        ValueMax - (pixel.Y - PadY) / PlotHeight * (ValueMax - ValueMin));

    /// <summary>기울기(dv/dt)를 화면 팔 벡터로 — x는 항상 진행 방향으로 한 팔 길이.</summary>
    private Point TangentHandle(CurveKey key, double slope, bool inward)
    {
        Point origin = ToPixel(key.Time, key.Value);
        double dx = TangentArm * (inward ? -1 : 1);
        double dy = slope * (PlotHeight / (ValueMax - ValueMin)) / PlotWidth * dx;
        return new Point(origin.X + dx, origin.Y + dy);
    }

    private double SlopeFromHandle(CurveKey key, Point handle)
    {
        Point origin = ToPixel(key.Time, key.Value);
        double dx = handle.X - origin.X;

        if (Math.Abs(dx) < 1)
        {
            dx = dx < 0 ? -1 : 1; // 수직에 가까우면 급경사로 — 무한 탄젠트(계단)는 여기서 안 만든다
        }

        double dy = handle.Y - origin.Y;
        return dy / dx * PlotWidth / (PlotHeight / (ValueMax - ValueMin));
    }

    // ── 그리기 ───────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        var background = new SolidColorBrush(Color.FromArgb(235, 18, 18, 26));
        context.FillRectangle(background, new Rect(Bounds.Size));

        // 기준선: v=0·v=1 (이동의 출발·도착), t 눈금 4등분.
        var faint = new Pen(new SolidColorBrush(Color.FromArgb(50, 148, 163, 184)), 1);
        var baseline = new Pen(new SolidColorBrush(Color.FromArgb(110, 148, 163, 184)), 1);
        context.DrawLine(baseline, ToPixel(0, 0), ToPixel(1, 0));
        context.DrawLine(baseline, ToPixel(0, 1), ToPixel(1, 1));

        for (int i = 1; i < 4; i++)
        {
            context.DrawLine(faint, ToPixel(i / 4.0, ValueMin), ToPixel(i / 4.0, ValueMax));
        }

        if (_keys.Count < 2)
        {
            return;
        }

        // 곡선 — 코어 평가기 그대로 64샘플.
        CurveKey[] keys = _keys.ToArray();
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(235, 250, 204, 21)), 2);
        const int samples = 64;
        Point previous = ToPixel(0, CurveFunctions.Evaluate(keys, 0f));

        for (int i = 1; i <= samples; i++)
        {
            float t = i / (float)samples;
            Point next = ToPixel(t, CurveFunctions.Evaluate(keys, t));
            context.DrawLine(pen, previous, next);
            previous = next;
        }

        // 키 + 선택 키의 탄젠트 핸들.
        for (int i = 0; i < _keys.Count; i++)
        {
            CurveKey key = _keys[i];
            Point center = ToPixel(key.Time, key.Value);
            bool locked = i == 0 || i == _keys.Count - 1;

            if (i == _selected)
            {
                var handlePen = new Pen(new SolidColorBrush(Color.FromArgb(200, 125, 211, 252)), 1);
                var handleBrush = new SolidColorBrush(Color.FromArgb(230, 125, 211, 252));
                Point inHandle = TangentHandle(key, key.InTangent, inward: true);
                Point outHandle = TangentHandle(key, key.OutTangent, inward: false);

                if (i > 0)
                {
                    context.DrawLine(handlePen, center, inHandle);
                    context.DrawEllipse(handleBrush, null, inHandle, 3.5, 3.5);
                }

                if (i < _keys.Count - 1)
                {
                    context.DrawLine(handlePen, center, outHandle);
                    context.DrawEllipse(handleBrush, null, outHandle, 3.5, 3.5);
                }
            }

            var keyBrush = new SolidColorBrush(i == _selected
                ? Color.FromArgb(255, 250, 204, 21)
                : locked ? Color.FromArgb(200, 148, 163, 184) : Color.FromArgb(230, 226, 232, 240));
            context.FillRectangle(keyBrush, new Rect(center.X - 4, center.Y - 4, 8, 8));
        }
    }

    // ── 조작 ─────────────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs args)
    {
        base.OnPointerPressed(args);
        Point position = args.GetPosition(this);

        // 1) 선택 키의 탄젠트 핸들.
        if (_selected >= 0)
        {
            CurveKey selected = _keys[_selected];

            if (_selected > 0 &&
                Distance(position, TangentHandle(selected, selected.InTangent, inward: true)) < HitRadius)
            {
                _drag = DragTarget.InTangent;
                args.Pointer.Capture(this);
                args.Handled = true;
                return;
            }

            if (_selected < _keys.Count - 1 &&
                Distance(position, TangentHandle(selected, selected.OutTangent, inward: false)) < HitRadius)
            {
                _drag = DragTarget.OutTangent;
                args.Pointer.Capture(this);
                args.Handled = true;
                return;
            }
        }

        // 2) 키.
        for (int i = 0; i < _keys.Count; i++)
        {
            if (Distance(position, ToPixel(_keys[i].Time, _keys[i].Value)) < HitRadius)
            {
                _selected = i;
                _drag = DragTarget.Key;
                args.Pointer.Capture(this);
                InvalidateVisual();
                args.Handled = true;
                return;
            }
        }

        // 3) 빈 자리 = 그 t에 키 추가 (값은 지금 곡선 위 — 모양이 튀지 않는 시작점).
        (double t, _) = ToCurve(position);

        if (_keys.Count >= 2 && t > 0.01 && t < 0.99)
        {
            float value = CurveFunctions.Evaluate(_keys.ToArray(), (float)t);
            int insertAt = _keys.FindIndex(key => key.Time > t);
            insertAt = insertAt < 0 ? _keys.Count - 1 : insertAt;

            // 새 키의 탄젠트는 그 자리의 접선 근사 — 추가 직후 곡선이 거의 안 변한다.
            const float h = 0.01f;
            float slope = (CurveFunctions.Evaluate(_keys.ToArray(), (float)t + h) -
                           CurveFunctions.Evaluate(_keys.ToArray(), (float)t - h)) / (2 * h);

            _keys.Insert(insertAt, new CurveKey((float)t, value, slope, slope));
            _selected = insertAt;
            _drag = DragTarget.Key;
            args.Pointer.Capture(this);
            InvalidateVisual();
            CurveChanged?.Invoke();
            args.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs args)
    {
        base.OnPointerMoved(args);

        if (_drag == DragTarget.None || _selected < 0)
        {
            return;
        }

        Point position = args.GetPosition(this);
        CurveKey key = _keys[_selected];

        if (_drag == DragTarget.Key)
        {
            (double t, double v) = ToCurve(position);

            // 첫/끝 키는 t 잠금(런타임 로더 규칙), 중간 키는 이웃 사이로만 — 순서가 신원이다.
            float clampedT = _selected == 0 ? 0f
                : _selected == _keys.Count - 1 ? 1f
                : (float)Math.Clamp(t, _keys[_selected - 1].Time + 0.005, _keys[_selected + 1].Time - 0.005);

            _keys[_selected] = new CurveKey(
                clampedT,
                (float)Math.Clamp(v, ValueMin, ValueMax),
                key.InTangent,
                key.OutTangent);
        }
        else
        {
            double slope = SlopeFromHandle(key, position);
            bool broken = args.KeyModifiers.HasFlag(KeyModifiers.Shift);

            _keys[_selected] = _drag == DragTarget.InTangent
                ? new CurveKey(key.Time, key.Value, (float)slope, broken ? key.OutTangent : (float)slope)
                : new CurveKey(key.Time, key.Value, broken ? key.InTangent : (float)slope, (float)slope);
        }

        InvalidateVisual();
        CurveChanged?.Invoke();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs args)
    {
        base.OnPointerReleased(args);
        _drag = DragTarget.None;
        args.Pointer.Capture(null);
    }

    private static double Distance(Point a, Point b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
