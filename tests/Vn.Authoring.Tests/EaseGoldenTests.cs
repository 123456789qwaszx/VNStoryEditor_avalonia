using System.Text.Json;
using Ked.Presentation.Core;

namespace Vn.Authoring.Tests;

/// <summary>
/// W66b — 사본 코어의 <see cref="EaseFunctions"/> ↔ 런타임 골든 덤프(ease-golden.json).
///
/// 등가의 1차 심판은 런타임 쪽 EditMode 테스트다(DOTween을 참조할 수 있는 유일한 자리).
/// 이쪽 대조의 역할은 다르다: <b>사본 코어가 낡는 사고</b> — 저쪽 이징이 바뀌었는데
/// 이쪽 사본이 옛 수식인 채로 프리뷰가 다른 모양을 그리는 것 — 를 픽스처가 잡는다.
/// 덤프 갱신 절차는 저쪽 메뉴 Ked/W66b/Export Ease Golden Dump → 파일 교체.
/// </summary>
public class EaseGoldenTests
{
    private static readonly string FixturePath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "TuningFixtures", "ease-golden.json"));

    private sealed record GoldenEase(string Name, float[] Samples);

    private static (float Overshoot, float Period, IReadOnlyList<GoldenEase> Eases) Load()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(FixturePath));
        JsonElement root = document.RootElement;

        var eases = root.GetProperty("eases").EnumerateArray()
            .Select(entry => new GoldenEase(
                entry.GetProperty("name").GetString()!,
                entry.GetProperty("samples").EnumerateArray()
                    .Select(sample => sample.GetSingle()).ToArray()))
            .ToArray();

        return (
            root.GetProperty("overshootOrAmplitude").GetSingle(),
            root.GetProperty("period").GetSingle(),
            eases);
    }

    [Fact]
    public void 골든_덤프의_전_항목_전_샘플이_사본_코어와_1e4_안이다()
    {
        (float overshoot, float period, IReadOnlyList<GoldenEase> eases) = Load();

        Assert.Equal(257, eases[0].Samples.Length);

        foreach (GoldenEase golden in eases)
        {
            var kind = Enum.Parse<EaseKind>(golden.Name);

            for (int i = 0; i < golden.Samples.Length; i++)
            {
                float t = i / 256f;
                float evaluated = EaseFunctions.Evaluate(kind, t, 1f, overshoot, period);

                Assert.True(
                    Math.Abs(evaluated - golden.Samples[i]) < 1e-4f,
                    $"{golden.Name} t={t}: 코어 {evaluated} vs 골든 {golden.Samples[i]}");
            }
        }
    }

    [Fact]
    public void 이징_어휘는_골든_덤프와_양방향으로_같다()
    {
        // 덤프의 eases 목록이 표준 이징의 정본이다 — enum이 더 갖거나 덜 가지면
        // 카탈로그 후보(W67)와 런타임 재생이 어긋난다.
        (_, _, IReadOnlyList<GoldenEase> eases) = Load();

        string[] golden = eases.Select(entry => entry.Name)
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
        string[] core = Enum.GetNames<EaseKind>()
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.Equal(golden, core);
    }

    [Fact]
    public void 기본_상수는_덤프가_적은_값과_같다()
    {
        (float overshoot, float period, _) = Load();

        Assert.Equal(EaseFunctions.DefaultOvershootOrAmplitude, overshoot, 5);
        Assert.Equal(EaseFunctions.DefaultPeriod, period, 5);
    }
}
