using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>장면은 한 자리에서만 시작한다 — 저작 중에 말해 준다</b> (V2 · `docs/plans/R6.md` §9).
///
/// ⛔ 이 규칙은 내보내기 관문(코어의 `VerifySceneEntries`)에만 있었다. 잘못된 것이 나가지는
/// 않았지만 기획자는 <b>관문에서야</b> 알았고, 그러면 어디를 고칠지 되짚어야 한다.
///
/// ⚠ 새 규칙이 아니다 — 그래서 이 파일은 <b>관문과 같은 답을 내는지</b>를 함께 잰다.
/// 저작 중에 조용한데 관문이 거부하거나, 그 반대이거나 하면 둘 다 못 믿는다.
/// </summary>
public sealed class ChapterSceneEntryCheckTests
{
    [Fact]
    public void 들어오는_자리가_둘인_장면을_짚는다()
    {
        // root → a, root → b 라 shared 장면에 착지점이 둘이다. 롤백이 되돌아갈 곳이
        // 정해지지 않는다.
        ChapterDocument chapter = Split();

        ChapterDiagnostic found = Assert.Single(
            Errors(chapter), item => item.Code == ChapterDiagnosticCode.SceneHasManyEntries);

        Assert.Contains("shared", found.Message, StringComparison.Ordinal);
        Assert.Contains("a", found.Message, StringComparison.Ordinal);
        Assert.Contains("b", found.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 저작_중의_답과_관문의_답이_같다()
    {
        // ⛔ 이것이 이 조각의 관문이다 — 규칙이 두 벌이 되는 순간 둘 다 못 믿는다.
        Assert.True(Refused(Split()));
        Assert.False(Refused(Clean()));
    }

    [Fact]
    public void 한_자리로_여러_간선이_들어오는_것은_정상이다()
    {
        // 자리의 수를 세지 간선의 수를 세지 않는다 — 여러 길이 같은 문으로 들어오는 것은
        // 흔한 짜임이고, 되돌아갈 곳은 여전히 하나다.
        ChapterDocument chapter = Chapter(
            ("opening", "root"), ("opening", "second"), ("shared", "gate"));

        chapter.Edges.Add(new ChapterEdge("root", "gate", "이쪽", null, null, 0));
        chapter.Edges.Add(new ChapterEdge("second", "gate", "저쪽", null, null, 0));

        Assert.DoesNotContain(
            Errors(chapter), item => item.Code == ChapterDiagnosticCode.SceneHasManyEntries);
    }

    [Fact]
    public void 장면_안에서_움직이는_간선은_들어오는_길이_아니다()
    {
        // 안 그러면 장면 안의 갈래마다 "들어오는 자리"가 늘어 정상인 챕터가 통째로 붉어진다.
        ChapterDocument chapter = Chapter(
            ("opening", "root"), ("opening", "a"), ("opening", "b"));

        chapter.Edges.Add(new ChapterEdge("root", "a", "A로", null, null, 0));
        chapter.Edges.Add(new ChapterEdge("root", "b", "B로", null, null, 0));

        Assert.DoesNotContain(
            Errors(chapter), item => item.Code == ChapterDiagnosticCode.SceneHasManyEntries);
    }

    [Fact]
    public void 두_번째_자리를_만든_간선을_짚는다()
    {
        // 첫 자리가 아니라 그것이 고칠 곳이다 — 코어도 같은 자리를 짚는다.
        ChapterDocument chapter = Chapter(("opening", "root"), ("shared", "a"), ("shared", "b"));

        chapter.Edges.Add(new ChapterEdge("root", "a", "A로", null, null, 11));
        chapter.Edges.Add(new ChapterEdge("root", "b", "B로", null, null, 22));

        ChapterDiagnostic found = Assert.Single(
            Errors(chapter), item => item.Code == ChapterDiagnosticCode.SceneHasManyEntries);

        Assert.Equal(22, found.Row);
        Assert.Equal("B", found.Column);   // `간선` 시트의 `도착` 칸
    }

    [Fact]
    public void 화면의_표식과_진단이_같은_자리를_본다()
    {
        // 트리의 ⌂·⚠는 `ChapterSceneGrouping`에서 나오고 그쪽은 `IsSceneRoot`를 쓴다.
        // 둘이 갈리면 사람은 어느 쪽을 믿을지 모른다.
        ChapterGraphModel model = Split().ToGraphModel("chapters/ch01.xlsx");

        ChapterScene shared = ChapterSceneGrouping.Of(model)
            .Single(scene => scene.SceneId == "shared");

        Assert.True(shared.HasSplitEntry);
        Assert.Contains(
            model.Errors, item => item.Code == ChapterDiagnosticCode.SceneHasManyEntries);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static List<ChapterDiagnostic> Errors(ChapterDocument chapter) =>
        [.. chapter.ToGraphModel("chapters/ch01.xlsx").Errors];

    private static bool Refused(ChapterDocument chapter) =>
        ChapterProgressionExporter
            .Export(chapter.ToGraphModel("chapters/ch01.xlsx"), episodesFolder: null)
            .Refused;

    /// <summary>shared 장면에 밖에서 들어오는 자리가 둘 — 규칙 위반.</summary>
    private static ChapterDocument Split()
    {
        ChapterDocument chapter = Chapter(("opening", "root"), ("shared", "a"), ("shared", "b"));

        chapter.Edges.Add(new ChapterEdge("root", "a", "A로", null, null, 0));
        chapter.Edges.Add(new ChapterEdge("root", "b", "B로", null, null, 0));

        return chapter;
    }

    /// <summary>같은 짜임인데 갈래마다 제 장면 — 정상.</summary>
    private static ChapterDocument Clean()
    {
        ChapterDocument chapter = Chapter(("opening", "root"), ("left", "a"), ("right", "b"));

        chapter.Edges.Add(new ChapterEdge("root", "a", "A로", null, null, 0));
        chapter.Edges.Add(new ChapterEdge("root", "b", "B로", null, null, 0));

        return chapter;
    }

    private static ChapterDocument Chapter(params (string SceneId, string EpisodeId)[] episodes)
    {
        var chapter = new ChapterDocument { ChapterId = "ch01" };

        foreach ((string sceneId, string episodeId) in episodes)
        {
            chapter.Episodes.Add(new ChapterEpisode(
                episodeId, episodeId, string.Empty, episodeId, 0, 0, null, 0)
            {
                SceneId = sceneId
            });
        }

        return chapter;
    }
}
