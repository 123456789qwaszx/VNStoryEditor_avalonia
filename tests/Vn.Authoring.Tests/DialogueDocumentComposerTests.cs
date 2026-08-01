using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;

namespace Vn.Authoring.Tests;

/// <summary>
/// 구조화된 DialogueNode를 평평한 문서로 펼쳐도 조건 의미와 원본 식별자가 사라지지 않는지 검증한다.
/// Preview 문자열보다 Segment 목록이 먼저이며, Formatter는 그 목록을 읽기만 해야 한다.
/// </summary>
public class DialogueDocumentComposerTests
{
    [Fact]
    public void 연결된_SetNode의_assignment만_링크_순서대로_합성한다()
    {
        var sample = new Sample();
        sample.SetNode.Assignments.Add(new VariableAssignment { Variable = "favor", Value = "0" });

        SetNode unlinked = sample.Editor.AddSetNode(sample.File.Id, name: "연결 안 됨");
        unlinked.Assignments.Add(new VariableAssignment { Variable = "ignored", Value = "1" });

        SetNode second = sample.Editor.AddSetNode(sample.File.Id, name: "두 번째 설정");
        second.Assignments.Add(new VariableAssignment { Variable = "trust", Value = "2" });
        NodeLink secondLink = sample.Editor.AddSettingsLink(second.Id, sample.Dialogue.Id);
        secondLink.Order = 10;

        sample.Line("본문");

        RenderedDocument document = DialogueDocumentComposer.Compose(sample.Project, sample.Dialogue.Id);
        RenderedSegment[] assignments = document.Segments
            .Where(segment => segment.Kind == RenderedSegmentKind.SetAssignment)
            .ToArray();

        Assert.Equal(new[] { "favor", "trust" }, assignments.Select(segment => segment.Variable));
        Assert.DoesNotContain(assignments, segment => segment.Variable == "ignored");

        Assert.Equal(sample.File.Id, assignments[0].Source.StoryFileId);
        Assert.Equal(sample.SetNode.Id, assignments[0].Source.NodeId);
        Assert.Equal(sample.SetNode.Id, assignments[0].Source.SetNodeId);
        Assert.Equal(sample.SettingsLink.Id, assignments[0].Source.LinkId);
        Assert.Equal(secondLink.Id, assignments[1].Source.LinkId);
    }

