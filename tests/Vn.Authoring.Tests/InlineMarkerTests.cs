using System.Text;
using Vn.Core;
using Vn.Core.Analysis;
using Vn.Core.Diagnostics;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>
/// 인라인 동기화 마커([adv/]). 한 라인의 커맨드를 그룹으로 나누고, Story 본문의 마커 위치에서
/// 서브 레인이 한 스텝 진행한다. Pres 사본은 그 라인 자리에 1 + 마커 수 개의 라인을 내
/// 라인 예산(계약서 B)을 맞춘다. 마커명은 adv 고정이다.
/// </summary>
public class InlineMarkerTests
{
    private static readonly string GoldenDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Golden"));

    private const string LineText = "천천히 걸어온다... 그리고 멈춘다... 마지막으로 돌아본다";

    [Fact]
    public void 마커는_저장과_결과_동결을_왕복한다()
    {
        MarkerWorld world = BuildWorld();

        // 프로젝트 저장 왕복.
        StoryProject reloaded = ProjectSnapshotCodec.Decode(
            ProjectSnapshotCodec.Encode(world.Sample.Project));
        PresentationLineBinding binding = reloaded.FindPresentation(world.PresentationNode.Id)!
            .FindBinding(world.LineId)!;
        Assert.Equal(2, binding.Markers.Count);
        Assert.Equal(1, binding.Markers[0].FirstCommandIndex);

        // 발행 결과와 결과 저장 왕복.
        PresentationResultBinding frozen = world.Presentation.FindBinding(world.LineId)!;
        Assert.Equal(2, frozen.MarkerList.Count);

        ResultRepository store = ResultStoreJson.Read(ResultStoreJson.Write(world.Sample.Project.Results));
        Assert.Equal(
            frozen.MarkerList,
            store.PresentationResults.Single().FindBinding(world.LineId)!.MarkerList);
    }

    [Fact]
    public void 비활성_커맨드가_있으면_마커의_그룹_경계를_활성_기준으로_다시_센다()
    {
        MarkerWorld world = BuildWorld();

        // 그룹 0의 커맨드를 끈다 — 발행 결과에서 빠지므로 마커 경계가 한 칸 당겨져야 한다.
        world.Sample.Editor.SetPresentationCommandEnabled(
            world.PresentationNode.Id,
            world.Commands[0].Id,
            enabled: false);

        PresentationResult republished =
            world.Sample.Editor.PublishPresentation(world.PresentationNode.Id).Result;
        PresentationResultBinding binding = republished.FindBinding(world.LineId)!;

        Assert.Equal(2, binding.Commands.Count);
        Assert.Equal(0, binding.MarkerList[0].FirstCommandIndex);
        Assert.Equal(1, binding.MarkerList[1].FirstCommandIndex);
    }

    [Fact]
    public void Story_본문에_마커가_삽입되고_Pres는_그룹별_사본_라인으로_갈라진다()
    {
        MarkerWorld world = BuildWorld();
        YarnBundle bundle = Emit(world);

        Assert.False(bundle.HasBlockingProblems);

        // Story: 본문 오프셋 위치에 [adv/] 두 개, 태그는 그대로 하나.
        Assert.Contains(
            $"천천히 걸어온다... [adv/]그리고 멈춘다... [adv/]마지막으로 돌아본다 #line:{world.LineId}",
            bundle.StoryText,
            StringComparison.Ordinal);

        // Pres: 1 + 마커 수 = 3개의 사본 라인, 그룹 k의 커맨드가 k번째 라인 앞에 붙는다.
        Assert.Contains(
            "<<camera closeup>>\n천천히 걸어온다... \n\n<<screen_effect shake>>\n그리고 멈춘다... \n\n<<character_acting smile>>\n마지막으로 돌아본다\n",
            bundle.PresText!,
            StringComparison.Ordinal);

        // 손으로 입력한 마커 경고는 없다 — 모델 마커는 정식 지원이다.
        Assert.DoesNotContain(bundle.Problems, problem =>
            problem.Message.Contains("[adv/]", StringComparison.Ordinal));
    }

    [Fact]
    public void Pres_사본_라인_수는_Story_라인_수_더하기_마커_수다()
    {
        MarkerWorld world = BuildWorld();
        YarnBundle bundle = Emit(world);

        int storyLines = CountPlainLines(bundle.StoryText);
        int markerCount = bundle.StoryText.Split("[adv/]").Length - 1;

        Assert.Equal(2, markerCount);
        Assert.Equal(storyLines + markerCount, CountPlainLines(bundle.PresText!));
    }

