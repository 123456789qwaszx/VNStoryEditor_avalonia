using Ked.Presentation.Core;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>
/// W67 후속 — 커스텀 이징 곡선. 지키는 것: ① 곡선은 프로젝트에 살고 왕복한다
/// ② `@이름` 참조가 어긋나면 내보내기가 막는다(런타임은 조용히 OutCubic으로 물러서므로
/// 저작이 유일한 방어다) ③ curves.json은 런타임 확정 스키마(배열+name) 그대로다
/// </summary>
public class EaseCurveTests
{
    private static readonly PresentationCommandCatalog Catalog = PresentationCommandCatalog.Default;

    private static CurveKey[] SampleKeys() =>
    [
        new(0f, 0f, 0f, 2.6f),
        new(0.4f, 0.9f, 0.8f, 0.3f),
        new(1f, 1f, 0.1f, 0f)
    ];

    // ── 모델·편집 통로 ────────────────────────────────────────────────────

    [Fact]
    public void 곡선은_편집_통로로_들어가고_되돌리기가_원복한다()
    {
        var sample = new Sample();

        sample.Editor.SetEaseCurve("hop_snappy", SampleKeys());
        Assert.Single(sample.Project.EaseCurves);
        Assert.Equal(0.9f, sample.Project.EaseCurves[0].Keys[1].Value);

        // 같은 이름 = 키 교체(쌓이지 않는다).
        CurveKey[] changed = SampleKeys();
        changed[1] = new CurveKey(0.4f, 0.5f, 0.8f, 0.3f);
        sample.Editor.SetEaseCurve("hop_snappy", changed);
        Assert.Single(sample.Project.EaseCurves);
        Assert.Equal(0.5f, sample.Project.EaseCurves[0].Keys[1].Value);

        sample.Editor.Undo();
        Assert.Equal(0.9f, sample.Project.EaseCurves[0].Keys[1].Value);

        sample.Editor.RemoveEaseCurve("hop_snappy");
        Assert.Empty(sample.Project.EaseCurves);
    }

    [Fact]
    public void 이름과_키_규칙_위반은_저장_자체를_거부한다()
    {
        var sample = new Sample();

        // 런타임 로더는 위반을 경고+무시로 물러선다 — 저작은 만들 때 막는 것이 1차 방어다.
        Assert.Throws<InvalidOperationException>(() => sample.Editor.SetEaseCurve("Hop!", SampleKeys()));
        Assert.Throws<InvalidOperationException>(() => sample.Editor.SetEaseCurve("ok", [new CurveKey(0f, 0f, 0f, 0f)]));
        Assert.Throws<InvalidOperationException>(() => sample.Editor.SetEaseCurve(
            "ok", [new CurveKey(0.1f, 0f, 0f, 0f), new CurveKey(1f, 1f, 0f, 0f)])); // 첫 키 t≠0
        Assert.Empty(sample.Project.EaseCurves);
    }

    [Fact]
    public void 곡선은_manifest로_왕복한다()
    {
        var sample = new Sample();
        sample.Editor.SetEaseCurve("hop_snappy", SampleKeys());

        string json = ProjectManifestJson.Write(sample.Project);
        ProjectManifest manifest = ProjectManifestJson.Read(json);

        EaseCurve restored = Assert.Single(manifest.EaseCurves);
        Assert.Equal("hop_snappy", restored.Name);
        Assert.Equal(SampleKeys(), restored.Keys);
    }

    // ── 텍스트 문법 ──────────────────────────────────────────────────────

    [Fact]
    public void 앳_이름은_문법으로_통과하고_틀린_이름은_오류다()
    {
        Assert.True(CommandText.Parse("<<move_by c1 +2u 0u 12fr @hop_snappy>>", Catalog).Success);

        CommandTextParseResult bad = CommandText.Parse("<<move_by c1 +2u 0u 12fr @Hop-Snappy>>", Catalog);
        Assert.False(bad.Success);
        Assert.Contains("곡선 이름", bad.Error, StringComparison.Ordinal);
    }

    // ── 내보내기 ─────────────────────────────────────────────────────────