    [Fact]
    public void 조건_전환과_갈래_출구를_실행_순서대로_평평하게_합성한다()
    {
        var sample = new Sample();
        sample.SetNode.Assignments.Add(new VariableAssignment { Variable = "favor", Value = "0" });
        var (_, l1, l2, l3, l4, l5, _) = sample.BuildSpecExample();

        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, l1.Id, sample.TargetA.Id);
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, l3.Id, sample.TargetB.Id);
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Default, null, sample.TargetDefault.Id);

        RenderedDocument document = DialogueDocumentComposer.Compose(sample.Project, sample.Dialogue.Id);
        RenderedSegment[] meaningful = document.Segments
            .Where(segment => segment.Kind != RenderedSegmentKind.SetAssignment)
            .ToArray();

        Assert.Equal(
            new[]
            {
                RenderedSegmentKind.NodeHeader,
                RenderedSegmentKind.DialogueLine,
                RenderedSegmentKind.ConditionBegin,
                RenderedSegmentKind.DialogueLine,
                RenderedSegmentKind.DialogueLine,
                RenderedSegmentKind.BranchJump,
                RenderedSegmentKind.ConditionElseIf,
                RenderedSegmentKind.DialogueLine,
                RenderedSegmentKind.DialogueLine,
                RenderedSegmentKind.BranchJump,
                RenderedSegmentKind.ConditionEnd,
                RenderedSegmentKind.DialogueLine,
                RenderedSegmentKind.DialogueLine,
                RenderedSegmentKind.DefaultJump,
                RenderedSegmentKind.NodeFooter
            },
            meaningful.Select(segment => segment.Kind));

        RenderedSegment firstJump = meaningful.Single(segment =>
            segment.Kind == RenderedSegmentKind.BranchJump &&
            segment.TargetNodeId == sample.TargetA.Id);
        RenderedSegment secondJump = meaningful.Single(segment =>
            segment.Kind == RenderedSegmentKind.BranchJump &&
            segment.TargetNodeId == sample.TargetB.Id);

        // 갈래 출구는 화면상 마지막 줄이 아니라 갈래를 여는 안정된 LineId를 소유한다.
        Assert.Equal(l1.Id, firstJump.Source.LineId);
        Assert.Equal(l3.Id, secondJump.Source.LineId);
        Assert.Equal(sample.Dialogue.Id, firstJump.Source.NodeId);
        Assert.Equal(sample.File.Id, firstJump.Source.StoryFileId);

        RenderedSegment end = meaningful.Single(segment =>
            segment.Kind == RenderedSegmentKind.ConditionEnd);
        Assert.Equal(l5.Id, end.Source.LineId);

        // l2와 l4가 각 갈래의 마지막 대사이고 그 직후에 jump가 나온다.
        Assert.True(Array.IndexOf(meaningful, firstJump) > Array.FindIndex(meaningful, segment =>
            segment.Kind == RenderedSegmentKind.DialogueLine && segment.Source.LineId == l2.Id));
        Assert.True(Array.IndexOf(meaningful, secondJump) > Array.FindIndex(meaningful, segment =>
            segment.Kind == RenderedSegmentKind.DialogueLine && segment.Source.LineId == l4.Id));
    }

    [Fact]
    public void Yarn_Formatter는_NodeId와_LineId를_사용해_읽기_전용_문서를_만든다()
    {
        var sample = new Sample();
        sample.SetNode.Assignments.Add(new VariableAssignment { Variable = "favor", Value = "0" });

        LineBox opening = sample.Line("맞아요", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Editor.SetLineText(sample.Dialogue.Id, opening.Id, "라루", "맞아요");
        LineBox ending = sample.Line("끝", LineConditionTransition.EndIf());

        sample.Editor.SetExitTarget(
            sample.Dialogue.Id,
            ExitPortKind.Branch,
            opening.Id,
            sample.TargetA.Id);
        sample.Editor.SetExitTarget(
            sample.Dialogue.Id,
            ExitPortKind.Default,
            null,
            sample.TargetDefault.Id);

        RenderedDocument document = DialogueDocumentComposer.Compose(sample.Project, sample.Dialogue.Id);
        string text = YarnPreviewFormatter.Format(document);

        string expected =
            $"title: {sample.Dialogue.Id}\n" +
            $"// name: {sample.Dialogue.Name}\n" +
            "---\n" +
            "<<set $favor = 0>>\n" +
            $"<<if {sample.ConditionA.Expression}>>\n" +
            $"    라루: 맞아요 #line:{opening.Id}\n" +
            $"    <<jump {sample.TargetA.Id}>>\n" +
            "<<endif>>\n" +
            $"끝 #line:{ending.Id}\n" +
            $"<<jump {sample.TargetDefault.Id}>>\n" +
            "===\n";

        Assert.Equal(expected, text);
        Assert.DoesNotContain('\r', text);
    }

    [Fact]
    public void Settings_link가_끊겨도_Transition과_조건식은_보존하고_경고_Segment를_만든다()
    {
        var sample = new Sample();
        LineBox opening = sample.Line("조건 대사", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Editor.RemoveLink(sample.SettingsLink.Id);

        RenderedDocument document = DialogueDocumentComposer.Compose(sample.Project, sample.Dialogue.Id);

        RenderedSegment warning = Assert.Single(document.Segments, segment =>
            segment.Kind == RenderedSegmentKind.Warning);
        RenderedSegment condition = Assert.Single(document.Segments, segment =>
            segment.Kind == RenderedSegmentKind.ConditionBegin);

        Assert.Equal(opening.Id, warning.Source.LineId);
        Assert.Contains("포함되지 않습니다", warning.Text ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(sample.ConditionA.Expression, condition.Expression);
        Assert.Equal(sample.ConditionA.Id, condition.Source.ConditionId);
        Assert.Equal(sample.ConditionA.Id, sample.Dialogue.Lines[0].Transition!.ConditionId);

        string preview = YarnPreviewFormatter.Format(document);
        Assert.Contains("// WARNING:", preview, StringComparison.Ordinal);
        Assert.Contains($"<<if {sample.ConditionA.Expression}>>", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void Composer는_대사가_소속된_StoryFileId를_모든_대사_Segment에_유지한다()
    {
        var sample = new Sample();
        LineBox line = sample.Line("원본 위치");

        RenderedDocument document = DialogueDocumentComposer.Compose(sample.Project, sample.Dialogue.Id);
        RenderedSegment segment = Assert.Single(document.Segments, item =>
            item.Kind == RenderedSegmentKind.DialogueLine);

        Assert.Equal(sample.File.Id, segment.Source.StoryFileId);
        Assert.Equal(sample.Dialogue.Id, segment.Source.NodeId);
        Assert.Equal(line.Id, segment.Source.LineId);
    }

    [Fact]
    public void 선택된_PresentationNode의_활성_Command를_대사_앞에_작성_순서대로_삽입한다()
    {
        var sample = new Sample();
        LineBox line = sample.Line("연출 대사");
        PresentationNode presentation = sample.Editor.AddPresentationNode(sample.File.Id, name: "카메라 연출");
        sample.Editor.SetPresentationTarget(presentation.Id, sample.Dialogue.Id);
        PresentationCommandInstance camera = sample.Editor.AddPresentationCommand(
            presentation.Id,
            line.Id,
            "camera.closeup",
            new Dictionary<string, string> { ["preset"] = "closeup" });
        PresentationCommandInstance acting = sample.Editor.AddPresentationCommand(
            presentation.Id,
            line.Id,
            "acting.smile",
            new Dictionary<string, string> { ["preset"] = "smile" });

        RenderedDocument document = DialogueDocumentComposer.Compose(
            sample.Project,
            sample.Dialogue.Id,
            includedPresentationNodeIds: new HashSet<string>(new[] { presentation.Id }, StringComparer.Ordinal));

        RenderedSegment[] commands = document.Segments
            .Where(segment => segment.Kind == RenderedSegmentKind.PresentationCommand)
            .ToArray();
        RenderedSegment dialogue = document.Segments.Single(segment =>
            segment.Kind == RenderedSegmentKind.DialogueLine && segment.Source.LineId == line.Id);

        Assert.Equal(new[] { camera.Id, acting.Id },
            commands.Select(segment => segment.Source.PresentationCommandId));
        Assert.All(commands, segment => Assert.Equal(line.Id, segment.Source.LineId));
        Assert.All(commands, segment => Assert.Equal(presentation.Id, segment.Source.PresentationNodeId));
        Assert.True(Array.IndexOf(document.Segments.ToArray(), commands[1]) <
                    Array.IndexOf(document.Segments.ToArray(), dialogue));

        string text = YarnPreviewFormatter.Format(document);
        int cameraIndex = text.IndexOf("<<camera preset=closeup>>", StringComparison.Ordinal);
        int actingIndex = text.IndexOf("<<character_acting preset=smile>>", StringComparison.Ordinal);
        int dialogueIndex = text.IndexOf("연출 대사 #line:", StringComparison.Ordinal);
        Assert.True(cameraIndex >= 0 && cameraIndex < actingIndex);
        Assert.True(actingIndex < dialogueIndex);
    }

    [Fact]
    public void 비활성_Command와_선택되지_않은_PresentationNode는_문서에서_제외한다()
    {
        var sample = new Sample();
        LineBox line = sample.Line("본문");
        PresentationNode selected = sample.Editor.AddPresentationNode(sample.File.Id, name: "선택");
        PresentationNode excluded = sample.Editor.AddPresentationNode(sample.File.Id, name: "제외");
        sample.Editor.SetPresentationTarget(selected.Id, sample.Dialogue.Id);
        sample.Editor.SetPresentationTarget(excluded.Id, sample.Dialogue.Id);

        PresentationCommandInstance disabled = sample.Editor.AddPresentationCommand(
            selected.Id, line.Id, "screen.flash");
        sample.Editor.SetPresentationCommandEnabled(selected.Id, disabled.Id, enabled: false);
        sample.Editor.AddPresentationCommand(excluded.Id, line.Id, "camera.wide");

        RenderedDocument document = DialogueDocumentComposer.Compose(
            sample.Project,
            sample.Dialogue.Id,
            includedPresentationNodeIds: new HashSet<string>(new[] { selected.Id }, StringComparer.Ordinal));

        Assert.DoesNotContain(document.Segments, segment =>
            segment.Kind == RenderedSegmentKind.PresentationCommand);
    }

    [Fact]
    public void Presentation_Command_Segment는_연출_파일과_Dialogue_LineId_원본을_유지한다()
    {
        var sample = new Sample();
        var presentationFile = new StoryFile("sf_presentation_doc", "연출 파일", "story/presentation-doc.vnstory.json");
        sample.Project.Files.Add(presentationFile);
        LineBox line = sample.Line("원본");
        PresentationNode presentation = sample.Editor.AddPresentationNode(presentationFile.Id);
        NodeLink link = sample.Editor.SetPresentationTarget(presentation.Id, sample.Dialogue.Id)!;
        PresentationCommandInstance command = sample.Editor.AddPresentationCommand(
            presentation.Id, line.Id, "screen.shake");

        RenderedDocument document = DialogueDocumentComposer.Compose(sample.Project, sample.Dialogue.Id);
        RenderedSegment segment = Assert.Single(
            document.Segments,
            item => item.Kind == RenderedSegmentKind.PresentationCommand);

        Assert.Equal(presentationFile.Id, segment.Source.StoryFileId);
        Assert.Equal(sample.Dialogue.Id, segment.Source.NodeId);
        Assert.Equal(line.Id, segment.Source.LineId);
        Assert.Equal(link.Id, segment.Source.LinkId);
        Assert.Equal(presentation.Id, segment.Source.PresentationNodeId);
        Assert.Equal(command.Id, segment.Source.PresentationCommandId);
        Assert.Equal(DocumentLayer.Presentation, segment.Layer);
    }

}
