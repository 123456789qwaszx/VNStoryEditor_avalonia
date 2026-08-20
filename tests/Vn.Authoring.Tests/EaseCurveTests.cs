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
/// ④ 베이크는 편집하기 좋은 출발점이다 — 키 자리에서 정확하고 사이에서 가깝다.
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

    // ── 베이크 ───────────────────────────────────────────────────────────

    [Fact]
    public void 베이크는_키_자리에서_정확하고_사이에서_가깝다()
    {
        foreach (EaseKind kind in (EaseKind[])[EaseKind.Linear, EaseKind.OutCubic, EaseKind.InOutSine])
        {
            CurveKey[] keys = EaseCurveBaker.Bake(kind);

            Assert.Equal(EaseCurveBaker.KeyCount, keys.Length);
            Assert.Equal(0f, keys[0].Time);
            Assert.Equal(1f, keys[^1].Time);
            Assert.Null(EaseCurve.ValidateKeys(keys)); // 런타임 로더 규칙을 그대로 통과한다

            // 키 자리 = 원형 그대로.
            foreach (CurveKey key in keys)
            {
                Assert.Equal(EaseFunctions.Evaluate(kind, key.Time), key.Value, 4);
            }

            // 키 사이 = 편집 출발점으로 충분히 가깝다(완벽 재현이 목표가 아니다 —
            // 5키로 만질 만한 모양이 목표다). Linear는 Hermite가 정확히 재현한다.
            double tolerance = kind == EaseKind.Linear ? 1e-4 : 0.02;

            for (int i = 0; i <= 64; i++)
            {
                float t = i / 64f;
                double error = Math.Abs(
                    CurveFunctions.Evaluate(keys, t) - EaseFunctions.Evaluate(kind, t));
                Assert.True(error < tolerance, $"{kind} t={t}: 오차 {error}");
            }
        }
    }
}
