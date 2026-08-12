using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Tests;

/// <summary>
/// G3-1 — <b>LineId를 텍스트 경계에서 잃지 않는다.</b>
///
/// 엑셀 경로는 시트의 LineId 열로 줄의 신원을 이미 알고 있다. 그 지식을 텍스트로 넘어가면서
/// 버리면 확실한 ID 매칭이 내용 추정으로 격하되고, 비슷한 대사가 여러 줄일 때 동기화가
/// <c>Ambiguous</c>를 내면서 <b>기획자의 멀쩡한 저장이 통째로 거부된다</b>(Gate B 6번).
///
/// 표기 정본은 Yarn 접미형 <c>#line:ln_0001</c>이다(D8) — 계약서 C1이 요구하는 형식이고
/// 이미터가 이미 쓰는 형식이라, 정본을 여기 맞추면 표기가 하나도 늘지 않는다.
/// </summary>
public class LineIdRoundTripTests
{
    private static readonly GameDefinition Definition = GameDefinition.Parse("""
        { "speakers": [ { "name": "라루", "characterId": "laru" }, { "name": "윌로", "characterId": "willo" } ] }
        """)!;

    // ── 파서 ────────────────────────────────────────────────────────────────

    [Fact]
    public void 태그가_대사_본문에_남지_않는다()
    {
        ScenarioParseResult parsed = ScenarioTextParser.Parse(
            "라루: 여기서 잠깐 쉬어도 될까? #line:ln_0002", Definition);

        ScenarioLine line = Assert.Single(parsed.Lines);

        Assert.Equal("라루", line.Speaker);
        Assert.Equal("여기서 잠깐 쉬어도 될까?", line.Text);
        Assert.Equal("ln_0002", line.LineId);
    }

    [Fact]
    public void 화자가_없는_줄에서도_태그를_떼어낸다()
    {
        ScenarioParseResult parsed = ScenarioTextParser.Parse("복도는 조용했다. #line:ln_0007", Definition);

        ScenarioLine line = Assert.Single(parsed.Lines);

        Assert.Equal(string.Empty, line.Speaker);
        Assert.Equal("복도는 조용했다.", line.Text);
        Assert.Equal("ln_0007", line.LineId);
    }

    [Fact]
    public void 태그가_없으면_LineId도_없다()
    {
        ScenarioParseResult parsed = ScenarioTextParser.Parse("라루: 그냥 대사", Definition);

        Assert.Null(Assert.Single(parsed.Lines).LineId);
    }

    [Fact]
    public void 뒤에_공백이_있으면_태그가_아니라_본문이다()
    {
        // "#line:" 뒤에 공백이 오면 그건 사람이 쓴 문장이지 태그가 아니다. 임의로 잘라내지 않는다.
        ScenarioParseResult parsed = ScenarioTextParser.Parse(
            "라루: 메모 #line: 여기 확인", Definition);

        ScenarioLine line = Assert.Single(parsed.Lines);

        Assert.Null(line.LineId);
        Assert.Equal("메모 #line: 여기 확인", line.Text);
    }

    // ── 동기화 (Gate B 3·6번) ───────────────────────────────────────────────

    [Fact]
    public void 비슷한_대사가_여럿이어도_ID가_있으면_통째_거부되지_않는다()
    {
        // Gate B 6번. 내용만으로는 "…" 세 줄 중 어느 것이 어느 것인지 확신할 수 없어
        // Ambiguous가 나고 저장이 통째로 거부된다. ID가 있으면 그 일이 구조적으로 없다.
        (ProjectEditor editor, DialogueNode node) = BuildEditor(
            ("ln_001", "라루", "…"),
            ("ln_002", "윌로", "…"),
            ("ln_003", "라루", "…"));

        ScenarioPasteOutcome outcome = editor.ApplyScenarioText(node.Id, """
            라루: … #line:ln_001
            윌로: 말이 없었다. #line:ln_002
            라루: … #line:ln_003
            """, Definition);

        Assert.True(outcome.Applied, outcome.Summary());
        Assert.Empty(outcome.Plan!.Conflicts);
        Assert.Equal("말이 없었다.", editor.Project.FindScript(node.ScriptId!)!.Text("ln_002").Text);
    }

    [Fact]
    public void ID가_같고_문구가_바뀌면_수정이고_LineId는_보존된다()
    {
        (ProjectEditor editor, DialogueNode node) = BuildEditor(
            ("ln_001", "라루", "첫 줄"),
            ("ln_002", "윌로", "둘째 줄"));

        ScenarioPasteOutcome outcome = editor.ApplyScenarioText(node.Id, """
            라루: 첫 줄 #line:ln_001
            윌로: 아주 다른 문장 #line:ln_002
            """, Definition);

        Assert.True(outcome.Applied, outcome.Summary());
        Assert.Equal(1, outcome.Plan!.Count(ScriptSyncKind.Modified));
        Assert.Equal(
            ["ln_001", "ln_002"],
            editor.Project.FindScript(node.ScriptId!)!.ActiveLines.Select(line => line.Id));
    }

