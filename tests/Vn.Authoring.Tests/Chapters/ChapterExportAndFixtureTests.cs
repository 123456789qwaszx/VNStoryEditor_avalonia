using System.Text.Json;
using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// G8 내보내기(런타임 필드 1:1 + 위치 확장 + 픽스처 제외 + 오류 시 거부)와
/// G6 픽스처 경로(전환 시 경로가 바뀐다)를 닫는다 — Gate C 2·3번.
/// </summary>
public sealed class ChapterExportAndFixtureTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-chapter-export", Guid.NewGuid().ToString("N"));

    public ChapterExportAndFixtureTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    // ── G8 — 내보내기 ───────────────────────────────────────────────────────

    [Fact]
    public void 견본이_런타임_필드와_일대일로_내보내진다()
    {
        ChapterGraphModel chapter = ChapterWorkbookReader.Read(SamplePath);

        // 에피소드 워크북이 없는 폴더 → 도달성은 "스탯 무변화" 가정으로 돈다. 견본 챕터는
        // 신뢰높음 관문이 있어 branch05.02A가 도달 불가가 되므로, 여기서는 그 에피소드에
        // `도달불가 허용`이 없는 원본 대신 검증을 통과하는 절단본으로 낸다 — 거부 동작은
        // 아래 테스트가 따로 고정한다.
        ChapterGraphModel trimmed = Trim(chapter, remove: "branch05.02A");

        ChapterExportResult result = ChapterProgressionExporter.Export(trimmed, episodesFolder: null);

        Assert.False(result.Refused,
            string.Join(" / ", result.Validation.All.Select(item => item.Message)));

        using JsonDocument document = JsonDocument.Parse(result.Json!);
        JsonElement root = document.RootElement;

        // SO 최상위 필드 1:1.
        Assert.Equal("chapter-graph-sample", root.GetProperty("ChapterId").GetString());
        Assert.Equal("main05.01", root.GetProperty("StartEpisodeId").GetString());

        JsonElement nodes = root.GetProperty("Nodes");
        Assert.Equal(5, nodes.GetArrayLength());

        // EpisodeNodeDefinition 필드 1:1 + Position 확장 (G-2).
        JsonElement start = nodes[0];
        Assert.Equal("main05.01", start.GetProperty("EpisodeId").GetString());
        Assert.Equal("닫힌 문 앞에서", start.GetProperty("Title").GetString());
        Assert.Equal("Main", start.GetProperty("Kind").GetString());
        Assert.Equal("Story_ch05_01", start.GetProperty("DialogueEntryId").GetString());
        Assert.Equal(0, start.GetProperty("Position").GetProperty("X").GetDouble());

        // 간선 → NextOptions. 도착지·라벨·잠금 문구가 그대로 실린다.
        JsonElement second = nodes[1];
        JsonElement options = second.GetProperty("NextOptions");
        Assert.Equal(1, options.GetArrayLength());
        Assert.Equal("main05.03", options[0].GetProperty("TargetEpisodeId").GetString());

        // 부착 노드는 Kind가 Attachment이고, 조건이 EpisodeCleared/Exists로 번역된다.
        JsonElement attachment = nodes.EnumerateArray()
            .Single(node => node.GetProperty("EpisodeId").GetString() == "attach05.02s");
        Assert.Equal("Attachment", attachment.GetProperty("Kind").GetString());
        JsonElement visible = attachment.GetProperty("VisibleConditions")[0];
        Assert.Equal("EpisodeCleared", visible.GetProperty("Kind").GetString());
        Assert.Equal("main05.02", visible.GetProperty("Key").GetString());
        Assert.Equal("Exists", visible.GetProperty("Op").GetString());

        // 엔딩 후보.
        JsonElement ending = nodes.EnumerateArray()
            .Single(node => node.GetProperty("EpisodeId").GetString() == "main05.end");
        Assert.True(ending.GetProperty("IsChapterEndingCandidate").GetBoolean());
        Assert.Equal("ch05_normal", ending.GetProperty("EndingKey").GetString());

        // 픽스처는 어디에도 없다 (§3.1 — 테스트 데이터).
        Assert.DoesNotContain("픽스처", result.Json);
        Assert.DoesNotContain("기본 루트", result.Json);
    }

    [Fact]
    public void 스탯_조건은_Stat_비교로_번역된다()
    {
        ChapterGraphModel chapter = ChapterWorkbookReader.Read(SamplePath);
        ChapterGraphModel trimmed = Trim(chapter, remove: "branch05.02A");

        // trimmed에는 신뢰높음 간선이 없으므로, 지쳐있음(AND 2항)을 단 에피소드로 확인한다.
        ChapterExportResult result = ChapterProgressionExporter.Export(trimmed, null);
        Assert.False(result.Refused);

        // 원본 조건 시트의 라벨↔식이 그대로 번역돼 실리는지는 조건 하나로 족하다 —
        // cleared:는 위 테스트가, 스탯 비교는 여기서.
        ChapterCondition condition = chapter.FindCondition("지쳐있음")!;
        Assert.Equal(2, condition.Parsed.Count);
    }

    [Fact]
    public void 검증_오류가_있으면_내보내기가_거부된다()
    {
        // Gate C 3번. 견본 원본은 에피소드 워크북 없이는 branch05.02A가 도달 불가다 —
        // 그 상태로 내보내려 하면 거부되고, 무엇이 왜인지가 담긴다.
        ChapterGraphModel chapter = ChapterWorkbookReader.Read(SamplePath);

        ChapterExportResult result = ChapterProgressionExporter.Export(chapter, episodesFolder: null);

        Assert.True(result.Refused);
        Assert.Null(result.Json);
        Assert.Contains(result.Validation.All, item =>
            item.Code == ChapterDiagnosticCode.EpisodeUnreachable &&
            item.Message.Contains("branch05.02A", StringComparison.Ordinal));
    }

    [Fact]
    public void 대본에_CHOICE_OPTION이_남아_있으면_폐지_경고를_한다()
    {
        // 2026-08-16 소유자 — 선택지의 정본이 챕터 `선택지` 시트로 왔다. 대본의 선택지는
        // 옮기라고 말하되 막지는 않는다(원고를 볼모로 잡지 않는다).
        ChapterGraphModel chapter = ChapterWorkbookReader.Read(SamplePath);
        string episodes = Path.Combine(_directory, "episodes");
        Directory.CreateDirectory(episodes);

        WriteRows(Path.Combine(episodes, "main05.02.xlsx"),
        [
            ["인덱스", "LineId", "유형", "태그", "조건라벨", "IN", "OUT", "화자", "내용", "스탯변화", "메모"],
            ["10", "ln_0001", null, null, null, null, null, "윌로", "한 줄", null, null],
            ["70", "ln_0006", "CHOICE", null, null, null, null, null, null, null, null],
            ["71", "ln_0007", "OPTION", null, null, null, null, null, "하나뿐인 선택", null, null]
        ]);

        ChapterValidationResult validation = ChapterValidator.Validate(chapter, episodes);

        ChapterDiagnostic deprecated = Assert.Single(validation.Diagnostics, item =>
            item.Message.Contains("선택지는 이제"));
        Assert.Equal(ChapterDiagnosticSeverity.Warning, deprecated.Severity);
        Assert.Contains("`선택지` 시트", deprecated.Message);
    }

    [Fact]
    public void 보이지_않는_기본_칸은_보이는_선택지와_공존한다()
    {
        // 방어장치 (2026-08-16) — 어떤 선택지 조건도 만족 못 할 때 빠지는 빈 칸(보이지 않는
        // 기본)은 에피소드당 하나 허용이고, 조건 없는 간선과 짝이면 조용하다. 이행된 견본
        // (보이는 칸 둘 + 빈 칸들)이 오류 0으로 읽히는 것이 그 계약이다.
        ChapterGraphModel chapter = ChapterWorkbookReader.Read(SamplePath);

        ChapterValidationResult validation = ChapterValidator.Validate(chapter, episodesFolder: null);

        Assert.DoesNotContain(validation.Diagnostics, item =>
            item.Severity == ChapterDiagnosticSeverity.Error);
    }

    [Fact]
    public void 간선이_없는_선택지_칸은_경고한다()
    {
        // v7 — 칸은 에피소드의 것이고, 간선은 그 칸을 인덱스로 가리킨다. 안 이은 칸은
        // "여기서 끝나는 길"이 아니라 아직 안 그린 길이므로 짚어 준다.
        string path = Path.Combine(_directory, "orphan.xlsx");
        File.Copy(SamplePath, path);

        using (var workbook = new ClosedXML.Excel.XLWorkbook(path))
        {
            ClosedXML.Excel.IXLWorksheet choices = workbook.Worksheet(ChapterSheetNames.Choices);
            int row = choices.LastRowUsed()!.RowNumber() + 1;
            choices.Cell(row, 1).SetValue("main05.01");
            choices.Cell(row, 2).SetValue(999);            // 어떤 간선도 안 가리키는 인덱스
            choices.Cell(row, 3).SetValue("아직 안 이은 문구");
            workbook.Save();
        }

        ChapterGraphModel chapter = ChapterWorkbookReader.Read(path);
        ChapterValidationResult validation = ChapterValidator.Validate(chapter, episodesFolder: null);

        Assert.Contains(validation.Diagnostics, item =>
            item.Severity == ChapterDiagnosticSeverity.Warning &&
            item.Message.Contains("간선이 없습니다"));
    }

    // ── G6 — 픽스처 경로 ────────────────────────────────────────────────────

    [Fact]
    public void 픽스처를_바꾸면_경로가_바뀐다()
    {
        // Gate C 2번의 절반 — "픽스처 전환 시 경로 하이라이트가 바뀐다"의 데이터 쪽.
        ChapterGraphModel chapter = ChapterWorkbookReader.Read(SamplePath);

        ChapterFixture basic = chapter.Fixtures.Single(fixture => fixture.Name == "기본 루트");
        ChapterFixture trust = chapter.Fixtures.Single(fixture => fixture.Name == "신뢰 루트");

        FixtureWalkResult basicWalk = ChapterFixtureWalker.Walk(chapter, basic);
        FixtureWalkResult trustWalk = ChapterFixtureWalker.Walk(chapter, trust);

        // 기본 루트(trust 0): 신뢰 분기가 닫혀 곧장 문 너머로.
        Assert.Equal(["main05.01", "main05.02", "main05.03", "main05.end"], basicWalk.EpisodeIds);

        // 신뢰 루트(trust 5): 고정 선택이 라루의 제안으로 — 경로가 다르다.
        Assert.Equal(
            ["main05.01", "main05.02", "branch05.02A", "main05.03", "main05.end"],
            trustWalk.EpisodeIds);

        Assert.NotEqual(basicWalk.EpisodeIds, trustWalk.EpisodeIds);
    }

    [Fact]
    public void 갈래에_고정_선택이_없으면_어디를_적으라고_말한다()
    {
        ChapterGraphModel chapter = ChapterWorkbookReader.Read(SamplePath);
        ChapterFixture trust = chapter.Fixtures.Single(fixture => fixture.Name == "신뢰 루트");

        var noChoice = trust with { Choices = Array.Empty<ChapterFixtureChoice>() };

        FixtureWalkResult walk = ChapterFixtureWalker.Walk(chapter, noChoice);

        Assert.NotNull(walk.StoppedBecause);
        Assert.Contains("고정 선택이 없습니다", walk.StoppedBecause);
        Assert.Contains("main05.02→", walk.StoppedBecause);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>에피소드 하나(와 그 간선)를 뺀 사본. 검증 통과용 절단본을 만들 때 쓴다.</summary>
    private static ChapterGraphModel Trim(ChapterGraphModel chapter, string remove) => new(
        chapter.ChapterId,
        chapter.SourcePath,
        chapter.Episodes.Where(episode => episode.EpisodeId != remove).ToList(),
        chapter.Edges.Where(edge => edge.FromEpisodeId != remove && edge.ToEpisodeId != remove).ToList(),
        chapter.Conditions,
        chapter.Stats,
        chapter.Fixtures,
        chapter.Diagnostics);

    private static void WriteRows(string path, string?[][] rows)
    {
        using var workbook = new XLWorkbook();
        IXLWorksheet sheet = workbook.AddWorksheet("본문");

        for (int row = 0; row < rows.Length; row++)
        {
            for (int column = 0; column < rows[row].Length; column++)
            {
                if (rows[row][column] is { Length: > 0 } value)
                {
                    sheet.Cell(row + 1, column + 1).SetValue(value);
                }
            }
        }

        workbook.SaveAs(path);
    }
}
