using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Tests;

/// <summary>
/// <b>개명은 참조를 끌고 간다</b> (2026-08-24 소유자: "설정 노드에서 아이템/능력의 이름을
/// 바꿨을 때 … 기존 것이 미등록으로 되면서 연결이 끊어지는데, 이게 연결이 계속 이어지도록").
///
/// 아이템·능력은 <b>이름이 곧 신원</b>이다. 그것을 붙드는 두 자리 — 조건식의 <c>$이름</c>과
/// 대사 줄의 <c>&lt;&lt;set 이름&gt;&gt;</c> — 도 이름 문자열 하나뿐이라, 등록 쪽에서만 갈면
/// 나머지가 전부 미아가 된다. 이 파일이 지키는 것은 그 <b>한 번의 개명이 세 자리를 함께
/// 움직이는가</b>이다.
///
/// 반대편 빗장도 함께 지킨다: <b>추측해서 잇지 않는다</b>. 줄이 생기거나 사라졌을 때,
/// 이름이 비슷하기만 할 때, A계층 공급 노드의 식일 때는 손대지 않는다.
/// </summary>
public sealed class RenameCarriesReferencesTests
{
    private sealed record World(
        ProjectEditor Editor, StoryFile File, SetNode SetNode, DialogueNode Dialogue, string LineId);

    /// <summary>설정노드 하나(아이템 `열쇠`) · 그것을 쓰는 조건 하나 · 그것을 쓰는 대사 줄 하나.</summary>
    private static World Build(bool ability = false)
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_ch01", "ch01", "story/ch01.vnstory.json");
        project.Files.Add(file);

        int next = 0;
        var editor = new ProjectEditor(project, newLineId: () => $"ln_{++next:D3}");

        SetNode setNode = editor.AddSetNode(file.Id, name: "ch01 설정");
        editor.SetAssignments(setNode.Id, [
            new VariableAssignment
            {
                Variable = "열쇠",
                Value = ability ? "false" : "0",
                Type = ability ? VariableAssignment.BoolType : VariableAssignment.FloatType
            }
        ]);

        editor.AddCondition(setNode.Id, "열쇠 있음", ability ? "$열쇠 == true" : "$열쇠 >= 1");

        ScriptDocument script = editor.AddScript("본문");
        ScriptLine line = editor.InsertScriptLine(script.Id);
        editor.SetScriptLineText(script.Id, line.Id, "라루", "문을 연다");

        DialogueNode dialogue = editor.AddDialogueNode(file.Id, name: "본문", scriptId: script.Id);
        editor.SetLineSetOperations(dialogue.Id, line.Id, [
            new SetOperation { Variable = "열쇠", Operator = SetOperatorKind.Assign, Value = ability ? "true" : "1" }
        ]);

