using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;
using Vn.Authoring.Script;

namespace Vn.Authoring.Tests;

/// <summary>
/// 연출은 편집 중인 대사 노드가 아니라 <b>발행된 대사 결과</b>를 읽는다.
///
/// 이전 구조에서는 <c>NodeLinkKind.Presentation</c>이 연출 노드를 편집 중인 대사 노드에
/// 실시간으로 이었다. 그 링크와 그것을 검증하던 테스트는 폐기했다. 링크가 있으면
/// 연출가가 작업하는 동안 발밑의 대사가 바뀌고, 완성한 연출표가 어느 대사에 맞는 것인지
/// 아무도 말할 수 없기 때문이다. 여기서 그 자리를 대신하는 불변 조건을 검증한다.
/// </summary>
public class PresentationNodeTests
{
    [Fact]
    public void 연출은_정확한_대사_결과_Id와_버전과_해시를_기억한다()
    {
        PresentationContext context = BuildContext();
        DialogueResult published = context.Editor.PublishDialogue(context.DialogueA.Id).Result;

        context.Editor.SetPresentationSource(
            context.PresentationA.Id,
            published.Identity.ResultId,
            published.Identity.Version);

        DialogueResultReference source = context.PresentationA.Source!.Value;
        Assert.Equal(published.Identity.ResultId, source.ResultId);
        Assert.Equal(1, source.Version);
        Assert.Equal(published.Identity.ContentHash, source.ContentHash);
        Assert.True(source.Matches(published.Identity));
    }

    [Fact]
    public void 발행하지_않은_대사는_연출의_입력이_될_수_없다()
    {
        PresentationContext context = BuildContext();

        Assert.Throws<InvalidOperationException>(() =>
            context.Editor.SetPresentationSource(context.PresentationA.Id, "rs_없음", 1));

        Assert.Null(context.PresentationA.Source);
    }

    [Fact]
    public void 입력_결과의_대사와_LineId는_읽기_전용_스냅샷이다()
    {
        PresentationContext context = BuildContext();
        DialogueResult published = context.Editor.PublishDialogue(context.DialogueA.Id).Result;
        context.Editor.SetPresentationSource(
            context.PresentationA.Id,
            published.Identity.ResultId,
            published.Identity.Version);

        // 발행 뒤 원본 대본을 고쳐도 연출이 보는 것은 얼어붙은 결과 그대로다.
        context.Editor.SetScriptLineText(context.ScriptA.Id, context.FirstLineId, "다른 화자", "다른 대사");

        PresentationWorkspace workspace = PresentationBindingResolver.Resolve(
            context.Project,
            context.PresentationA);

        DialogueResultLine line = workspace.Dialogue!.Lines[0];
        Assert.Equal(context.FirstLineId, line.LineId);
        Assert.Equal("첫 줄", line.Text);
        Assert.False(workspace.IsStale);
    }

    [Fact]
    public void LineId별_command_순서가_유지된다()
    {
        PresentationContext context = BuildContext();
        AttachLatest(context);

        PresentationCommandInstance first = context.Editor.AddPresentationCommand(
            context.PresentationA.Id,
            context.FirstLineId,
            "camera.closeup");
        PresentationCommandInstance second = context.Editor.AddPresentationCommand(
            context.PresentationA.Id,
            context.FirstLineId,
            "screen.fade");

        PresentationLineBinding binding = Assert.Single(context.PresentationA.Bindings);
        Assert.Equal(new[] { first.Id, second.Id }, binding.Commands.Select(item => item.Id));

        context.Editor.MovePresentationCommand(context.PresentationA.Id, second.Id, -1);
        Assert.Equal(new[] { second.Id, first.Id }, binding.Commands.Select(item => item.Id));
    }

