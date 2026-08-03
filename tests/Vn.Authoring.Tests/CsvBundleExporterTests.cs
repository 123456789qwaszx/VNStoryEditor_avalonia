using System.Text;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// CSV 3종은 발행 결과에서만 나온다(.yarn과 같은 입구). 인코딩은 UTF-8 BOM 포함 —
/// .yarn의 no-BOM 규칙과 의도적으로 다르다(엑셀 한글 호환). RFC 4180 이스케이프.
/// </summary>
public class CsvBundleExporterTests
{
    private static readonly string GoldenDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Golden"));

    [Theory]
    [InlineData("Script_choices_ep.csv")]
    [InlineData("Review_choices_ep.csv")]
    [InlineData("Direction_choices_ep.csv")]
    public void 열_스키마와_내용이_골든과_같다(string fileName)
    {
        CsvBundle bundle = ExportChoices();
        string actual = bundle.Files.Single(file => file.FileName == fileName).Text;
        string goldenPath = Path.Combine(GoldenDirectory, fileName);

        if (!File.Exists(goldenPath))
        {
            Directory.CreateDirectory(GoldenDirectory);
            File.WriteAllText(goldenPath, actual, new UTF8Encoding(false));
            Assert.Fail($"골든 파일이 없어 새로 기록했습니다. 내용을 검토하고 커밋하세요: {goldenPath}");
        }

        Assert.Equal(File.ReadAllText(goldenPath, Encoding.UTF8), actual);
    }

    [Fact]
    public void 헤더_열_스키마는_작업지시서_그대로다()
    {
        CsvBundle bundle = ExportChoices();

        Assert.StartsWith("LineId,화자,대사,노드,인덱스\r\n", bundle.ScriptCsv, StringComparison.Ordinal);
        Assert.StartsWith("노드,인덱스,LineId,화자,대사,조건,선택,Set,출구\r\n", bundle.ReviewCsv, StringComparison.Ordinal);
        Assert.StartsWith("LineId,대사,순서,커맨드,인자,메모\r\n", bundle.DirectionCsv, StringComparison.Ordinal);
    }

    [Fact]
    public void 검수_CSV는_조건_선택_set_출구를_줄마다_담는다()
    {
        ChoiceTests.ChoiceWorld world = ChoiceTests.BuildChoiceWorld();
        CsvBundle bundle = CsvBundleExporter.Export(
            world.Dialogue,
            world.Presentation,
            world.Sample.Project,
            Sample.Definition,
            bundleName: "choices_ep");

        string[] rows = bundle.ReviewCsv.Split("\r\n");

        // 라벨 줄: 선택 열에 블록·옵션·라벨이, Set 열에 효과가 적힌다.
        string labelRow = rows.Single(row => row.Contains(world.Label1, StringComparison.Ordinal));
        Assert.Contains("블록1 옵션1 라벨", labelRow, StringComparison.Ordinal);
        Assert.Contains("fatigue += 10 ; common_ingredient += 15", labelRow, StringComparison.Ordinal);

        // 옵션 출구는 대상 노드 이름으로 적힌다.
        string exitRow = rows.Single(row => row.Contains("바로 돌아간다", StringComparison.Ordinal));
        Assert.Contains("A로 간다", exitRow, StringComparison.Ordinal);
    }

    [Fact]
    public void 연출_테이블은_커맨드_하나가_한_줄이고_인자를_해석해_담는다()
    {
        ChoiceTests.ChoiceWorld world = ChoiceTests.BuildChoiceWorld();
        CsvBundle bundle = CsvBundleExporter.Export(
            world.Dialogue,
            world.Presentation,
            world.Sample.Project,
            Sample.Definition,
            bundleName: "choices_ep");

        string[] rows = bundle.DirectionCsv.Split("\r\n");
        string commandRow = Assert.Single(rows, row => row.Contains("camera", StringComparison.Ordinal));

        Assert.Contains("preset=closeup", commandRow, StringComparison.Ordinal);
        Assert.Contains("안전한 길에서 평범한 재료를 얻었다.", commandRow, StringComparison.Ordinal);
    }

    [Fact]
    public void 쉼표와_따옴표와_줄바꿈은_RFC4180으로_이스케이프한다()
    {
        var sample = new Sample();
        string line = sample.Line("쉼표, 그리고 \"따옴표\"");
        sample.Editor.SetScriptLineText(sample.Script.Id, line, "라루", "쉼표, 그리고 \"따옴표\"");
        DialogueResult dialogue = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        CsvBundle bundle = CsvBundleExporter.Export(dialogue, bundleName: "escape_ep");

        Assert.Contains("\"쉼표, 그리고 \"\"따옴표\"\"\"", bundle.ScriptCsv, StringComparison.Ordinal);
    }

    [Fact]
    public void 파일은_UTF8_BOM과_CRLF로_쓴다()
    {
        CsvBundle bundle = ExportChoices();
        string directory = Path.Combine(Path.GetTempPath(), $"VnTool.Csv.{Guid.NewGuid():N}");

        try
        {
            IReadOnlyList<string> written = CsvBundleExporter.WriteTo(bundle, directory);

            Assert.Equal(3, written.Count);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));

            foreach (string path in written)
            {
                byte[] bytes = File.ReadAllBytes(path);

                // 엑셀은 BOM이 없으면 한글 CSV를 ANSI로 읽는다 — .yarn과 반대 규칙이다.
                Assert.True(
                    bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                    "CSV에는 BOM이 있어야 한다");
                Assert.Contains("\r\n", File.ReadAllText(path, Encoding.UTF8), StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static CsvBundle ExportChoices()
    {
        ChoiceTests.ChoiceWorld world = ChoiceTests.BuildChoiceWorld();

        return CsvBundleExporter.Export(
            world.Dialogue,
            world.Presentation,
            world.Sample.Project,
            Sample.Definition,
            bundleName: "choices_ep");
    }
}
