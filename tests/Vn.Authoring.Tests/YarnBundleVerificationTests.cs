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
    [InlineData("golden_ep.yarn")]
    [InlineData("declarations.yarn")]
    public void 골든_대본과_글자_하나까지_같다(string fileName)
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

        // 대본이 새 문구를 담는다 (2026-08-18 — 대조할 Pres 사본이 없다).
        Assert.Contains($"완전히 고친 첫 줄 #line:{world.FirstLineId}", after.StoryText, StringComparison.Ordinal);
        Assert.DoesNotContain("첫 줄 그대로", after.StoryText, StringComparison.Ordinal);

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

            // 2026-08-18 — 대본 하나다. 레인 사본 노드(Set/Pres)는 나오지 않는다.
            string[] titles = report.Nodes.Select(node => node.Title).ToArray();
            Assert.Contains("golden_ep", titles);
            Assert.DoesNotContain("Set_golden_ep", titles);
            Assert.DoesNotContain("Pres_golden_ep", titles);
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
            // 작가의 아이템·능력은 챕터 접두를 받는다 (2026-08-17) — 이 픽스처의 favor·trust는
            // 둘 다 작가 설정노드의 것이다. 같은 판의 두 번들이 같은 이름을 쓰면 선언은
            // 여전히 한 번뿐이다(접두가 같으니 합집합도 그대로 돈다).
            Assert.Single(Regex.Matches(declarations, Regex.Escape("<<declare $__t1_sf_test_favor")));
            Assert.Contains("<<declare $__t1_sf_test_trust = 0>>", declarations, StringComparison.Ordinal);

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
    [InlineData("choices_ep.yarn")]
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
    public void 결합_종료_번들도_실컴파일된다()
    {
        // W55 — 조건 종료 한 줄이 선택지도 닫는다: Pres에서 선택 합성 조건이
        // 조건 사본보다 먼저 닫혀(endif 두 개) 실제 컴파일을 통과해야 한다.
        var sample = new Sample();
        sample.SetNode.Assignments.Add(new VariableAssignment { Variable = "favor", Value = "0" });
        sample.Editor.UpdateCondition(sample.ConditionA.Id, "호감 높음", "$favor >= 5");

        sample.Line("시작.");
        sample.Line("조건 시작", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Line("사과", LineConditionTransition.BeginChoice());
        string body = sample.Line("사과 본문");
        sample.Line("포도", LineConditionTransition.BeginNextOption());
        sample.Line("포도 본문");
        sample.Line("모두 끝", LineConditionTransition.EndIf()); // 결합 종료 (W55)
        sample.Line("바깥.");

        DialogueResult dialogue = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;
        PresentationNode node = sample.Editor.AddPresentationNode(sample.File.Id, name: "결합 연출");
        sample.Editor.SetPresentationSource(node.Id, dialogue.Identity.ResultId, dialogue.Identity.Version);
        sample.Editor.AddPresentationCommand(node.Id, body, "camera.closeup");
        PresentationResult presentation = sample.Editor.PublishPresentation(node.Id).Result;

        YarnBundle bundle = YarnBundleEmitter.Emit(
            dialogue, presentation, sample.Project, Sample.Definition, bundleName: "combined_ep");

        // 2026-08-18 — 조건 구조는 대본 하나 안에만 선다. 예전 기대값 2는 Pres 사본이
        // 선택 갈래를 <<if $__ch_N>>으로 재현하며 만들던 여분의 endif를 세던 것이고,
        // 그 합성 조건이 사라져 작가가 쓴 조건 하나만 남는다.
        Assert.Single(Regex.Matches(bundle.StoryText, Regex.Escape("<<endif>>")));

        string directory = Path.Combine(Path.GetTempPath(), $"VnTool.Compile.{Guid.NewGuid():N}");

        try
        {
            YarnBundleEmitter.WriteBundles(new[] { bundle }, directory);
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

            // 같은 태그를 한 번 더 심는다. 2026-08-18까지는 Pres 사본에 심었는데,
            // 파일이 하나가 되어 대본 안에서 겹치게 만든다 — 전역 라인 ID 유일성 위반이다.
            string storyPath = Path.Combine(directory, bundle.StoryFileName);
            string original = File.ReadAllText(storyPath, Encoding.UTF8);
            string tampered = original.Replace(
                $"#line:{world.FirstLineId}",
                $"#line:{world.FirstLineId}\n또 한 줄 #line:{world.FirstLineId}",
                StringComparison.Ordinal);

            Assert.NotEqual(original, tampered);
            File.WriteAllText(storyPath, tampered, new UTF8Encoding(false));

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

    // ── 연출 실행 변수의 수명은 챕터다 (2026-08-24 호스트 실측) ─────────────

    [Fact]
    public void 설정노드_초기값은_선언으로만_나가고_노드_머리에는_안_박힌다()
    {
        // ⛔ 호스트 실측 (`work-orders/chapter-scope-variables-orders.md`):
        // "에피소드1 열쇠 = true → 에피소드2 열쇠 = false ← 머리의 세 줄이 다시 밟는다."
        //
        // 설정노드의 할당은 <b>"이 챕터에 이런 변수가 있다"</b>는 선언이지, "이 노드에
        // 들어올 때 이 값으로 되돌려라"가 아니다. 같은 목록이 두 뜻으로 쓰이고 있었고,
        // 앞의 것만 참이다. 되돌리기는 챕터 진입에서 런타임이 한 번 한다.
        YarnBundle bundle = EmitGoldenBundle();

        Assert.DoesNotContain("<<set $__t1_sf_test_favor", bundle.StoryText, StringComparison.Ordinal);

        // 그런데 <b>선언에는 있어야 한다</b> — 없으면 런타임이 되돌릴 값을 모른다.
        Assert.Contains(
            bundle.Declarations,
            declaration => declaration.Variable == "__t1_sf_test_favor" &&
                declaration.InitialValue == "0");
    }

    [Fact]
    public void 작가가_줄에_단_set은_그대로_나간다()
    {
        // 지워야 할 것은 초기값 되밟기뿐이다. 이야기 도중의 변화는 [3]의 정상적인 쓰임이라
        // 그대로 간다 — 여기까지 지우면 작가가 쓴 이야기가 사라진다.
        YarnBundle bundle = EmitGoldenBundle();

        Assert.Contains(
            "<<set $__t1_sf_test_fatigue += 10>>", bundle.StoryText, StringComparison.Ordinal);
    }

    [Fact]
    public void 선언_파일이_같은_폴더에_함께_쓰인다()
    {
        // 호스트 §6 — "선언 파일이 유니티에 안 온다". 이쪽 출력에는 있다는 것을 못 박는다:
        // 이 테스트가 초록인 채로 그쪽에 안 닿으면 원인은 복사·임포트 쪽이다.
        YarnBundle bundle = EmitGoldenBundle();

        string directory = Path.Combine(
            Path.GetTempPath(), "vn-decl-file", Guid.NewGuid().ToString("N"));

        try
        {
            IReadOnlyList<string> written = YarnBundleEmitter.WriteBundles([bundle], directory);

            string declarations = Assert.Single(
                written, path => Path.GetFileName(path) == YarnBundleEmitter.DeclarationsFileName);

            Assert.Contains(
                "<<declare $__t1_sf_test_favor = 0>>",
                File.ReadAllText(declarations),
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

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
