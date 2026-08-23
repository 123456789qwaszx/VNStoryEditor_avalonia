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
    public void 타이틀은_대사엔트리를_정규화한_것이다()
    {
        // ⛔ `Story_` 접두는 2026-08-24에 폐지됐다(소유자, 런타임과 함께). 접두가 <b>두
        // 번</b> 붙고 있었다 — 기획자가 `대사엔트리`에 이미 `Story_ch05_01`이라 적는데
        // 이미터가 또 붙여 `Story_Story_ch05_01`이 나갔다.
        //
        // ⚠ 그래도 <b>규칙은 여전히 두 단계다</b>: 정규화가 남아 있다. `new01`처럼 얌전한
        // 이름만 보면 그것이 안 드러나므로 공백·점·하이픈을 케이스로 든다. 진행
        // 내보내기의 DialogueEntryId가 이것과 같은 글자여야 런타임이 노드를 찾는다.
        Assert.Equal("new01", YarnBundleEmitter.StoryNodeTitleOf("new01"));
        Assert.Equal("장면_1", YarnBundleEmitter.StoryNodeTitleOf("장면 1"));
        Assert.Equal("a_b_c", YarnBundleEmitter.StoryNodeTitleOf("a.b-c"));

        // 대사엔트리가 `Story_`로 시작하면 그 글자가 <b>그대로</b> 타이틀이다 — 이미터가
        // 덧붙이지 않는다.
        Assert.Equal("Story_ch05_01", YarnBundleEmitter.StoryNodeTitleOf("Story_ch05_01"));

        // 이름이 없으면 번들 이름 규칙이 정한 대체 이름을 그대로 쓴다 — 여기서 따로
        // 판단하지 않는다(규칙 사본 금지).
        Assert.Equal(
            YarnBundleEmitter.StoryTitleOf(YarnBundleEmitter.BundleNameOf(null, "n7")),
            YarnBundleEmitter.StoryNodeTitleOf(null, "n7"));
    }

    [Fact]
    public void 대본은_Story_텍스트_하나로_조립된다()
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
            new[] { "test_ep.yarn" },
            bundle.Files.Select(file => file.FileName));
        Assert.StartsWith("title: test_ep\n---\n", bundle.StoryText, StringComparison.Ordinal);
        // 2026-08-18 — 파일 하나다. 레인이 없어져 Set·Pres 사본을 만들지 않는다.
        Assert.EndsWith("===\n", bundle.StoryText, StringComparison.Ordinal);
        Assert.False(bundle.HasBlockingProblems);
    }

    [Fact]
    public void 레인_진입_커맨드는_없고_라인마다_line_태그를_단다()
    {
        BundleFixture fixture = BuildFixture();

        YarnBundle bundle = Emit(fixture);

        // 레인 진입 커맨드는 사라졌다 — 열 레인이 없다.
        Assert.DoesNotContain("<<beat Set_", bundle.StoryText, StringComparison.Ordinal);
        Assert.DoesNotContain("pres_start", bundle.StoryText, StringComparison.Ordinal);

        // Story 대사 라인에 #line: 필수 (C1). 없으면 세이브 로드가 조용히 행에 빠진다.
        Assert.Contains($"첫 줄 #line:{fixture.FirstLineId}", bundle.StoryText, StringComparison.Ordinal);
    }

    [Fact]
    public void set과_조건은_Story_안에_한_벌만_있다()
    {
        BundleFixture fixture = BuildFixture();

        YarnBundle bundle = Emit(fixture);

        // 작가가 줄에 단 set은 Story에 한 번만 나온다 — 복제할 레인이 없다.
        Assert.Contains("<<set $__t1_sf_test_fatigue += 10>>", bundle.StoryText, StringComparison.Ordinal);

        // ⛔ 설정노드의 초기값은 <b>머리에 안 나온다</b> (2026-08-24, 작업지시 §4).
        // 나오면 그 초기화의 수명이 에피소드가 되어, 앞 에피소드에서 켠 값이 지워진다.
        Assert.DoesNotContain("<<set $__t1_sf_test_favor", bundle.StoryText, StringComparison.Ordinal);

        // 대신 선언으로 나간다 — 런타임이 챕터 진입에서 이 초기값으로 되돌린다.
        Assert.Contains(
            bundle.Declarations,
            declaration => declaration.Variable == "__t1_sf_test_favor" &&
                declaration.InitialValue == "0");

        // 조건 구조는 Story 안에 그대로 선다.
        Assert.Contains("<<if $__t1_sf_test_favor >= 5>>", bundle.StoryText, StringComparison.Ordinal);
        Assert.Contains("<<endif>>", bundle.StoryText, StringComparison.Ordinal);
    }

    [Fact]
    public void 갈래_출구는_갈래의_끝에_놓인다()
    {
        BundleFixture fixture = BuildFixture();

        YarnBundle bundle = Emit(fixture);

        // 2026-08-18 — 레인이 없어져 출구 앞의 <<pres_end>>도 함께 사라졌다.
        // 조건 갈래의 출구는 detour다 — 커스텀 씬을 재생하고 갈래로 돌아온다.
        int branchLine = bundle.StoryText.IndexOf("갈래 안 #line:", StringComparison.Ordinal);
        int detour = bundle.StoryText.IndexOf("<<detour A로_간다>>", StringComparison.Ordinal);
        int endif = bundle.StoryText.IndexOf("<<endif>>", StringComparison.Ordinal);

        Assert.True(branchLine >= 0 && detour > branchLine, "detour는 갈래 본문 뒤에 있어야 한다");
        Assert.DoesNotContain("pres_end", bundle.StoryText, StringComparison.Ordinal);
        Assert.True(endif > detour, "갈래 출구는 endif 앞, 갈래의 끝에 있어야 한다");

        Assert.Contains("<<jump 기본으로_간다>>", bundle.StoryText, StringComparison.Ordinal);
    }

    [Fact]
    public void 노드_셋업_커맨드는_Story_머리에_인라인으로_선다()
    {
        BundleFixture fixture = BuildFixture();

        YarnBundle bundle = Emit(fixture);

        // 2026-08-18 — LineId 없는 커맨드는 Set 노드(원샷 레인) 본문이었다. 레인이
        // 없어져 Story 머리(첫 본문 앞)에 그대로 선다.
        int camera = bundle.StoryText.IndexOf("<<camera wide>>", StringComparison.Ordinal);
        int firstLine = bundle.StoryText.IndexOf("첫 줄", StringComparison.Ordinal);

        Assert.True(camera >= 0, "노드 셋업 커맨드가 Story에 있어야 한다");
        Assert.True(firstLine > camera, "셋업 커맨드는 첫 대사보다 앞에 있어야 한다");
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
            new[] { ("__t1_sf_test_favor", "0"), ("__t1_sf_test_fatigue", "0") },
            bundle.Declarations.Select(declaration => (declaration.Variable, declaration.InitialValue)));

        string declarations = YarnBundleEmitter.ComposeDeclarationsText(new[] { bundle })!;
        Assert.StartsWith("title: _declarations\n---\n", declarations, StringComparison.Ordinal);
        Assert.Contains("<<declare $__t1_sf_test_favor = 0>>", declarations, StringComparison.Ordinal);
        Assert.Contains("<<declare $__t1_sf_test_fatigue = 0>>", declarations, StringComparison.Ordinal);
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
    public void 메인_레인_전용_커맨드도_그냥_나간다()
    {
        // 2026-08-18 — 레인이 하나뿐이라 "메인 레인 전용"이 가릴 대상을 잃었다.
        // 예전에는 beat_fx 같은 커맨드가 Set·Pres로 새지 않도록 막았는데(옛 §E2),
        // 이제 모든 커맨드가 메인 레인에 있으므로 막을 이유가 없다.
        //
        // ⚠ 카탈로그의 `mainLaneOnly` 플래그는 이로써 아무 데서도 안 쓰인다 —
        // 레인이 다시 생기면 그때 이 검사도 함께 돌아온다.
        BundleFixture fixture = BuildFixture(withPresentationCommands: false);
        fixture.Sample.Editor.AddPresentationCommand(
            fixture.PresentationNode.Id,
            fixture.FirstLineId,
            "control_flow.beat_fx");
        PresentationResult presentation =
            fixture.Sample.Editor.PublishPresentation(fixture.PresentationNode.Id).Result;

        YarnBundle bundle = YarnBundleEmitter.Emit(
            fixture.Dialogue,
            presentation,
            fixture.Sample.Project);

        Assert.False(bundle.HasBlockingProblems);
        Assert.Contains("beat_fx", bundle.StoryText, StringComparison.Ordinal);
    }

    [Fact]
    public void 연출_없이_합성하면_Story_하나만_나온다()
    {
        var sample = new Sample();
        sample.Line("혼자 가는 줄");
        DialogueResult dialogue = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        YarnBundle bundle = YarnBundleEmitter.Emit(dialogue, project: sample.Project);

        Assert.Single(bundle.Files);
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

            // 대본 하나 + 선언 파일 하나 (2026-08-18 — 트리오가 아니다).
            Assert.Equal(2, written.Count);
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
