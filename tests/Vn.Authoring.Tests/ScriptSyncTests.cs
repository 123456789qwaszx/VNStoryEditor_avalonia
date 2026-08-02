using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Tests;

/// <summary>
/// 대본을 다시 읽었을 때 LineId가 어디까지 이어지는가.
///
/// 이것이 이 도구 전체에서 가장 조용히 깨질 수 있는 계약이다. LineId가 새로 발급되면
/// 연출·녹음·번역이 한꺼번에 끊어지는데, 화면에는 아무 오류도 나타나지 않는다.
/// </summary>
public class ScriptSyncTests
{
    [Fact]
    public void 같은_내용을_다시_읽으면_LineId가_전부_유지된다()
    {
        var fixture = new SyncFixture("윌로: 하나\n라루: 둘\n윌로: 셋\n");
        string[] before = fixture.LineIds();

        ScriptSyncPlan plan = fixture.Sync("윌로: 하나\n라루: 둘\n윌로: 셋\n");

        Assert.False(plan.HasConflicts);
        Assert.True(plan.IsNoOp);
        Assert.Equal(before, fixture.LineIds());
    }

    [Fact]
    public void 문구를_고치면_LineId는_유지하고_Revision만_오른다()
    {
        var fixture = new SyncFixture("윌로: 하나\n라루: 둘\n윌로: 셋\n");
        string[] before = fixture.LineIds();

        ScriptSyncPlan plan = fixture.Sync("윌로: 하나\n라루: 둘을 고쳤다\n윌로: 셋\n");

        Assert.Equal(1, plan.Count(ScriptSyncKind.Modified));
        Assert.Equal(before, fixture.LineIds());
        Assert.Equal("둘을 고쳤다", fixture.Script.Text(before[1]).Text);
        Assert.Equal(2, fixture.Script.FindLine(before[1])!.Revision);
        Assert.Equal(1, fixture.Script.FindLine(before[0])!.Revision);
    }

    [Fact]
    public void 화자만_고쳐도_LineId는_유지된다()
    {
        var fixture = new SyncFixture("윌로: 하나\n라루: 둘\n윌로: 셋\n");
        string[] before = fixture.LineIds();

        fixture.Sync("윌로: 하나\n윌로: 둘\n윌로: 셋\n");

        Assert.Equal(before, fixture.LineIds());
        Assert.Equal("윌로", fixture.Script.Text(before[1]).Speaker);
    }

    [Fact]
    public void 중간에_줄을_넣어도_앞뒤_LineId가_유지된다()
    {
        var fixture = new SyncFixture("윌로: 하나\n라루: 둘\n윌로: 셋\n");
        string[] before = fixture.LineIds();

        ScriptSyncPlan plan = fixture.Sync("윌로: 하나\n라루: 새 줄\n라루: 둘\n윌로: 셋\n");

        Assert.Equal(1, plan.Count(ScriptSyncKind.Inserted));
        string[] after = fixture.LineIds();
        Assert.Equal(4, after.Length);
        Assert.Equal(before[0], after[0]);
        Assert.Equal(before[1], after[2]);
        Assert.Equal(before[2], after[3]);
        Assert.DoesNotContain(after[1], before);
    }

    [Fact]
    public void 줄을_지우면_은퇴하고_남은_LineId는_그대로다()
    {
        var fixture = new SyncFixture("윌로: 하나\n라루: 둘\n윌로: 셋\n");
        string[] before = fixture.LineIds();

        ScriptSyncPlan plan = fixture.Sync("윌로: 하나\n윌로: 셋\n");

        Assert.Equal(1, plan.Count(ScriptSyncKind.Deleted));
        Assert.Equal(new[] { before[0], before[2] }, fixture.LineIds());

        // 지우지 않고 은퇴시킨다. 무엇이 사라졌는지 나중에 물을 수 있어야 한다.
        ScriptLine retired = fixture.Script.FindLine(before[1])!;
        Assert.True(retired.IsRetired);
        Assert.Equal("둘", fixture.Script.Text(before[1]).Text);
    }

    [Fact]
    public void 줄을_옮기면_LineId가_따라간다()
    {
        var fixture = new SyncFixture("윌로: 하나\n라루: 둘\n윌로: 셋\n");
        string[] before = fixture.LineIds();

        ScriptSyncPlan plan = fixture.Sync("윌로: 셋\n윌로: 하나\n라루: 둘\n");

        Assert.False(plan.HasConflicts);
        Assert.Equal(new[] { before[2], before[0], before[1] }, fixture.LineIds());
        Assert.Contains(plan.Entries, entry => entry.Kind == ScriptSyncKind.Moved);
    }

    /// <summary>
    /// 삭제됐던 문장이 다시 나타나도 옛 Id를 되살리지 않는다.
    /// 그 사이에 그 Id를 가리키던 연출이 무엇을 뜻하는지 알 수 없기 때문이다.
    /// </summary>
    [Fact]
    public void 은퇴한_LineId는_다시_나타난_같은_문장에_재사용되지_않는다()
    {
        var fixture = new SyncFixture("윌로: 하나\n라루: 둘\n");
        string retiredId = fixture.LineIds()[1];

        fixture.Sync("윌로: 하나\n");
        fixture.Sync("윌로: 하나\n라루: 둘\n");

        string[] after = fixture.LineIds();
        Assert.Equal(2, after.Length);
        Assert.NotEqual(retiredId, after[1]);
        Assert.True(fixture.Script.FindLine(retiredId)!.IsRetired);
    }

