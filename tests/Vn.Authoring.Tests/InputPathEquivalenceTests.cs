using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// W21 회귀 골든 — 입력 경로가 달라도 한 통로.
///
/// 편집기 인자 편집·텍스트 입력 어느 길로 만들었든 같은 연출이면 발행→이미터 경로의
/// 출력이 <b>문자 하나까지 같다</b>. 경로마다 출력이 다르면 입력 방법이 규칙이 되어
/// 버린다 — 규칙은 카탈로그 하나여야 한다.
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
    public void 인자_편집으로_고친_것과_텍스트로_고친_것의_이미터_출력이_같다()
    {
        // 다른 길로 같은 값을 만들었으면 발행 결과가 문자 하나까지 같아야 한다.
        // 한쪽은 ProjectEditor.SetPresentationCommandArgument, 다른 쪽은 CommandText.Parse를 지난다.
        (Sample sampleA, PresentationNode nodeA, string firstA, _) = BuildStage();
        PresentationCommandInstance edited = sampleA.Editor.AddPresentationCommand(
            nodeA.Id, firstA, "char_rig_staging.move_by", Args(("slot", "c1"), ("x", "+1u")));

        // 가로 +2u, 시간 12fr로 확정 — 인자마다 편집 하나다.
        sampleA.Editor.SetPresentationCommandArgument(nodeA.Id, edited.Id, "x", "+2u");
        sampleA.Editor.SetPresentationCommandArgument(nodeA.Id, edited.Id, "duration", "12fr");

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
