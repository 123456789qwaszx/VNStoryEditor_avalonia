using Ked.Presentation.Core;
using Vn.Authoring.Definition;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Flow;

/// <summary>노드 하나가 이 커맨드 동안 지나는 구간. 값은 코어가 접은 두 상태 그대로다.</summary>
public sealed record MotionNodeTween(string NodeKey, RectNodeState From, RectNodeState To);

/// <summary>
/// 카메라가 이 커맨드 동안 지나는 구간 (2026-08-21 소유자: "shot_to, shot_focus_to 등의
/// 카메라가 지금 시간이 안 먹고 있어").
///
/// ⚠ 샷은 <b>RectNode가 아니라</b> <see cref="StageState.Shot"/>이라는 별도 상태다 —
/// 노드 차이만 보던 계획이 카메라를 못 본 것이 그 정체였다. 런타임도 zoom·pan·focus
/// 셋을 함께 Lerp한다(<c>PresentationShotIntentMath.Interpolate</c>).
/// </summary>
public sealed record MotionShotTween(ShotIntentState From, ShotIntentState To);

/// <summary>
/// 제자리 몸짓 하나가 이 커맨드 동안 흔드는 폭 (2026-08-21 `gesture` 개통).
///
/// ⚠ 다른 구간과 근본이 다르다: <b>폴드 차이가 근거가 아니다.</b> gesture는 순변위 0이라
/// 코어가 무변으로 접고(그것이 정의다), 그래서 직전·직후 상태가 같다 — 차이를 재는
/// 방식으로는 영원히 안 보인다. 대신 <b>인자가 직접</b> 말한다: 변위(t) = 진폭 × 곡선(t).
///
/// 표적은 <c>CharacterPortrait_Shake</c> — 이동 계열이 안 쓰는 노드라 겹쳐도 안 부딪친다.
/// </summary>
/// <param name="NodeKey">흔들 노드(<c>{슬롯}/CharacterPortrait_Shake</c>).</param>
/// <param name="AmplitudeX">가로 진폭(픽셀). 부호는 곡선 좌우 반전.</param>
/// <param name="AmplitudeY">세로 진폭(픽셀).</param>
/// <param name="SourceX">
/// 가로 진동 재료 — <b>곡선 키 → 이징 핑퐁 → 기본 혹</b>의 우선순위를 값 하나가 담는다.
/// 그 세 갈래를 푸는 것은 코어 <see cref="OscillationFunctions.Evaluate(in OscillationSource, float)"/>
/// 하나이고 런타임 스펙도 같은 타입을 싣는다 — 프리뷰가 재생과 갈릴 자리가 없다.
/// </param>
/// <param name="SourceY">세로 진동 재료.</param>
public sealed record MotionGestureTween(
    string NodeKey,
    double AmplitudeX,
    double AmplitudeY,
    OscillationSource SourceX,
    OscillationSource SourceY);

/// <summary>
/// 커맨드 하나의 시간 구간 — 자기 <c>duration</c>·<c>ease</c>로 자기가 바꾼 노드들을 끈다.
/// 노드가 여럿인 것은 정상이다(예: <c>size</c>는 배율과 포커스 보정을 함께 바꾼다).
/// </summary>
public sealed record MotionTween(
    string CommandId,
    string OutputCommand,
    double DurationSeconds,
    string? Ease,
    IReadOnlyList<CurveKey>? CurveKeys,
    IReadOnlyList<MotionNodeTween> Nodes,
    MotionShotTween? Shot = null,
    MotionGestureTween? Gesture = null);

