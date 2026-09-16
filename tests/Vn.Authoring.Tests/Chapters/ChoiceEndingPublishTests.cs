using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 선택지로 끝나는 에피소드가 <b>발행되고 실제로 컴파일되는가</b> — 2단계 포트 규칙
/// (2026-08-14 소유자 승인)의 끝에서 끝까지.
///
/// ⚠ 2026-09-16에 `EpisodeSyncServiceTests`에서 옮겨 왔다 (R-D). 그 파일은 역방향
/// 동기화의 것이라 함께 걷혔는데, 이 테스트는 <b>동기화를 쓰지 않는다</b> — 작가 판에서
/// 노드를 직접 세워 발행까지 간다. 남길 것을 남긴다.
/// </summary>
public sealed class ChoiceEndingPublishTests : IDisposable
{
    private static readonly GameDefinition Definition = GameDefinition.Parse("""
        { "speakers": [ { "name": "라루", "characterId": "laru" }, { "name": "윌로", "characterId": "willo" } ] }
        """)!;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-choice-publish", Guid.NewGuid().ToString("N"));

    public ChoiceEndingPublishTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 선택지로_끝나는_에피소드가_발행되고_옵션_출구가_점프로_나간다()
    {
        // 2단계 포트 규칙 (2026-08-14 소유자 승인) — 각 옵션의 도착은 작가가 보드에서
        // 잇고, 안 이은 옵션은 그 자리에서 에피소드 종료다. 선택지로 끝나는 노드가
        // 발행되고 이어진 옵션이 점프로 나가는지를 건다.
        //
        // v10에서 선택지는 <b>대본이 선언하지 않는다</b> — 주인이 챕터 `간선` 시트다.
        // 그래서 옵션 줄은 작가 판에서 직접 세운다(자유 씬이 그러듯).
        var project = new StoryProject();
        var file = new StoryFile("sf_choice", "테스트", "story/choice.vnstory.json");
        project.Files.Add(file);

        int next = 0;
        var editor = new ProjectEditor(project, newLineId: () => $"ln_new_{++next:D3}");
        string fileId = file.Id;

        DialogueNode node = editor.AddDialogueNode(fileId, name: "Story_choice_end");
        string scriptId = editor.EnsureDialogueScript(node.Id).Id;
        string firstLine = editor.Project.FindScript(scriptId)!.ActiveLines.First().Id;
        editor.SetScriptLineText(scriptId, firstLine, "윌로", "어떻게 할래?");

        string optionA = editor.InsertScriptLine(scriptId).Id;
        string optionB = editor.InsertScriptLine(scriptId).Id;
        editor.SetScriptLineText(scriptId, optionA, string.Empty, "라루의 제안을 듣는다");
        editor.SetScriptLineText(scriptId, optionB, string.Empty, "혼자 문을 연다");

        editor.SetLineTransition(node.Id, optionA, LineConditionTransition.BeginChoice());
        editor.SetLineTransition(node.Id, optionB, LineConditionTransition.BeginNextOption());

        List<DialogueLineExtension> options = node.LineExtensions
            .Where(extension => extension.Transition?.OpensOption == true)
            .ToList();
        Assert.Equal(2, options.Count);

        // 첫 옵션만 작가의 곁가지로 잇는다. 나머지는 안 잇는다(= 그 자리에서 에피소드 종료).
        DialogueNode side = editor.AddDialogueNode(fileId, name: "곁가지_창고");
        editor.SetScriptLineText(
            editor.EnsureDialogueScript(side.Id).Id,
            editor.Project.FindScript(side.ScriptId)!.ActiveLines.First().Id,
            "라루", "여긴 창고야.");
        editor.SetExitTarget(node.Id, Vn.Authoring.Flow.ExitPortKind.Branch, options[0].LineId, side.Id);

        // 발행이 더는 거부되지 않는다 — 열린 채 끝난 블록은 알림일 뿐이다.
        Assert.DoesNotContain(
            editor.InspectDialoguePublish(node.Id, Definition).Problems,
            problem => problem.IsBlocking);

        Vn.Authoring.Results.DialogueResult published =
            editor.PublishDialogue(node.Id, Definition).Result;
        Vn.Authoring.Results.DialogueResult sidePublished =
            editor.PublishDialogue(side.Id, Definition).Result;

        // 이미터 → 실컴파일. 곁가지도 함께 내보내야 점프 대상이 실재한다.
        Vn.Authoring.Rendering.YarnBundle bundle =
            Vn.Authoring.Rendering.YarnBundleEmitter.Emit(published, project: editor.Project);
        Vn.Authoring.Rendering.YarnBundle sideBundle =
            Vn.Authoring.Rendering.YarnBundleEmitter.Emit(sidePublished, project: editor.Project);

        string compileDirectory = Path.Combine(_directory, "compile-trailing");
        Directory.CreateDirectory(compileDirectory);
        Vn.Authoring.Rendering.YarnBundleEmitter.WriteBundles([bundle, sideBundle], compileDirectory);

        var utf8 = new System.Text.UTF8Encoding(false);
        File.WriteAllText(Path.Combine(compileDirectory, "Demo.yarnproject"),
            """
            {
              "projectFileVersion": 3,
              "baseLanguage": "ko",
              "sourceFiles": [ "**/*.yarn" ],
              "excludeFiles": []
            }
            """, utf8);
        File.WriteAllText(Path.Combine(compileDirectory, "game.schema.json"),
            """
            { "schemaVersion": 1,
              "variables": [
                { "id": "$trust", "type": "number" },
                { "id": "$anger", "type": "number" },
                { "id": "$fatigue", "type": "number" }
              ],
              "commands": [] }
            """, utf8);

        Vn.Core.Analysis.AnalysisReport compiled = new Vn.Core.VnProjectAnalyzer().Analyze(
            Path.Combine(compileDirectory, "Demo.yarnproject"),
            Path.Combine(compileDirectory, "game.schema.json"));

        var errors = compiled.Diagnostics
            .Where(item => item.Severity == Vn.Core.Diagnostics.DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0, "컴파일 오류: " + string.Join(
            Environment.NewLine,
            errors.Select(error => $"{error.Code} {error.FilePath}:{error.Line} {error.Message}")));

        // 이어진 옵션은 곁가지로 점프한다 — 산출 Yarn에 실재해야 한다.
        string yarn = string.Join("\n", Directory.EnumerateFiles(compileDirectory, "*.yarn")
            .Select(File.ReadAllText));
        Assert.Contains("곁가지_창고", yarn);
    }
}
