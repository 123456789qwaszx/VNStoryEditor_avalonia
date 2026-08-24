using Vn.Authoring.Definition;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// X12(c) — 라이브 CompositionNode 출력. 새 이미터는 없다: 발행 Freeze +
/// 기존 YarnBundleEmitter 하나를 지나므로 수동 내보내기와 바이트가 같고,
/// 발행은 게이트가 아니며(D-2), 버전 짝 어긋남 경고는 라이브에서도 산다.
/// </summary>
public class LiveComposerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void 발행_없이도_현재_상태가_그대로_합성된다()
    {
        var sample = new Sample();
        string first = sample.Line("첫 줄");
        sample.Editor.SetScriptLineText(sample.Script.Id, first, "라루", "첫 줄");

        // 발행하지 않았다 — D-2: 발행은 출력 게이트가 아니다.
        LiveComposition composition = LiveNodeComposer.Compose(
            sample.Project, sample.Dialogue.Id, GameDefinition.Empty, Now);

        Assert.True(composition.CanWrite);
        Assert.Contains("첫 줄", composition.Bundle!.StoryText, StringComparison.Ordinal);
        Assert.Contains($"#line:{first}", composition.Bundle.StoryText, StringComparison.Ordinal); // C1
    }

    [Fact]
    public void 라이브_출력과_수동_내보내기는_바이트가_같다()
    {
        // 수용 기준 3 — 같은 합성기 하나(LiveNodeComposer)를 지나므로 성립한다.
        var sample = new Sample();
        string first = sample.Line("첫 줄");
        sample.Editor.SetScriptLineText(sample.Script.Id, first, "라루", "첫 줄");

        string liveDirectory = TempDirectory();
        string manualDirectory = TempDirectory();

        try
        {
            LiveComposition live = LiveNodeComposer.Compose(
                sample.Project, sample.Dialogue.Id, GameDefinition.Empty, Now);
            YarnBundleEmitter.WriteBundles(new[] { live.Bundle! }, liveDirectory);

            LiveComposition manual = LiveNodeComposer.Compose(
                sample.Project, sample.Dialogue.Id, GameDefinition.Empty, Now);
            YarnBundleEmitter.WriteBundles(new[] { manual.Bundle! }, manualDirectory);

            string[] liveFiles = Directory.GetFiles(liveDirectory).Order(StringComparer.Ordinal).ToArray();
            string[] manualFiles = Directory.GetFiles(manualDirectory).Order(StringComparer.Ordinal).ToArray();

            Assert.Equal(
                liveFiles.Select(Path.GetFileName),
                manualFiles.Select(Path.GetFileName));

            foreach ((string livePath, string manualPath) in liveFiles.Zip(manualFiles))
            {
                Assert.Equal(File.ReadAllBytes(livePath), File.ReadAllBytes(manualPath));
            }

            // 편집하면 재합성 결과가 달라진다 — 자동 갱신의 재료.
            sample.Editor.SetScriptLineText(sample.Script.Id, first, "라루", "고친 줄");
            LiveComposition updated = LiveNodeComposer.Compose(
                sample.Project, sample.Dialogue.Id, GameDefinition.Empty, Now);
            Assert.NotEqual(live.Bundle!.StoryText, updated.Bundle!.StoryText);
            Assert.Contains("고친 줄", updated.Bundle.StoryText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(liveDirectory, recursive: true);
            Directory.Delete(manualDirectory, recursive: true);
        }
    }

    [Fact]
    public void 연출이_읽은_발행본과_현재_대사가_다르면_라이브에서도_경고한다()
    {
        // 수용 기준 4 — 버전 짝 추적은 라이브 모델에서도 유지된다(조용한 드리프트 금지).
        var sample = new Sample();
        string first = sample.Line("첫 줄");
        sample.Editor.SetScriptLineText(sample.Script.Id, first, "라루", "첫 줄");

        DialogueResult published = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;
        PresentationNode presentation = sample.Editor.AddPresentationNode(sample.File.Id, name: "연출");
        sample.Editor.SetPresentationSource(
            presentation.Id, published.Identity.ResultId, published.Identity.Version);
        sample.Editor.SetPresentationSupplyTarget(presentation.Id, sample.Dialogue.Id);

        // 발행본과 같은 동안에는 경고가 없다.
        LiveComposition inSync = LiveNodeComposer.Compose(
            sample.Project, sample.Dialogue.Id, GameDefinition.Empty, Now);
        Assert.True(inSync.CanWrite);
        Assert.NotNull(inSync.WorkingPresentation);
        Assert.DoesNotContain(inSync.Warnings, warning => warning.Contains("다릅니다", StringComparison.Ordinal));

        // 대사를 고치면 — 연출이 읽은 발행본과 어긋난다.
        sample.Editor.SetScriptLineText(sample.Script.Id, first, "라루", "고친 줄");

        LiveComposition drifted = LiveNodeComposer.Compose(
            sample.Project, sample.Dialogue.Id, GameDefinition.Empty, Now);

        Assert.True(drifted.CanWrite); // 경고지 차단이 아니다
        Assert.Contains(drifted.Warnings, warning =>
            warning.Contains("발행본과 현재 대사 내용이 다릅니다", StringComparison.Ordinal));
    }

    [Fact]
    public void 합성된_번들이_제_챕터를_싣고_나온다()
    {
        // 내보내기 팝업의 챕터 거르개가 이 값 하나에 얹혀 있다 (2026-08-25 소유자:
        // "각각의 노드들이 어떤 챕터의 것인지 알아 볼 수가 없잖아"). 여기가 비면
        // 목록의 모든 줄이 "(챕터 없음)"이 되어 거르개가 아무것도 못 가른다 —
        // 그런데 팝업은 모달이라 화면 시험이 닿지 않으므로, 그 밑의 값을 여기서 붙든다.
        var sample = new Sample();
        string first = sample.Line("첫 줄");
        sample.Editor.SetScriptLineText(sample.Script.Id, first, "라루", "첫 줄");

        LiveComposition composition = LiveNodeComposer.Compose(
            sample.Project, sample.Dialogue.Id, GameDefinition.Empty, Now);

        // 판 이름이 곧 챕터다 (챕터=판 1:1).
        Assert.Equal(sample.File.Name, composition.Bundle!.ChapterId);
    }

    private static string TempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"VnTool.Live.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