    [Fact]
    public void 같은_문장이_여러_번_나와도_순서대로_이어진다()
    {
        var fixture = new SyncFixture("윌로: 네\n라루: 중간\n윌로: 네\n");
        string[] before = fixture.LineIds();

        ScriptSyncPlan plan = fixture.Sync("윌로: 네\n라루: 중간을 고쳤다\n윌로: 네\n");

        Assert.False(plan.HasConflicts);
        Assert.Equal(before, fixture.LineIds());
        Assert.Equal("중간을 고쳤다", fixture.Script.Text(before[1]).Text);
    }

    /// <summary>
    /// 한 구간에서 서로 다른 문장이 둘 이상 동시에 바뀌면 어느 줄이 어느 줄의 수정인지
    /// 알 수 없다. <b>임의로 고르지 않고 멈춘다.</b>
    /// </summary>
    [Fact]
    public void 애매한_매칭은_임의로_잇지_않고_충돌로_보고한다()
    {
        var fixture = new SyncFixture("윌로: 하나\n라루: 둘\n윌로: 셋\n");
        string[] before = fixture.LineIds();

        ScriptSyncPlan plan = fixture.Plan("윌로: 하나\n라루: 완전히 다른 둘\n윌로: 완전히 다른 셋\n");

        Assert.True(plan.HasConflicts);
        Assert.NotEmpty(plan.Conflicts);
        Assert.All(
            plan.Conflicts.Where(entry => entry.NewIndex is not null),
            entry => Assert.Null(entry.LineId));

        // 적용을 거부하고 대본은 손대지 않는다.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => fixture.Editor.ApplyScriptSync(plan));
        Assert.Contains("확인이 필요한", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, fixture.LineIds());
        Assert.Equal("둘", fixture.Script.Text(before[1]).Text);
    }

    [Fact]
    public void 동기화는_원본_해시와_개정_번호를_기록한다()
    {
        var fixture = new SyncFixture("윌로: 하나\n");

        Assert.Equal(1, fixture.Script.SourceRevision);
        Assert.Equal(
            ScriptParser.Parse("윌로: 하나\n").ContentHash,
            fixture.Script.SourceContentHash);

        fixture.Sync("윌로: 하나\n라루: 둘\n");

        Assert.Equal(2, fixture.Script.SourceRevision);
        Assert.Equal(
            ScriptParser.Parse("윌로: 하나\n라루: 둘\n").ContentHash,
            fixture.Script.SourceContentHash);
    }

    /// <summary>
    /// LineId가 유지되면 그 줄에 붙은 조건과 연출도 그대로 살아 있어야 한다.
    /// 이것이 LineIndex가 아니라 LineId로 매다는 이유 전부다.
    /// </summary>
    [Fact]
    public void 줄을_삽입해도_기존_줄의_조건과_출구가_살아남는다()
    {
        var fixture = new SyncFixture("윌로: 하나\n라루: 둘\n윌로: 셋\n");
        DialogueNode dialogue = fixture.Dialogue;
        string second = fixture.LineIds()[1];

        fixture.Editor.SetLineTransition(
            dialogue.Id,
            second,
            LineConditionTransition.BeginIf("cd_없어도됨"));

        fixture.Sync("윌로: 앞에 새 줄\n윌로: 하나\n라루: 둘\n윌로: 셋\n");

        Assert.Equal(second, fixture.LineIds()[2]);
        Assert.Equal(
            "cd_없어도됨",
            dialogue.FindExtension(second)!.Transition!.ConditionId);
    }
}

/// <summary>대본 하나와 그것을 읽는 대사 노드. LineId는 예측 가능하게 발급한다.</summary>
internal sealed class SyncFixture
{
    private int _nextLine;

    public SyncFixture(string initialText)
    {
        var project = new StoryProject { Title = "동기화" };
        var file = new StoryFile("sf_sync", "동기화 파일");
        project.Files.Add(file);

        Editor = new ProjectEditor(project, newLineId: () => $"ln_{++_nextLine:D3}");
        Script = Editor.AddScript("동기화 대본");
        Dialogue = Editor.AddDialogueNode(file.Id, name: "장면", scriptId: Script.Id);

        Sync(initialText);
    }

    public ProjectEditor Editor { get; }

    public ScriptDocument Script { get; }

    public DialogueNode Dialogue { get; }

    public ScriptSyncPlan Plan(string text) => Editor.PlanScriptSync(Script.Id, text);

    public ScriptSyncPlan Sync(string text)
    {
        ScriptSyncPlan plan = Plan(text);

        if (!plan.HasConflicts)
        {
            Editor.ApplyScriptSync(plan);
        }

        return plan;
    }

    public string[] LineIds() => Script.ActiveLines.Select(line => line.Id).ToArray();
}
