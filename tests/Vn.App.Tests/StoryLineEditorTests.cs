using System.Text;
using Vn.App.Services;

namespace Vn.App.Tests;

/// <summary>
/// 대사 한 줄을 고쳤을 때 나머지가 한 바이트도 안 달라져야 한다.
///
/// 트리로 Yarn을 다시 만들면 작가가 한 줄만 고쳤는데 파일 전체의 공백·주석·줄바꿈이 바뀐다.
/// <see cref="StoryFileService"/>의 왕복 검사는 인코딩·BOM·줄바꿈 수준이라 이것을 못 잡는다.
/// 그래서 원본 문자열에서 그 줄의 구간만 갈아 끼운다.
/// </summary>
public class StoryLineEditorTests
{
    private const string Source =
        "title: Start\r\n" +
        "---\r\n" +
        "Ann: 어서 오세요.\r\n" +
        "    라루: 들여쓴 대사.\r\n" +
        "Ann: 마지막.\r\n" +
        "===\r\n";

    [Fact]
    public void 한_줄만_고치면_나머지_바이트가_그대로다()
    {
        string edited = StoryLineEditor.Apply(
            Source,
            new[] { new StoryLineEdit(3, "Ann", "다시 오셨군요.") });

        Assert.Equal(
            "title: Start\r\n" +
            "---\r\n" +
            "Ann: 다시 오셨군요.\r\n" +
            "    라루: 들여쓴 대사.\r\n" +
            "Ann: 마지막.\r\n" +
            "===\r\n",
            edited);

        // 고친 줄을 뺀 나머지를 바이트로 대조한다.
        AssertOtherLinesUnchanged(Source, edited, changedLine: 3);
    }

    [Fact]
    public void 줄바꿈을_건드리지_않는다()
    {
        // CRLF 파일에서 CR이 사라지면 모든 줄이 diff에 뜬다.
        string edited = StoryLineEditor.Apply(
            Source,
            new[] { new StoryLineEdit(3, "Ann", "짧게.") });

        // CR과 LF의 개수가 그대로여야 한다. 하나라도 어긋나면 줄바꿈을 만진 것이다.
        Assert.Equal(Source.Count(c => c == '\r'), edited.Count(c => c == '\r'));
        Assert.Equal(Source.Count(c => c == '\n'), edited.Count(c => c == '\n'));

        // 외톨이 CR이나 LF가 생기지 않았는지도 본다.
        Assert.Equal(
            Source.Count(c => c == '\r'),
            System.Text.RegularExpressions.Regex.Matches(edited, "\r\n").Count);
    }

    [Fact]
    public void 들여쓰기를_원본에서_가져온다()
    {
        string edited = StoryLineEditor.Apply(
            Source,
            new[] { new StoryLineEdit(4, "라루", "고친 대사.") });

        Assert.Contains("    라루: 고친 대사.\r\n", edited, StringComparison.Ordinal);
        AssertOtherLinesUnchanged(Source, edited, changedLine: 4);
    }

    [Fact]
    public void 화자를_지우면_텍스트만_남는다()
    {
        string edited = StoryLineEditor.Apply(
            Source,
            new[] { new StoryLineEdit(3, null, "화자 없는 대사.") });

        Assert.Contains("\r\n화자 없는 대사.\r\n", edited, StringComparison.Ordinal);
    }

    [Fact]
    public void 여러_줄을_한_번에_고쳐도_자리가_밀리지_않는다()
    {
        string edited = StoryLineEditor.Apply(
            Source,
            new[]
            {
                new StoryLineEdit(3, "Ann", "훨씬 더 긴 첫 대사로 바꾼다."),
                new StoryLineEdit(5, "Ann", "짧게")
            });

        Assert.Contains("Ann: 훨씬 더 긴 첫 대사로 바꾼다.\r\n", edited, StringComparison.Ordinal);
        Assert.Contains("Ann: 짧게\r\n", edited, StringComparison.Ordinal);
        Assert.Contains("    라루: 들여쓴 대사.\r\n", edited, StringComparison.Ordinal);
    }

    [Fact]
    public void 없는_줄을_고치라고_하면_아무것도_바뀌지_않는다()
    {
        Assert.Equal(
            Source,
            StoryLineEditor.Apply(Source, new[] { new StoryLineEdit(99, "Ann", "없음") }));
    }

    /// <summary>고친 줄을 뺀 나머지 줄이 바이트 단위로 같은지 본다.</summary>
    private static void AssertOtherLinesUnchanged(string before, string after, int changedLine)
    {
        string[] originalLines = before.Split("\r\n");
        string[] editedLines = after.Split("\r\n");

        Assert.Equal(originalLines.Length, editedLines.Length);

        for (int index = 0; index < originalLines.Length; index++)
        {
            if (index + 1 == changedLine)
            {
                continue;
            }

            Assert.Equal(
                Encoding.UTF8.GetBytes(originalLines[index]),
                Encoding.UTF8.GetBytes(editedLines[index]));
        }
    }
}
