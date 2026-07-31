using System.Text;
using Vn.Core.Analysis;
using Vn.Core.Story;

namespace Vn.Core.Tests;

public class StoryLineTests
{
    private const string ValidProject = "../../../../../samples/Valid/Demo.yarnproject";
    private const string ValidSchema = "../../../../../samples/Valid/game.schema.json";

    private static StoryNode Node(AnalysisReport report, string title)
    {
        return Assert.Single(report.Nodes, node => node.Title == title);
    }

    private static AnalysisReport AnalyzeValid()
    {
        return new VnProjectAnalyzer().Analyze(ValidProject, ValidSchema);
    }

    [Fact]
    public void Valid의_Start는_라인이_셋이다()
    {
        StoryNode start = Node(AnalyzeValid(), "Start");

        Assert.Equal(new[] { 3, 7, 11 }, start.Lines.Select(line => line.Line));
    }

    [Fact]
    public void 선택지만_IsOption이고_셋_다_Depth가_0이다()
    {
        StoryNode start = Node(AnalyzeValid(), "Start");

        Assert.Equal(
            new[] { false, true, true },
            start.Lines.Select(line => line.IsOption));

        Assert.All(start.Lines, line => Assert.Equal(0, line.Depth));
    }

    [Fact]
    public void 화자를_분리한다()
    {
        AnalysisReport report = AnalyzeValid();

        StoryLine dialogue = Node(report, "Start").Lines[0];
        Assert.Equal("Ann", dialogue.Speaker);
        Assert.Equal("어서 오세요.", dialogue.Text);

        // 선택지에는 화자가 없다. 화살표는 텍스트에 남지 않는다.
        StoryLine option = Node(report, "Start").Lines[1];
        Assert.Null(option.Speaker);
        Assert.Equal("열쇠를 건넨다", option.Text);
    }

    [Fact]
    public void 명령은_다음_라인에_쌓인다()
    {
        StoryNode start = Node(AnalyzeValid(), "Start");

        // 4행 <<play_bgm>>은 독립 항목이 아니라 7행 선택지의 쌓인 명령이다.
        Assert.Empty(start.Lines[0].CommandsSincePreviousLine);

        Assert.Equal(
            new[] { "<<play_bgm \"guesthouse_day\">>", "<<set $affection_ann += 1>>" },
            start.Lines[1].CommandsSincePreviousLine);

        Assert.Equal(
            new[]
            {
                "<<set $has_room_key = true>>",
                "<<give_item \"room_key\">>",
                "<<jump Ending>>"
            },
            start.Lines[2].CommandsSincePreviousLine);
    }

    /// <summary>
    /// Valid/Story.yarn 12행의 <c>&lt;&lt;jump Ending&gt;&gt;</c>는 뒤에 라인이 없어서
    /// 어느 박스에도 들어가지 않는다. 지금은 의도된 동작이다.
    ///
    /// 이 테스트가 없으면 나중에 진짜 버그로 명령이 사라져도 아무도 모른다.
    /// 10행과 12행의 원문이 <c>&lt;&lt;jump Ending&gt;&gt;</c>로 똑같으므로 문자열이 아니라
    /// 개수로 확인한다. 12행이 섞여 들어오면 둘이 된다.
    /// </summary>
    [Fact]
    public void 마지막_라인_뒤에_남은_명령은_어느_박스에도_안_들어간다()
    {
        StoryNode start = Node(AnalyzeValid(), "Start");

        string[] collected = start.Lines
            .SelectMany(line => line.CommandsSincePreviousLine)
            .ToArray();

        // 본문의 명령은 4, 5, 8, 9, 10, 12행으로 여섯 개지만 박스에 담기는 것은 다섯 개다.
        Assert.Equal(5, collected.Length);
        Assert.Single(collected, command => command == "<<jump Ending>>");
    }

    [Fact]
    public void 들여쓴_라인은_Depth가_올라간다()
    {
        // Valid 샘플의 들여쓴 줄은 전부 명령이라 Depth 1인 라인이 없다.
        // 그래서 합성 픽스처로 확인한다.
        AnalysisReport report = AnalyzeSource("""
            title: T
            ---
            윌로: 깊이 0입니다.
            -> 선택지
                라루: 깊이 1입니다.
                -> 안쪽 선택지
                    아야메: 깊이 2입니다.
            ===
            """);

        StoryNode node = Node(report, "T");

        Assert.Equal(
            new[] { 0, 0, 1, 1, 2 },
            node.Lines.Select(line => line.Depth));

        Assert.Equal(
            new[] { false, true, false, true, false },
            node.Lines.Select(line => line.IsOption));
    }

