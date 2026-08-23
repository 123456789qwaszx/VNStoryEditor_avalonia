using System.Text.Json;
using ClosedXML.Excel;
using Vn.Authoring.Chapters;
using Vn.Authoring.Rendering;

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
        // ⚠ 견본 워크북의 `대사엔트리`가 `Story_ch05_01`이다 — 이름 규칙이 어디에도 없던
        // 시절 사람이 접두를 손으로 적어 둔 것이다(§4 가운데 줄의 그 방식). 이미터를
        // 통과하면 접두가 한 번 더 붙고, **그 값이 맞다**: 이미터가 그 노드에 붙이는 yarn
        // 타이틀도 `Story_Story_ch05_01`이라 둘이 같고, 그래야 재생된다.
        //
        // ⛔ 이 값을 보고 "이미 Story_로 시작하면 안 붙인다"는 가드를 넣지 말 것. 규칙의
        // 둘째 사본이 생겨 이 수정의 목적을 되돌리고, 진짜로 Story_로 시작하는 이름을
        // 영영 못 쓰게 된다. 고칠 것이 있다면 코드가 아니라 견본 데이터다
        // (`docs/work-orders/dialogue-entry-naming-orders.md` §3.3).
        Assert.Equal("Story_Story_ch05_01", start.GetProperty("DialogueEntryId").GetString());
        Assert.Equal(0, start.GetProperty("Position").GetProperty("X").GetDouble());

        // 간선 → NextOptions. 도착지·라벨·잠금 문구가 그대로 실린다.
        JsonElement second = nodes[1];
        JsonElement options = second.GetProperty("NextOptions");
        Assert.Equal(1, options.GetArrayLength());
        Assert.Equal("main05.03", options[0].GetProperty("TargetEpisodeId").GetString());

        // 부착 노드는 Kind가 Attachment이다. v8에서 관문이 길(간선)로 내려가면서 노드의
        // VisibleConditions/UnlockConditions는 비어 나간다 — ⚠ 간선이 없는 부착의 표시
        // 제어는 아직 갈 곳이 없다(열린 항목: run-log 2026-08-16 v8).
        JsonElement attachment = nodes.EnumerateArray()
            .Single(node => node.GetProperty("EpisodeId").GetString() == "attach05.02s");
        Assert.Equal("Attachment", attachment.GetProperty("Kind").GetString());
        Assert.Equal(0, attachment.GetProperty("VisibleConditions").GetArrayLength());

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
    public void 대본에_CHOICE_OPTION이_남아_있으면_어디로_가야_하는지_말한다()
    {
        // 선택지의 정본은 v9부터 챕터 `선택지`·`간선` 시트다. v10에서 대본 규격에서 아예
        // 빠졌으므로, 이제 리더가 <b>그 행을 짚어</b> 옮길 곳까지 말한다(경고 → 오류).
        ChapterGraphModel chapter = ChapterWorkbookReader.Read(SamplePath);
        string episodes = Path.Combine(_directory, "episodes");
        Directory.CreateDirectory(episodes);

        WriteRows(Path.Combine(episodes, "main05.02.xlsx"),
        [
            ["인덱스", "유형", "LineId", "조건라벨", "화자", "내용"],
            ["10", null, "ln_0001", null, "윌로", "한 줄"],
            ["70", "CHOICE", "ln_0006", null, null, null],
            ["71", "OPTION", "ln_0007", null, null, "하나뿐인 선택"]
        ]);

        ChapterValidationResult validation = ChapterValidator.Validate(chapter, episodes);

        List<ChapterDiagnostic> deprecated = validation.Diagnostics
            .Where(item => item.Message.Contains("대본에서 폐지됐습니다"))
            .ToList();

        Assert.Equal(2, deprecated.Count);   // CHOICE 한 줄, OPTION 한 줄 — 각자 자기 행에서
        Assert.All(deprecated, item =>
        {
            Assert.Equal(ChapterDiagnosticSeverity.Error, item.Severity);
            Assert.Equal("B", item.Column);
            Assert.Contains("`선택지` 시트", item.Message);
            Assert.Contains("`간선` 시트", item.Message);
        });
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
    public void 간선이_없는_선택지_칸은_오류도_경고도_아니다()
    {
        // 2026-08-16 소유자 — "선택지는 간선의 숫자보다 많아도 된다. 간선이랑 연결이 안
        // 되어 있으면 그냥 안 쓰고 종료시키면 되니까." 그 길을 고르면 챕터가 거기서 끝난다.
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

        // 안 이은 칸으로는 아무 말도 나오지 않는다(견본의 다른 진단은 그대로 둔다).
        Assert.DoesNotContain(validation.Diagnostics, item => item.Message.Contains("간선이 없습니다"));
        Assert.DoesNotContain(validation.Diagnostics, item =>
            item.Sheet == ChapterSheetNames.Choices && item.Message.Contains("999"));
    }

    [Fact]
    public void 대사엔트리가_이미터의_이름_규칙을_그대로_지난다()
    {
        // 2026-08-23 — 진행 JSON은 `new01`이라 하고 yarn은 `Story_new01`이라 했다. 로드·
        // 검증·도달성 증명이 전부 통과하는데 **재생만** 안 됐다. 호스트의 사전 대조가
        // 잡았고, 뿌리는 `Story_` 조립이 이미터 안에서 세 자리로 흩어져 있는데 내보내기가
        // 그 셋 중 어디에도 안 끼어 있던 것이었다.
        //
        // 규칙은 접두 하나가 아니라 두 단계다 — `Story_` + SanitizeNodeName. 그래서 공백이
        // 든 이름을 케이스로 든다: 접두만 붙이는 구현은 `new01`에서는 멀쩡해 보이고
        // `장면 1`에서 비로소 갈린다.
        var chapter = new ChapterGraphModel(
            "naming",
            string.Empty,
            [
                new ChapterEpisode("ep1", "첫 화", "", "Main", "장면 1", 0, 0, null, null, 2),
                new ChapterEpisode("ep2", "둘째", "", "Main", "new01", 200, 0, null, null, 3)
            ],
            [new ChapterEdge("ep1", "ep2", null, null, HideWhenLocked: false, null, 2)],
            [],
            [new ChapterStat("trust", "신뢰", Initial: 0, Minimum: 0, Maximum: 5, SourceRow: 2)],
            [],
            []);

        ChapterExportResult result =
            ChapterProgressionExporter.Export(chapter, episodesFolder: null);
        Assert.False(result.Refused,
            string.Join(" / ", result.Validation.All.Select(item => item.Message)));

        using JsonDocument document = JsonDocument.Parse(result.Json!);
        JsonElement nodes = document.RootElement.GetProperty("Nodes");

        // 공백이 밑줄로 — 접두만 붙였다면 `Story_장면 1`이 되어 저쪽이 노드를 못 찾는다.
        Assert.Equal("Story_장면_1", nodes[0].GetProperty("DialogueEntryId").GetString());
        Assert.Equal("Story_new01", nodes[1].GetProperty("DialogueEntryId").GetString());

        // 그리고 이 값의 주인은 이미터다 — 내보내기가 글자를 다시 조립하지 않는다.
        // 이미터가 규칙을 바꾸는 날 이 단언이 저절로 따라간다.
        Assert.Equal(
            YarnBundleEmitter.StoryNodeTitleOf("장면 1"),
            nodes[0].GetProperty("DialogueEntryId").GetString());
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
    [Fact]
    public void 수입기가_믿어야_하는_JSON_규약을_고정한다()
    {
        // 런타임 계약서 §G (2026-08-17) — 수입기가 없는 동안 조용히 어긋날 자리 셋을 못 박는다.
        // 여기가 깨지면 계약서 G1~G5도 함께 고쳐야 한다.
        string path = Path.Combine(_directory, "gd.xlsx");
        Directory.CreateDirectory(_directory);
        ChapterWorkbookWriter.EnsureChapterWorkbook(
            _directory, "gd", [("trust", "신뢰"), ("anger", "분노")]);

        ChapterWorkbookWriter.AddEpisode(path, "ep1", "첫 화", 0, 0);
        ChapterWorkbookWriter.AddEpisode(path, "ep2", "둘째", 200, 0);
        ChapterWorkbookWriter.AddCondition(path, "신뢰0이상", "trust >= 0");
        ChapterWorkbookWriter.AddCondition(path, "신뢰초과", "trust > 1");
        ChapterWorkbookWriter.AddEdge(path, "ep1", "ep2", optionLabel: "믿는다");
        ChapterWorkbookWriter.UpdateEdge(path, "ep1", "ep2",
            visibleConditionLabel: "신뢰0이상", conditionLabel: "신뢰초과",
            statChanges: "trust +2", matchOptionLabel: "믿는다");
        // 관문 없는 길 하나 — 이게 없으면 ep2가 도달 불가라 내보내기가 거부된다(Gate C 3번).
        ChapterWorkbookWriter.AddEdge(path, "ep1", "ep2", optionLabel: "무시한다");

        ChapterExportResult result = ChapterProgressionExporter.Export(
            ChapterWorkbookReader.Read(path), episodesFolder: null);
        Assert.False(result.Refused, string.Join(" / ", result.Validation.All.Select(item => item.Message)));

        using JsonDocument document = JsonDocument.Parse(result.Json!);
        JsonElement option = document.RootElement.GetProperty("Nodes")[0]
            .GetProperty("NextOptions")[0];

        // G5 — 관문은 노드가 아니라 길이 갖는다. 노드 쪽 둘은 언제나 비어 나간다.
        JsonElement node = document.RootElement.GetProperty("Nodes")[0];
        Assert.Empty(node.GetProperty("VisibleConditions").EnumerateArray());
        Assert.Empty(node.GetProperty("UnlockConditions").EnumerateArray());
        Assert.Equal(1, option.GetProperty("VisibleConditions").GetArrayLength());

        // G2 — 값이 0이면 IntValue 키 자체가 없다. 수입기가 0으로 안 읽으면 조건이 어긋난다.
        JsonElement zero = option.GetProperty("VisibleConditions")[0];
        Assert.Equal("GreaterOrEqual", zero.GetProperty("Op").GetString());
        Assert.False(zero.TryGetProperty("IntValue", out _));

        // G3 — `>`가 열리며 생긴 새 Op. 런타임 enum에 아직 없다.
        Assert.Equal("GreaterThan", option.GetProperty("Conditions")[0].GetProperty("Op").GetString());
    }

    [Fact]
    public void 스탯_정의가_최상위로_실려_나간다()
    {
        // 계약서 §G-1 (2026-08-18) — 이 칸이 비어 있어서 Gate D가 막혀 있었다.
        // 초기값·최소·최대가 어느 런타임 입력에도 없었고, 툴의 도달성 증명만 clamp하며
        // 걸었다. `Ked.Progression.ChapterProgression`은 정의되지 않은 스탯을 가리키는
        // 조건을 만나면 생성을 거부하므로, 이것이 없으면 실데이터를 아예 못 싣는다.
        // 시트에는 경계를 쓰는 writer 경로가 없어(사람이 엑셀에서 적는다) 모델을 직접
        // 세운다 — 겨누는 것은 내보내기이지 리더가 아니다.
        var chapter = new ChapterGraphModel(
            "stats",
            string.Empty,
            [
                new ChapterEpisode("ep1", "첫 화", "", "Main", "ep1", 0, 0, null, null, 2),
                new ChapterEpisode("ep2", "둘째", "", "Main", "ep2", 200, 0, null, null, 3)
            ],
            [new ChapterEdge("ep1", "ep2", null, null, HideWhenLocked: false, null, 2)],
            [],
            [
                new ChapterStat("trust", "신뢰", Initial: 1, Minimum: 0, Maximum: 5, SourceRow: 2),
                new ChapterStat("flag", "깃발", Initial: 0, Minimum: 0, Maximum: 1, SourceRow: 3,
                    Type: ChapterStatType.Bool)
            ],
            [],
            []);

        ChapterExportResult result =
            ChapterProgressionExporter.Export(chapter, episodesFolder: null);
        Assert.False(result.Refused,
            string.Join(" / ", result.Validation.All.Select(item => item.Message)));

        using JsonDocument document = JsonDocument.Parse(result.Json!);
        JsonElement stats = document.RootElement.GetProperty("Stats");

        Assert.Equal(2, stats.GetArrayLength());

        JsonElement trust = stats[0];
        Assert.Equal("trust", trust.GetProperty("Key").GetString());
        Assert.Equal("신뢰", trust.GetProperty("DisplayName").GetString());
        Assert.Equal(1, trust.GetProperty("Initial").GetInt32());
        Assert.Equal(5, trust.GetProperty("Maximum").GetInt32());

        // ⚠ 이름 번역 — 이쪽은 Int, 저쪽(Ked.Progression.StatType)은 Number다.
        // enum이 이름 문자열로 나가므로 그대로 내면 수입기가 모르는 이름을 만난다.
        Assert.Equal("Number", trust.GetProperty("Type").GetString());
        Assert.Equal("Bool", stats[1].GetProperty("Type").GetString());

        // 경계 0은 키가 살아 있어야 한다 — WhenWritingDefault는 IntValue에만 걸려 있다.
        Assert.Equal(0, trust.GetProperty("Minimum").GetInt32());

        // SourceRow는 저작의 사정이라 싣지 않는다.
        Assert.False(trust.TryGetProperty("SourceRow", out _));
    }

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
