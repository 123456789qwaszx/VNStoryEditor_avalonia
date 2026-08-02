using System.Text;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;
using Vn.Authoring.Script;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>
/// 작가의 평평한 대본 한 벌이 저장·재실행 가능한 출력까지 왕복하는 전체 흐름.
///
/// <code>
/// 평평한 대본 → LineId·ko-KR 산출물 → 조건 → DialogueResult v1
///            → 그 v1을 읽는 연출 → PresentationResult
///            → RuntimeComposition → 다섯 가지 출력
///            → 저장 → 다시 열기 → 같은 결과 관계와 같은 문자열
/// </code>
///
/// 조각마다 테스트가 있어도 이 왕복이 성립하지 않으면 제품은 성립하지 않는다.
/// </summary>
public class VerticalSliceTests
{
    private const string RawScript = """
        윌로: 이번 의뢰는 보수의 3분의 1밖에 드릴 수 없겠군요.
        라루: 3분의 1이요?
        라루: 그래도 당신이라면 믿어 보죠.
        윌로: 고맙습니다.
        라루: 생각할 시간을 좀 주세요.
        """;

    [Fact]
    public void 대본_한_벌이_출력까지_왕복하고_다시_열어도_같다()
    {
        string directory = TempDirectory();
        string manifestPath = Path.Combine(directory, "project" + ProjectManifestJson.FileExtension);
        string rawPath = Path.Combine(directory, "raw", "chapter.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(rawPath)!);
        File.WriteAllText(rawPath, RawScript, new UTF8Encoding(false));

        try
        {
            // ── 1. 평평한 대본을 읽어 LineId와 ko-KR 산출물을 만든다 ──────────
            var project = new StoryProject { Title = "세로 흐름" };
            var file = new StoryFile("sf_slice", "1장", "story/chapter.vnstory.json");
            project.Files.Add(file);
            var editor = new ProjectEditor(
                project,
                now: () => new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero));

            ScriptDocument script = editor.AddScript("1장 대본");
            ScriptSyncPlan plan = editor.PlanScriptSync(
                script.Id,
                File.ReadAllText(rawPath),
                rawPath);
            Assert.False(plan.HasConflicts);
            editor.ApplyScriptSync(plan);

            Assert.Equal(5, script.ActiveLines.Count());
            Assert.Equal(
                new LocalizedLine("윌로", "이번 의뢰는 보수의 3분의 1밖에 드릴 수 없겠군요."),
                script.Text(script.Lines[0].Id));

            // ── 2. DialogueNode에서 조건을 세운다 ─────────────────────────────
            SetNode settings = editor.AddSetNode(file.Id, name: "1장 설정");
            ConditionDefinition favor = editor.AddCondition(settings.Id, "호감 높음", "favor >= 5");
            DialogueNode scene = editor.AddDialogueNode(file.Id, name: "의뢰 장면", scriptId: script.Id);
            DialogueNode ending = AddEndNode(editor, file, "호감 결말");
            editor.AddSettingsLink(settings.Id, scene.Id);

            string[] lineIds = script.ActiveLines.Select(line => line.Id).ToArray();
            editor.SetLineTransition(scene.Id, lineIds[2], LineConditionTransition.BeginIf(favor.Id));
            editor.SetLineTransition(scene.Id, lineIds[4], LineConditionTransition.EndIf());
            editor.SetExitTarget(scene.Id, Vn.Authoring.Flow.ExitPortKind.Branch, lineIds[2], ending.Id);

            // ── 3. DialogueResult v1을 발행한다 ───────────────────────────────
            DialogueResult dialogue = editor.PublishDialogue(scene.Id).Result;
            Assert.Equal(1, dialogue.Identity.Version);

            // ── 4. 그 v1을 읽는 연출을 만들고 LineId 하나에 연출을 준다 ────────
            PresentationNode direction = editor.AddPresentationNode(file.Id, name: "의뢰 장면 연출");
            editor.SetPresentationSource(
                direction.Id,
                dialogue.Identity.ResultId,
                dialogue.Identity.Version);
            editor.AddPresentationCommand(direction.Id, lineIds[0], "camera.closeup");

            PresentationResult presentation = editor.PublishPresentation(direction.Id).Result;
            Assert.Equal(dialogue.Identity.ContentHash, presentation.Source.ContentHash);

            // ── 5. 두 결과를 조합한다 ─────────────────────────────────────────
            RuntimeComposition composition = editor.AddComposition(
                dialogue.Identity.ResultId,
                dialogue.Identity.Version,
                presentation.Identity.ResultId,
                presentation.Identity.Version,
                name: "1장 Runtime");

            Dictionary<OutputPresetId, string> outputs = RenderAll(project, composition);

            Assert.Contains("<<camera preset=closeup>>", outputs[OutputPresetId.RuntimeFull], StringComparison.Ordinal);
            Assert.Contains($"<<if {favor.Expression}>>", outputs[OutputPresetId.RuntimeFull], StringComparison.Ordinal);
            Assert.Contains($"<<jump {ending.Id}>>", outputs[OutputPresetId.RuntimeFull], StringComparison.Ordinal);

            // 대본집은 연출을 빼고, 연출표는 대사를 화자 없이 보여 준다.
            Assert.DoesNotContain("camera", outputs[OutputPresetId.ScenarioOnly], StringComparison.Ordinal);
            Assert.Contains("[Camera]", outputs[OutputPresetId.DirectionSheet], StringComparison.Ordinal);
            Assert.Contains("LineId\tSpeaker", outputs[OutputPresetId.LocalizationScript], StringComparison.Ordinal);
            Assert.Contains("[LINE ", outputs[OutputPresetId.RecordingScript], StringComparison.Ordinal);

            // ── 6. 저장하고 다시 연다 ─────────────────────────────────────────
            ProjectStore.Save(manifestPath, project);
            StoryProject reloaded = ProjectStore.Load(manifestPath).Project;

            DialogueResult reloadedDialogue = reloaded.Results.FindDialogue(
                dialogue.Identity.ResultId,
                dialogue.Identity.Version)!;
            PresentationResult reloadedPresentation = reloaded.Results.FindPresentation(
                presentation.Identity.ResultId,
                presentation.Identity.Version)!;

            Assert.Equal(dialogue.Identity, reloadedDialogue.Identity);
            Assert.Equal(presentation.Identity, reloadedPresentation.Identity);
            Assert.Equal(presentation.Source, reloadedPresentation.Source);
            Assert.Equal(
                reloadedDialogue.Identity.ContentHash,
                reloadedPresentation.Source.ContentHash);

            // 연출 노드가 여전히 정확히 같은 결과를 가리킨다.
            Assert.Equal(
                direction.Source,
                reloaded.FindPresentation(direction.Id)!.Source);

            // ── 7. 다시 만든 출력이 글자 하나까지 같다 ────────────────────────
            RuntimeComposition reloadedComposition = reloaded.FindComposition(composition.Id)!;
            Dictionary<OutputPresetId, string> again = RenderAll(reloaded, reloadedComposition);

            foreach (OutputPresetId preset in outputs.Keys)
            {
                Assert.Equal(outputs[preset], again[preset]);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 저장했다 다시 연 결과의 본문 해시를 다시 계산해도 같은 값이어야 한다.
    /// 이것이 깨지면 저장 형식이 결과의 정체성을 바꾸고 있다는 뜻이다.
    /// </summary>
    [Fact]
    public void 저장하고_다시_연_결과는_같은_해시로_재현된다()
    {
        string directory = TempDirectory();
        string manifestPath = Path.Combine(directory, "project" + ProjectManifestJson.FileExtension);

        try
        {
            var fixture = new SliceFixture();
            ProjectStore.Save(manifestPath, fixture.Project);

            StoryProject reloaded = ProjectStore.Load(manifestPath).Project;
            DialogueResult result = reloaded.Results.FindDialogue(
                fixture.Dialogue.Identity.ResultId,
                fixture.Dialogue.Identity.Version)!;

            Assert.Equal(fixture.Dialogue.Identity.ContentHash, ResultHash.Of(result));
            Assert.True(ResultHash.IsIntact(result));
            Assert.Equal(
                fixture.Dialogue.Lines.Select(line => (line.LineId, line.Text)),
                result.Lines.Select(line => (line.LineId, line.Text)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 대본과_노드와_결과가_서로_다른_파일에_저장된다()
    {
        string directory = TempDirectory();
        string manifestPath = Path.Combine(directory, "project" + ProjectManifestJson.FileExtension);

        try
        {
            var fixture = new SliceFixture();
            ProjectStore.Save(manifestPath, fixture.Project);

            string manifest = File.ReadAllText(manifestPath);
            string scriptPath = Path.Combine(
                directory,
                "script",
                fixture.Sample.Script.Id + ScriptDocumentJson.FileExtension);
            string storyPath = Path.Combine(
                directory,
                fixture.Sample.File.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            string resultsPath = Path.Combine(directory, ResultStoreJson.DefaultFileName);

            Assert.True(File.Exists(scriptPath));
            Assert.True(File.Exists(storyPath));
            Assert.True(File.Exists(resultsPath));

            // manifest는 목차만 담는다. 본문은 어디에도 없다.
            Assert.DoesNotContain("\"nodes\"", manifest, StringComparison.Ordinal);
            Assert.DoesNotContain("첫 대사", manifest, StringComparison.Ordinal);

            // 화자와 대사는 대본 파일에만 있다. 노드 파일에는 LineId만 있다.
            Assert.Contains("첫 대사", File.ReadAllText(scriptPath), StringComparison.Ordinal);
            Assert.DoesNotContain("첫 대사", File.ReadAllText(storyPath), StringComparison.Ordinal);

            // 발행 결과는 그 시점의 본문을 얼려서 함께 들고 있다.
            Assert.Contains("첫 대사", File.ReadAllText(resultsPath), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Dictionary<OutputPresetId, string> RenderAll(
        StoryProject project,
        RuntimeComposition composition)
    {
        ResolvedComposition resolved = RuntimeCompositionResolver.Resolve(
            project.Results,
            composition);
        Assert.True(resolved.IsCompatible);

        return OutputPresetCatalog.All.ToDictionary(
            preset => preset.Id,
            preset => DocumentPreviewFormatter.Format(
                ResultDocumentComposer.ComposePreset(resolved, preset, project)));
    }

    private static DialogueNode AddEndNode(ProjectEditor editor, StoryFile file, string name)
    {
        ScriptDocument script = editor.AddScript(name + " 대본");
        ScriptLine line = editor.InsertScriptLine(script.Id);
        editor.SetScriptLineText(script.Id, line.Id, "라루", name);
        return editor.AddDialogueNode(file.Id, name: name, scriptId: script.Id);
    }

    private static string TempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"VnTool.Slice.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
