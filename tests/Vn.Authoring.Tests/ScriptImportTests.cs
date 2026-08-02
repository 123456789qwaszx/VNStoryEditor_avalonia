using Vn.Authoring.Script;

namespace Vn.Authoring.Tests;

/// <summary>
/// 작가의 평평한 대본을 읽는 규칙.
///
/// <b>지원하지 않는 줄을 조용히 버리지 않는다</b>가 이 파일의 중심 계약이다.
/// 버려진 한 줄은 몇 달 뒤 녹음 스튜디오에서 발견된다.
/// </summary>
public class ScriptParserTests
{
    [Fact]
    public void 화자와_대사를_첫_구분자에서_나눈다()
    {
        ParsedScript parsed = ScriptParser.Parse("윌로: 안녕하세요");

        ParsedScriptLine line = Assert.Single(parsed.Lines);
        Assert.Equal("윌로", line.Speaker);
        Assert.Equal("안녕하세요", line.Text);
        Assert.Empty(parsed.Problems);
    }

    [Fact]
    public void 대사_안의_콜론은_그대로_남는다()
    {
        ParsedScript parsed = ScriptParser.Parse("윌로: 약속은 이렇습니다: 반드시 갚겠습니다.");

        ParsedScriptLine line = Assert.Single(parsed.Lines);
        Assert.Equal("윌로", line.Speaker);
        Assert.Equal("약속은 이렇습니다: 반드시 갚겠습니다.", line.Text);
    }

    [Fact]
    public void 빈_줄과_주석은_건너뛰되_문제로_알리지_않는다()
    {
        ParsedScript parsed = ScriptParser.Parse("// 메모\n\n   \n윌로: 대사\n");

        Assert.Single(parsed.Lines);
        Assert.Empty(parsed.Problems);
    }

    [Fact]
    public void 구분자가_없는_줄은_버리지_않고_화자_없는_줄로_알린다()
    {
        ParsedScript parsed = ScriptParser.Parse("혼잣말처럼 흘러가는 지문");

        ParsedScriptLine line = Assert.Single(parsed.Lines);
        Assert.Equal(string.Empty, line.Speaker);
        Assert.Equal("혼잣말처럼 흘러가는 지문", line.Text);

        ScriptParseProblem problem = Assert.Single(parsed.Problems);
        Assert.Equal(ScriptParseProblemKind.MissingSpeaker, problem.Kind);
        Assert.Equal(1, problem.SourceLineNumber);
    }

    /// <summary>
    /// <c>"12:30에 만나자"</c>를 화자 <c>12</c>로 읽으면 그 줄은 영영 잘못된 화자를 갖는다.
    /// 확신할 수 없으면 자르지 않는다.
    /// </summary>
    [Fact]
    public void 화자로_보이지_않는_앞부분은_자르지_않는다()
    {
        ParsedScript parsed = ScriptParser.Parse("그래서 말인데, 우리 이렇게 하자: 내가 먼저 간다");

        ParsedScriptLine line = Assert.Single(parsed.Lines);
        Assert.Equal(string.Empty, line.Speaker);
        Assert.Equal("그래서 말인데, 우리 이렇게 하자: 내가 먼저 간다", line.Text);
        Assert.Equal(
            ScriptParseProblemKind.AmbiguousSpeaker,
            Assert.Single(parsed.Problems).Kind);
    }

    [Fact]
    public void 앞의_콜론은_화자_없음을_뜻하고_문제가_아니다()
    {
        ParsedScript parsed = ScriptParser.Parse(": 12:30에 만나자");

        ParsedScriptLine line = Assert.Single(parsed.Lines);
        Assert.Equal(string.Empty, line.Speaker);
        Assert.Equal("12:30에 만나자", line.Text);
        Assert.Empty(parsed.Problems);
    }

    [Fact]
    public void 대사가_빈_줄도_남기고_알린다()
    {
        ParsedScript parsed = ScriptParser.Parse("윌로:");

        ParsedScriptLine line = Assert.Single(parsed.Lines);
        Assert.Equal("윌로", line.Speaker);
        Assert.Equal(string.Empty, line.Text);
        Assert.Equal(ScriptParseProblemKind.EmptyText, Assert.Single(parsed.Problems).Kind);
    }

    [Fact]
    public void BOM과_CRLF는_정규화되고_해시에_영향을_주지_않는다()
    {
        ParsedScript crlf = ScriptParser.Parse("﻿윌로: 안녕\r\n라루: 반가워\r\n");
        ParsedScript lf = ScriptParser.Parse("윌로: 안녕\n라루: 반가워\n");

        Assert.Equal(lf.ContentHash, crlf.ContentHash);
        Assert.Equal(
            lf.Lines.Select(line => line.Text),
            crlf.Lines.Select(line => line.Text));
    }

    [Fact]
    public void 같은_내용은_같은_해시를_만든다()
    {
        const string text = "윌로: 안녕\n라루: 반가워\n";

        Assert.Equal(ScriptParser.Parse(text).ContentHash, ScriptParser.Parse(text).ContentHash);
        Assert.NotEqual(
            ScriptParser.Parse(text).ContentHash,
            ScriptParser.Parse(text + "윌로: 하나 더\n").ContentHash);
    }
}