    [Fact]
    public void 대상_결과에_없는_LineId의_연출은_고아로_남고_지워지지_않는다()
    {
        PresentationContext context = BuildContext();
        AttachLatest(context);

        context.Editor.AddPresentationCommand(context.PresentationA.Id, "ln_없는줄", "camera.wide");

        PresentationWorkspace workspace = PresentationBindingResolver.Resolve(
            context.Project,
            context.PresentationA);
        ResolvedPresentationBinding orphan = Assert.Single(workspace.Orphans);

        Assert.Equal("ln_없는줄", orphan.Binding.LineId);
        Assert.Null(orphan.Line);

        // 자동 삭제하지 않는다. 연출가가 쓴 것이 말없이 사라지면 안 된다.
        Assert.Contains(context.PresentationA.Bindings, binding => binding.LineId == "ln_없는줄");
    }

    [Fact]
    public void 입력_결과를_다른_버전으로_바꿔도_연출_binding은_그대로다()
    {
        PresentationContext context = BuildContext();
        AttachLatest(context);
        context.Editor.AddPresentationCommand(
            context.PresentationA.Id,
            context.FirstLineId,
            "camera.closeup");

        // 대본을 고치고 다시 발행하면 v2가 생긴다.
        context.Editor.SetScriptLineText(context.ScriptA.Id, context.FirstLineId, "윌로", "고친 첫 줄");
        DialogueResult second = context.Editor.PublishDialogue(context.DialogueA.Id).Result;
        Assert.Equal(2, second.Identity.Version);

        context.Editor.SetPresentationSource(
            context.PresentationA.Id,
            second.Identity.ResultId,
            second.Identity.Version);

        // LineId가 유지되었으므로 연출도 그대로 붙는다.
        PresentationWorkspace workspace = PresentationBindingResolver.Resolve(
            context.Project,
            context.PresentationA);
        Assert.Empty(workspace.Orphans);
        Assert.Equal(2, context.PresentationA.Source!.Value.Version);
    }

    [Fact]
    public void PresentationResult는_대상_대사_결과의_Id와_버전과_해시를_보존한다()
    {
        PresentationContext context = BuildContext();
        DialogueResult dialogue = AttachLatest(context);
        context.Editor.AddPresentationCommand(
            context.PresentationA.Id,
            context.FirstLineId,
            "camera.closeup");

        PresentationResult result = context.Editor.PublishPresentation(context.PresentationA.Id).Result;

        Assert.Equal(dialogue.Identity.ResultId, result.Source.ResultId);
        Assert.Equal(dialogue.Identity.Version, result.Source.Version);
        Assert.Equal(dialogue.Identity.ContentHash, result.Source.ContentHash);
        Assert.Equal(1, result.Identity.Version);
    }

    [Fact]
    public void 입력_결과가_없으면_연출을_발행하지_않는다()
    {
        PresentationContext context = BuildContext();

        PublishRejectedException error = Assert.Throws<PublishRejectedException>(
            () => context.Editor.PublishPresentation(context.PresentationA.Id));

        Assert.Contains(
            error.Problems,
            problem => problem.Kind == PublishProblemKind.MissingSourceResult);
        Assert.Empty(context.Project.Results.PresentationResults);
    }

    [Fact]
    public void 고아_binding은_발행을_막지_않고_orphan으로_표시된다()
    {
        PresentationContext context = BuildContext();
        AttachLatest(context);
        context.Editor.AddPresentationCommand(context.PresentationA.Id, "ln_없는줄", "camera.wide");

        PresentationResult result = context.Editor.PublishPresentation(context.PresentationA.Id).Result;

        PresentationResultBinding orphan = Assert.Single(result.Orphans);
        Assert.Equal("ln_없는줄", orphan.LineId);
    }

    [Fact]
    public void 비활성_command는_결과에_들어가지_않지만_작성_데이터는_남는다()
    {
        PresentationContext context = BuildContext();
        AttachLatest(context);
        PresentationCommandInstance command = context.Editor.AddPresentationCommand(
            context.PresentationA.Id,
            context.FirstLineId,
            "camera.closeup");

        context.Editor.SetPresentationCommandEnabled(context.PresentationA.Id, command.Id, enabled: false);
        PresentationResult result = context.Editor.PublishPresentation(context.PresentationA.Id).Result;

        Assert.Empty(Assert.Single(result.Bindings).Commands);
        Assert.Single(Assert.Single(context.PresentationA.Bindings).Commands);
    }