/// <summary>
/// 한 라인이 시간에 따라 흐르는 모양 (2026-08-21 소유자: "place랑 depth의 경우에도 move랑
/// 마찬가지로 duration이 들어갈 시에 snap 되는게 아니라 실제 코어쪽과 동일하게 시간에 따라
/// 움직이도록 … move,place,depth를 셋다 동시에 같이쓰는 경우에도").
///
/// <b>축을 해석하지 않는다.</b> 커맨드가 무엇을 얼마나 바꾸는지는 <see cref="CoreStageFold"/>가
/// 접은 <b>직전 상태와 직후 상태의 차이</b>가 말한다 — 그래서 이동(<c>move_by</c>)·배치
/// (<c>place</c>)·뎁스(<c>size</c>)·배율·회전이 저마다 다른 노드를 만져도 같은 규칙 하나로
/// 흐르고, <b>셋을 동시에 써도 각자의 노드에서 각자의 시간으로</b> 흐른 뒤 컴포저가 합친다.
///
/// 시간을 안 가진 커맨드(<c>duration</c> 미선언·0)는 계획에 들어오지 않는다 — 런타임도
/// 그때는 즉시 스냅이고, 그 결과는 이미 <see cref="Final"/>에 반영돼 있다.
/// </summary>
public sealed class StageMotionPlan
{
    private StageMotionPlan(IReadOnlyList<MotionTween> tweens, StageState final)
    {
        Tweens = tweens;
        Final = final;
        var animated = tweens
            .SelectMany(tween => tween.Nodes)
            .Select(node => SlotOfNode(node.NodeKey))
            .Where(slot => slot.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        foreach (MotionTween tween in tweens)
        {
            if (tween.Gesture is { } gesture && SlotOfNode(gesture.NodeKey) is { Length: > 0 } slot)
            {
                animated.Add(slot);
            }
        }

        // 카메라가 흐르면 무대 위의 <b>전부</b>가 흐른다 — 샷은 슬롯 하나의 일이 아니다.
        if (tweens.Any(tween => tween.Shot is not null))
        {
            foreach (string slot in final.Slots)
            {
                animated.Add(slot);
            }
        }

        AnimatedSlots = animated;
        LongestSeconds = tweens.Count == 0 ? 0 : tweens.Max(tween => tween.DurationSeconds);
    }

    /// <summary>커맨드 순서 그대로. 같은 노드를 겹쳐 만지면 <b>뒤의 것이 이긴다</b>(런타임 DOKill 의미론).</summary>
    public IReadOnlyList<MotionTween> Tweens { get; }

    /// <summary>라인의 모든 커맨드가 적용된 확정 상태 — 진행 1의 자리다.</summary>
    public StageState Final { get; }

    /// <summary>이 라인에서 시간에 따라 움직이는 슬롯들. 나머지는 라인 사이 전이의 몫이다.</summary>
    public IReadOnlyCollection<string> AnimatedSlots { get; }

    /// <summary>가장 긴 구간(초). 라인이 다 흐르는 데 걸리는 시간의 하한이다.</summary>
    public double LongestSeconds { get; }

    /// <summary>
    /// 라인 시작으로부터 <paramref name="elapsedSeconds"/>가 지난 순간의 무대 상태.
    /// 0이면 라인이 시작되는 순간(모든 구간의 출발), 충분히 크면 <see cref="Final"/>과 같다.
    /// 원본은 건드리지 않는다 — 복제본을 돌려준다.
    /// </summary>
    public StageState Evaluate(double elapsedSeconds)
    {
        StageState state = Final.Clone();

        foreach (MotionTween tween in Tweens)
        {
            double progress = tween.DurationSeconds <= 0
                ? 1
                : Math.Clamp(elapsedSeconds / tween.DurationSeconds, 0, 1);
            float shaped = Shape(tween, progress);

            if (tween.Shot is { } shot)
            {
                // 런타임과 같은 셋 Lerp — zoom·pan·focus(PresentationShotIntentMath).
                state.Shot = Lerp(shot.From, shot.To, shaped);
            }

            if (tween.Gesture is { } gesture && state.Nodes.Contains(gesture.NodeKey))
            {
                // ⚠ 진동은 <b>이징을 안 탄다</b> — 런타임도 트윈을 Linear로 흘리고 콜백에서
                // 두 축 곡선을 각각 평가한다(GestureCommandCharR). 모양의 주인이 진동
                // 곡선이라, 진행도에 또 곡선을 먹이면 재생과 갈린다.
                float raw = (float)progress;

                RectNodeState rest = state.Nodes.GetState(gesture.NodeKey);
                state.Nodes.SetState(gesture.NodeKey, rest.WithAnchoredPosition(new Vec2(
                    (float)(gesture.AmplitudeX * OscillationFunctions.Evaluate(gesture.SourceX, raw)),
                    (float)(gesture.AmplitudeY * OscillationFunctions.Evaluate(gesture.SourceY, raw)))));
            }

            foreach (MotionNodeTween node in tween.Nodes)
            {
                if (state.Nodes.Contains(node.NodeKey))
                {
                    state.Nodes.SetState(node.NodeKey, Lerp(node.From, node.To, shaped));
                }
            }
        }

        return state;
    }

    /// <summary>
    /// 진행도 → 곡선 값. 커스텀 곡선이 있으면 그것, 없으면 이징이다 — 재생·스크럽·정지가
    /// 전부 이 하나를 지난다(런타임과 등가 고정된 코어 함수).
    /// </summary>
    private static float Shape(MotionTween tween, double progress) =>
        tween.CurveKeys is { Count: >= 2 } keys
            ? CurveFunctions.Evaluate(keys as CurveKey[] ?? keys.ToArray(), (float)progress)
            : EaseFunctions.Evaluate(EaseKindOf(tween.Ease), (float)progress);

    /// <summary>모르는 이름은 런타임 스펙 기본값(OutCubic)으로 — 브리지의 파싱 실패와 같은 방향.</summary>
    public static EaseKind EaseKindOf(string? name) =>
        Enum.TryParse(name, ignoreCase: true, out EaseKind kind) ? kind : EaseKind.OutCubic;

    /// <summary>노드 키("c1/CharSlot_Track")의 슬롯 부분.</summary>
    public static string SlotOfNode(string nodeKey)
    {
        int slash = nodeKey.IndexOf('/', StringComparison.Ordinal);
        return slash > 0 ? nodeKey[..slash] : string.Empty;
    }

    private static RectNodeState Lerp(in RectNodeState from, in RectNodeState to, float t) => new(
        Lerp(from.AnchoredPosition, to.AnchoredPosition, t),
        Lerp(from.AnchorMin, to.AnchorMin, t),
        Lerp(from.AnchorMax, to.AnchorMax, t),
        Lerp(from.Pivot, to.Pivot, t),
        Lerp(from.SizeDelta, to.SizeDelta, t),
        Lerp(from.LocalScale, to.LocalScale, t),
        Lerp(from.LocalEulerAngles, to.LocalEulerAngles, t));

    /// <summary>코어 평가기가 배열을 받는다 — 이미 배열이면 그대로 쓴다(복사 없음).</summary>
    private static CurveKey[]? KeyArray(IReadOnlyList<CurveKey>? keys) =>
        keys is null ? null : keys as CurveKey[] ?? keys.ToArray();

    private static ShotIntentState Lerp(in ShotIntentState from, in ShotIntentState to, float t) => new(
        from.Zoom + ((to.Zoom - from.Zoom) * t),
        Lerp(from.PanInRigSpace, to.PanInRigSpace, t),
        Lerp(from.FocusPointInRigSpace, to.FocusPointInRigSpace, t));

    private static Vec2 Lerp(in Vec2 from, in Vec2 to, float t) =>
        new(from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t));

