using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// 발행 결과는 불변이다.
///
/// v1을 발행한 뒤 작업 노드를 아무리 고쳐도 v1이 바뀌지 않아야 한다. 이것이 성립하지 않으면
/// 버전 번호를 매길 이유가 없고, "그때 그 출력을 다시 만들어 줘"에 답할 수 없다.
/// </summary>
public class PublishTests
{
    [Fact]
    public void 발행_결과는_Id와_버전과_스키마와_해시를_가진다()
    {
        var sample = new Sample();
        sample.Line("첫 줄");

        DialogueResult result = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        Assert.StartsWith("rs_", result.Identity.ResultId, StringComparison.Ordinal);
        Assert.Equal(1, result.Identity.Version);
        Assert.Equal(DialogueResult.CurrentSchemaVersion, result.Identity.SchemaVersion);
        Assert.StartsWith("sha256:", result.Identity.ContentHash, StringComparison.Ordinal);
        Assert.True(result.Identity.IsPublished);
    }

    [Fact]
    public void v1을_발행한_뒤_작업_노드를_고쳐도_v1은_바뀌지_않는다()
    {
        var sample = new Sample();
        string line = sample.Line("처음 내용");
        DialogueResult v1 = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;
        string hashBefore = v1.Identity.ContentHash;

        sample.Editor.SetScriptLineText(sample.Script.Id, line, "라루", "완전히 다른 내용");
        sample.Editor.InsertScriptLine(sample.Script.Id);

        Assert.Equal("처음 내용", v1.Lines[0].Text);
        Assert.Single(v1.Lines);
        Assert.Equal(hashBefore, v1.Identity.ContentHash);

        // 보관소에서 다시 꺼내도 마찬가지다.
        DialogueResult stored = sample.Project.Results.FindDialogue(v1.Identity.ResultId, 1)!;
        Assert.Equal("처음 내용", stored.Lines[0].Text);
    }

    [Fact]
    public void 내용이_바뀌면_같은_계보의_v2가_생긴다()
    {
        var sample = new Sample();
        string line = sample.Line("처음");
        DialogueResult v1 = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        sample.Editor.SetScriptLineText(sample.Script.Id, line, string.Empty, "고침");
        PublishOutcome<DialogueResult> outcome = sample.Editor.PublishDialogue(sample.Dialogue.Id);

        Assert.True(outcome.Created);
        Assert.Equal(v1.Identity.ResultId, outcome.Result.Identity.ResultId);
        Assert.Equal(2, outcome.Result.Identity.Version);
        Assert.NotEqual(v1.Identity.ContentHash, outcome.Result.Identity.ContentHash);
        Assert.Equal(2, sample.Project.Results.DialogueResults.Count);
    }

    /// <summary>
    /// 저장 버튼을 두 번 눌렀다는 이유로 v2, v3이 쌓이면 어느 것이 의미 있는 버전인지
    /// 알 수 없게 된다. 판정 기준은 내용 해시 하나다.
    /// </summary>
    [Fact]
    public void 같은_내용을_다시_발행하면_새_버전을_만들지_않는다()
    {
        var sample = new Sample();
        sample.Line("그대로");
        DialogueResult first = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        PublishOutcome<DialogueResult> second = sample.Editor.PublishDialogue(sample.Dialogue.Id);

        Assert.False(second.Created);
        Assert.Same(first, second.Result);
        Assert.Single(sample.Project.Results.DialogueResults);
    }

    /// <summary>
    /// 같은 작업 상태에서는 몇 번을 계산해도 같은 해시가 나와야 한다.
    /// 발행 시각이나 목록 순서가 해시에 새어 들어가면 "같은 내용"의 정의가 흔들린다.
    /// </summary>
    [Fact]
    public void 같은_작업_상태는_언제_계산해도_같은_해시를_만든다()
    {
        var sample = new Sample();
        sample.Line("결정적");
        sample.Line("두 번째");

        DialogueResult first = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        // 값을 넣었다가 되돌려 원래 상태로 돌아온다.
        string line = sample.Script.ActiveLines.First().Id;
        sample.Editor.SetScriptLineText(sample.Script.Id, line, "라루", "잠깐 다른 값");
        sample.Editor.Undo();

        PublishOutcome<DialogueResult> again = sample.Editor.PublishDialogue(sample.Dialogue.Id);

        Assert.False(again.Created);
        Assert.Equal(first.Identity.ContentHash, again.Result.Identity.ContentHash);
    }

    [Fact]
    public void 대본이_없으면_발행하지_않는다()
    {
        var sample = new Sample();
        DialogueNode empty = sample.Editor.AddDialogueNode(sample.File.Id, name: "대본 없음");

        PublishRejectedException error = Assert.Throws<PublishRejectedException>(
            () => sample.Editor.PublishDialogue(empty.Id));

        Assert.Contains(error.Problems, problem => problem.Kind == PublishProblemKind.MissingScript);
        Assert.Empty(sample.Project.Results.DialogueResults);
    }

