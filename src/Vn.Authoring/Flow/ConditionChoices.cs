using Vn.Authoring.Definition;
using Vn.Authoring.Model;

namespace Vn.Authoring.Flow;

public enum ConditionChoiceKind
{
    /// <summary>전환 없음. 앞 줄의 상태를 그대로 물려받는다.</summary>
    Inherit,

    /// <summary>조건 바깥에서 새 갈래를 연다.</summary>
    BeginIf,

    /// <summary>조건 안에서 같은 깊이의 다른 갈래로 넘어간다.</summary>
    BeginElseIf,

    /// <summary>조건 체인을 닫는다.</summary>
    EndIf,

    /// <summary>이 줄을 첫 옵션 라벨로 삼아 선택 블록을 연다.</summary>
    BeginChoice,

    /// <summary>이 줄을 같은 블록의 다음 옵션 라벨로 삼는다.</summary>
    BeginNextOption,

    /// <summary>선택 블록을 닫는다.</summary>
    EndChoice
}

/// <param name="Label">드롭다운에 그대로 보이는 문구.</param>
/// <param name="IsAvailable">현재 DialogueNode의 Settings 범위 또는 게임 전역 조건에 포함되는가.</param>
public sealed record ConditionChoice(
    ConditionChoiceKind Kind,
    string? ConditionId,
    string Label,
    bool IsAvailable = true)
{
    /// <summary>
    /// 선택 전환의 OptionId는 여기서 만들지 않는다 — 드롭다운 항목은 여러 줄이 공유하는
    /// 정적 목록이라, 여기서 발급하면 서로 다른 줄이 같은 Id를 받는다.
    /// null로 두면 <c>ProjectEditor.SetLineTransition</c>이 기존 Id를 잇거나 새로 발급한다.
    /// </summary>
    public LineConditionTransition? ToTransition()
    {
        return Kind switch
        {
            ConditionChoiceKind.BeginIf => LineConditionTransition.BeginIf(ConditionId!),
            ConditionChoiceKind.BeginElseIf => LineConditionTransition.BeginElseIf(ConditionId!),
            ConditionChoiceKind.EndIf => LineConditionTransition.EndIf(),
            ConditionChoiceKind.BeginChoice => LineConditionTransition.BeginChoice(),
            ConditionChoiceKind.BeginNextOption => LineConditionTransition.BeginNextOption(),
            ConditionChoiceKind.EndChoice => LineConditionTransition.EndChoice(),
            _ => null
        };
    }
}

/// <summary>
/// 줄 하나의 조건 드롭다운에 무엇을 보여 줄지 정한다.
///
/// 이 드롭다운은 "이 줄이 조건에 포함되는가"를 표시하는 것이 아니다.
/// <b>이 줄에서 조건 흐름을 바꿀 것인지</b>를 고르는 자리다. 그래서 선택지의 의미가
/// 바로 앞 줄까지의 상태에 따라 달라진다. 프로젝트의 모든 SetNode 조건을 전역 노출하지 않고,
/// 현재 DialogueNode에 Settings link로 연결된 SetNode와 게임 전역 조건만 사용한다.
/// </summary>
public static class ConditionChoices
{
    public const string InheritOutsideLabel = "조건 없음";
    public const string InheritInsideLabel = "현재 조건 유지";
    public const string EndIfLabel = "조건 종료";
    public const string BeginChoiceLabel = "선택지 시작 (이 줄이 첫 옵션)";
    public const string InheritInsideChoiceLabel = "현재 옵션 유지";
    public const string NextOptionLabel = "다음 옵션 (이 줄이 라벨)";
    public const string EndChoiceLabel = "선택지 끝";