    private static Vec3 Lerp(in Vec3 from, in Vec3 to, float t) =>
        new(from.X + ((to.X - from.X) * t),
            from.Y + ((to.Y - from.Y) * t),
            from.Z + ((to.Z - from.Z) * t));

    // ── 계획 세우기 ────────────────────────────────────────────────────

    /// <summary>
    /// 이 라인의 커맨드들을 시간 구간으로 펼친다. 접을 수 없으면(튜닝 없음·커맨드 없음)
    /// null — 근사 배치 화면에서는 시간도 근사할 수 없다.
    /// </summary>
    public static StageMotionPlan? Build(
        PresentationCommandCatalog catalog,
        IReadOnlyList<PresentationResultCommand> setupCommands,
        IReadOnlyList<MiniStageFoldLine> foldLines,
        IReadOnlyList<PresentationResultCommand>? lineCommands,
        StageReducerTuning? tuning,
        IReadOnlyList<EaseCurve>? curves = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(setupCommands);
        ArgumentNullException.ThrowIfNull(foldLines);

        if (tuning is null || lineCommands is null || lineCommands.Count == 0)
        {
            return null;
        }

        if (CoreStageFold.Fold(catalog, setupCommands, foldLines, tuning).CoreState is not { } final)
        {
            return null;
        }

        var tweens = new List<MotionTween>();

        for (int index = 0; index < lineCommands.Count; index++)
        {
            PresentationResultCommand command = lineCommands[index];

            if (catalog.Find(command.DefinitionId) is not { } definition ||
                !TryReadSeconds(definition, command, out double seconds) || seconds <= 0)
            {
                continue; // 시간을 안 가진 커맨드 — 런타임도 즉시 스냅이다.
            }

            StageState? before = CoreStageFold
                .Fold(catalog, setupCommands, foldLines, tuning, command.CommandId).CoreState;

            // 직후 상태 = "다음 커맨드 직전". 마지막 커맨드면 라인의 확정 상태가 그것이다.
            StageState? after = index + 1 < lineCommands.Count
                ? CoreStageFold
                    .Fold(catalog, setupCommands, foldLines, tuning, lineCommands[index + 1].CommandId)
                    .CoreState
                : final;

            if (before is null || after is null)
            {
                continue;
            }

            IReadOnlyList<MotionNodeTween> nodes = DiffNodes(before, after);

            // 카메라(shot_to·shot_focus_to·shot_zoom·shot_track·shot_reset)는 노드가
            // 아니라 StageState.Shot을 바꾼다 — 노드 차이만 보면 영원히 스냅이다.
            MotionShotTween? shot = Differs(before.Shot, after.Shot)
                ? new MotionShotTween(before.Shot, after.Shot)
                : null;

            // 제자리 몸짓은 상태를 안 바꾸는 것이 정의라 차이로는 안 보인다 — 인자가 말한다.
            MotionGestureTween? gesture = GestureOf(definition, command, final, tuning, curves);

            if (nodes.Count == 0 && shot is null && gesture is null)
            {
                continue; // 바뀐 자리가 없다 — 태울 것도 없다(0u 이동 등).
            }

            string? ease = EaseOf(definition, command);

            tweens.Add(new MotionTween(
                command.CommandId,
                definition.OutputCommandName,
                seconds,
                ease,
                CurveKeysOf(ease, curves),
                nodes,
                shot,
                gesture));
        }

        return tweens.Count > 0 ? new StageMotionPlan(tweens, final) : null;
    }

