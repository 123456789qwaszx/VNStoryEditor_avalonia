using System.Text;
using System.Text.RegularExpressions;
using Vn.Core;
using Vn.Core.Analysis;
using Vn.Core.Diagnostics;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// 이미터 산출물의 회귀 검증 3종.
///
/// 1. 골든 — 같은 저작 상태는 글자 하나까지 같은 트리오를 만든다. 기대 출력이 곧 스펙이다.
/// 2. 왕복 — 대사 문구 수정 → 재발행 → 재출력에서 Story·Pres 사본이 함께 바뀌고
///    <c>#line:</c> 태그와 노드 타이틀은 불변이다 (세이브 유지 — 계약서 C1·C2).
/// 3. 실컴파일 — Vn.Core의 Yarn 컴파일러로 산출물을 실제로 컴파일한다.
///    문법 오류와 전역 라인 ID 유일성(C4) 회귀를 잡는다.
/// </summary>
public class YarnBundleVerificationTests
{
    private static readonly string GoldenDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Golden"));

    [Theory]
    [InlineData("Story_golden_ep.yarn")]
    [InlineData("Set_golden_ep.yarn")]
    [InlineData("Pres_golden_ep.yarn")]
    [InlineData("declarations.yarn")]
    public void 골든_트리오와_글자_하나까지_같다(string fileName)
    {
        YarnBundle bundle = EmitGoldenBundle();
        string actual = fileName == YarnBundleEmitter.DeclarationsFileName
            ? YarnBundleEmitter.ComposeDeclarationsText(new[] { bundle })!
            : bundle.Files.Single(file => file.FileName == fileName).Text;
        string goldenPath = Path.Combine(GoldenDirectory, fileName);

        if (!File.Exists(goldenPath))
        {
            // 첫 실행: 실제 출력을 골든으로 기록하고 실패시킨다.
            // 사람이 내용을 검토·커밋해야 골든이 된다.
            Directory.CreateDirectory(GoldenDirectory);
            File.WriteAllText(goldenPath, actual, new UTF8Encoding(false));
            Assert.Fail($"골든 파일이 없어 새로 기록했습니다. 내용을 검토하고 커밋하세요: {goldenPath}");
        }

        Assert.Equal(
            File.ReadAllText(goldenPath, Encoding.UTF8).Replace("\r\n", "\n", StringComparison.Ordinal),
            actual);
    }

    [Fact]
    public void 문구_수정_재발행_재출력에서_사본은_일치하고_태그와_타이틀은_불변이다()
    {
        BundleWorld world = BuildWorld();
        YarnBundle before = Emit(world);

        // 대사 문구를 고친다 — LineId는 그대로다 (Revision만 오른다).
        world.Sample.Editor.SetScriptLineText(
            world.Sample.Script.Id,
            world.FirstLineId,
            "라루",
            "완전히 고친 첫 줄");

        DialogueResult v2 = world.Sample.Editor.PublishDialogue(world.Sample.Dialogue.Id).Result;
        Assert.Equal(2, v2.Identity.Version);

        // 연출은 새 대사 결과를 명시적으로 다시 고른 뒤 재발행한다.
        world.Sample.Editor.SetPresentationSource(
            world.PresentationNode.Id,
            v2.Identity.ResultId,
            v2.Identity.Version);
        PresentationResult presentation =
            world.Sample.Editor.PublishPresentation(world.PresentationNode.Id).Result;

        YarnBundle after = YarnBundleEmitter.Emit(
            v2,
            presentation,
            world.Sample.Project,
            Sample.Definition,
            bundleName: "golden_ep");

        // Story와 Pres 사본이 같은 새 문구를 담는다.
        Assert.Contains($"완전히 고친 첫 줄 #line:{world.FirstLineId}", after.StoryText, StringComparison.Ordinal);
        Assert.Contains("완전히 고친 첫 줄\n", after.PresText!, StringComparison.Ordinal);
        Assert.DoesNotContain("첫 줄 그대로", after.PresText!, StringComparison.Ordinal);

        // #line: 태그 집합 불변 (C1) — 태그가 바뀌면 기존 세이브가 조용히 행에 빠진다.
        Assert.Equal(LineTagsOf(before.StoryText), LineTagsOf(after.StoryText));

        // 노드 타이틀 불변 (C2) — 타이틀은 세이브 키이자 에피소드 진입 키다.
        Assert.Equal(TitlesOf(before), TitlesOf(after));
    }

