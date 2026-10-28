using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// W21 회귀 골든 — 세 입력 경로, 한 통로.
///
/// 갤러리(직접 추가)·텍스트 입력·직접 조작 어느 길로 만들었든 같은 연출이면
/// 발행→이미터 경로의 출력이 <b>문자 하나까지 같다</b>. 경로마다 출력이 다르면
/// 입력 방법이 규칙이 되어 버린다 — 규칙은 카탈로그 하나여야 한다.
/// </summary>
public class InputPathEquivalenceTests
{
    private static readonly PresentationCommandCatalog Catalog = PresentationCommandCatalog.Default;

    private static Dictionary<string, string> Args(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    /// <summary>공통 무대: 대사 두 줄을 발행하고 연출 노드를 입력에 붙인다.</summary>
    private static (Sample Sample, PresentationNode Node, string First, string Second) BuildStage()
    {
        var sample = new Sample();
        string first = sample.Line("첫 줄");
        sample.Editor.SetScriptLineText(sample.Script.Id, first, "라루", "첫 줄");
        string second = sample.Line("둘째 줄");
        sample.Editor.SetScriptLineText(sample.Script.Id, second, "윌로", "둘째 줄");

        DialogueResult dialogue = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;
        PresentationNode node = sample.Editor.AddPresentationNode(sample.File.Id, name: "연출");
        sample.Editor.SetPresentationSource(node.Id, dialogue.Identity.ResultId, dialogue.Identity.Version);

        return (sample, node, first, second);
    }

    private static YarnBundle Emit(Sample sample, PresentationNode node)
    {
        DialogueResult dialogue = sample.Project.Results.DialogueResultsOf(sample.Dialogue.Id).Last();
        PresentationResult presentation = sample.Editor.PublishPresentation(node.Id).Result;

        return YarnBundleEmitter.Emit(
            dialogue,
            presentation,
            sample.Project,
            GameDefinition.Empty, // 기본(런타임 교차 검증) 카탈로그
            bundleName: "equiv_ep");
    }

    [Fact]
    public void 손으로_만든_것과_텍스트_직접조작으로_만든_것의_이미터_출력이_같다()
    {
        // A: 갤러리/직접 추가 경로 — 인자를 손으로 채운다.
        (Sample sampleA, PresentationNode nodeA, string firstA, string secondA) = BuildStage();
        sampleA.Editor.AddPresentationSetupCommand(
            nodeA.Id, "background.bg_spawn", Args(("rigKey", "bg0"), ("spriteKey", "office")));
        sampleA.Editor.AddPresentationCommand(
            nodeA.Id, firstA, "char_rig_cast.slot", Args(("slotKey", "c1")));
        sampleA.Editor.AddPresentationCommand(
            nodeA.Id, firstA, "char_rig_cast.cast", Args(("slot", "c1"), ("characterKey", "laru")));
        sampleA.Editor.AddPresentationCommand(
            nodeA.Id, firstA, "char_rig_presentation.fade_in", Args(("slot", "c1")));
        sampleA.Editor.AddPresentationCommand(
            nodeA.Id, secondA, "char_rig_presentation.face_swap", Args(("slot", "c1"), ("emotion", "5")));

        // B: 텍스트 입력(Setup) + 직접 조작(캐스팅 시퀀스·표정 클릭) 경로.
        (Sample sampleB, PresentationNode nodeB, string firstB, string secondB) = BuildStage();

        CommandTextParseResult parsed = CommandText.Parse("<<bg_spawn bg0 office>>", Catalog);
        Assert.True(parsed.Success);
        sampleB.Editor.AddPresentationSetupCommand(nodeB.Id, parsed.Definition!.Id, parsed.Arguments!);

        PresentationStageActions.ApplyCastingSequence(
            sampleB.Editor, Catalog, MiniStageState.Empty, nodeB.Id, firstB, "c1", "laru");
        PresentationStageActions.Apply(
            sampleB.Editor, Catalog, nodeB.Id, secondB, "face_swap", Args(("slot", "c1"), ("emotion", "5")));

        YarnBundle bundleA = Emit(sampleA, nodeA);
        YarnBundle bundleB = Emit(sampleB, nodeB);

        Assert.False(bundleA.HasBlockingProblems);
        Assert.Equal(bundleA.StoryText, bundleB.StoryText);

        // 결과가 실제로 그 연출을 담고 있는지도 못박는다 — 빈 출력끼리 같은 것이 아니다.
        // 2026-08-18: 레인이 없어져 노드 셋업도 줄 연출도 모두 Story 하나에 있다.
        Assert.Contains("<<bg_spawn bg0 office>>", bundleA.StoryText, StringComparison.Ordinal);
        Assert.Contains("<<cast c1 laru a 1>>", bundleA.StoryText, StringComparison.Ordinal);
        Assert.Contains("<<face_swap c1 5 10fr>>", bundleA.StoryText, StringComparison.Ordinal);
    }

    [Fact]
    public void 슬라이더로_고친_것과_텍스트로_고친_것의_이미터_출력이_같다()
    {
        // W66 — 무대 슬라이더는 네 번째 입력 경로다. 다른 길로 같은 값을 만들었으면
        // 발행 결과가 문자 하나까지 같아야 한다.
        //
        // 슬라이더가 실제로 부르는 것은 ProjectEditor.SetPresentationCommandArgument 하나이고
        // (UI는 토큰 문자열을 만들 뿐이다), 텍스트 경로는 CommandText.Parse를 지난다.
        (Sample sampleA, PresentationNode nodeA, string firstA, _) = BuildStage();
        PresentationCommandInstance slid = sampleA.Editor.AddPresentationCommand(
            nodeA.Id, firstA, "char_rig_staging.move_by", Args(("slot", "c1"), ("x", "+1u")));

        // 슬라이더를 끌어 가로 +2u, 시간 12fr로 확정 — 인자마다 편집 하나다.
        sampleA.Editor.SetPresentationCommandArgument(nodeA.Id, slid.Id, "x", "+2u");
        sampleA.Editor.SetPresentationCommandArgument(nodeA.Id, slid.Id, "duration", "12fr");

        (Sample sampleB, PresentationNode nodeB, string firstB, _) = BuildStage();
        CommandTextParseResult parsed = CommandText.Parse("<<move_by c1 +2u 0u 12fr>>", Catalog);
        Assert.True(parsed.Success);
        sampleB.Editor.AddPresentationCommand(nodeB.Id, firstB, parsed.Definition!.Id, parsed.Arguments!);

        YarnBundle bundleA = Emit(sampleA, nodeA);
        YarnBundle bundleB = Emit(sampleB, nodeB);

        Assert.False(bundleA.HasBlockingProblems);
        Assert.Equal(bundleA.StoryText, bundleB.StoryText);
        Assert.Contains("<<move_by c1 +2u 0u 12fr>>", bundleA.StoryText, StringComparison.Ordinal);
    }
}