    [Fact]
    public void 연출_노드는_실행_출구를_가지지_않는다()
    {
        PresentationContext context = BuildContext();

        context.Editor.SetExitTarget(
            context.PresentationA.Id,
            ExitPortKind.Default,
            null,
            context.DialogueB.Id);

        Assert.Null(context.PresentationA.DefaultExitTargetNodeId);
        Assert.Empty(NodeConnections.PortsOf(context.PresentationA, context.Project));
    }

    [Fact]
    public void command_편집은_PresentationContent로_알린다()
    {
        PresentationContext context = BuildContext();
        AttachLatest(context);
        PresentationCommandInstance command = context.Editor.AddPresentationCommand(
            context.PresentationA.Id,
            context.FirstLineId,
            "camera.closeup",
            new Dictionary<string, string> { ["preset"] = "closeup" });

        ProjectChangedEventArgs? change = null;
        context.Editor.Changed += (_, args) => change = args;

        context.Editor.SetPresentationCommandDefinition(
            context.PresentationA.Id,
            command.Id,
            "camera.wide",
            new Dictionary<string, string> { ["preset"] = "wide" });

        Assert.Equal(ProjectChangeKind.PresentationContent, change!.Kind);
        Assert.Equal("camera.wide", command.DefinitionId);
        Assert.Equal("wide", command.Arguments["preset"]);
    }

    private static DialogueResult AttachLatest(PresentationContext context)
    {
        DialogueResult published = context.Editor.PublishDialogue(context.DialogueA.Id).Result;
        context.Editor.SetPresentationSource(
            context.PresentationA.Id,
            published.Identity.ResultId,
            published.Identity.Version);
        return published;
    }

    private static PresentationContext BuildContext()
    {
        var project = new StoryProject { Title = "Presentation" };
        var file = new StoryFile("sf_presentation", "연출", "story/presentation.vnstory.json");
        project.Files.Add(file);

        int nextLine = 0;
        var editor = new ProjectEditor(
            project,
            now: () => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            newLineId: () => $"ln_{++nextLine:D3}");

        ScriptDocument scriptA = editor.AddScript("A 대본");
        ScriptLine firstLine = editor.InsertScriptLine(scriptA.Id);
        editor.SetScriptLineText(scriptA.Id, firstLine.Id, "라루", "첫 줄");
        ScriptLine secondLine = editor.InsertScriptLine(scriptA.Id);
        editor.SetScriptLineText(scriptA.Id, secondLine.Id, "윌로", "둘째 줄");

        ScriptDocument scriptB = editor.AddScript("B 대본");
        ScriptLine onlyLine = editor.InsertScriptLine(scriptB.Id);
        editor.SetScriptLineText(scriptB.Id, onlyLine.Id, "라루", "B 줄");

        DialogueNode dialogueA = editor.AddDialogueNode(file.Id, name: "대사 A", scriptId: scriptA.Id);
        DialogueNode dialogueB = editor.AddDialogueNode(file.Id, name: "대사 B", scriptId: scriptB.Id);
        PresentationNode presentationA = editor.AddPresentationNode(file.Id, name: "연출 A");

        return new PresentationContext(
            project,
            file,
            editor,
            scriptA,
            firstLine.Id,
            dialogueA,
            dialogueB,
            presentationA);
    }

    private sealed record PresentationContext(
        StoryProject Project,
        StoryFile File,
        ProjectEditor Editor,
        ScriptDocument ScriptA,
        string FirstLineId,
        DialogueNode DialogueA,
        DialogueNode DialogueB,
        PresentationNode PresentationA);
}