    [Fact]
    public void 구간_이동_뒤에도_나머지_LineId가_전부_보존된다()
    {
        // Gate B 3번. 평평화가 구간을 옮기면 줄의 순서가 바뀐다 — 그래도 신원은 그대로여야 한다.
        (ProjectEditor editor, DialogueNode node) = BuildEditor(
            ("ln_001", "윌로", "첫 줄"),
            ("ln_002", "라루", "둘째 줄"),
            ("ln_003", "윌로", "구간 첫 줄"),
            ("ln_004", "라루", "구간 끝 줄"),
            ("ln_005", "윌로", "수렴 지점"));

        // 구간(003·004)이 조건 자리로 올라온다 — 순서만 바뀌고 문구는 그대로다.
        ScenarioPasteOutcome outcome = editor.ApplyScenarioText(node.Id, """
            윌로: 첫 줄 #line:ln_001
            윌로: 구간 첫 줄 #line:ln_003
            라루: 구간 끝 줄 #line:ln_004
            라루: 둘째 줄 #line:ln_002
            윌로: 수렴 지점 #line:ln_005
            """, Definition);

        Assert.True(outcome.Applied, outcome.Summary());

        Assert.Equal(
            ["ln_001", "ln_003", "ln_004", "ln_002", "ln_005"],
            editor.Project.FindScript(node.ScriptId!)!.ActiveLines.Select(line => line.Id));

        // 하나도 새로 발급되지 않았다 — 전부 원래의 신원 그대로다.
        Assert.Equal(0, outcome.Plan!.Count(ScriptSyncKind.Inserted));
    }

    [Fact]
    public void 삽입은_새_ID를_받고_나머지는_그대로다()
    {
        (ProjectEditor editor, DialogueNode node) = BuildEditor(
            ("ln_001", "라루", "첫 줄"),
            ("ln_002", "윌로", "둘째 줄"));

        ScenarioPasteOutcome outcome = editor.ApplyScenarioText(node.Id, """
            라루: 첫 줄 #line:ln_001
            라루: 사이에 끼운 새 줄
            윌로: 둘째 줄 #line:ln_002
            """, Definition);

        Assert.True(outcome.Applied, outcome.Summary());
        Assert.Equal(1, outcome.Plan!.Count(ScriptSyncKind.Inserted));

        List<string> ids = editor.Project.FindScript(node.ScriptId!)!
            .ActiveLines.Select(line => line.Id).ToList();

        Assert.Equal(3, ids.Count);
        Assert.Equal("ln_001", ids[0]);
        Assert.Equal("ln_002", ids[2]);
    }

    [Fact]
    public void 태그가_없는_텍스트는_지금까지대로_내용으로_맞춘다()
    {
        // 회귀 방지 — 평평한 대본 붙여넣기(신원을 모르는 입력)는 동작이 바뀌면 안 된다.
        (ProjectEditor editor, DialogueNode node) = BuildEditor(
            ("ln_001", "라루", "첫 줄"),
            ("ln_002", "윌로", "둘째 줄"));

        ScenarioPasteOutcome outcome = editor.ApplyScenarioText(node.Id, """
            라루: 첫 줄
            윌로: 고친 둘째 줄
            """, Definition);

        Assert.True(outcome.Applied, outcome.Summary());
        Assert.Equal(
            ["ln_001", "ln_002"],
            editor.Project.FindScript(node.ScriptId!)!.ActiveLines.Select(line => line.Id));
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 줄 N개짜리 대사노드. LineId는 <c>ln_001</c>부터 차례로 붙는다 — 발급기가 결정적이라
    /// 테스트가 그 이름을 그대로 쓸 수 있다. 첫 줄은 노드를 만들 때 이미 생겨 있다.
    /// </summary>
    private static (ProjectEditor Editor, DialogueNode Node) BuildEditor(
        params (string LineId, string Speaker, string Text)[] lines)
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_roundtrip", "테스트", "story/roundtrip.vnstory.json");
        project.Files.Add(file);

        int next = 0;
        var editor = new ProjectEditor(project, newLineId: () => $"ln_{++next:D3}");
        DialogueNode node = editor.AddDialogueNode(file.Id, name: "장면");
        string scriptId = node.ScriptId!;

        editor.SetScriptLineText(scriptId, lines[0].LineId, lines[0].Speaker, lines[0].Text);

        foreach ((string lineId, string speaker, string text) in lines.Skip(1))
        {
            ScriptLine created = editor.InsertScriptLine(scriptId);
            Assert.Equal(lineId, created.Id);
            editor.SetScriptLineText(scriptId, created.Id, speaker, text);
        }

        return (editor, node);
    }
}
