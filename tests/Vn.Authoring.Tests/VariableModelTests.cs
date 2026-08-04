using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>그룹 1(X2·X6·X7) — 설정노드 변수 모델. 저장 형식과 선언 출력은 불변이어야 한다.</summary>
public class VariableModelTests
{
    private static (ProjectEditor Editor, SetNode Node) BuildEditor()
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_vars", "테스트", "story/vars.vnstory.json");
        project.Files.Add(file);
        var editor = new ProjectEditor(project);
        SetNode node = editor.AddSetNode(file.Id, name: "설정");
        return (editor, node);
    }

    [Fact]
    public void 기본_타입_float은_저장_파일에_쓰이지_않는다()
    {
        (ProjectEditor editor, SetNode node) = BuildEditor();
        editor.SetAssignments(node.Id, [new VariableAssignment { Variable = "favor", Value = "0" }]);

        string saved = StoryFileJson.Write(editor.Project.Files[0]);

        // X2 — 기존 프로젝트 파일이 한 글자도 바뀌지 않아야 한다.
        Assert.DoesNotContain("\"type\"", saved, StringComparison.Ordinal);

        StoryFile reloaded = StoryFileJson.Read(saved);
        SetNode readBack = Assert.IsType<SetNode>(reloaded.Nodes[0]);
        Assert.Equal(VariableAssignment.FloatType, readBack.Assignments[0].Type);
    }

    [Fact]
    public void 기본이_아닌_타입은_왕복한다()
    {
        (ProjectEditor editor, SetNode node) = BuildEditor();
        editor.SetAssignments(
            node.Id,
            [new VariableAssignment { Variable = "contract_signed", Value = "false", Type = "bool" }]);

        StoryFile reloaded = StoryFileJson.Read(StoryFileJson.Write(editor.Project.Files[0]));
        SetNode readBack = Assert.IsType<SetNode>(reloaded.Nodes[0]);

        Assert.Equal("bool", readBack.Assignments[0].Type);
    }

    [Fact]
    public void 슬라이더_범위는_등록했을_때만_저장되고_왕복한다()
    {
        (ProjectEditor editor, SetNode node) = BuildEditor();

        // 미등록 범위 → 저장 파일에 키 없음 + 기본 -5~+5.
        editor.SetAssignments(node.Id, [new VariableAssignment { Variable = "favor", Value = "0" }]);
        string saved = StoryFileJson.Write(editor.Project.Files[0]);
        Assert.DoesNotContain("sliderMin", saved, StringComparison.Ordinal);
        Assert.Equal(-5, node.Assignments[0].EffectiveSliderMin);
        Assert.Equal(5, node.Assignments[0].EffectiveSliderMax);

        // 등록하면 왕복한다.
        editor.SetAssignments(
            node.Id,
            [new VariableAssignment { Variable = "favor", Value = "0", SliderMin = 0, SliderMax = 100 }]);
        StoryFile reloaded = StoryFileJson.Read(StoryFileJson.Write(editor.Project.Files[0]));
        VariableAssignment readBack = ((SetNode)reloaded.Nodes[0]).Assignments[0];
        Assert.Equal(0, readBack.SliderMin);
        Assert.Equal(100, readBack.SliderMax);
        Assert.Equal(0, readBack.EffectiveSliderMin);
        Assert.Equal(100, readBack.EffectiveSliderMax);
    }

    [Fact]
    public void set_출력은_슬라이더_도입과_무관하게_그대로다()
    {
        // X6 수용 — 값 문자열이 그대로 저장되므로 <<set>> 출력은 바이트 단위 불변.
        // 여기서는 저장 계층을 못박고, 이미터 바이트는 골든(Story_golden_ep)이 지킨다.
        (ProjectEditor editor, SetNode node) = BuildEditor();
        editor.SetAssignments(
            node.Id,
            [new VariableAssignment { Variable = "favor", Value = "0", SliderMin = -10, SliderMax = 10 }]);

        var file = editor.Project.Files[0];
        StoryFile reloaded = StoryFileJson.Read(StoryFileJson.Write(file));

        Assert.Equal("0", ((SetNode)reloaded.Nodes[0]).Assignments[0].Value);
    }

    [Fact]
    public void 타입만_바뀌어도_편집으로_인식된다()
    {
        (ProjectEditor editor, SetNode node) = BuildEditor();
        editor.SetAssignments(node.Id, [new VariableAssignment { Variable = "favor", Value = "0" }]);

        editor.SetAssignments(
            node.Id,
            [new VariableAssignment { Variable = "favor", Value = "0", Type = "bool" }]);
        Assert.Equal("bool", node.Assignments[0].Type);

        editor.Undo();
        Assert.Equal(
            VariableAssignment.FloatType,
            ((SetNode)editor.Project.FindNode(node.Id)!).Assignments[0].Type);
    }
}