    [Fact]
    public void 주석은_텍스트에도_해시태그에도_들어가지_않는다()
    {
        AnalysisReport report = AnalyzeSource("""
            title: T
            ---
            이건 처음 닿았을 때의 이야기다. //#box:blackbook
            윌로: 태그와 주석 둘 다. #real // 주석입니다
            정상 줄입니다. #tagOnly
            ===
            """);

        IReadOnlyList<StoryLine> lines = Node(report, "T").Lines;

        // //#box:blackbook 전체가 주석이다. 텍스트에도 태그에도 남으면 안 된다.
        Assert.Equal("이건 처음 닿았을 때의 이야기다.", lines[0].Text);
        Assert.Empty(lines[0].Hashtags);
        Assert.Null(lines[0].Speaker);

        // 해시태그가 먼저 오고 주석이 뒤에 오는 경우.
        Assert.Equal("윌로", lines[1].Speaker);
        Assert.Equal("태그와 주석 둘 다.", lines[1].Text);
        Assert.Equal(new[] { "real" }, lines[1].Hashtags);

        Assert.Equal("정상 줄입니다.", lines[2].Text);
        Assert.Equal(new[] { "tagOnly" }, lines[2].Hashtags);
    }

    [Fact]
    public void 조건부_선택지의_조건식과_인터폴레이션은_텍스트에_남는다()
    {
        AnalysisReport report = AnalyzeSource("""
            title: T
            ---
            값은 {$favor}입니다.
            -> 조용히 믿어본다 <<if $favor >= 8>>
                윌로: 갈래 안입니다.
            ===
            """);

        IReadOnlyList<StoryLine> lines = Node(report, "T").Lines;

        // StringTable을 썼다면 {0}으로 바뀌고 <<if ...>>가 잘려 있었을 자리다.
        Assert.Equal("값은 {$favor}입니다.", lines[0].Text);
        Assert.Equal("조용히 믿어본다 <<if $favor >= 8>>", lines[1].Text);
    }

    [Fact]
    public void 화자로_보지_않는_경우가_있다()
    {
        AnalysisReport report = AnalyzeSource("""
            title: T
            ---
            콜론없는 라인입니다
            결과: 피로도 {$fatigue}
            앞에 공백 있는 것: 화자가 아니다
            ===
            """);

        IReadOnlyList<StoryLine> lines = Node(report, "T").Lines;

        Assert.Null(lines[0].Speaker);

        // Yarn과 Unity가 이것을 화자로 읽으므로 도구도 같게 본다.
        Assert.Equal("결과", lines[1].Speaker);
        Assert.Equal("피로도 {$fatigue}", lines[1].Text);

        // 콜론 앞에 공백이 있으면 화자가 아니다.
        Assert.Null(lines[2].Speaker);
    }

    [Fact]
    public void 조건문은_쌓이는_명령으로_취급한다()
    {
        AnalysisReport report = AnalyzeSource("""
            title: T
            ---
            <<if $favor >= 5>>
            라루: 조건 안입니다.
            <<endif>>
            윌로: 조건 뒤입니다.
            ===
            """);

        IReadOnlyList<StoryLine> lines = Node(report, "T").Lines;

        Assert.Equal(2, lines.Count);
        Assert.Equal(new[] { "<<if $favor >= 5>>" }, lines[0].CommandsSincePreviousLine);
        Assert.Equal(new[] { "<<endif>>" }, lines[1].CommandsSincePreviousLine);
    }

    /// <summary>
    /// 임시 폴더에 .yarn 한 개짜리 프로젝트를 만들어 분석한다.
    /// 스키마는 빈 껍데기라 VN3001/VN3002가 쏟아지지만 여기서는 라인만 본다.
    /// </summary>
    private static AnalysisReport AnalyzeSource(string yarn)
    {
        string workDirectory = Path.Combine(
            Path.GetTempPath(),
            $"VnTool.StoryLineTests.{Guid.NewGuid():N}");

        Directory.CreateDirectory(workDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(workDirectory, "Story.yarn"),
                yarn,
                new UTF8Encoding(false));

            File.WriteAllText(
                Path.Combine(workDirectory, "Demo.yarnproject"),
                """
                {
                  "projectFileVersion": 3,
                  "baseLanguage": "ko",
                  "sourceFiles": [ "**/*.yarn" ],
                  "excludeFiles": []
                }
                """,
                new UTF8Encoding(false));

            string schemaPath = Path.Combine(workDirectory, "game.schema.json");

            File.WriteAllText(
                schemaPath,
                """{ "schemaVersion": 1, "variables": [], "commands": [] }""",
                new UTF8Encoding(false));

            return new VnProjectAnalyzer().Analyze(
                Path.Combine(workDirectory, "Demo.yarnproject"),
                schemaPath);
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (IOException)
            {
                // 임시 폴더 정리 실패로 테스트를 떨어뜨리지 않는다.
            }
        }
    }
}
