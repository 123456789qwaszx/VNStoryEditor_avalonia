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
/// - 키 드래그: 자리(t)와 값(v). 첫/끝 키는 <b>자리째 잠긴다</b> — 기울기(탄젠트)만
///   만진다 (2026-08-21 소유자: "overshoot은 중간에서만 허용" — 끝값이 어긋나면
///   리듀서가 접은 종점과 재생이 갈린다)
/// - 잠기는 끝값은 <b>곡선 종류</b>가 정한다: 이동은 (1,1), 진동(`gesture`)은 (1,0).
///   판정 규칙은 코어 <see cref="CurveKindRules"/> 하나이고 런타임 로더도 그것을 쓴다
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

    /// <summary>
    /// 이 곡선이 무엇에 쓰이는가 — <b>끝값이 곧 종류</b>다 (2026-08-21 `gesture` 개통).
    /// <see cref="CurveKind.Motion"/>은 (1,1)로, <see cref="CurveKind.Oscillation"/>은
    /// (1,0)으로 끝난다. 판정 규칙은 코어 <see cref="CurveKindRules"/> 하나이고
    /// 런타임 로더도 그것을 쓴다 — 여기서 다른 눈금을 쓰면 "툴에서는 저장되는데 재생에서는
    /// 사라지는" 곡선이 생긴다.
    /// </summary>
    public CurveKind Kind { get; private set; } = CurveKind.Motion;

    /// <summary>끝 키가 서야 할 값. 이동은 1, 진동은 0.</summary>
    private float EndValue => Kind == CurveKind.Oscillation ? 0f : 1f;

    public void Load(IReadOnlyList<CurveKey> keys) => Load(keys, CurveKind.Motion);

    /// <param name="kind">
    /// 잠글 끝값을 정한다. 들어온 곡선이 그 종류가 아니어도 <b>자리로 되돌린다</b> —
    /// 값까지 끌 수 있던 시절의 곡선이나 종류를 바꿔 여는 경우가 여기로 온다.
    /// </param>
    public void Load(IReadOnlyList<CurveKey> keys, CurveKind kind)
    {
        Kind = kind;
        _keys.Clear();
        _keys.AddRange(keys);

        // 끝점은 (0,0)·(1, EndValue)가 계약이다 — 어긋난 끝값을 들고 오면 자리로
        // 되돌린다(탄젠트는 사람의 것이라 보존).
        if (_keys.Count >= 2)
        {
            CurveKey first = _keys[0];
            CurveKey last = _keys[^1];
            _keys[0] = new CurveKey(0f, 0f, first.InTangent, first.OutTangent);
            _keys[^1] = new CurveKey(1f, EndValue, last.InTangent, last.OutTangent);
        }

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

    /// <summary>
    /// 기울기(dv/dt) ↔ 화면 팔 벡터 변환의 순수 산수 — <b>화면 y는 값과 반대로 자란다</b>
    /// (값이 오르면 픽셀 y는 줄어든다). 처음 이 부호를 빼먹어 핸들이 곡선과 거울로 서고
    /// 날개를 돌리면 반대로 굽는 버그가 있었다(2026-08-20 소유자 보고) — 왕복 항등과
    /// 방향을 테스트가 지킨다.
    /// </summary>
    internal static double HandleDyPixels(double slope, double dxPixels, double pixelsPerT, double pixelsPerValue)
        => -slope * dxPixels / pixelsPerT * pixelsPerValue;

    internal static double SlopeFromHandleDelta(double dxPixels, double dyPixels, double pixelsPerT, double pixelsPerValue)
        => -dyPixels / pixelsPerValue * pixelsPerT / dxPixels;

    private double PixelsPerT => PlotWidth;

    private double PixelsPerValue => PlotHeight / (ValueMax - ValueMin);

    /// <summary>기울기(dv/dt)를 화면 팔 벡터로 — x는 항상 진행 방향으로 한 팔 길이.</summary>
    private Point TangentHandle(CurveKey key, double slope, bool inward)
    {
        Point origin = ToPixel(key.Time, key.Value);
        double dx = TangentArm * (inward ? -1 : 1);
        double dy = HandleDyPixels(slope, dx, PixelsPerT, PixelsPerValue);
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
        return SlopeFromHandleDelta(dx, dy, PixelsPerT, PixelsPerValue);
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

        // v=1 선은 <b>이동 곡선의 도착선</b>이다 — 진동은 0으로 돌아오는 것이 전부라
        // 그 선을 그리면 있지도 않은 목표가 있는 것처럼 읽힌다.
        if (Kind != CurveKind.Oscillation)
        {
            context.DrawLine(baseline, ToPixel(0, 1), ToPixel(1, 1));
        }

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

        // 2) 키. 첫/끝 키는 자리째 잠겨 있어 선택만 된다 — 탄젠트 핸들이 만질 전부다.
        for (int i = 0; i < _keys.Count; i++)
        {
            if (Distance(position, ToPixel(_keys[i].Time, _keys[i].Value)) < HitRadius)
            {
                bool locked = i == 0 || i == _keys.Count - 1;
                _selected = i;
                _drag = locked ? DragTarget.None : DragTarget.Key;

                if (!locked)
                {
                    args.Pointer.Capture(this);
                }

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
            // 첫/끝 키는 자리째 잠겨 애초에 Key 드래그가 시작되지 않는다(OnPointerPressed).
            // 중간 키는 이웃 사이로만 — 순서가 신원이다.
            if (_selected == 0 || _selected == _keys.Count - 1)
            {
                return;
            }

            (double t, double v) = ToCurve(position);
            float clampedT = (float)Math.Clamp(
                t, _keys[_selected - 1].Time + 0.005, _keys[_selected + 1].Time - 0.005);

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