    [Fact]
    public void 알_수_없는_조건을_가리키면_발행하지_않는다()
    {
        var sample = new Sample();
        sample.Line("조건", LineConditionTransition.BeginIf("cd_없는조건"));

        PublishRejectedException error = Assert.Throws<PublishRejectedException>(
            () => sample.Editor.PublishDialogue(sample.Dialogue.Id));

        Assert.Contains(error.Problems, problem => problem.Kind == PublishProblemKind.UnknownCondition);
    }

    [Fact]
    public void 짝이_맞지_않는_조건_구조는_발행하지_않는다()
    {
        var sample = new Sample();
        sample.Line("엉뚱한 종료", LineConditionTransition.EndIf());

        PublishRejectedException error = Assert.Throws<PublishRejectedException>(
            () => sample.Editor.PublishDialogue(sample.Dialogue.Id));

        Assert.Contains(
            error.Problems,
            problem => problem.Kind == PublishProblemKind.InvalidConditionStructure);
    }

    /// <summary>
    /// 고아 데이터는 알리되 발행을 막지 않는다. 막으면 대본을 한 줄 지웠다는 이유로
    /// 프로젝트 전체를 발행할 수 없게 된다.
    /// </summary>
    [Fact]
    public void 고아_조건은_알리되_발행을_막지_않는다()
    {
        var sample = new Sample();
        string opening = sample.Line("사라질 줄", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        string closing = sample.Line("함께 사라질 줄", LineConditionTransition.EndIf());
        sample.Line("남는 줄");

        // 대본에서 조건 갈래 전체가 빠졌다. 조건 데이터는 지우지 않고 고아로 남는다.
        sample.Editor.RetireScriptLine(sample.Script.Id, opening);
        sample.Editor.RetireScriptLine(sample.Script.Id, closing);

        DialogueDraft draft = sample.Editor.InspectDialoguePublish(sample.Dialogue.Id);

        Assert.Contains(draft.Problems, problem => problem.Kind == PublishProblemKind.OrphanData);
        Assert.True(draft.CanPublish);

        DialogueResult result = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;
        Assert.Equal("남는 줄", Assert.Single(result.Lines).Text);

        // 고아 데이터는 결과에 들어가지 않지만 작업 노드에는 그대로 남아 있다.
        Assert.Equal(
            new[] { opening, closing },
            sample.Dialogue.LineExtensions.Select(extension => extension.LineId));
    }

    [Fact]
    public void 결과는_발행_시점의_조건_이름과_식을_얼린다()
    {
        var sample = new Sample();
        sample.Line("조건", LineConditionTransition.BeginIf(sample.ConditionA.Id));

        DialogueResult result = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;
        sample.Editor.UpdateCondition(sample.ConditionA.Id, "이름이 바뀜", "favor >= 99");

        DialogueResultTransition transition = result.Lines[0].Transition!;
        Assert.Equal("호감 높음", transition.ConditionName);
        Assert.Equal("favor >= 5", transition.Expression);
    }

    [Fact]
    public void 결과는_발행_시점의_대본_개정_번호를_기록한다()
    {
        var sample = new Sample();
        string line = sample.Line("첫 내용");
        sample.Editor.SetScriptLineText(sample.Script.Id, line, "윌로", "고친 내용");

        DialogueResult result = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        Assert.Equal(sample.Script.Id, result.SourceScriptId);
        Assert.Equal(sample.Script.SourceRevision, result.SourceScriptRevision);

        // 줄을 만들고 두 번 고쳤으므로 개정 번호는 3이다.
        Assert.Equal(3, result.Lines[0].Revision);
    }

    [Fact]
    public void 발행은_되돌릴_수_있고_되돌려도_남은_결과의_내용은_그대로다()
    {
        var sample = new Sample();
        string line = sample.Line("처음");
        DialogueResult v1 = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        sample.Editor.SetScriptLineText(sample.Script.Id, line, string.Empty, "고침");
        sample.Editor.PublishDialogue(sample.Dialogue.Id);
        Assert.Equal(2, sample.Project.Results.DialogueResults.Count);

        sample.Editor.Undo();

        // v2는 목록에서 빠졌지만 v1의 내용은 바뀌지 않았다.
        Assert.Single(sample.Project.Results.DialogueResults);
        DialogueResult remaining = sample.Project.Results.DialogueResults[0];
        Assert.Equal(v1.Identity.ContentHash, remaining.Identity.ContentHash);
        Assert.Equal("처음", remaining.Lines[0].Text);
    }
}
