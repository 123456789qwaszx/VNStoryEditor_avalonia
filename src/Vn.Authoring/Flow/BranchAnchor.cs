namespace Vn.Authoring.Flow;

/// <summary>
/// <b>조건·선택 갈래의 신원을 짓는 유일한 자리</b> (2026-08-24).
///
/// 갈래의 신원은 원래 <b>그 갈래를 여는 줄의 Id</b> 하나였다. 그 가정이 두 군데서 깨졌다:
///
/// <list type="number">
///   <item>
///     <b>한 줄이 갈래를 여럿 연다.</b> 대사 없는 조건 블록은 <c>BeginIf</c>·<c>EndIf</c>가
///     블록 <em>다음</em> 대사 줄에 함께 실리므로, 빈 블록 뒤에 또 블록이 열리면 같은 줄이
///     갈래 둘을 연다.
///   </item>
///   <item>
///     <b>줄이 아예 없는 갈래가 있다.</b> 대사 없는 블록이 대본의 <em>끝</em>에 있으면
///     전환을 실을 다음 줄이 없다(<see cref="Model.DialogueNode.TrailingTransitions"/>).
///   </item>
/// </list>
///
/// ⛔ <b>첫째 갈래는 맨 줄 Id 그대로다.</b> 그 글자가 지금까지의 신원이고
/// <see cref="Model.DialogueNode.BranchExits"/>가 그것으로 출구를 붙들고 있다 — 규칙을
/// 바꾸면서 접미를 붙이면 <b>이미 매달린 출구가 전부 고아가 된다.</b> 새 모양은 둘째부터다.
/// </summary>
public static class BranchAnchor
{
    /// <summary>한 줄이 연 갈래들을 가르는 글자. 발급되는 Id에는 안 쓰이는 글자다.</summary>
    public const char Separator = '#';

    /// <summary>줄 없는(대본 끝의) 갈래가 사는 자리. 줄 Id와 절대 안 겹친다.</summary>
    public const string TrailingRoot = "#끝";

    /// <summary><paramref name="ordinal"/>번째(0부터) — 0이면 줄 Id 그대로다.</summary>
    public static string ForLine(string lineId, int ordinal) =>
        ordinal <= 0 ? lineId : $"{lineId}{Separator}{ordinal}";

    /// <summary>대본 끝의 <paramref name="ordinal"/>번째(0부터) 갈래.</summary>
    public static string ForTrailing(int ordinal) =>
        ordinal <= 0 ? TrailingRoot : $"{TrailingRoot}{Separator}{ordinal}";

    /// <summary>
    /// 이 신원이 <b>줄 없는 갈래</b>의 것인가. 줄에 매달린 청소(고아 출구 정리)가
    /// 이것들까지 쓸어 가지 않게 가르는 자리다.
    /// </summary>
    public static bool IsTrailing(string? anchor) =>
        anchor is not null && anchor.StartsWith(TrailingRoot, StringComparison.Ordinal);
}
