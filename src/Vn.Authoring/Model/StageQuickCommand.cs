namespace Vn.Authoring.Model;

/// <summary>
/// 칩이 낼 커맨드 한 단계 — 정의 + 미리 채운 인자.
///
/// <b>담을 때 인자를 통째로 복사한다</b> (2026-08-22 소유자: "저장되는 커맨드는 슬롯과
/// duration 수치까지 그대로 복사하도록 하는게 포인트"). 단계는 "이 커맨드를 이 값으로"라는
/// 한 벌이고, 그중 무엇을 남기고 무엇을 버릴지 도구가 고르지 않는다.
///
/// 대상 슬롯이 <b>비어 있는 단계</b>(기본 목록처럼 애초에 대상이 없거나, 손으로 지운 것)만
/// 누를 때 조절창의 선택 슬롯을 받는다 — 담긴 값이 있으면 그 값이 이긴다.
/// </summary>
public sealed record StageQuickStep(
    string DefinitionId,
    IReadOnlyDictionary<string, string> Arguments)
{
    public StageQuickStep Copy() =>
        this with { Arguments = new Dictionary<string, string>(Arguments, StringComparer.Ordinal) };

    public bool SameAs(StageQuickStep other) =>
        other is not null &&
        string.Equals(DefinitionId, other.DefinitionId, StringComparison.Ordinal) &&
        Arguments.Count == other.Arguments.Count &&
        Arguments.All(pair =>
            other.Arguments.TryGetValue(pair.Key, out string? value) &&
            string.Equals(pair.Value, value, StringComparison.Ordinal));
}

/// <summary>
/// 무대 조절창 [자주 쓰는] 탭의 칩 하나 — 이름 + <b>커맨드 단계 목록</b>.
///
/// <b>칩은 묶음이다</b> (2026-08-24 소유자: "여러개의 커맨드 단위로 커스텀"). 한 단계짜리
/// 칩은 묶음의 특수한 경우일 뿐이고 별도 개념이 아니다 — 두 벌로 나누면 "이건 칩인가
/// 묶음인가"를 화면도 코드도 매번 물어야 한다.
///
/// <b>순서가 뜻이다.</b> 누르면 <see cref="Steps"/> 순서 그대로 지금 라인에 붙는다.
/// 같은 커맨드·같은 대상이 두 번 나오면 뒤의 것이 이긴다 — 칩을 나눠 누르는 것과 묶어
/// 누르는 것이 같은 결과여야 하므로, 그 합치기 규칙도 조절창 전체와 같은 하나다
/// (<c>PresentationStageActions</c>).
/// </summary>
public sealed record StageQuickCommand(
    string DisplayName,
    IReadOnlyList<StageQuickStep> Steps)
{
    /// <summary>한 단계짜리 칩 — 터미널에서 커맨드 하나를 집는 기존 경로가 이것을 만든다.</summary>
    public static StageQuickCommand Single(
        string displayName,
        string definitionId,
        IReadOnlyDictionary<string, string> arguments) =>
        new(displayName, [new StageQuickStep(definitionId, arguments)]);

    public StageQuickCommand Copy() =>
        this with { Steps = Steps.Select(step => step.Copy()).ToArray() };

    /// <summary>같은 단계를 같은 순서로 담고 있는가 — 같은 것을 두 번 담는 것은 실수다.</summary>
    public bool SameStepsAs(StageQuickCommand other) =>
        other is not null &&
        Steps.Count == other.Steps.Count &&
        !Steps.Where((step, index) => !step.SameAs(other.Steps[index])).Any();
}

/// <summary>
/// 기본 칩 목록 — <b>샷 셋뿐이다</b> (2026-08-22 소유자: "shot_zoom, shot_to, shot_reset만
/// 있으면 될 것 같아"). 조절창의 네 탭이 누가·어디·어떤 표정을 이미 덮고, 카메라만 어느
/// 탭에도 없어서 126종 목록으로 나가야 했다. 나머지는 사람이 담는다 — 터미널에서 맞춰
/// 놓은 커맨드를 통째로 집는 것이 목록을 짓는 정식 경로다.
///
/// 코드가 기본을 쥐고 프로젝트가 덮어쓴다(<see cref="StoryProject.QuickCommands"/>).
/// 정의 Id는 기본 카탈로그(<c>game.definition.default.json</c>)의 것이라 게임 정의가
/// 다른 어휘를 쓰면 <b>못 찾은 칩은 조용히 빠진다</b> — 갤러리 "최근"과 같은 규칙이다.
/// </summary>
public static class StageQuickCommands
{
    private static StageQuickCommand Chip(
        string displayName,
        string definitionId,
        params (string Key, string Value)[] arguments) =>
        StageQuickCommand.Single(
            displayName,
            definitionId,
            arguments.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

    public static IReadOnlyList<StageQuickCommand> Default { get; } =
    [
        Chip("줌 인", "shot.shot_zoom", ("zoom", "1.4"), ("duration", "0.45s")),
        Chip("카메라 이동", "shot.shot_to", ("zoom", "1"), ("x", "2.5u"), ("y", "0u"), ("duration", "0.45s")),
        Chip("카메라 원위치", "shot.shot_reset", ("duration", "0.3s"))
    ];
}
