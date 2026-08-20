using Ked.Presentation.Core;
using Vn.Authoring.Definition;
using Vn.Authoring.Results;

namespace Vn.Authoring.Flow;

/// <summary>
/// 커맨드 하나가 그리는 이동 구간 — 어디서 시작해 어디로, 얼마 동안, 어떤 모양으로.
/// 좌표는 <b>노드의 앵커 좌표(px)</b>다. 화면 좌표로 옮기는 것은 컴포저의 일이다.
/// </summary>
public sealed record MotionSegment(
    string CommandId,
    string OutputCommand,
    string SlotKey,
    string NodeKey,
    Vec2 Start,
    Vec2 End,
    double DurationSeconds,
    double DurationFrames,
    string? Ease)
{
    /// <summary>이동량. 상대 커맨드면 인자가 곧 이 값이다.</summary>
    public Vec2 Delta => End - Start;

    /// <summary>시간이 없으면 계단이다 — 런타임도 <c>duration &lt;= 0</c>이면 즉시 스냅한다.</summary>
    public bool IsInstant => DurationSeconds <= 0;
}

/// <summary>
/// 카탈로그의 모션 선언(<see cref="PresentationMotionDeclaration"/>)을 근거로 커맨드를
/// 이동 구간으로 펼친다 (W66).
///
/// 규칙 사본을 만들지 않는다 — 단위 환산은 <see cref="UnitToken"/>, 시간은
/// <see cref="DurationToken"/>, <b>종점은 <see cref="MoveByReduction"/></b>, 시작 자리는
/// 폴드(<see cref="CoreStageFold"/>)가 준다. 그래서 그래프가 말하는 값과 실제 재생·정착이
/// 같은 곳에서 갈린다.
///
/// 선언이 없는 커맨드는 <c>null</c>이다. 추측하지 않는다.
/// </summary>
public static class MotionInspection
{
    /// <summary>
    /// 커맨드를 이동 구간으로 펼친다. <paramref name="stateBefore"/>는 <b>이 커맨드를
    /// 적용하기 직전</b>의 무대 상태여야 한다(<see cref="CoreStageFold.Fold"/>의
    /// <c>stopBeforeCommandId</c>가 그것을 만든다).
    ///
    /// 펼칠 수 없으면 null — 선언이 없거나, 슬롯이 아직 무대에 없거나, 값 토큰을 못 읽은 때다.
    /// </summary>
    public static MotionSegment? Inspect(
        PresentationCommandDefinition definition,
        PresentationResultCommand command,
        StageState stateBefore,
        StageReducerTuning tuning,
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(stateBefore);
        ArgumentNullException.ThrowIfNull(tuning);

        if (definition.Motion is not { } motion)
        {
            return null;
        }

        string Argument(string name)
        {
            if (overrides is not null && overrides.TryGetValue(name, out string? overridden))
            {
                return overridden;
            }

            return command.Arguments.TryGetValue(name, out string? value)
                ? value
                : definition.FindParameter(name)?.Default ?? string.Empty;
        }

        // 대상 해석은 무대 상태의 몫이다 — 별칭도 캐릭터 키도 같은 길로 풀린다.
        PresentationCommandParameter? targetParameter = definition.Parameters
            .FirstOrDefault(item => ArgumentTokenCandidates.IsStageTargetType(item.Type));

        if (targetParameter is null ||
            !stateBefore.TryResolveSlot(Argument(targetParameter.Name), out string slotKey))
        {
            return null;
        }

        string nodeKey = StageState.NodeKeyOf(slotKey, motion.NodeId);

        if (!stateBefore.Nodes.TryGetState(nodeKey, out RectNodeState nodeState))
        {
            return null;
        }

        if (!TryReadAxes(motion, tuning, Argument, out Vec2 value))
        {
            return null;
        }

        Vec2 start = nodeState.AnchoredPosition;

        // 종점은 런타임 커맨드가 쓰는 그 함수다. 여기서 손으로 더하면 사본이 하나 는다.
        StageNodeClaim claim = MoveByReduction.Reduce(
            nodeKey,
            new MoveByReduction.Args(!motion.Relative, value),
            start);

        double seconds = 0;

        if (motion.DurationParameterName is { } durationParameter &&
            DurationToken.TryParseSeconds(Argument(durationParameter), out float parsed))
        {
            seconds = parsed;
        }

        return new MotionSegment(
            command.CommandId,
            definition.OutputCommandName,
            slotKey,
            nodeKey,
            start,
            claim.Value.XY,
            seconds,
            seconds * DurationToken.FramesPerSecond,
            motion.DefaultEase);
    }

    /// <summary>선언된 축을 픽셀 값으로 읽는다. 하나라도 못 읽으면 통째로 실패다(부분 해석 금지).</summary>
    private static bool TryReadAxes(
        PresentationMotionDeclaration motion,
        StageReducerTuning tuning,
        Func<string, string> argument,
        out Vec2 value)
    {
        value = Vec2.Zero;
        float x = 0;
        float y = 0;

        foreach (PresentationMotionAxis axis in motion.Axes)
        {
            if (!UnitToken.TryParseSignedPixels(
                    argument(axis.ParameterName), tuning.ReferenceStageWidth, out float pixels))
            {
                return false;
            }

            if (string.Equals(axis.Axis, PresentationMotionAxis.XAxis, StringComparison.Ordinal))
            {
                x = pixels;
            }
            else if (string.Equals(axis.Axis, PresentationMotionAxis.YAxis, StringComparison.Ordinal))
            {
                y = pixels;
            }
            else
            {
                // 모르는 축 이름 — 조용히 0으로 두지 않는다. 선언이 틀린 것이다.
                return false;
            }
        }

        value = new Vec2(x, y);
        return true;
    }
}
