using Ked.Presentation.Core;
using Vn.Authoring.Definition;
using Vn.Authoring.Results;

namespace Vn.App.Services;

/// <summary>
/// 라인 전이 시간의 규약 (W33) — "이 라인으로 넘어가는 데 얼마나 걸리는가".
///
/// 라인의 커맨드 duration(작성 값, 없으면 카탈로그 기본값) 중 <b>최댓값 그대로</b>다.
/// 해석은 코어 <see cref="DurationToken"/> 한 곳(24fps 규약 — 지시서의 1/60초 가정은
/// 코어 확인으로 정정됐다).
///
/// <b>상한을 두지 않는다</b> (2026-08-21 소유자: "24프레임 넘어가는 건 그냥 바로 끊긴
/// 다음 snap시키는데, 실제 커맨드가 사용하는 프레임을 쓰도록") — 예전의 1초 상한이
/// 정확히 24프레임이라, 그보다 긴 커맨드는 라인 시계가 먼저 끝나 중간에 잘리고
/// 확정 자리로 튀었다. 커맨드가 쓴 프레임이 곧 이 라인이 흐르는 시간이다.
///
/// 시간을 가진 커맨드가 하나도 없으면 짧은 기본 전이를 쓴다 — 무엇도 안 변하는
/// 라인이면 보간이 스스로 무행위가 된다.
/// </summary>
internal static class StageTransitions
{
    public const double DefaultSeconds = 0.35;

    public static double SecondsFor(
        PresentationCommandCatalog catalog,
        IReadOnlyList<PresentationResultCommand>? commands)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        double max = 0;

        foreach (PresentationResultCommand command in commands ?? [])
        {
            PresentationCommandDefinition? definition = catalog.Find(command.DefinitionId);

            string? token =
                command.Arguments.TryGetValue("duration", out string? written) &&
                !string.IsNullOrWhiteSpace(written)
                    ? written
                    : definition?.FindParameter("duration")?.Default;

            if (token is not null && DurationToken.TryParseSeconds(token, out float seconds))
            {
                max = Math.Max(max, seconds);
            }
        }

        return max > 0 ? max : DefaultSeconds;
    }
}