    /// <summary>두 상태에서 값이 달라진 노드들. 새로 생긴 노드는 보간 대상이 아니다(등장이지 이동이 아니다).</summary>
    private static IReadOnlyList<MotionNodeTween> DiffNodes(StageState before, StageState after)
    {
        var nodes = new List<MotionNodeTween>();

        foreach (string key in after.Nodes.Keys)
        {
            if (!before.Nodes.TryGetState(key, out RectNodeState from))
            {
                continue;
            }

            RectNodeState to = after.Nodes.GetState(key);

            if (Differs(from, to))
            {
                nodes.Add(new MotionNodeTween(key, from, to));
            }
        }

        return nodes;
    }

    private const float Epsilon = 0.0005f;

    private static bool Differs(in RectNodeState from, in RectNodeState to) =>
        Differs(from.AnchoredPosition, to.AnchoredPosition) ||
        Differs(from.AnchorMin, to.AnchorMin) ||
        Differs(from.AnchorMax, to.AnchorMax) ||
        Differs(from.Pivot, to.Pivot) ||
        Differs(from.SizeDelta, to.SizeDelta) ||
        Differs(from.LocalScale, to.LocalScale) ||
        Differs(from.LocalEulerAngles, to.LocalEulerAngles);

    private static bool Differs(in ShotIntentState from, in ShotIntentState to) =>
        Math.Abs(from.Zoom - to.Zoom) > Epsilon ||
        Differs(from.PanInRigSpace, to.PanInRigSpace) ||
        Differs(from.FocusPointInRigSpace, to.FocusPointInRigSpace);

