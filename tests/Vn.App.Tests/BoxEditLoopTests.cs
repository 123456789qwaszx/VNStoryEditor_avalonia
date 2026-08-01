using Vn.App.Views;
using Vn.Core;
using Vn.Core.Analysis;
using Vn.Core.Story;

namespace Vn.App.Tests;

/// <summary>
/// 구조 카드의 TextBox는 사용자가 글자를 칠 때뿐 아니라 바인딩이 초기값을 넣을 때도
/// TextChanged를 낸다. 그 이벤트를 편집으로 취급하면 두 가지가 한꺼번에 무너진다.
///
/// 1. 프로젝트를 열자마자 문서가 "저장되지 않음"이 된다. 작가는 고친 적이 없다.
/// 2. 그 편집이 거부되면 카드 목록을 다시 만들고, 새 TextBox가 또 TextChanged를 내서
///    레이아웃이 영원히 끝나지 않는다. 창은 뜨지만 아무 조작도 받지 않는다.
///
/// 그래서 "지금 카드 내용이 원문 줄과 실제로 다른가"가 편집의 유일한 기준이어야 한다.
/// </summary>
public class BoxEditLoopTests
{
    private static IReadOnlyList<object> BodyOf(string sampleDirectory, string? nodeTitle = null)
    {
        AnalysisReport report = new VnProjectAnalyzer().Analyze(
            $"../../../../../samples/{sampleDirectory}/Demo.yarnproject",
            $"../../../../../samples/{sampleDirectory}/game.schema.json");

        StoryNode node = nodeTitle is null
            ? report.Nodes[0]
            : Assert.Single(report.Nodes, item => item.Title == nodeTitle);

        return BoxListView.BuildChildren(node.Body, File.ReadAllText(node.FilePath));
    }

    private static IEnumerable<BoxItem> AllBoxes(IReadOnlyList<object> body)
    {
        foreach (object item in body)
        {
            switch (item)
            {
                case BoxItem box:
                    yield return box;
                    break;

                case BlockItem block:
                    foreach (BranchItem branch in block.Branches)
                    {
                        foreach (BoxItem nested in AllBoxes(branch.Children))
                        {
                            yield return nested;
                        }
                    }

                    break;
            }
        }
    }

    [Theory]
    [InlineData("Valid")]
    [InlineData("Real")]
    public void 카드를_막_만들었을_때는_고쳐진_것이_없다(string sample)
    {
        BoxItem[] boxes = AllBoxes(BodyOf(sample)).ToArray();

        Assert.NotEmpty(boxes);
        Assert.All(boxes, box => Assert.False(
            box.HasPendingChange,
            $"'{box.Text}' 카드가 그려지자마자 편집된 것으로 보고되었습니다."));
    }

    [Fact]
    public void 대사를_고치면_변경으로_본다()
    {
        BoxItem box = AllBoxes(BodyOf("Valid", "Start")).First(item => !item.IsLocked);

        box.Text = box.Text + " 정말로요.";

        Assert.True(box.HasPendingChange);
    }

    [Fact]
    public void 화자를_고치면_변경으로_본다()
    {
        BoxItem box = AllBoxes(BodyOf("Real"))
            .First(item => !item.IsLocked && !string.IsNullOrEmpty(item.Speaker));

        box.Speaker = box.Speaker + "2";

        Assert.True(box.HasPendingChange);
    }

    /// <summary>
    /// 고쳤다가 원래대로 되돌린 것도 변경이 아니다.
    /// 되돌린 상태를 계속 편집으로 보고하면 저장할 것이 없는데도 문서가 계속 더러운 채로 남는다.
    /// </summary>
    [Fact]
    public void 고쳤다가_되돌리면_다시_변경이_아니다()
    {
        BoxItem box = AllBoxes(BodyOf("Valid", "Start")).First(item => !item.IsLocked);

        string originalText = box.Text;
        string originalSpeaker = box.Speaker;

        box.Text = "잠깐 다른 말";
        Assert.True(box.HasPendingChange);

        box.Text = originalText;
        box.Speaker = originalSpeaker;
        Assert.False(box.HasPendingChange);
    }

    /// <summary>
    /// 잠긴 카드는 원문 줄을 기억하지 않는다. 그런 카드가 편집을 보고하면
    /// 적용은 늘 실패하고 다시 그리기만 반복된다.
    /// </summary>
    [Fact]
    public void 잠긴_카드는_변경을_보고하지_않는다()
    {
        BoxItem[] locked = AllBoxes(BodyOf("Real")).Where(item => item.IsLocked).ToArray();

        Assert.NotEmpty(locked);
        Assert.All(locked, box => Assert.False(box.HasPendingChange));
    }
}
