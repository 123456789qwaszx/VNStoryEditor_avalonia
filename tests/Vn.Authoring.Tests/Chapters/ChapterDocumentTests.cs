using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>프로젝트가 챕터를 든다</b> (R-F · 2026-09-16, 지시서 §1.1).
///
/// ⛔ <b>여기가 뒤집기의 나머지 절반이 서는 자리다.</b> R-E까지는 대사 본문의 주인이 옮겨
/// 왔고, 에피소드 구조·간선·선택지·조건·스탯은 아직 <c>chapters/{Id}.xlsx</c>가 쥐고 있었다.
///
/// 이 묶음이 지키는 것 셋:
/// ① 저장하고 다시 열어도 값이 하나도 안 상한다 — <b>주인이 되려면 먼저 잃지 않아야 한다</b>
/// ② 파생값(조건 해석)은 <b>담지 않고 다시 푼다</b> — 담아 두면 식과 갈린다
/// ③ 읽기의 부산물(진단·경로·화자 시트)은 저작 값에 안 붙는다
/// </summary>
public sealed class ChapterDocumentTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-chapter-doc", Guid.NewGuid().ToString("N"));

    private string ManifestPath => Path.Combine(_directory, "p" + ProjectManifestJson.FileExtension);

    public ChapterDocumentTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 저장하고_다시_열어도_챕터가_그대로다()
    {
        // ⛔ <b>주인이 되려면 먼저 잃지 않아야 한다.</b> 워크북이 원본이던 시절에는 프로젝트가
        //    값을 흘려도 다음 읽기가 메워 줬다 — 이제 메워 줄 곳이 없다.
        var project = new StoryProject { Title = "챕터를 든다" };
        project.Chapters.Add(Sample());

        ProjectStore.Save(ManifestPath, project);
        ChapterDocument read = Assert.Single(ProjectStore.Load(ManifestPath).Project.Chapters);

        Assert.Equal("ch01", read.ChapterId);

        // 에피소드 — 자리·표시 순번·장면 경계·이벤트키·도달불가 허용까지.
        ChapterEpisode episode = read.Episodes[0];

        Assert.Equal("ep01", episode.EpisodeId);
        Assert.Equal("복도", episode.Title);
        Assert.Equal("05A", episode.Index);
        Assert.Equal("Story_ch01_01", episode.DialogueEntry);
        Assert.Equal((120, 40), (episode.X, episode.Y));
        Assert.Equal("첫 장면", episode.Memo);
        Assert.Equal("ev_corridor", episode.EventKey);
        Assert.Equal("sc_corridor", episode.SceneId);
        Assert.Equal(3, episode.SourceRow);
        Assert.True(read.Episodes[1].AllowUnreachable);

        // 간선 — 문구·관문 둘·잠금 안내문·자동·스탯변화.
        ChapterEdge edge = read.Edges[0];

        Assert.Equal(("ep01", "ep02"), (edge.FromEpisodeId, edge.ToEpisodeId));
        Assert.Equal("같이 간다", edge.OptionLabel);
        Assert.Equal("신뢰높음", edge.ConditionLabel);
        Assert.Equal("보임조건", edge.VisibleConditionLabel);
        Assert.Equal("아직 이릅니다", edge.LockedMessage);
        Assert.False(edge.Auto);

        Assert.Equal(
            [("trust", 3, StatChangeKind.Add), ("met", 1, StatChangeKind.Set)],
            edge.StatChanges.Select(delta => (delta.Key, delta.Amount, delta.Kind)));

        Assert.True(read.Edges[1].Auto);

        // 사전·조건·스탯·픽스처.
        Assert.Equal([("10", "같이 간다", "첫 갈림")],
            read.ChoiceOptions.Select(item => (item.Index, item.Text, item.Memo)));

        Assert.Equal([("신뢰높음", "trust >= 3", "친해졌을 때")],
            read.Conditions.Select(item => (item.Label, item.Expression, item.Description)));

        Assert.Equal([("trust", "신뢰", 0, 0, 10, ChapterStatType.Int), ("met", "만남", 0, 0, 1, ChapterStatType.Bool)],
            read.Stats.Select(item =>
                (item.Key, item.DisplayName, item.Initial, item.Minimum, item.Maximum, item.Type)));

        ChapterFixture fixture = Assert.Single(read.Fixtures);

        Assert.Equal("친한 경로", fixture.Name);
        Assert.True(fixture.IsActive);
        Assert.Equal(5, fixture.Stats["trust"]);
        Assert.Equal([("ep01", "ep02")], fixture.Choices.Select(choice => (choice.From, choice.To)));
    }

    [Fact]
    public void 조건_해석은_담지_않고_식에서_다시_푼다()
    {
        // ⚠ 원문이 정본이다 (§0.5 무해석성). 해석 결과를 저장했다가 식만 고치면 둘이 갈리고,
        //    검증은 <b>옛 해석</b>으로 참을 말한다 — 조용히 틀리는 자리다.
        ChapterDocument document = Sample();

        Assert.Empty(document.Conditions[0].Parsed);   // 손으로 세운 값에는 해석이 없다

        ChapterGraphModel model = document.ToGraphModel(Path.Combine(_directory, "ch01.xlsx"));
        ChapterCondition parsed = model.Conditions[0];

        Assert.True(parsed.IsValid);
        Assert.Equal(["trust"], parsed.Parsed.Select(term => term.Key));
    }

    [Fact]
    public void 읽기의_부산물은_저작_값에_안_붙는다()
    {
        // 진단은 <b>그 파일에 대한 리더의 불평</b>이다. 들여올지 말지는 임포터가 이미
        // 정했고(§5.2), 통과한 뒤에도 들고 다니면 프로젝트가 옛 파일의 흠을 영원히 진다.
        var model = new ChapterGraphModel(
            "ch01",
            sourcePath: @"C:\옛날\ch01.xlsx",
            episodes: [],
            edges: [],
            conditions: [],
            stats: [],
            fixtures: [],
            diagnostics: [new ChapterDiagnostic(
                ChapterDiagnosticSeverity.Warning,
                ChapterDiagnosticCode.ColumnHeaderUnexpected,
                @"C:\옛날\ch01.xlsx",
                Sheet: null, Row: null, Column: null,
                "옛 워크북의 흠")],
            speakers: [new ChapterSpeaker("윌로", "willo", null, 2)],
            hasSpeakerSheet: true);

        ChapterDocument document = ChapterDocument.From(model);
        ChapterGraphModel projected = document.ToGraphModel(@"C:\지금\ch01.xlsx");

        Assert.Empty(projected.Diagnostics);
        Assert.Empty(projected.Speakers);
        Assert.False(projected.HasSpeakerSheet);

        // 그리고 경로는 <b>읽은 자리가 아니라 낼 자리</b>다.
        Assert.Equal(@"C:\지금\ch01.xlsx", projected.SourcePath);
    }

    [Fact]
    public void 챕터가_없으면_매니페스트에_열쇠도_안_쓴다()
    {
        // 이 저장소의 매니페스트 작법 — 기본값을 굳히지 않는다. 구판 프로젝트가 이 변경으로
        // 한 글자도 안 바뀐다.
        ProjectStore.Save(ManifestPath, new StoryProject { Title = "빈 프로젝트" });

        Assert.DoesNotContain("chapters", File.ReadAllText(ManifestPath));
    }

    /// <summary>규격의 칸을 되도록 다 채운 챕터 하나 — 빈 값은 왕복에서 안 드러난다.</summary>
    private static ChapterDocument Sample() => new()
    {
        ChapterId = "ch01",
        Episodes =
        [
            new ChapterEpisode("ep01", "복도", "05A", "Story_ch01_01", 120, 40, "첫 장면", 3)
            {
                EventKey = "ev_corridor",
                SceneId = "sc_corridor"
            },
            new ChapterEpisode("ep02", "옥상", "10", "Story_ch01_02", 300, 40, null, 4,
                AllowUnreachable: true)
        ],
        Edges =
        [
            new ChapterEdge("ep01", "ep02", "같이 간다", "신뢰높음", "아직 이릅니다", 3)
            {
                VisibleConditionLabel = "보임조건",
                StatChanges =
                [
                    new StatDelta("trust", 3),
                    new StatDelta("met", 1, StatChangeKind.Set)
                ]
            },
            new ChapterEdge("ep02", "ep01", null, null, null, 4) { Auto = true }
        ],
        Conditions =
        [
            new ChapterCondition("신뢰높음", "trust >= 3", "친해졌을 때", [], IsValid: false, 3)
        ],
        Stats =
        [
            new ChapterStat("trust", "신뢰", 0, 0, 10, 3),
            new ChapterStat("met", "만남", 0, 0, 1, 4, ChapterStatType.Bool)
        ],
        ChoiceOptions = [new ChapterChoiceOption("10", "같이 간다", "첫 갈림", 3)],
        Fixtures =
        [
            new ChapterFixture(
                "친한 경로",
                IsActive: true,
                new Dictionary<string, int>(StringComparer.Ordinal) { ["trust"] = 5 },
                [new ChapterFixtureChoice("ep01", "ep02")],
                3)
        ]
    };
}
