using System.Text;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// 이미터의 사양은 runtime-contract.md다. 여기 테스트 이름의 괄호가 그 조항이다.
/// 어긋난 출력은 컴파일이 되어도 런타임에서 조용히 깨지므로, 파일에 쓰기 전에 잡는다.
/// </summary>
public class YarnBundleEmitterTests
{
    [Fact]
    public void 트리오는_Story_Set_Pres_세_텍스트로_조립된다()
    {
        BundleFixture fixture = BuildFixture();

        YarnBundle bundle = YarnBundleEmitter.Emit(
            fixture.Dialogue,
            fixture.Presentation,
            fixture.Sample.Project,
            Sample.Definition,
            bundleName: "test_ep");

        Assert.Equal("test_ep", bundle.BundleName);
        Assert.Equal(
            new[] { "Story_test_ep.yarn", "Set_test_ep.yarn", "Pres_test_ep.yarn" },
            bundle.Files.Select(file => file.FileName));
        Assert.StartsWith("title: Story_test_ep\n---\n", bundle.StoryText, StringComparison.Ordinal);
        Assert.Contains("title: Set_test_ep\n---\n", bundle.SetText!, StringComparison.Ordinal);
        Assert.Contains("title: Pres_test_ep\n---\n", bundle.PresText!, StringComparison.Ordinal);
        Assert.EndsWith("===\n", bundle.StoryText, StringComparison.Ordinal);
        Assert.False(bundle.HasBlockingProblems);
    }

    [Fact]
    public void Story는_beat와_pres_start로_레인을_열고_라인마다_line_태그를_단다()
    {
        BundleFixture fixture = BuildFixture();

        YarnBundle bundle = Emit(fixture);

        // 레인이 필요한 Story 노드는 자기 pres_start로 연다 (A5).
        Assert.Contains("<<beat Set_test_ep>>\n<<pres_start Pres_test_ep>>\n", bundle.StoryText, StringComparison.Ordinal);

        // Story 대사 라인에 #line: 필수 (C1). 없으면 세이브 로드가 조용히 행에 빠진다.
        Assert.Contains($"첫 줄 #line:{fixture.FirstLineId}", bundle.StoryText, StringComparison.Ordinal);

        // Pres 사본은 무태그 (C4) — 전역 라인 ID 유일성 위반은 컴파일 오류다.
        Assert.DoesNotContain("#line:", bundle.PresText!, StringComparison.Ordinal);
    }

    [Fact]
    public void Pres_사본의_라인_수와_순서는_Story와_같다()
    {
        BundleFixture fixture = BuildFixture();

        YarnBundle bundle = Emit(fixture);

        // 라인 동기화 예산 (B) — 개수·순서가 같아야 락스텝이 유지된다.
        Assert.Equal(
            CountDialogueLines(bundle.StoryText),
            CountDialogueLines(bundle.PresText!));
    }

    [Fact]
    public void set은_Story에만_나오고_조건은_양쪽에_복제된다()
    {
        BundleFixture fixture = BuildFixture();

        YarnBundle bundle = Emit(fixture);

        // D2 — 저장소 공유이므로 Pres에 복제하면 이중 실행된다.
        Assert.Contains("<<set $favor = 0>>", bundle.StoryText, StringComparison.Ordinal);
        Assert.Contains("<<set $fatigue += 10>>", bundle.StoryText, StringComparison.Ordinal);
        Assert.DoesNotContain("<<set", bundle.PresText!, StringComparison.Ordinal);
        Assert.DoesNotContain("<<set", bundle.SetText!, StringComparison.Ordinal);

        // D3 — 분기 내 라인 수가 같아야 하므로 구조를 그대로 복제한다.
        Assert.Contains("<<if $favor >= 5>>", bundle.StoryText, StringComparison.Ordinal);
        Assert.Contains("<<if $favor >= 5>>", bundle.PresText!, StringComparison.Ordinal);
        Assert.Contains("<<endif>>", bundle.PresText!, StringComparison.Ordinal);
    }

    [Fact]
    public void jump_직전에는_pres_end가_나오고_갈래_출구는_갈래_끝에_놓인다()
    {
        BundleFixture fixture = BuildFixture();

        YarnBundle bundle = Emit(fixture);

        // A5 — jump는 서브 레인을 정리하지 않는다. pres_end 없이 노드를 옮기면
        // 다음 노드의 라인마다 advance를 계속 소비하며 조용히 어긋난다.
        int branchLine = bundle.StoryText.IndexOf("갈래 안 #line:", StringComparison.Ordinal);
        int presEnd = bundle.StoryText.IndexOf("<<pres_end>>", StringComparison.Ordinal);
        int jump = bundle.StoryText.IndexOf("<<jump Story_A로_간다>>", StringComparison.Ordinal);
        int endif = bundle.StoryText.IndexOf("<<endif>>", StringComparison.Ordinal);

        Assert.True(branchLine >= 0 && presEnd > branchLine, "pres_end는 갈래 본문 뒤에 있어야 한다");
        Assert.True(jump > presEnd, "jump는 pres_end 뒤에 있어야 한다");
        Assert.True(endif > jump, "갈래 출구는 endif 앞, 갈래의 끝에 있어야 한다");

        // 기본 출구에도 pres_end가 선행한다.
        Assert.Contains("<<pres_end>>\n<<jump Story_기본으로_간다>>", bundle.StoryText, StringComparison.Ordinal);

        // Pres에는 jump가 없다 — 서브 레인은 자연 소진으로 닫힌다 (A4).
        Assert.DoesNotContain("<<jump", bundle.PresText!, StringComparison.Ordinal);
        Assert.DoesNotContain("pres_end", bundle.PresText!, StringComparison.Ordinal);
    }