        return new World(editor, file, setNode, dialogue, line.Id);
    }

    private static void Rename(World world, string from, string to)
    {
        List<VariableAssignment> next = world.SetNode.Assignments
            .Select(item => item.Clone())
            .ToList();

        next.Single(item => item.Variable == from).Variable = to;

        world.Editor.SetAssignments(world.SetNode.Id, next);
    }

    private static string ExpressionOf(World world) => world.SetNode.Conditions[0].Expression;

    private static string SetVariableOf(World world) => world.Dialogue
        .FindExtension(world.LineId)!.SetOperations[0].Variable;

    // ── 이 기능의 이유 ──────────────────────────────────────────────────────

    [Fact]
    public void 아이템_개명이_조건식과_대사_줄의_set을_함께_끌고_간다()
    {
        // ⛔ 이것이 무너지면 이름 한 번 고칠 때마다 그 챕터의 조건과 set이 통째로
        //    "(미등록)"이 된다 — 소유자가 보고한 그 모양이다.
        World world = Build();

        Rename(world, "열쇠", "보물열쇠");

        Assert.Equal("$보물열쇠 >= 1", ExpressionOf(world));
        Assert.Equal("보물열쇠", SetVariableOf(world));
    }

    [Fact]
    public void 능력_개명도_같다()
    {
        World world = Build(ability: true);

        Rename(world, "열쇠", "자물쇠따기");

        Assert.Equal("$자물쇠따기 == true", ExpressionOf(world));
        Assert.Equal("자물쇠따기", SetVariableOf(world));
    }

    [Fact]
    public void 되돌리기_한_번이_개명과_전파를_함께_되돌린다()
    {
        // 둘이 갈리면 "되돌렸는데 조건식만 새 이름"이 된다 — 되돌릴 손잡이가 없는 상태다.
        World world = Build();

        Rename(world, "열쇠", "보물열쇠");
        world.Editor.Undo();

        SetNode setNode = (SetNode)world.Editor.Project.FindNode(world.SetNode.Id)!;
        var dialogue = (DialogueNode)world.Editor.Project.FindNode(world.Dialogue.Id)!;

        Assert.Equal("열쇠", setNode.Assignments[0].Variable);
        Assert.Equal("$열쇠 >= 1", setNode.Conditions[0].Expression);
        Assert.Equal("열쇠", dialogue.FindExtension(world.LineId)!.SetOperations[0].Variable);
    }

    // ── 추측해서 잇지 않는다 ────────────────────────────────────────────────

    [Fact]
    public void 이름이_비슷하기만_한_것은_안_딸려간다()
    {
        // 토큰 단위로 갈아 끼운다 — `$열쇠`를 고쳤다고 `$열쇠2`가 따라가면 남의 변수가 바뀐다.
        World world = Build();
        world.Editor.UpdateCondition(
            world.SetNode.Conditions[0].Id, "열쇠 둘", "$열쇠 >= 1 and $열쇠2 >= 1");

        Rename(world, "열쇠", "보물열쇠");

        Assert.Equal("$보물열쇠 >= 1 and $열쇠2 >= 1", ExpressionOf(world));
    }

    [Fact]
    public void 줄이_지워지면_전파하지_않는다()
    {
        // 자리로 짝지을 근거가 사라진다. 첫 줄(`열쇠`)을 지우면 둘째 줄(`지도`)이 그 자리로
        // 올라오는데, 그것을 개명으로 읽으면 조건식의 `$열쇠`가 `$지도`가 되어 버린다.
        World world = Build();
        world.Editor.SetAssignments(world.SetNode.Id, [
            new VariableAssignment { Variable = "열쇠", Value = "0" },
            new VariableAssignment { Variable = "지도", Value = "0" }
        ]);

        world.Editor.SetAssignments(world.SetNode.Id, [
            new VariableAssignment { Variable = "지도", Value = "0" }
        ]);

        Assert.DoesNotContain(world.SetNode.Assignments, item => item.Variable == "열쇠");
        Assert.Equal("$열쇠 >= 1", ExpressionOf(world));
        Assert.Equal("열쇠", SetVariableOf(world));
    }

    [Fact]
    public void 이름을_처음_적는_것은_개명이_아니다()
    {
        // 갓 만든 빈 줄에 이름을 적는 흔한 동작이다 — 여기서 무엇이든 번지면 사고다.
        World world = Build();

        List<VariableAssignment> next = world.SetNode.Assignments
            .Select(item => item.Clone())
            .ToList();
        next.Add(new VariableAssignment { Variable = string.Empty, Value = "0" });
        world.Editor.SetAssignments(world.SetNode.Id, next);

        next = world.SetNode.Assignments.Select(item => item.Clone()).ToList();
        next[1].Variable = "지도";
        world.Editor.SetAssignments(world.SetNode.Id, next);

        Assert.Equal("$열쇠 >= 1", ExpressionOf(world));
        Assert.Equal("열쇠", SetVariableOf(world));
    }

    [Fact]
    public void 이름을_지우는_것도_개명이_아니다()
    {
        // "무엇으로" 바꿀지가 없다. 빈 이름을 번지게 하면 `$ >= 1`이 남는다.
        World world = Build();

        Rename(world, "열쇠", string.Empty);

        Assert.Equal("$열쇠 >= 1", ExpressionOf(world));
        Assert.Equal("열쇠", SetVariableOf(world));
    }

    [Fact]
    public void 맞바꾸기도_성립한다()
    {
        // 한 토큰은 한 번만 매핑된다 — 순차 치환이면 둘 다 같은 이름이 되어 버린다.
        World world = Build();
        world.Editor.SetAssignments(world.SetNode.Id, [
            new VariableAssignment { Variable = "열쇠", Value = "0" },
            new VariableAssignment { Variable = "지도", Value = "0" }
        ]);
        world.Editor.UpdateCondition(
            world.SetNode.Conditions[0].Id, "둘 다", "$열쇠 >= 1 and $지도 >= 1");

        world.Editor.SetAssignments(world.SetNode.Id, [
            new VariableAssignment { Variable = "지도", Value = "0" },
            new VariableAssignment { Variable = "열쇠", Value = "0" }
        ]);

        Assert.Equal("$지도 >= 1 and $열쇠 >= 1", ExpressionOf(world));
    }

    [Fact]
    public void 같은_이름이_서로_다른_곳으로_가면_그_이름은_손대지_않는다()
    {
        // 어느 쪽을 따라야 할지 데이터가 말하지 못한다. 나머지 개명은 그대로 간다.
        World world = Build();
        world.Editor.SetAssignments(world.SetNode.Id, [
            new VariableAssignment { Variable = "열쇠", Value = "0" },
            new VariableAssignment { Variable = "열쇠", Value = "0" },
            new VariableAssignment { Variable = "지도", Value = "0" }
        ]);
        world.Editor.UpdateCondition(
            world.SetNode.Conditions[0].Id, "둘 다", "$열쇠 >= 1 and $지도 >= 1");

        world.Editor.SetAssignments(world.SetNode.Id, [
            new VariableAssignment { Variable = "보물열쇠", Value = "0" },
            new VariableAssignment { Variable = "녹슨열쇠", Value = "0" },
            new VariableAssignment { Variable = "낡은지도", Value = "0" }
        ]);

        Assert.Equal("$열쇠 >= 1 and $낡은지도 >= 1", ExpressionOf(world));
    }

    [Fact]
    public void A계층_공급_노드의_식은_건드리지_않는다()
    {
        // 그 식의 변수는 챕터 스탯이고 주인은 기획자의 엑셀이다. 작가가 아이템을 `trust`라
        // 지었다고 남의 식을 고치면, 이름 하나로 두 계층이 다시 섞인다(§2.4).
        World world = Build();

        SetNode supply = world.Editor.AddSetNode(
            world.File.Id, name: EpisodeSyncService.ConditionSupplyNodeName("ch01"));
        ConditionDefinition planner =
            world.Editor.AddCondition(supply.Id, "신뢰 높음", "$열쇠 >= 3");

        Rename(world, "열쇠", "보물열쇠");

        Assert.Equal("$열쇠 >= 3", planner.Expression);
        Assert.Equal("$보물열쇠 >= 1", ExpressionOf(world));
    }

    [Fact]
    public void 다른_판의_조건식은_건드리지_않는다()
    {
        // 아이템·능력은 오직 챕터 단위로 산다 (2026-08-17) — 같은 이름이라도 다른 판의
        // 것은 다른 변수다(이미터가 판 Id로 네임스페이스를 붙인다).
        World world = Build();

        var other = new StoryFile("sf_ch03", "ch03", "story/ch03.vnstory.json");
        world.Editor.Project.Files.Add(other);
        SetNode otherSet = world.Editor.AddSetNode(other.Id, name: "ch03 설정");
        ConditionDefinition elsewhere =
            world.Editor.AddCondition(otherSet.Id, "열쇠 있음", "$열쇠 >= 1");

        Rename(world, "열쇠", "보물열쇠");

        Assert.Equal("$열쇠 >= 1", elsewhere.Expression);
    }

    // ── 조건 ────────────────────────────────────────────────────────────────

    [Fact]
    public void 조건_이름을_바꿔도_매달린_갈래는_그대로다()
    {
        // 작가가 설정노드에서 고치는 조건 이름 — 줄에 매달린 전환은 <b>Id</b>로 잇는다.
        // 이 성질이 깨지는 날은 조건에 Id가 없어진 날이고, 그러면 이름 한 글자에 그 조건을
        // 쓰던 갈래가 전부 "알 수 없는 조건"이 된다.
        World world = Build();
        ConditionDefinition condition = world.SetNode.Conditions[0];

        world.Editor.SetLineTransitions(world.Dialogue.Id, world.LineId, [
            LineConditionTransition.BeginIf(condition.Id),
            LineConditionTransition.EndIf()
        ]);

        world.Editor.UpdateCondition(condition.Id, "보물열쇠 있음", condition.Expression);

        Assert.Equal("보물열쇠 있음", world.SetNode.Conditions[0].Name);
        Assert.Equal(
            condition.Id,
            world.Dialogue.FindExtension(world.LineId)!.Transitions[0].ConditionId);

        // 그리고 그 조건이 여전히 <b>고를 수 있는 목록</b>에 새 이름으로 서 있다.
        AvailableCondition? available = AvailableConditionResolver
            .Resolve(world.Editor.Project, world.Dialogue.Id)
            .Find(condition.Id);

        Assert.Equal("보물열쇠 있음", available?.DisplayName);
    }

    // ── 화자 ────────────────────────────────────────────────────────────────

    [Fact]
    public void 화자_개명이_이미_쓰인_줄을_끌고_간다()
    {
        World world = Build();

        Assert.Equal(1, world.Editor.RenameSpeaker("라루", "라루엘"));

        ScriptDocument script = world.Editor.Project.FindScript(world.Dialogue.ScriptId!)!;
        Assert.Equal("라루엘", script.Text(world.LineId, script.PrimaryLocale).Speaker);
    }

    [Fact]
    public void 화자_개명은_이미_번역된_locale을_건드리지_않는다()
    {
        // ⚠ 로컬라이징 대비 — `LocalizedLine.Speaker`는 locale별 값이다. 일본어 판이 이미
        //   `ウィロー`면 그것은 이 개명의 대상이 아니고, 아직 번역 전이라 한국어 이름을
        //   그대로 들고 있는 locale은 자연히 함께 따라온다.
        World world = Build();
        ScriptDocument script = world.Editor.Project.FindScript(world.Dialogue.ScriptId!)!;

        world.Editor.SetScriptLineText(script.Id, world.LineId, "ラル", "扉を開く", locale: "ja-JP");
        world.Editor.SetScriptLineText(script.Id, world.LineId, "라루", "문을 연다", locale: "en-US");

        world.Editor.RenameSpeaker("라루", "라루엘");

        Assert.Equal("ラル", script.Text(world.LineId, "ja-JP").Speaker);
        Assert.Equal("라루엘", script.Text(world.LineId, "en-US").Speaker);
        Assert.Equal("라루엘", script.Text(world.LineId, script.PrimaryLocale).Speaker);
    }

    [Fact]
    public void 화자_개명은_줄의_개정판을_올리지_않는다()
    {
        // Revision은 "이 줄의 문구가 바뀌었으니 번역·녹음이 다시 보라"는 신호다. 등록부의
        // 개명은 줄이 말하는 내용을 바꾸지 않는다 — 올리면 화자 하나에 그 화자의 모든 줄이
        // 재작업 대상이 된다.
        World world = Build();
        ScriptDocument script = world.Editor.Project.FindScript(world.Dialogue.ScriptId!)!;
        int before = script.FindLine(world.LineId)!.Revision;

        world.Editor.RenameSpeaker("라루", "라루엘");

        Assert.Equal(before, script.FindLine(world.LineId)!.Revision);
    }

    [Fact]
    public void 화자_개명은_되돌릴_수_있다()
    {
        World world = Build();
        ScriptDocument script = world.Editor.Project.FindScript(world.Dialogue.ScriptId!)!;

        world.Editor.RenameSpeaker("라루", "라루엘");
        world.Editor.Undo();

        script = world.Editor.Project.FindScript(world.Dialogue.ScriptId!)!;
        Assert.Equal("라루", script.Text(world.LineId, script.PrimaryLocale).Speaker);
    }
}