    private static bool Differs(in Vec2 from, in Vec2 to) =>
        Math.Abs(from.X - to.X) > Epsilon || Math.Abs(from.Y - to.Y) > Epsilon;

    private static bool Differs(in Vec3 from, in Vec3 to) =>
        Math.Abs(from.X - to.X) > Epsilon ||
        Math.Abs(from.Y - to.Y) > Epsilon ||
        Math.Abs(from.Z - to.Z) > Epsilon;

    /// <summary>
    /// 이 커맨드가 가진 시간. <c>duration</c> 파라미터 <b>선언</b>이 근거다 — 이름으로
    /// 추측하지 않는다. 모션 선언이 따로 시간 파라미터를 말하면 그것이 우선이다.
    ///
    /// 한때 <c>frames</c> 타입도 셌다(2026-08-21) — <c>left_per</c> 계열 넷이 그 값
    /// 하나로 거리와 시간을 함께 말했기 때문이다. 그 넷이 폐지되면서 함께 걷혔다.
    /// </summary>
    private static bool TryReadSeconds(
        PresentationCommandDefinition definition, PresentationResultCommand command, out double seconds)
    {
        seconds = 0;

        string? parameterName = definition.Motion?.DurationParameterName ??
            definition.Parameters
                .FirstOrDefault(item => string.Equals(item.Type, "duration", StringComparison.Ordinal))?
                .Name;

        if (parameterName is null)
        {
            return false;
        }

        string? token = command.Arguments.TryGetValue(parameterName, out string? written) &&
            !string.IsNullOrWhiteSpace(written)
                ? written
                : definition.FindParameter(parameterName)?.Default;

        if (token is null || !DurationToken.TryParseSeconds(token, out float parsed))
        {
            return false;
        }

        seconds = parsed;
        return true;
    }

    /// <summary>
    /// 이징: 인자로 적힌 것(W67의 다섯째 토큰) → 모션 선언의 런타임 기본값 → null.
    /// null은 <see cref="EaseKindOf"/>가 런타임 스펙 기본(OutCubic)으로 받는다 —
    /// <c>place</c>·<c>size</c>처럼 <b>이징 칸이 없는 커맨드</b>가 여기로 온다.
    /// </summary>
    private static string? EaseOf(
        PresentationCommandDefinition definition, PresentationResultCommand command)
    {
        string? parameterName = definition.Motion?.EaseParameterName ??
            definition.Parameters
                .FirstOrDefault(item => string.Equals(item.Type, "ease", StringComparison.Ordinal))?
                .Name;

        if (parameterName is not null &&
            command.Arguments.TryGetValue(parameterName, out string? written) &&
            !string.IsNullOrWhiteSpace(written))
        {
            return written;
        }

        return definition.Motion?.DefaultEase;
    }

    /// <summary>
    /// 이 커맨드가 제자리 몸짓인가, 그렇다면 무엇을 얼마나 흔드는가 (2026-08-21).
    ///
    /// 판정은 <b>출력 커맨드 이름</b>이다 — 폴드가 무변이라 상태 차이로는 못 알아본다.
    /// 이름으로 추측하지 않는다는 규칙의 예외처럼 보이지만 그렇지 않다: <c>gesture</c>는
    /// "상태를 안 바꾸는 것이 정의"인 유일한 시간 커맨드라, 계약을 아는 것 말고는 길이 없다.
    ///
    /// 진폭이 양쪽 다 0이면 null — 흔들 것이 없다(런타임도 같은 값에서 아무 일도 안 한다).
    /// </summary>
    private static MotionGestureTween? GestureOf(
        PresentationCommandDefinition definition,
        PresentationResultCommand command,
        StageState state,
        StageReducerTuning tuning,
        IReadOnlyList<EaseCurve>? curves)
    {
        if (!string.Equals(definition.OutputCommandName, "gesture", StringComparison.Ordinal))
        {
            return null;
        }

        if (!state.TryResolveSlot(Argument(definition, command, "slot"), out string slotKey))
        {
            return null; // 없는 슬롯 — 폴드가 이미 사유를 남겼다
        }

        double x = AmplitudePixels(definition, command, "xAmp", tuning);
        double y = AmplitudePixels(definition, command, "yAmp", tuning);

        if (Math.Abs(x) < 0.001 && Math.Abs(y) < 0.001)
        {
            return null;
        }

        return new MotionGestureTween(
            StageState.NodeKeyOf(slotKey, "CharacterPortrait_Shake"),
            x,
            y,
            OscillationOf(Argument(definition, command, "xEase"), curves),
            OscillationOf(Argument(definition, command, "yEase"), curves));
    }