    [Fact]
    public void 산출물은_Vn_Core_컴파일러로_실제_컴파일된다()
    {
        BundleWorld world = BuildWorld();
        YarnBundle bundle = Emit(world);
        string directory = Path.Combine(Path.GetTempPath(), $"VnTool.Compile.{Guid.NewGuid():N}");

        try
        {
            // jump 대상 노드들도 함께 내보낸다. 실제 익스포트 폴더의 모양 그대로 —
            // 여러 번들을 한 번에 쓰고 선언 파일은 합집합으로 한 번만 나온다.
            var bundles = new List<YarnBundle> { bundle };

            foreach (DialogueNode target in new[] { world.Sample.TargetA, world.Sample.TargetDefault })
            {
                DialogueResult result = world.Sample.Editor.PublishDialogue(target.Id).Result;
                bundles.Add(YarnBundleEmitter.Emit(result, project: world.Sample.Project));
            }

            YarnBundleEmitter.WriteBundles(bundles, directory);

            AnalysisReport report = Analyze(directory);

            IReadOnlyList<VnDiagnostic> errors = report.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();

            Assert.True(errors.Count == 0, "컴파일 오류: " + string.Join(
                Environment.NewLine,
                errors.Select(error => $"{error.Code} {error.FilePath}:{error.Line} {error.Message}")));

            // 트리오 세 노드가 모두 컴파일 결과에 존재한다.
            string[] titles = report.Nodes.Select(node => node.Title).ToArray();
            Assert.Contains("Story_golden_ep", titles);
            Assert.Contains("Set_golden_ep", titles);
            Assert.Contains("Pres_golden_ep", titles);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 두_합성을_같은_폴더로_내보내면_선언은_합집합_한_번이고_컴파일된다()
    {
        BundleWorld world = BuildWorld();
        YarnBundle first = Emit(world);

        // 두 번째 합성 — 같은 변수(favor)를 쓰고 새 변수(trust)도 하나 쓴다.
        DialogueNode second = world.Sample.TargetB;
        string secondLine = world.Sample.Project.FindScript(second.ScriptId)!.ActiveLines.First().Id;
        world.Sample.Editor.SetLineSetOperations(second.Id, secondLine, new[]
        {
            new SetOperation { Variable = "favor", Operator = SetOperatorKind.Add, Value = "1" },
            new SetOperation { Variable = "trust", Operator = SetOperatorKind.Subtract, Value = "2" }
        });
        DialogueResult secondResult = world.Sample.Editor.PublishDialogue(second.Id).Result;
        YarnBundle secondBundle = YarnBundleEmitter.Emit(
            secondResult,
            project: world.Sample.Project,
            definition: Sample.Definition);

        string directory = Path.Combine(Path.GetTempPath(), $"VnTool.Compile.{Guid.NewGuid():N}");

        try
        {
            var bundles = new List<YarnBundle> { first, secondBundle };

            foreach (DialogueNode target in new[] { world.Sample.TargetA, world.Sample.TargetDefault })
            {
                DialogueResult result = world.Sample.Editor.PublishDialogue(target.Id).Result;
                bundles.Add(YarnBundleEmitter.Emit(result, project: world.Sample.Project));
            }

            IReadOnlyList<string> written = YarnBundleEmitter.WriteBundles(bundles, directory);

            // 선언 파일은 폴더당 하나이고, 같은 변수(favor)는 한 번만 선언된다.
            string declarationsPath = Assert.Single(written, path =>
                Path.GetFileName(path) == YarnBundleEmitter.DeclarationsFileName);
            string declarations = File.ReadAllText(declarationsPath, Encoding.UTF8);
            Assert.Single(Regex.Matches(declarations, Regex.Escape("<<declare $favor")));
            Assert.Contains("<<declare $trust = 0>>", declarations, StringComparison.Ordinal);

            AnalysisReport report = Analyze(directory);
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

    // ── 선택지 번들 (W8) ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Story_choices_ep.yarn")]
    [InlineData("Pres_choices_ep.yarn")]
    public void 선택지_골든과_글자_하나까지_같다(string fileName)
    {
        YarnBundle bundle = EmitChoicesBundle(ChoiceTests.BuildChoiceWorld());
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
    public void 선택지_번들도_실컴파일된다()
    {
        ChoiceTests.ChoiceWorld world = ChoiceTests.BuildChoiceWorld();
        YarnBundle bundle = EmitChoicesBundle(world);
        string directory = Path.Combine(Path.GetTempPath(), $"VnTool.Compile.{Guid.NewGuid():N}");

        try
        {
            var bundles = new List<YarnBundle> { bundle };

            foreach (DialogueNode target in new[] { world.Sample.TargetA, world.Sample.TargetDefault })
            {
                DialogueResult result = world.Sample.Editor.PublishDialogue(target.Id).Result;
                bundles.Add(YarnBundleEmitter.Emit(result, project: world.Sample.Project));
            }

            YarnBundleEmitter.WriteBundles(bundles, directory);
            AnalysisReport report = Analyze(directory);

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

    [Fact]
    public void 조건_안_선택지_번들도_실컴파일된다()
    {
        // W54 — 중첩 들여쓰기·합성 조건·출구 점프가 실제 Yarn 컴파일러를 통과해야 한다.
        ChoiceTests.ChoiceWorld world = ChoiceTests.BuildNestedChoiceWorld();
        YarnBundle bundle = YarnBundleEmitter.Emit(
            world.Dialogue,
            world.Presentation,
            world.Sample.Project,
            Sample.Definition,
            bundleName: "nested_ep");
        string directory = Path.Combine(Path.GetTempPath(), $"VnTool.Compile.{Guid.NewGuid():N}");

        try
        {
            var bundles = new List<YarnBundle> { bundle };

            foreach (DialogueNode target in new[] { world.Sample.TargetA, world.Sample.TargetDefault })
            {
                DialogueResult result = world.Sample.Editor.PublishDialogue(target.Id).Result;
                bundles.Add(YarnBundleEmitter.Emit(result, project: world.Sample.Project));
            }

            YarnBundleEmitter.WriteBundles(bundles, directory);
            AnalysisReport report = Analyze(directory);

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

    [Fact]
    public void 라벨_수정_재발행_재출력에서_OptionId와_순서와_동기_변수는_불변이다()
    {
        ChoiceTests.ChoiceWorld world = ChoiceTests.BuildChoiceWorld();
        YarnBundle before = EmitChoicesBundle(world);

        world.Sample.Editor.SetScriptLineText(
            world.Sample.Script.Id,
            world.Label1,
            string.Empty,
            "완전히 고친 라벨");

        DialogueResult v2 = world.Sample.Editor.PublishDialogue(world.Sample.Dialogue.Id).Result;
        Assert.Equal(2, v2.Identity.Version);

        // 라벨 문구만 고쳤으므로 순서 경고가 없어야 한다.
        Assert.DoesNotContain(
            DialoguePublisher.Draft(world.Sample.Project, world.Sample.Dialogue.Id).Problems,
            problem => problem.Kind == PublishProblemKind.ChoiceOrderChanged);

        world.Sample.Editor.SetPresentationSource(
            world.PresentationNode.Id,
            v2.Identity.ResultId,
            v2.Identity.Version);
        PresentationResult presentation =
            world.Sample.Editor.PublishPresentation(world.PresentationNode.Id).Result;

        YarnBundle after = YarnBundleEmitter.Emit(
            v2,
            presentation,
            world.Sample.Project,
            Sample.Definition,
            bundleName: "choices_ep");

        // OptionId(= 라벨 라인의 LineId 태그 순서)와 $__ch 번호가 그대로다.
        Assert.Contains($"-> 완전히 고친 라벨 #fatigue:+10 #common_ingredient:+15 #line:{world.Label1}",
            after.StoryText, StringComparison.Ordinal);
        Assert.Equal(OptionLineTags(before.StoryText), OptionLineTags(after.StoryText));
        Assert.Equal(SyncSets(before.StoryText), SyncSets(after.StoryText));
        Assert.Equal(
            v2.FindLine(world.Label1)!.Transition!.OptionId,
            world.Dialogue.FindLine(world.Label1)!.Transition!.OptionId);
    }

    private static YarnBundle EmitChoicesBundle(ChoiceTests.ChoiceWorld world)
    {
        return YarnBundleEmitter.Emit(
            world.Dialogue,
            world.Presentation,
            world.Sample.Project,
            Sample.Definition,
            bundleName: "choices_ep");
    }

    private static IReadOnlyList<string> OptionLineTags(string yarn)
    {
        return yarn.Split('\n')
            .Where(line => line.TrimStart().StartsWith("-> ", StringComparison.Ordinal))
            .Select(line => Regex.Match(line, "#line:(\\S+)").Groups[1].Value)
            .ToArray();
    }

    private static IReadOnlyList<string> SyncSets(string yarn)
    {
        return Regex.Matches(yarn, @"<<set \$__ch_\d+ = \d+>>")
            .Select(match => match.Value)
            .ToArray();
    }

    [Fact]
    public void 같은_line_태그를_두_번_내면_컴파일러가_잡는다()
    {
        // C4 회귀 감시 장치의 자체 검증: Pres 사본에 Story와 같은 태그를 중복 출력하는
        // 회귀가 생기면 실컴파일 테스트가 정말로 실패하는지 확인한다.
        var world = BuildWorld();
        YarnBundle bundle = Emit(world);
        string directory = Path.Combine(Path.GetTempPath(), $"VnTool.Compile.{Guid.NewGuid():N}");

        try
        {
            YarnBundleEmitter.WriteTo(bundle, directory);

            // Pres 사본에 태그를 강제로 복제한 사본을 만든다.
            string presPath = Path.Combine(directory, bundle.PresFileName);
            string tampered = File.ReadAllText(presPath, Encoding.UTF8).Replace(
                "첫 줄 그대로\n",
                $"첫 줄 그대로 #line:{world.FirstLineId}\n",
                StringComparison.Ordinal);
            File.WriteAllText(presPath, tampered, new UTF8Encoding(false));

            AnalysisReport report = Analyze(directory);

            Assert.Contains(report.Diagnostics, diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ── 고정 저작 상태 ──────────────────────────────────────────────────────

    private static YarnBundle EmitGoldenBundle() => Emit(BuildWorld());

    private static YarnBundle Emit(BundleWorld world)
    {
        return YarnBundleEmitter.Emit(
            world.Dialogue,
            world.Presentation,
            world.Sample.Project,
            Sample.Definition,
            bundleName: "golden_ep");
    }

    /// <summary>
    /// 조건 갈래·갈래 출구·기본 출구·node set·line set·setup·라인 연출을 모두 갖춘
    /// 결정적 저작 상태. LineId와 발행 시각이 주입되어 언제 만들어도 같은 트리오가 나온다.
    /// </summary>
    private static BundleWorld BuildWorld()
    {
        var sample = new Sample();
        sample.SetNode.Assignments.Add(new VariableAssignment { Variable = "favor", Value = "0" });
        sample.Editor.UpdateCondition(sample.ConditionA.Id, "호감 높음", "$favor >= 5");

        string first = sample.Line("첫 줄 그대로");
        sample.Editor.SetScriptLineText(sample.Script.Id, first, "라루", "첫 줄 그대로");
        string open = sample.Line("갈래 안 대사", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Editor.SetScriptLineText(sample.Script.Id, open, "윌로", "갈래 안 대사");
        string close = sample.Line("갈래 뒤 대사", LineConditionTransition.EndIf());
        sample.Editor.SetScriptLineText(sample.Script.Id, close, string.Empty, "갈래 뒤 대사");

        sample.Editor.SetLineSetOperations(sample.Dialogue.Id, first, new[]
        {
            new SetOperation { Variable = "fatigue", Operator = SetOperatorKind.Add, Value = "10" }
        });
        sample.Editor.SetExitTarget(
            sample.Dialogue.Id, Vn.Authoring.Flow.ExitPortKind.Branch, open, sample.TargetA.Id);
        sample.Editor.SetExitTarget(
            sample.Dialogue.Id, Vn.Authoring.Flow.ExitPortKind.Default, null, sample.TargetDefault.Id);

        DialogueResult dialogue = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        PresentationNode node = sample.Editor.AddPresentationNode(sample.File.Id, name: "골든 연출");
        sample.Editor.SetPresentationSource(node.Id, dialogue.Identity.ResultId, dialogue.Identity.Version);
        sample.Editor.AddPresentationSetupCommand(node.Id, "camera.wide");
        sample.Editor.AddPresentationCommand(node.Id, first, "camera.closeup");
        sample.Editor.AddPresentationCommand(node.Id, open, "acting.smile");

        PresentationResult presentation = sample.Editor.PublishPresentation(node.Id).Result;

        return new BundleWorld(sample, first, dialogue, node, presentation);
    }

    private static AnalysisReport Analyze(string directory)
    {
        var utf8 = new UTF8Encoding(false);
        string projectPath = Path.Combine(directory, "Demo.yarnproject");

        File.WriteAllText(
            projectPath,
            """
            {
              "projectFileVersion": 3,
              "baseLanguage": "ko",
              "sourceFiles": [ "**/*.yarn" ],
              "excludeFiles": []
            }
            """,
            utf8);

        // 변수 선언은 이미터가 낸 <<declare>>가 담당한다 (D4) — 스키마가 같은 변수를
        // 또 선언하면 중복 선언으로 컴파일이 깨진다. 명령 어휘는 런타임 등록 커맨드의
        // 축소판을 실어 VN3002(알 수 없는 명령) 검증까지 통과시킨다.
        string schemaPath = Path.Combine(directory, "game.schema.json");
        File.WriteAllText(
            schemaPath,
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

        return new VnProjectAnalyzer().Analyze(projectPath, schemaPath);
    }

    private static IReadOnlyList<string> LineTagsOf(string yarn)
    {
        return Regex.Matches(yarn, "#line:(\\S+)")
            .Select(match => match.Groups[1].Value)
            .ToArray();
    }

    private static IReadOnlyList<string> TitlesOf(YarnBundle bundle)
    {
        return bundle.Files
            .SelectMany(file => file.Text.Split('\n'))
            .Where(line => line.StartsWith("title: ", StringComparison.Ordinal))
            .ToArray();
    }

    private sealed record BundleWorld(
        Sample Sample,
        string FirstLineId,
        DialogueResult Dialogue,
        PresentationNode PresentationNode,
        PresentationResult Presentation);
}
