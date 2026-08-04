using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Script;

namespace Vn.Authoring.Tests;

/// <summary>
/// X12(a) — ScenarioOnly 붙여넣기. 전량 재생성은 없다: diff는 ScriptSynchronizer를
/// 지나 기존 LineId를 보존하고(불변식 1), 해석 못 한 줄은 목록으로 남는다(규칙 14).
/// </summary>
public class ScenarioPasteTests
{
    private static readonly GameDefinition Definition = GameDefinition.Parse("""
        { "speakers": [ { "name": "라루", "characterId": "laru" }, { "name": "윌로", "characterId": "willo" } ] }
        """)!;

    private static (ProjectEditor Editor, DialogueNode Node) BuildEditor()
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_paste", "테스트", "story/paste.vnstory.json");
        project.Files.Add(file);
        int next = 0;
        var editor = new ProjectEditor(project, newLineId: () => $"ln_{++next:D3}");
        DialogueNode node = editor.AddDialogueNode(file.Id, name: "장면");

        // 자동 생성된 첫 빈 줄은 붙여넣기 시 삭제 후보가 되므로 본문을 채워 시작한다.
        editor.SetScriptLineText(node.ScriptId!, "ln_001", "라루", "첫 줄");
        return (editor, node);
    }

    [Fact]
    public void 붙여넣은_대본이_화자_지문_미등록으로_올바르게_들어온다()
    {
        (ProjectEditor editor, DialogueNode node) = BuildEditor();

        // 수용 기준 1 — 12줄짜리 대본.
        string text = """
            라루: 첫 줄
            윌로: 둘째 줄
            창밖에는 비가 내리고 있었다.
            낯선이: 누구세요?
            라루: 셋째 줄
            윌로: 넷째 줄
            문이 천천히 열렸다.
            라루: 다섯째 줄
            윌로: 여섯째 줄
            라루: 일곱째 줄
            윌로 : 공백 접두는 지문이다
            마지막 지문.
            """;

        ScenarioPasteOutcome outcome = editor.ApplyScenarioText(node.Id, text, Definition);

        Assert.True(outcome.Applied);
        ScriptDocument script = editor.Project.FindScript(node.ScriptId)!;
        List<(string Speaker, string Text)> lines = script.ActiveLines
            .Select(line => (script.Text(line.Id).Speaker, script.Text(line.Id).Text))
            .ToList();

        Assert.Equal(12, lines.Count);
        Assert.Equal(("라루", "첫 줄"), lines[0]);
        Assert.Equal(("", "창밖에는 비가 내리고 있었다."), lines[2]); // 지문
        Assert.Equal(("낯선이", "누구세요?"), lines[3]);              // 미등록 화자도 화자다
        Assert.Equal(("", "윌로 : 공백 접두는 지문이다"), lines[10]);  // 접두 공백 → 지문
        Assert.Contains(outcome.Parsed.Lines, line => line is { Speaker: "낯선이", SpeakerUnregistered: true });
    }

    [Fact]
    public void 수정_삽입_삭제_후에도_나머지_LineId가_전부_보존된다()
    {
        // 수용 기준 2 — 테스트로 고정. 수정 3줄(1·4·8) + 삽입 1줄(2 뒤) + 삭제 1줄(6).
        // (한 구간에 서로 다른 변경이 겹치면 Ambiguous로 적용을 거부하는 것이
        //  이 알고리즘의 설계다 — 그 거부는 아래 별도 테스트가 지킨다.)
        (ProjectEditor editor, DialogueNode node) = BuildEditor();
        string scriptId = node.ScriptId!;

        editor.ApplyScenarioText(node.Id, """
            라루: 첫 줄
            윌로: 둘째 줄
            라루: 셋째 줄
            윌로: 넷째 줄
            라루: 다섯째 줄
            윌로: 여섯째 줄
            라루: 일곱째 줄
            윌로: 여덟째 줄
            """, Definition, confirmDeletes: true);

        ScriptDocument script = editor.Project.FindScript(scriptId)!;
        List<string> before = script.ActiveLines.Select(line => line.Id).ToList();
        Assert.Equal(8, before.Count);

        ScenarioPasteOutcome outcome = editor.ApplyScenarioText(node.Id, """
            라루: 첫 줄 (고침)
            윌로: 둘째 줄
            사이에 끼운 지문.
            라루: 셋째 줄
            윌로: 넷째 줄 (고침)
            라루: 다섯째 줄
            라루: 일곱째 줄
            윌로: 여덟째 줄 (고침)
            """, Definition, confirmDeletes: true);

        Assert.True(outcome.Applied);
        List<string> after = script.ActiveLines.Select(line => line.Id).ToList();

        Assert.Equal(8, after.Count);
        Assert.Equal(before[0], after[0]); // 수정 — 같은 위치 ID 보존
        Assert.Equal(before[1], after[1]); // 유지
        Assert.DoesNotContain(after[2], before); // 삽입만 새 ID
        Assert.Equal(before[2], after[3]); // 유지
        Assert.Equal(before[3], after[4]); // 수정 — 보존
        Assert.Equal(before[4], after[5]); // 유지
        Assert.Equal(before[6], after[6]); // 유지 (여섯째는 삭제)
        Assert.Equal(before[7], after[7]); // 수정 — 보존
        Assert.Equal("첫 줄 (고침)", script.Text(after[0]).Text);

        // 삭제된 여섯째 줄은 지워지지 않고 은퇴로 남는다.
        Assert.Contains(script.Lines, line => line.Id == before[5] && line.IsRetired);
    }

    [Fact]
    public void 한_구간에_변경이_겹쳐_확신할_수_없으면_아무것도_바꾸지_않는다()
    {
        // 불변식 1의 다른 반쪽 — 임의로 잇는 대신 통째로 거부한다.
        (ProjectEditor editor, DialogueNode node) = BuildEditor();
        editor.ApplyScenarioText(
            node.Id, "라루: 첫 줄\n윌로: 둘째 줄\n라루: 셋째 줄", Definition, confirmDeletes: true);

        ScriptDocument script = editor.Project.FindScript(node.ScriptId)!;
        List<string> before = script.ActiveLines.Select(line => line.Id).ToList();

        // 인접한 두 줄을 동시에 고치면 어느 줄이 어느 줄의 수정인지 알 수 없다.
        ScenarioPasteOutcome outcome = editor.ApplyScenarioText(
            node.Id, "라루: 완전히 새 문장 A\n윌로: 완전히 새 문장 B\n라루: 셋째 줄", Definition, confirmDeletes: true);

        Assert.False(outcome.Applied);
        Assert.True(outcome.Plan!.HasConflicts);
        Assert.Equal(before, script.ActiveLines.Select(line => line.Id)); // 전량 무변경
    }

    [Fact]
    public void 삭제는_확인_없이는_적용되지_않는다()
    {
        (ProjectEditor editor, DialogueNode node) = BuildEditor();
        editor.ApplyScenarioText(node.Id, "라루: 첫 줄\n윌로: 둘째 줄", Definition, confirmDeletes: true);

        ScenarioPasteOutcome first = editor.ApplyScenarioText(node.Id, "라루: 첫 줄", Definition);

        Assert.False(first.Applied);
        Assert.True(first.NeedsDeleteConfirmation);
        Assert.Equal(2, editor.Project.FindScript(node.ScriptId)!.ActiveLines.Count()); // 그대로

        ScenarioPasteOutcome second = editor.ApplyScenarioText(node.Id, "라루: 첫 줄", Definition, confirmDeletes: true);
        Assert.True(second.Applied);
        Assert.Single(editor.Project.FindScript(node.ScriptId)!.ActiveLines);
    }

    [Fact]
    public void 조건_문법이_왕복한다_표기가_곧_파싱이다()
    {
        // X11이 만든 전제: ScenarioOnly 표기 = 파싱 문법.
        (ProjectEditor editor, DialogueNode node) = BuildEditor();
        var file = editor.Project.Files[0];
        SetNode set = editor.AddSetNode(file.Id, name: "설정");
        ConditionDefinition condition = editor.AddCondition(set.Id, "호감 높음", "favor >= 5");
        editor.AddSettingsLink(set.Id, node.Id);

        // 기존 첫 줄("라루: 첫 줄")이 닻으로 남고 나머지는 순수 삽입이다.
        editor.ApplyScenarioText(node.Id, """
            라루: 첫 줄
            <<if favor >= 5>>
            윌로: 갈래 안 줄
            <<endif>>
            라루: 갈래 뒤 줄
            """, Definition, confirmDeletes: true);

        ScriptDocument script = editor.Project.FindScript(node.ScriptId)!;
        List<string> ids = script.ActiveLines.Select(line => line.Id).ToList();
        Assert.Equal(3, ids.Count);
        Assert.Equal(ConditionTransitionKind.BeginIf, node.FindExtension(ids[1])!.Transition!.Kind);
        Assert.Equal(condition.Id, node.FindExtension(ids[1])!.Transition!.ConditionId);
        Assert.Equal(ConditionTransitionKind.EndIf, node.FindExtension(ids[2])!.Transition!.Kind);

        // ScenarioOnly로 다시 펼친 텍스트를 그대로 붙여넣으면 완전한 무변경이다.
        RenderedDocument document = WorkingDialoguePreview.ComposePreset(
            editor.Project, node.Id, OutputPresetCatalog.ScenarioOnly, Definition);
        string roundTrip = DocumentPreviewFormatter.Format(document);

        ScenarioPasteOutcome outcome = editor.ApplyScenarioText(node.Id, roundTrip, Definition, confirmDeletes: true);
        Assert.True(outcome.Applied);
        Assert.True(outcome.Plan!.IsNoOp);
        Assert.Equal(ids, editor.Project.FindScript(node.ScriptId)!.ActiveLines.Select(line => line.Id));
    }

    [Fact]
    public void 미지의_조건식은_보정_없이_문제로_남고_전환은_반영되지_않는다()
    {
        (ProjectEditor editor, DialogueNode node) = BuildEditor();

        ScenarioPasteOutcome outcome = editor.ApplyScenarioText(node.Id, """
            라루: 첫 줄
            <<if 미지의식 >= 1>>
            윌로: 갈래 줄
            <<endif>>
            라루: 뒤 줄
            """, Definition, confirmDeletes: true);

        Assert.True(outcome.Applied); // 대사는 들어온다
        Assert.Contains(outcome.Problems, problem => problem.Contains("미지의식", StringComparison.Ordinal));

        ScriptDocument script = editor.Project.FindScript(node.ScriptId)!;
        string branchLineId = script.ActiveLines.Skip(1).First().Id;
        Assert.Null(node.FindExtension(branchLineId)?.Transition); // 추측해 잇지 않았다
    }

    [Fact]
    public void 해석_못_한_줄은_조용히_사라지지_않고_목록에_남는다()
    {
        (ProjectEditor editor, DialogueNode node) = BuildEditor();

        ScenarioPasteOutcome outcome = editor.ApplyScenarioText(node.Id, """
            라루: 첫 줄
            -> 선택지 라벨은 이 파서의 비범위다
            <<camera_shake>>
            [주의] 장식 줄
            윌로: 둘째 줄
            """, Definition, confirmDeletes: true);

        Assert.True(outcome.Applied);
        Assert.Equal(3, outcome.Parsed.UnparsedLines.Count);
        Assert.Equal(2, editor.Project.FindScript(node.ScriptId)!.ActiveLines.Count());
    }

    [Fact]
    public void 선택_전환은_붙여넣기가_건드리지_않는다()
    {
        (ProjectEditor editor, DialogueNode node) = BuildEditor();
        editor.ApplyScenarioText(node.Id, "라루: 첫 줄\n선택 라벨\n윌로: 갈래 줄", Definition, confirmDeletes: true);

        ScriptDocument script = editor.Project.FindScript(node.ScriptId)!;
        string labelId = script.ActiveLines.Skip(1).First().Id;
        editor.SetLineTransition(node.Id, labelId, LineConditionTransition.BeginChoice("op_test"));

        // 같은 본문을 다시 붙여넣어도(텍스트에는 선택 구조가 없다) 선택 전환은 남는다.
        ScenarioPasteOutcome outcome = editor.ApplyScenarioText(
            node.Id, "라루: 첫 줄\n선택 라벨\n윌로: 갈래 줄", Definition, confirmDeletes: true);

        Assert.True(outcome.Applied);
        Assert.Equal(
            ConditionTransitionKind.BeginChoice,
            node.FindExtension(labelId)!.Transition!.Kind);
        Assert.Equal("op_test", node.FindExtension(labelId)!.Transition!.OptionId);
    }
}
