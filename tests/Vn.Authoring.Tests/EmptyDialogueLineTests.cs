using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// 본문이 빈 대사 줄 (2026-08-25).
///
/// <b>순수 연출 노드가 이 모양이다</b> — 대사박스를 끄고 커맨드만 돌리는 씬은 줄에 적을
/// 말이 없다. 그래서 막을 것이 아니라 <b>내보낼 수 있게</b> 만드는 것이 일이고, 대신
/// 박스를 켜 둔 채로 비운 경우를 위해 경고를 함께 낸다.
/// </summary>
public sealed class EmptyDialogueLineTests
{
    [Fact]
    public void 빈_줄이_태그만_남은_채로_나가지_않는다()
    {
        // ⛔ 이것이 고친 버그다. `#line:` 태그만 남은 줄은 Yarn이 파싱하지 못한다.
        YarnBundle bundle = Bundle(out string lineId);

        Assert.DoesNotContain($"\n #line:{lineId}", bundle.StoryText);
        Assert.DoesNotContain($"\n#line:{lineId}", bundle.StoryText);

        // 자리는 보이지 않는 한 글자가 지킨다.
        Assert.Contains($"{YarnBundleEmitter.EmptyLineBody} #line:{lineId}", bundle.StoryText);
    }

    [Fact]
    public void 빈_줄은_경고로_짚되_막지는_않는다()
    {
        YarnBundle bundle = Bundle(out string lineId);

        YarnBundleProblem problem = Assert.Single(
            bundle.Problems, item => item.LineId == lineId);

        Assert.False(problem.IsBlocking);
        Assert.Contains("대사가 비어 있는", problem.Message);
    }

    [Fact]
    public void 채워진_줄에는_경고도_보이지_않는_글자도_없다()
    {
        var sample = new Sample();
        string lineId = sample.Line("말이 있다");

        YarnBundle bundle = YarnBundleEmitter.Emit(
            sample.Editor.PublishDialogue(sample.Dialogue.Id).Result,
            definition: Sample.Definition);

        Assert.Contains($"말이 있다 #line:{lineId}", bundle.StoryText);
        Assert.DoesNotContain(YarnBundleEmitter.EmptyLineBody, bundle.StoryText);
        Assert.DoesNotContain(bundle.Problems, item => item.LineId == lineId);
    }

    [Fact]
    public void 대본이_아예_없는_노드도_내보내진다()
    {
        // ⛔ 이것이 고친 두 번째 버그다. 대본이 없으면 발행이 <b>막혀</b> 그 노드의 .yarn이
        //    아예 안 나갔는데, 진행 JSON은 `ViaNodeId`로 그 이름을 그대로 들고 나갔다 —
        //    유니티의 사전 대조가 "부르는 노드가 없다"로 재생을 통째로 막는 자리다.
        var sample = new Sample();
        DialogueNode scene = sample.Editor.AddDialogueNode(sample.File.Id, name: "빈 연출");

        // 대본을 통째로 떼어 낸다 — 판에서 씬만 세우고 아직 아무것도 안 쓴 상태다.
        scene.ScriptId = null;

        LiveComposition composition = LiveNodeComposer.Compose(
            sample.Project, scene.Id, Sample.Definition, DateTimeOffset.UnixEpoch);

        Assert.True(composition.CanWrite, string.Join(" / ", composition.BlockingProblems));
        Assert.Contains($"title: {composition.Bundle!.BundleName}", composition.Bundle.StoryText);

        // 막지 않을 뿐, 조용하지도 않다.
        Assert.Contains(composition.Warnings, message => message.Contains("대본"));
    }

    [Fact]
    public void 줄이_하나도_없는_대본을_가진_노드도_내보내진다()
    {
        // 줄을 다 지운 씬 — "대사가 하나도 없다"의 다른 모양이다.
        var sample = new Sample();
        DialogueNode scene = sample.Editor.AddDialogueNode(sample.File.Id, name: "빈 연출2");

        sample.Project.Scripts.Single(script => script.Id == scene.ScriptId).Lines.Clear();

        LiveComposition composition = LiveNodeComposer.Compose(
            sample.Project, scene.Id, Sample.Definition, DateTimeOffset.UnixEpoch);

        Assert.True(composition.CanWrite, string.Join(" / ", composition.BlockingProblems));
        Assert.Contains($"title: {composition.Bundle!.BundleName}", composition.Bundle.StoryText);
    }

    /// <summary>본문이 빈 줄 하나짜리 노드 — 순수 연출 씬의 최소 모양.</summary>
    private static YarnBundle Bundle(out string lineId)
    {
        var sample = new Sample();
        lineId = sample.Line(string.Empty);

        return YarnBundleEmitter.Emit(
            sample.Editor.PublishDialogue(sample.Dialogue.Id).Result,
            definition: Sample.Definition);
    }
}