    /// <summary>적힌 값, 없으면 카탈로그 기본값. 둘 다 없으면 빈 문자열.</summary>
    private static string Argument(
        PresentationCommandDefinition definition, PresentationResultCommand command, string name) =>
        command.Arguments.TryGetValue(name, out string? written) && !string.IsNullOrWhiteSpace(written)
            ? written
            : definition.FindParameter(name)?.Default ?? string.Empty;

    /// <summary>u 토큰 → 픽셀. 환산의 유일한 자리는 <see cref="UnitToken"/>이다.</summary>
    private static double AmplitudePixels(
        PresentationCommandDefinition definition,
        PresentationResultCommand command,
        string name,
        StageReducerTuning tuning) =>
        UnitToken.TryParseSignedPixels(
            Argument(definition, command, name), tuning.ReferenceStageWidth, out float pixels)
            ? pixels
            : 0;

    /// <summary>
    /// 진동 한 축의 재료 — <b>브리지 <c>ResolveOscillation</c>과 같은 세 갈래</b>다
    /// (2026-08-21 증보 개통):
    ///
    /// <list type="number">
    /// <item><c>@이름</c> → 프로젝트 곡선. 단 <b>진동 종류일 때만</b>이고, 판정은 코어
    /// <see cref="CurveKindRules"/> 하나를 쓴다(런타임 로더와 같은 규칙). 이동 곡선이면
    /// 못 찾은 것으로 친다 — 한쪽만 알면 "툴에서는 되는데 재생에서 사라지는" 곡선이 된다</item>
    /// <item><b>표준 이징 이름</b> → 그 이징의 핑퐁. 숫자 토큰은 안 받는다 —
    /// <see cref="EaseKind"/>가 임의 정수로도 파싱돼 엉뚱한 이징이 되기 때문이다(런타임과 같은 방어)</item>
    /// <item>그 밖(빈 토큰·못 읽는 낱말) → 기본 혹. <c>OutSine</c>의 핑퐁과 같은 함수다</item>
    /// </list>
    /// </summary>
    private static OscillationSource OscillationOf(string token, IReadOnlyList<EaseCurve>? curves)
    {
        if (token is ['@', .. var name])
        {
            if (curves?.FirstOrDefault(curve =>
                    string.Equals(curve.Name, name, StringComparison.Ordinal))?.Keys is { } keys &&
                CurveKindRules.TryClassify(KeyArray(keys)!, out CurveKind kind, out _) &&
                kind == CurveKind.Oscillation)
            {
                return OscillationSource.FromCurve(KeyArray(keys)!);
            }

            return OscillationSource.Default;
        }

        // 숫자로 읽히는 토큰은 이징 이름이 아니다 — Enum.TryParse가 "3"을 받아들인다.
        return !string.IsNullOrWhiteSpace(token) &&
            !double.TryParse(token, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out _) &&
            Enum.TryParse(token, ignoreCase: true, out EaseKind ease)
                ? OscillationSource.FromEase(ease)
                : OscillationSource.Default;
    }

    /// <summary><c>@이름</c>이면 프로젝트 곡선의 키. 못 찾으면 null — 런타임과 같은 폴백이다.</summary>
    private static IReadOnlyList<CurveKey>? CurveKeysOf(string? ease, IReadOnlyList<EaseCurve>? curves) =>
        ease is ['@', .. var name]
            ? curves?.FirstOrDefault(curve =>
                string.Equals(curve.Name, name, StringComparison.Ordinal))?.Keys
            : null;
}