    /// <summary>
    /// <paramref name="preceding"/>는 이 줄의 전환을 적용하기 전의 갈래다.
    /// null이면 조건 바깥이다. 현재 전환이 연결 해제로 인해 사용할 수 없게 되었더라도
    /// <paramref name="currentTransition"/>을 그대로 선택할 수 있도록 목록에 보존한다.
    /// </summary>
    public static IReadOnlyList<ConditionChoice> For(
        ConditionBranch? preceding,
        DialogueNode dialogue,
        StoryProject project,
        GameDefinition? definition = null,
        LineConditionTransition? currentTransition = null)
    {
        ArgumentNullException.ThrowIfNull(dialogue);
        ArgumentNullException.ThrowIfNull(project);

        AvailableConditionCatalog catalog = AvailableConditionResolver.Resolve(
            project,
            dialogue.Id,
            definition);
        var choices = new List<ConditionChoice>();

        if (preceding is null)
        {
            // 바깥이다. 조건을 고르면 새 갈래가 열리고, 선택지 시작을 고르면 블록이 열린다.
            choices.Add(new ConditionChoice(ConditionChoiceKind.Inherit, null, InheritOutsideLabel));

            foreach (AvailableCondition condition in catalog.Conditions)
            {
                choices.Add(new ConditionChoice(
                    ConditionChoiceKind.BeginIf,
                    condition.Id,
                    condition.DisplayName));
            }

            choices.Add(new ConditionChoice(ConditionChoiceKind.BeginChoice, null, BeginChoiceLabel));
        }
        else if (preceding.IsChoice)
        {
            // 선택 블록 안이다. 조건 항목은 제시하지 않는다 — 두 체인의 중첩은 지원하지 않는다.
            choices.Add(new ConditionChoice(ConditionChoiceKind.Inherit, null, InheritInsideChoiceLabel));
            choices.Add(new ConditionChoice(ConditionChoiceKind.BeginNextOption, null, NextOptionLabel));
            choices.Add(new ConditionChoice(ConditionChoiceKind.EndChoice, null, EndChoiceLabel));
        }
        else
        {
            // 안이다. 유지 / 다른 조건으로 전환 / 종료.
            choices.Add(new ConditionChoice(ConditionChoiceKind.Inherit, null, InheritInsideLabel));

            foreach (AvailableCondition condition in catalog.Conditions)
            {
                // 지금 적용 중인 조건을 "새 전환 대상"으로 다시 보여 줄 이유가 없다.
                if (string.Equals(condition.Id, preceding.ConditionId, StringComparison.Ordinal))
                {
                    continue;
                }

                choices.Add(new ConditionChoice(
                    ConditionChoiceKind.BeginElseIf,
                    condition.Id,
                    condition.DisplayName));
            }

            choices.Add(new ConditionChoice(ConditionChoiceKind.EndIf, null, EndIfLabel));
        }

        PreserveUnavailableCurrent(
            choices,
            currentTransition,
            project,
            definition);

        return choices;
    }

    /// <summary>지금 줄의 전환이 목록의 어느 항목에 해당하는지.</summary>
    public static ConditionChoice Current(
        IReadOnlyList<ConditionChoice> choices,
        LineConditionTransition? transition)
    {
        if (transition is null)
        {
            return choices[0];
        }

        ConditionChoiceKind kind = KindOf(transition);

        return choices.FirstOrDefault(choice =>
                   choice.Kind == kind &&
                   string.Equals(choice.ConditionId, transition.ConditionId, StringComparison.Ordinal))
               ?? choices[0];
    }

    public static string DisplayName(ConditionDefinition condition)
    {
        return string.IsNullOrWhiteSpace(condition.Name)
            ? condition.Id
            : condition.Name;
    }

    private static void PreserveUnavailableCurrent(
        List<ConditionChoice> choices,
        LineConditionTransition? transition,
        StoryProject project,
        GameDefinition? definition)
    {
        if (transition is null || !transition.OpensBranch || transition.ConditionId is null)
        {
            return;
        }

        ConditionChoiceKind kind = KindOf(transition);
        bool alreadyPresent = choices.Any(choice =>
            choice.Kind == kind &&
            string.Equals(choice.ConditionId, transition.ConditionId, StringComparison.Ordinal));

        if (alreadyPresent)
        {
            return;
        }

        AvailableCondition? known = AvailableConditionResolver.FindKnown(
            project,
            definition,
            transition.ConditionId);
        var unavailable = new ConditionChoice(
            kind,
            transition.ConditionId,
            AvailableConditionResolver.UnavailableLabel(known, transition.ConditionId),
            IsAvailable: false);

        int endIndex = choices.FindIndex(choice => choice.Kind == ConditionChoiceKind.EndIf);
        if (endIndex < 0)
        {
            choices.Add(unavailable);
        }
        else
        {
            choices.Insert(endIndex, unavailable);
        }
    }

    private static ConditionChoiceKind KindOf(LineConditionTransition transition)
    {
        return transition.Kind switch
        {
            ConditionTransitionKind.BeginIf => ConditionChoiceKind.BeginIf,
            ConditionTransitionKind.BeginElseIf => ConditionChoiceKind.BeginElseIf,
            ConditionTransitionKind.BeginChoice => ConditionChoiceKind.BeginChoice,
            ConditionTransitionKind.BeginNextOption => ConditionChoiceKind.BeginNextOption,
            ConditionTransitionKind.EndChoice => ConditionChoiceKind.EndChoice,
            _ => ConditionChoiceKind.EndIf
        };
    }
}