    [Theory]
    [InlineData("Story_marker_ep.yarn")]
    [InlineData("Pres_marker_ep.yarn")]
    public void 마커_골든과_글자_하나까지_같다(string fileName)
    {
        YarnBundle bundle = Emit(BuildWorld());
        string actual = bundle.Files.Single(file => file.FileName == fileName).Text;
        string goldenPath = Path.Combine(GoldenDirectory, fileName);

        if (!File.Exists(goldenPath))
        {
            Directory.CreateDirectory(GoldenDirectory);
            File.WriteAllText(goldenPath, actual, new UTF8Encoding(false));
            Assert.Fail($"골든 파일이 없어 새로 기록했습니다. 내용을 검토하고 커밋하세요: {goldenPath}");
        }

        Assert.Equal(
            File.ReadAllText(goldenPath, Encoding.UTF8).Replace("\r\n", "\n", StringComparison.Ordinal),
            actual);
    }

    [Fact]
    public void 마커_번들도_실컴파일된다()
    {
        MarkerWorld world = BuildWorld();
        YarnBundle bundle = Emit(world);
        string directory = Path.Combine(Path.GetTempPath(), $"VnTool.Compile.{Guid.NewGuid():N}");

        try
        {
            YarnBundleEmitter.WriteBundles(new[] { bundle }, directory);

            var utf8 = new UTF8Encoding(false);
            File.WriteAllText(
                Path.Combine(directory, "Demo.yarnproject"),
                """
                {
                  "projectFileVersion": 3,
                  "baseLanguage": "ko",
                  "sourceFiles": [ "**/*.yarn" ],
                  "excludeFiles": []
                }
                """,
                utf8);
            File.WriteAllText(
                Path.Combine(directory, "game.schema.json"),
                """
                {
                  "schemaVersion": 1,
                  "variables": [],
                  "commands": [
                    { "id": "camera", "params": [{ "name": "preset", "type": "string" }] },
                    { "id": "character_acting", "params": [{ "name": "preset", "type": "string" }] },
                    { "id": "screen_effect", "params": [{ "name": "preset", "type": "string" }] },
                    { "id": "beat", "params": [{ "name": "node", "type": "string" }] },
                    { "id": "pres_start", "params": [{ "name": "node", "type": "string" }] },
                    { "id": "pres_end", "params": [] }
                  ]
                }
                """,
                utf8);

            AnalysisReport report = new VnProjectAnalyzer().Analyze(
                Path.Combine(directory, "Demo.yarnproject"),
                Path.Combine(directory, "game.schema.json"));

            IReadOnlyList<VnDiagnostic> errors = report.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();

            Assert.True(errors.Count == 0, "컴파일 오류: " + string.Join(
                Environment.NewLine,
                errors.Select(error => $"{error.Code} {error.FilePath}:{error.Line} {error.Message}")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static YarnBundle Emit(MarkerWorld world)
    {
        return YarnBundleEmitter.Emit(
            world.Dialogue,
            world.Presentation,
            world.Sample.Project,
            Sample.Definition,
            bundleName: "marker_ep");
    }

    private static int CountPlainLines(string yarn)
    {
        return yarn.Split('\n')
            .Select(line => line.Trim())
            .Count(line =>
                line.Length > 0 &&
                !line.StartsWith("<<", StringComparison.Ordinal) &&
                !line.StartsWith("title:", StringComparison.Ordinal) &&
                line != "---" &&
                line != "===");
    }

    /// <summary>
    /// 마커 2개 라인 샘플 — 커맨드 3개가 세 그룹으로 나뉜다.
    /// 그룹 0: 라인 시작, 그룹 1: 첫 마커, 그룹 2: 둘째 마커.
    /// </summary>
    private static MarkerWorld BuildWorld()
    {
        var sample = new Sample();
        string line = sample.Line(LineText);
        DialogueResult dialogue = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        PresentationNode node = sample.Editor.AddPresentationNode(sample.File.Id, name: "마커 연출");
        sample.Editor.SetPresentationSource(node.Id, dialogue.Identity.ResultId, dialogue.Identity.Version);

        var commands = new[]
        {
            sample.Editor.AddPresentationCommand(node.Id, line, "camera.closeup"),
            sample.Editor.AddPresentationCommand(node.Id, line, "screen.shake"),
            sample.Editor.AddPresentationCommand(node.Id, line, "acting.smile")
        };

        int firstOffset = LineText.IndexOf("그리고", StringComparison.Ordinal);
        int secondOffset = LineText.IndexOf("마지막으로", StringComparison.Ordinal);
        sample.Editor.SetPresentationLineMarkers(node.Id, line, new[]
        {
            new PresentationLineMarker { CharacterOffset = firstOffset, FirstCommandIndex = 1 },
            new PresentationLineMarker { CharacterOffset = secondOffset, FirstCommandIndex = 2 }
        });

        PresentationResult presentation = sample.Editor.PublishPresentation(node.Id).Result;

        return new MarkerWorld(sample, line, dialogue, node, commands, presentation);
    }

    private sealed record MarkerWorld(
        Sample Sample,
        string LineId,
        DialogueResult Dialogue,
        PresentationNode PresentationNode,
        IReadOnlyList<PresentationCommandInstance> Commands,
        PresentationResult Presentation);
}
