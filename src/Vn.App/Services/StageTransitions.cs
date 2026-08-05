using Ked.Presentation.Core;
using Vn.Authoring.Definition;
using Vn.Authoring.Results;

namespace Vn.App.Services;

/// <summary>
/// 라인 전이 시간의 규약 (W33) — "이 라인으로 넘어가는 데 얼마나 걸리는가".
///
/// 라인의 커맨드 duration(작성 값, 없으면 카탈로그 기본값) 중 최댓값을 쓴다.
/// 해석은 코어 <see cref="DurationToken"/> 한 곳(24fps 규약 — 지시서의 1/60초 가정은
/// 코어 확인으로 정정됐다). 저작 확인이 늘어지지 않게 상한을 두고, 커맨드가 없어도
/// 짧은 기본 전이가 있다 — 무엇도 안 변하는 라인이면 보간이 스스로 무행위가 된다.
/// </summary>
internal static class StageTransitions
{
    public const double DefaultSeconds = 0.35;

    public const double MinSeconds = 0.15;

    public const double MaxSeconds = 1.0;

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

        return Math.Clamp(max > 0 ? max : DefaultSeconds, MinSeconds, MaxSeconds);
    }
}
