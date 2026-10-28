using Vn.Authoring.Model;

namespace Vn.Authoring.Flow;

/// <summary>
/// 선택 라인이 옵션 라벨일 때, 그 라벨이 속한 선택 블록의 <b>옵션 전부</b>를 모은다.
///
/// 프리뷰가 쓴다: 라벨은 화자가 읽는 대사가 아니라 플레이어가 고르는 버튼이므로,
/// 대사창에 한 줄씩 흘리는 대신 블록의 버튼 묶음을 한 번에 보여 준다 — 런타임이
/// 선택지를 제시하는 순간의 근사다. 라벨이 아닌 라인에서는 null(보통 대사창)이다.
///
/// 순수 함수이고, 작업 중 대본(DialogueLine)과 발행 결과(DialogueResultLine)가
/// 델리게이트로 같은 구현 하나를 지난다(규칙 사본 금지).
/// </summary>
public static class ChoiceOptionBundle
{
    /// <param name="LineIndex">문서에서 이 라벨 라인의 위치.</param>
    /// <param name="IsSelected">지금 선택된 라벨인가.</param>
    public sealed record Option(int LineIndex, string Text, bool IsSelected);

    public static IReadOnlyList<Option>? At<TLine>(
        IReadOnlyList<TLine> lines,
        int selectedIndex,
        Func<TLine, ConditionTransitionKind?> kindOf,
        Func<TLine, string> textOf)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(kindOf);
        ArgumentNullException.ThrowIfNull(textOf);

        if (selectedIndex < 0 || selectedIndex >= lines.Count ||
            kindOf(lines[selectedIndex]) is not (ConditionTransitionKind.BeginChoice
                or ConditionTransitionKind.BeginNextOption))
        {
            return null;
        }

        // 블록의 시작(BeginChoice)까지 거슬러 올라간다. 도중에 EndChoice를 만나면
        // 구조가 깨진 것 — 판정하지 않는다(플로우 해석기가 따로 알린다).
        int start = selectedIndex;

        while (kindOf(lines[start]) is not ConditionTransitionKind.BeginChoice)
        {
            start--;

            if (start < 0 || kindOf(lines[start]) is ConditionTransitionKind.EndChoice)
            {
                return null;
            }
        }

        // 시작부터 앞으로 훑으며 라벨을 모은다. EndChoice 또는 새 BeginChoice가 블록의 끝이다.
        var options = new List<Option>();

        for (int index = start; index < lines.Count; index++)
        {
            ConditionTransitionKind? kind = kindOf(lines[index]);

            if (kind is ConditionTransitionKind.EndChoice ||
                (kind is ConditionTransitionKind.BeginChoice && index != start))
            {
                break;
            }

            if (kind is ConditionTransitionKind.BeginChoice or ConditionTransitionKind.BeginNextOption)
            {
                options.Add(new Option(index, textOf(lines[index]), index == selectedIndex));
            }
        }

        return options;
    }
}
