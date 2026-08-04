using Vn.Authoring.Flow;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests;

/// <summary>
/// 선택지 제시 근사 — 선택 라인이 옵션 라벨이면 그 블록의 라벨 전부가 한 번에 나온다.
/// 라벨이 아닌 라인(보통 대사·분기 대사)에서는 null — 대사창이 그대로다.
/// </summary>
public class ChoiceOptionBundleTests
{
    private static readonly (ConditionTransitionKind? Kind, string Text)[] Document =
    [
        (null, "일반 대사"),                                    // 0
        (ConditionTransitionKind.BeginChoice, "때린다"),        // 1 — 라벨 1
        (null, "분기 대사 A"),                                  // 2
        (ConditionTransitionKind.BeginNextOption, "도망친다"),  // 3 — 라벨 2
        (null, "분기 대사 B"),                                  // 4
        (ConditionTransitionKind.EndChoice, "선택 후 대사"),    // 5
        (ConditionTransitionKind.BeginChoice, "두 번째 블록"),  // 6 — 다른 블록
    ];

    private static IReadOnlyList<ChoiceOptionBundle.Option>? At(int index) =>
        ChoiceOptionBundle.At(Document, index, line => line.Kind, line => line.Text);

    [Fact]
    public void 라벨을_선택하면_블록의_옵션_전부가_나온다()
    {
        // 첫 라벨에서도, 둘째 라벨에서도 같은 묶음이다 — 선택 표시만 다르다.
        IReadOnlyList<ChoiceOptionBundle.Option> fromFirst = At(1)!;
        Assert.Equal(["때린다", "도망친다"], fromFirst.Select(option => option.Text));
        Assert.Equal([true, false], fromFirst.Select(option => option.IsSelected));

        IReadOnlyList<ChoiceOptionBundle.Option> fromSecond = At(3)!;
        Assert.Equal(["때린다", "도망친다"], fromSecond.Select(option => option.Text));
        Assert.Equal([false, true], fromSecond.Select(option => option.IsSelected));
    }

    [Fact]
    public void 라벨이_아닌_라인에서는_묶음이_없다()
    {
        Assert.Null(At(0));  // 블록 밖 대사
        Assert.Null(At(2));  // 분기 대사 — 이미 고른 뒤이므로 보통 대사창이다
        Assert.Null(At(5));  // EndChoice 라인은 이미 블록 밖이다
        Assert.Null(At(-1)); // 선택 없음
        Assert.Null(At(99));
    }

    [Fact]
    public void 블록은_서로_섞이지_않는다()
    {
        // 두 번째 블록의 라벨은 자기 블록만 모은다.
        IReadOnlyList<ChoiceOptionBundle.Option> second = At(6)!;
        Assert.Equal(["두 번째 블록"], second.Select(option => option.Text));
    }
}
