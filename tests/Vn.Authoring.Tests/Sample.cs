using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Tests;

/// <summary>
/// 테스트가 공유하는 작은 프로젝트 하나.
/// 대본 하나, 조건 두 개, 대사 노드 하나, 그리고 이동할 대상 노드 세 개를 갖춘다.
///
/// LineId와 발행 시각은 예측 가능한 값으로 주입한다. 무작위 Id와 현재 시각은
/// 결과 해시와 동기화 계획을 테스트에서 읽을 수 없게 만든다.
/// </summary>
internal sealed class Sample
{
    private int _nextLineNumber;

    public Sample()
    {
        var project = new StoryProject { Title = "테스트" };
        File = new StoryFile("sf_test", "테스트 파일");
        project.Files.Add(File);

        Editor = new ProjectEditor(
            project,
            now: () => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            newLineId: NextLineId);

        Script = Editor.AddScript("본문 대본");

        SetNode = Editor.AddSetNode(File.Id, name: "설정");
        ConditionA = Editor.AddCondition(SetNode.Id, "호감 높음", "favor >= 5");
        ConditionB = Editor.AddCondition(SetNode.Id, "신뢰 높음", "trust >= 3");

        Dialogue = Editor.AddDialogueNode(File.Id, name: "본문", scriptId: Script.Id);
        SettingsLink = Editor.AddSettingsLink(SetNode.Id, Dialogue.Id);

        TargetA = AddEndNode("A로 간다");
        TargetB = AddEndNode("B로 간다");
        TargetDefault = AddEndNode("기본으로 간다");
    }

    public ProjectEditor Editor { get; }

    public StoryProject Project => Editor.Project;

    public StoryFile File { get; }

    public ScriptDocument Script { get; }

    public SetNode SetNode { get; }

    public ConditionDefinition ConditionA { get; }

    public ConditionDefinition ConditionB { get; }

    public DialogueNode Dialogue { get; }

    public NodeLink SettingsLink { get; }

    public DialogueNode TargetA { get; }

    public DialogueNode TargetB { get; }

    public DialogueNode TargetDefault { get; }

    /// <summary>대본에 줄을 하나 붙이고 그 줄의 LineId를 돌려준다.</summary>
    public string Line(string text, LineConditionTransition? transition = null)
    {
        ScriptLine line = Editor.InsertScriptLine(Script.Id);
        Editor.SetScriptLineText(Script.Id, line.Id, string.Empty, text);

        if (transition is not null)
        {
            Editor.SetLineTransition(Dialogue.Id, line.Id, transition);
        }

        return line.Id;
    }

    /// <summary>작업지시서 6.2의 예시를 그대로 만든다.</summary>
    public (string L0, string L1, string L2, string L3, string L4, string L5, string L6) BuildSpecExample()
    {
        string l0 = Line("0");
        string l1 = Line("1", LineConditionTransition.BeginIf(ConditionA.Id));
        string l2 = Line("2");
        string l3 = Line("3", LineConditionTransition.BeginElseIf(ConditionB.Id));
        string l4 = Line("4");
        string l5 = Line("5", LineConditionTransition.EndIf());
        string l6 = Line("6");

        return (l0, l1, l2, l3, l4, l5, l6);
    }

    /// <summary>대본 한 줄짜리 종점 노드. 발행할 수 있는 상태로 만들어 둔다.</summary>
    private DialogueNode AddEndNode(string name)
    {
        ScriptDocument script = Editor.AddScript(name + " 대본");
        ScriptLine line = Editor.InsertScriptLine(script.Id);
        Editor.SetScriptLineText(script.Id, line.Id, "화자", name);
        return Editor.AddDialogueNode(File.Id, name: name, scriptId: script.Id);
    }

    private string NextLineId() => $"ln_{++_nextLineNumber:D3}";

    /// <summary>
    /// 테스트가 공유하는 작은 게임 정의. samples/Authoring의 3범주 정의와 같은 모양이다.
    /// 범주와 파라미터 순서는 전부 데이터에서 온다 — 테스트도 그 원칙 위에서 쓴다.
    /// </summary>
    public static GameDefinition Definition { get; } = new()
    {
        PresentationCommandCategories =
        {
            new PresentationCategorySpec { Id = "camera", Name = "Camera" },
            new PresentationCategorySpec { Id = "screen_effect", Name = "ScreenEffect" },
            new PresentationCategorySpec { Id = "character_acting", Name = "CharacterActing" }
        },
        PresentationCommands =
        {
            Command("camera.closeup", "클로즈업", "camera", "camera", "closeup"),
            Command("camera.wide", "와이드", "camera", "camera", "wide"),
            Command("screen.shake", "화면 흔들림", "screen_effect", "screen_effect", "shake"),
            Command("screen.flash", "플래시", "screen_effect", "screen_effect", "flash"),
            Command("acting.smile", "미소", "character_acting", "character_acting", "smile")
        }
    };

    private static PresentationCommandSpec Command(
        string id,
        string name,
        string category,
        string output,
        string presetDefault)
    {
        return new PresentationCommandSpec
        {
            Id = id,
            Name = name,
            Category = category,
            OutputCommand = output,
            Parameters =
            {
                new PresentationParameterSpec
                {
                    Name = "preset",
                    Type = "string",
                    Default = presetDefault
                }
            }
        };
    }
}
