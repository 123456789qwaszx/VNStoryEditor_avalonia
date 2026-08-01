using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests;

/// <summary>
/// 테스트가 공유하는 작은 프로젝트 하나.
/// 조건 두 개와 대사 노드 하나, 그리고 이동할 대상 노드 두 개를 갖춘다.
/// </summary>
internal sealed class Sample
{
    public Sample()
    {
        Editor = new ProjectEditor(new StoryProject { Title = "테스트" });

        SetNode = Editor.AddSetNode(name: "설정");
        ConditionA = Editor.AddCondition(SetNode.Id, "호감 높음", "favor >= 5");
        ConditionB = Editor.AddCondition(SetNode.Id, "신뢰 높음", "trust >= 3");

        Dialogue = Editor.AddDialogueNode(name: "본문");
        Dialogue.Lines.Clear();

        TargetA = Editor.AddDialogueNode(name: "A로 간다");
        TargetB = Editor.AddDialogueNode(name: "B로 간다");
        TargetDefault = Editor.AddDialogueNode(name: "기본으로 간다");
    }

    public ProjectEditor Editor { get; }

    public StoryProject Project => Editor.Project;

    public SetNode SetNode { get; }

    public ConditionDefinition ConditionA { get; }

    public ConditionDefinition ConditionB { get; }

    public DialogueNode Dialogue { get; }

    public DialogueNode TargetA { get; }

    public DialogueNode TargetB { get; }

    public DialogueNode TargetDefault { get; }

    /// <summary>대사 노드에 줄을 하나 붙이고 그 줄을 돌려준다.</summary>
    public LineBox Line(string text, LineConditionTransition? transition = null)
    {
        LineBox line = Editor.AddLine(Dialogue.Id);
        Editor.SetLineText(Dialogue.Id, line.Id, string.Empty, text);

        if (transition is not null)
        {
            Editor.SetLineTransition(Dialogue.Id, line.Id, transition);
        }

        return line;
    }

    /// <summary>작업지시서 6.2의 예시를 그대로 만든다.</summary>
    public (LineBox L0, LineBox L1, LineBox L2, LineBox L3, LineBox L4, LineBox L5, LineBox L6) BuildSpecExample()
    {
        LineBox l0 = Line("0");
        LineBox l1 = Line("1", LineConditionTransition.BeginIf(ConditionA.Id));
        LineBox l2 = Line("2");
        LineBox l3 = Line("3", LineConditionTransition.BeginElseIf(ConditionB.Id));
        LineBox l4 = Line("4");
        LineBox l5 = Line("5", LineConditionTransition.EndIf());
        LineBox l6 = Line("6");

        return (l0, l1, l2, l3, l4, l5, l6);
    }
}