    private static (Sample Sample, PresentationResult Presentation, DialogueResult Dialogue)
        PublishWithMove(string easeToken, bool defineCurve)
    {
        var sample = new Sample();

        if (defineCurve)
        {
            sample.Editor.SetEaseCurve("hop_snappy", SampleKeys());
        }

        string line = sample.Line("첫 줄");
        sample.Editor.SetScriptLineText(sample.Script.Id, line, "라루", "첫 줄");
        DialogueResult dialogue = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        PresentationNode node = sample.Editor.AddPresentationNode(sample.File.Id, name: "연출");
        sample.Editor.SetPresentationSource(node.Id, dialogue.Identity.ResultId, dialogue.Identity.Version);
        sample.Editor.AddPresentationCommand(node.Id, line, "char_rig_staging.move_by",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["slot"] = "c1", ["x"] = "+2u", ["duration"] = "12fr", ["ease"] = easeToken
            });

        PresentationResult presentation = sample.Editor.PublishPresentation(node.Id).Result;
        return (sample, presentation, dialogue);
    }

    [Fact]
    public void 프로젝트에_없는_곡선_참조는_내보내기를_막는다()
    {
        (Sample sample, var presentation, DialogueResult dialogue) =
            PublishWithMove("@hop_snappy", defineCurve: false);

        YarnBundle bundle = YarnBundleEmitter.Emit(
            dialogue, presentation, sample.Project, GameDefinition.Empty, "curve_ep");

        Assert.True(bundle.HasBlockingProblems);
        Assert.Contains(bundle.Problems, problem =>
            problem.IsBlocking && problem.Message.Contains("@hop_snappy", StringComparison.Ordinal));
    }

    [Fact]
    public void 곡선이_있으면_통과하고_텍스트에_앳_이름이_실린다()
    {
        (Sample sample, var presentation, DialogueResult dialogue) =
            PublishWithMove("@hop_snappy", defineCurve: true);

        YarnBundle bundle = YarnBundleEmitter.Emit(
            dialogue, presentation, sample.Project, GameDefinition.Empty, "curve_ep");

        Assert.False(bundle.HasBlockingProblems);
        Assert.Contains("<<move_by c1 +2u 0u 12fr @hop_snappy>>", bundle.StoryText, StringComparison.Ordinal);
    }

    [Fact]
    public void curves_json은_런타임_확정_스키마로_나가고_곡선이_없으면_안_나간다()
    {
        string directory = Path.Combine(Path.GetTempPath(), "vn-curve-" + Guid.NewGuid().ToString("N"));

        try
        {
            Assert.Null(YarnBundleEmitter.WriteCurves([], directory));

            var curves = new List<EaseCurve>
            {
                new() { Name = "hop_snappy", Keys = SampleKeys().ToList() }
            };

            string? path = YarnBundleEmitter.WriteCurves(curves, directory);
            Assert.NotNull(path);

            string text = File.ReadAllText(path!);

            // 런타임 회신 스키마: 배열 + name (JsonUtility가 딕셔너리를 못 읽는다).
            Assert.Contains("\"schema\": \"ease-curves/1\"", text, StringComparison.Ordinal);
            Assert.Contains("\"name\": \"hop_snappy\"", text, StringComparison.Ordinal);
            Assert.Contains("\"t\": 0.4", text, StringComparison.Ordinal);
            Assert.Contains("\"outTangent\": 2.6", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\r", text, StringComparison.Ordinal); // LF 고정

            // 결정적 출력 — 같은 입력은 바이트가 같다.
            string first = text;
            YarnBundleEmitter.WriteCurves(curves, directory);
            Assert.Equal(first, File.ReadAllText(path!));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    // ── 커맨드 소유 곡선 표시 ────────────────

    [Fact]
    public void 소유자_표시는_manifest와_undo_스냅샷을_왕복한다()
    {
        var sample = new Sample();
        sample.Editor.SetEaseCurve("cmd_pc_1", SampleKeys(), ownerCommandId: "pc_1");
        sample.Editor.SetEaseCurve("lib_one", SampleKeys());

        ProjectManifest manifest = ProjectManifestJson.Read(ProjectManifestJson.Write(sample.Project));
        Assert.Equal("pc_1", manifest.EaseCurves.Single(curve => curve.Name == "cmd_pc_1").OwnerCommandId);
        Assert.Null(manifest.EaseCurves.Single(curve => curve.Name == "lib_one").OwnerCommandId);

        StoryProject restored = ProjectSnapshotCodec.Decode(ProjectSnapshotCodec.Encode(sample.Project));
        Assert.Equal("pc_1", restored.EaseCurves.Single(curve => curve.Name == "cmd_pc_1").OwnerCommandId);
    }

    [Fact]
    public void 소유_이름은_커맨드_Id에서_안정적으로_나오고_이름_규칙을_지킨다()
    {
        string name = EaseCurve.OwnedNameFor("pc_AB-12");
        Assert.Equal("cmd_pc_ab_12", name);
        Assert.True(EaseCurve.IsValidName(name));
        Assert.Equal(name, EaseCurve.OwnedNameFor("pc_AB-12")); // 같은 Id = 같은 이름
    }

    // ── 곡선 종류 격리 (2026-08-21 `gesture` 개통) ─────────────────────────

    /// <summary>진동 곡선 — (0,0) → (1,0). 중간에 혹 하나.</summary>
    private static CurveKey[] BumpKeys() =>
    [
        new(0f, 0f, 0f, 3f),
        new(0.5f, 1f, 0f, 0f),
        new(1f, 0f, -3f, 0f)
    ];

    [Fact]
    public void 종류가_어긋난_곡선은_내보내기를_막는다()
    {
        // ⚠ 여기서 안 막으면 "툴에서는 저장되는데 재생에서는 조용히 사라지는" 곡선이
        // 된다 — 런타임 로더가 종류로 격리하기 때문이다(2026-08-21 회신). 판정 규칙은
        // 코어 CurveKindRules 하나를 양쪽이 함께 쓴다.
        var sample = new Sample();
        sample.Editor.SetEaseCurve("bump", BumpKeys()); // 진동 곡선인데…

        string line = sample.Line("첫 줄");
        sample.Editor.SetScriptLineText(sample.Script.Id, line, "라루", "첫 줄");
        DialogueResult dialogue = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        PresentationNode node = sample.Editor.AddPresentationNode(sample.File.Id, name: "연출");
        sample.Editor.SetPresentationSource(node.Id, dialogue.Identity.ResultId, dialogue.Identity.Version);

        // …이동 커맨드의 이징 자리에 끼웠다.
        sample.Editor.AddPresentationCommand(node.Id, line, "char_rig_staging.move_by",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["slot"] = "c1", ["x"] = "+2u", ["duration"] = "12fr", ["ease"] = "@bump"
            });

        YarnBundle bundle = YarnBundleEmitter.Emit(
            dialogue, sample.Editor.PublishPresentation(node.Id).Result,
            sample.Project, GameDefinition.Empty, "curve_ep");

        Assert.True(bundle.HasBlockingProblems);
        Assert.Contains(bundle.Problems, problem =>
            problem.Message.Contains("진동", StringComparison.Ordinal) &&
            problem.Message.Contains("이동", StringComparison.Ordinal));
    }

    [Fact]
    public void gesture는_진동_곡선을_받고_이동_곡선은_막는다()
    {
        var sample = new Sample();
        sample.Editor.SetEaseCurve("bump", BumpKeys());
        sample.Editor.SetEaseCurve("hop_snappy", SampleKeys()); // 이동 곡선

        string line = sample.Line("첫 줄");
        sample.Editor.SetScriptLineText(sample.Script.Id, line, "라루", "첫 줄");
        DialogueResult dialogue = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        YarnBundle Emit(string curveToken)
        {
            PresentationNode node = sample.Editor.AddPresentationNode(sample.File.Id, name: "연출" + curveToken);
            sample.Editor.SetPresentationSource(node.Id, dialogue.Identity.ResultId, dialogue.Identity.Version);
            sample.Editor.AddPresentationCommand(node.Id, line, "char_rig_staging.gesture",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["slot"] = "c1", ["yAmp"] = "1u", ["duration"] = "24fr", ["yEase"] = curveToken
                });

            return YarnBundleEmitter.Emit(
                dialogue, sample.Editor.PublishPresentation(node.Id).Result,
                sample.Project, GameDefinition.Empty, "curve_ep");
        }

        // 진동 곡선은 통과하고 토큰이 그대로 실린다.
        YarnBundle good = Emit("@bump");
        Assert.False(good.HasBlockingProblems);
        Assert.Contains("<<gesture c1 0u 1u 24fr \"\" @bump>>", good.StoryText, StringComparison.Ordinal);

        // 이동 곡선을 끼우면 막는다.
        YarnBundle bad = Emit("@hop_snappy");
        Assert.True(bad.HasBlockingProblems);
        Assert.Contains(bad.Problems, problem =>
            problem.Message.Contains("@hop_snappy", StringComparison.Ordinal));
    }
}