    [Fact]
    public void Setup_커맨드는_Set_노드_본문이_되고_대사는_들어가지_않는다()
    {
        BundleFixture fixture = BuildFixture();

        YarnBundle bundle = Emit(fixture);

        // A2 — 원샷 레인은 대사 라인을 경고와 함께 건너뛴다. Set 노드는 커맨드 전용이다.
        Assert.Contains("<<camera wide>>", bundle.SetText!, StringComparison.Ordinal);
        Assert.DoesNotContain("첫 줄", bundle.SetText!, StringComparison.Ordinal);
        Assert.DoesNotContain(":", bundle.SetText!.Replace("title: Set_test_ep", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void 변수_선언은_Story가_아니라_선언_파일_하나에_모인다()
    {
        BundleFixture fixture = BuildFixture();

        YarnBundle bundle = Emit(fixture);

        // D4 — 런타임에는 declare도 스마트 변수도 없다. 컴파일을 위해 이미터가 선언하되,
        // Story 노드마다 내면 여러 번들을 한 프로그램으로 컴파일할 때 중복 선언으로 깨진다.
        Assert.DoesNotContain("<<declare", bundle.StoryText, StringComparison.Ordinal);
        Assert.Equal(
            new[] { ("favor", "0"), ("fatigue", "0") },
            bundle.Declarations.Select(declaration => (declaration.Variable, declaration.InitialValue)));

        string declarations = YarnBundleEmitter.ComposeDeclarationsText(new[] { bundle })!;
        Assert.StartsWith("title: _declarations\n---\n", declarations, StringComparison.Ordinal);
        Assert.Contains("<<declare $favor = 0>>", declarations, StringComparison.Ordinal);
        Assert.Contains("<<declare $fatigue = 0>>", declarations, StringComparison.Ordinal);
    }

    [Fact]
    public void 같은_변수의_초기값이_합성_간에_다르면_선언_합집합을_거부한다()
    {
        BundleFixture fixture = BuildFixture();
        YarnBundle numberTyped = Emit(fixture);

        // favor를 string으로 선언하는 다른 게임 정의 — 초기값이 ""가 된다.
        var conflicting = new Vn.Authoring.Definition.GameDefinition
        {
            Variables =
            {
                new Vn.Authoring.Definition.VariableSpec { Name = "favor", Type = "string" }
            }
        };
        YarnBundle stringTyped = YarnBundleEmitter.Emit(
            fixture.Dialogue,
            fixture.Presentation,
            fixture.Sample.Project,
            conflicting,
            bundleName: "other_ep");

        Assert.Throws<InvalidOperationException>(() =>
            YarnBundleEmitter.ComposeDeclarationsText(new[] { numberTyped, stringTyped }));
    }

    [Fact]
    public void 메인_레인_전용_커맨드가_연출에_있으면_출력을_막는다()
    {
        BundleFixture fixture = BuildFixture(withPresentationCommands: false);
        fixture.Sample.Editor.AddPresentationCommand(
            fixture.PresentationNode.Id,
            fixture.FirstLineId,
            "control_flow.beat_fx");
        PresentationResult presentation =
            fixture.Sample.Editor.PublishPresentation(fixture.PresentationNode.Id).Result;

        // 기본 카탈로그(definition: null)가 beat_fx를 메인 레인 전용으로 알고 있다 (E2).
        YarnBundle bundle = YarnBundleEmitter.Emit(
            fixture.Dialogue,
            presentation,
            fixture.Sample.Project);

        Assert.True(bundle.HasBlockingProblems);
        Assert.Throws<InvalidOperationException>(() => YarnBundleEmitter.WriteTo(
            bundle,
            Path.Combine(Path.GetTempPath(), $"VnTool.Emit.{Guid.NewGuid():N}")));
    }

    [Fact]
    public void adv_마커가_본문에_있으면_경고를_남긴다()
    {
        var sample = new Sample();
        string line = sample.Line("마커가 [adv/] 있는 줄");
        DialogueResult dialogue = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        YarnBundle bundle = YarnBundleEmitter.Emit(dialogue, project: sample.Project);

        YarnBundleProblem problem = Assert.Single(bundle.Problems);
        Assert.False(problem.IsBlocking);
        Assert.Equal(line, problem.LineId);
        Assert.Contains("[adv/]", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 연출_없이_합성하면_Story_하나만_나온다()
    {
        var sample = new Sample();
        sample.Line("혼자 가는 줄");
        DialogueResult dialogue = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        YarnBundle bundle = YarnBundleEmitter.Emit(dialogue, project: sample.Project);

        Assert.Single(bundle.Files);
        Assert.Null(bundle.SetText);
        Assert.Null(bundle.PresText);
        Assert.DoesNotContain("pres_start", bundle.StoryText, StringComparison.Ordinal);
        Assert.DoesNotContain("<<beat", bundle.StoryText, StringComparison.Ordinal);
        Assert.DoesNotContain("pres_end", bundle.StoryText, StringComparison.Ordinal);
    }

    [Fact]
    public void 파일은_BOM_없는_UTF8_LF로_원자적으로_쓴다()
    {
        BundleFixture fixture = BuildFixture();
        YarnBundle bundle = Emit(fixture);
        string directory = Path.Combine(Path.GetTempPath(), $"VnTool.Emit.{Guid.NewGuid():N}");

        try
        {
            IReadOnlyList<string> written = YarnBundleEmitter.WriteTo(bundle, directory);

            // 트리오 3파일 + 선언 파일 하나.
            Assert.Equal(4, written.Count);
            Assert.Contains(written, path =>
                Path.GetFileName(path) == YarnBundleEmitter.DeclarationsFileName);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));

            foreach (string path in written)
            {
                byte[] bytes = File.ReadAllBytes(path);
                Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "BOM이 없어야 한다");
                Assert.DoesNotContain((byte)'\r', bytes);
            }

            // 같은 입력을 다시 써도 같은 바이트다.
            string before = File.ReadAllText(written[0], Encoding.UTF8);
            YarnBundleEmitter.WriteTo(Emit(fixture), directory);
            Assert.Equal(before, File.ReadAllText(written[0], Encoding.UTF8));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static YarnBundle Emit(BundleFixture fixture)
    {
        return YarnBundleEmitter.Emit(
            fixture.Dialogue,
            fixture.Presentation,
            fixture.Sample.Project,
            Sample.Definition,
            bundleName: "test_ep");
    }

    private static int CountDialogueLines(string yarn)
    {
        // 커맨드·타이틀·구분선을 뺀 나머지가 대사 라인이다.
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
    /// 조건 갈래·갈래 출구·기본 출구·node set·line set·setup·라인 연출을 모두 갖춘 작은 합성.
    /// </summary>
    private static BundleFixture BuildFixture(bool withPresentationCommands = true)
    {
        var sample = new Sample();
        sample.SetNode.Assignments.Add(new VariableAssignment { Variable = "favor", Value = "0" });
        sample.Editor.UpdateCondition(sample.ConditionA.Id, "호감 높음", "$favor >= 5");

        string first = sample.Line("첫 줄");
        sample.Editor.SetScriptLineText(sample.Script.Id, first, "라루", "첫 줄");
        string open = sample.Line("갈래 안", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Editor.SetScriptLineText(sample.Script.Id, open, "윌로", "갈래 안");
        string close = sample.Line("갈래 뒤", LineConditionTransition.EndIf());
        sample.Editor.SetLineSetOperations(sample.Dialogue.Id, first, new[]
        {
            new SetOperation { Variable = "fatigue", Operator = SetOperatorKind.Add, Value = "10" }
        });
        sample.Editor.SetExitTarget(sample.Dialogue.Id, Vn.Authoring.Flow.ExitPortKind.Branch, open, sample.TargetA.Id);
        sample.Editor.SetExitTarget(sample.Dialogue.Id, Vn.Authoring.Flow.ExitPortKind.Default, null, sample.TargetDefault.Id);

        DialogueResult dialogue = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        PresentationNode node = sample.Editor.AddPresentationNode(sample.File.Id, name: "연출");
        sample.Editor.SetPresentationSource(node.Id, dialogue.Identity.ResultId, dialogue.Identity.Version);
        sample.Editor.AddPresentationSetupCommand(node.Id, "camera.wide");

        PresentationResult? presentation = null;

        if (withPresentationCommands)
        {
            sample.Editor.AddPresentationCommand(node.Id, first, "camera.closeup");
            sample.Editor.AddPresentationCommand(node.Id, open, "acting.smile");
            presentation = sample.Editor.PublishPresentation(node.Id).Result;
        }

        return new BundleFixture(sample, first, dialogue, node, presentation);
    }

    private sealed record BundleFixture(
        Sample Sample,
        string FirstLineId,
        DialogueResult Dialogue,
        PresentationNode PresentationNode,
        PresentationResult? Presentation);
}
