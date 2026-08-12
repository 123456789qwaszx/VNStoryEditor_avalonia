using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// G-2 v2 — 툴 편집이 엑셀 셀만 외과수술로 쓴다. 지키는 약속: <b>왕복이 성립한다</b>(쓴 뒤
/// 리더가 그대로 읽는다), <b>다른 칸은 그대로다</b>(서식·메모 보존), <b>잠기면 쓰지 않는다</b>.
/// </summary>
public sealed class ChapterWorkbookWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-chapter-writer", Guid.NewGuid().ToString("N"));

    public ChapterWorkbookWriterTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    private string Copy()
    {
        string path = Path.Combine(_directory, "ch05.xlsx");
        File.Copy(SamplePath, path, overwrite: true);
        return path;
    }

    // ── 위치 (드래그) ───────────────────────────────────────────────────────

    [Fact]
    public void 드래그_위치가_그_행의_X_Y_셀에만_써진다()
    {
        string path = Copy();

        ChapterWriteResult result = ChapterWorkbookWriter.SetEpisodePosition(path, "main05.02", 300, -50);

        Assert.True(result.Written, result.Failure);

        ChapterGraphModel reread = ChapterWorkbookReader.Read(path);
        Assert.Equal((300d, -50d), (reread.FindEpisode("main05.02")!.X, reread.FindEpisode("main05.02")!.Y));

        // 다른 노드·다른 칸은 그대로다 — 왕복 성립.
        Assert.Equal((0d, 0d), (reread.FindEpisode("main05.01")!.X, reread.FindEpisode("main05.01")!.Y));
        Assert.Equal("조용한 복도", reread.FindEpisode("main05.02")!.Title);
        Assert.Empty(reread.Errors);
    }

    [Fact]
    public void 잠긴_워크북에는_쓰지_않고_사유를_돌려준다()
    {
        string path = Copy();
        byte[] before = File.ReadAllBytes(path);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ChapterWriteResult result = ChapterWorkbookWriter.SetEpisodePosition(path, "main05.02", 1, 1);

            Assert.False(result.Written);
            Assert.NotNull(result.Failure);
        }

        // 반쯤 쓴 워크북은 없다 — 바이트 그대로다.
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    // ── 에피소드 ────────────────────────────────────────────────────────────

    [Fact]
    public void 에피소드_추가는_행을_덧붙이고_리더가_그대로_읽는다()
    {
        string path = Copy();

        Assert.True(ChapterWorkbookWriter.AddEpisode(path, "main05.04", "새 화", 900, 40).Written);

        ChapterGraphModel reread = ChapterWorkbookReader.Read(path);
        ChapterEpisode added = reread.FindEpisode("main05.04")!;

        Assert.Equal("새 화", added.Title);
        Assert.Equal((900d, 40d), (added.X, added.Y));

        // 대사엔트리가 비어 있으므로 검증이 알린다 — 사람이 채울 때까지 조용히 넘어가지 않는다.
        Assert.Contains(reread.Errors, item =>
            item.Code == ChapterDiagnosticCode.DialogueEntryBlank &&
            item.Message.Contains("main05.04"));
    }

    [Fact]
    public void 중복_Id_추가는_거부된다()
    {
        string path = Copy();

        ChapterWriteResult result = ChapterWorkbookWriter.AddEpisode(path, "main05.02", "겹침", 0, 0);

        Assert.False(result.Written);
        Assert.Contains("이미 있습니다", result.Failure);
    }

    [Fact]
    public void 속성_편집은_준_필드만_바꾼다()
    {
        string path = Copy();

        Assert.True(ChapterWorkbookWriter.UpdateEpisode(
            path, "main05.02",
            title: "고친 제목",
            endingKey: "",              // 빈 문자열 = 지우기
            allowUnreachable: true).Written);

        ChapterGraphModel reread = ChapterWorkbookReader.Read(path);
        ChapterEpisode episode = reread.FindEpisode("main05.02")!;

        Assert.Equal("고친 제목", episode.Title);
        Assert.Equal("Story_ch05_02", episode.DialogueEntry);  // null = 그대로
        Assert.True(episode.AllowUnreachable);                 // L열 머리글까지 함께 생겼다
    }

    [Fact]
    public void 개명은_간선과_픽스처_참조까지_따라간다()
    {
        string path = Copy();

        Assert.True(ChapterWorkbookWriter.RenameEpisode(path, "main05.03", "main05.03b").Written);

        ChapterGraphModel reread = ChapterWorkbookReader.Read(path);

        Assert.Null(reread.FindEpisode("main05.03"));
        Assert.NotNull(reread.FindEpisode("main05.03b"));

        // 간선 끝점이 따라갔다 — 유령 간선 없음(끝점 오류 0).
        Assert.Empty(reread.Errors);
        Assert.Contains(reread.Edges, edge => edge.ToEpisodeId == "main05.03b");
        Assert.Contains(reread.Edges, edge => edge.FromEpisodeId == "main05.03b");

        // 픽스처 고정 선택도 따라갔다.
        Assert.Contains(reread.Fixtures, fixture =>
            fixture.Choices.Any(choice => choice.To == "main05.03b"));
    }

    [Fact]
    public void cleared_참조가_있는_에피소드의_개명은_거부된다()
    {
        // 조건식은 사람 소유다 — 툴이 고쳐 주지 않고, 남으면 유령이 되므로 막는다.
        string path = Copy();

        ChapterWriteResult result = ChapterWorkbookWriter.RenameEpisode(path, "main05.02", "다른이름");

        Assert.False(result.Written);
        Assert.Contains("cleared:main05.02", result.Failure);
        Assert.Contains("조건", result.Failure);

        // 아무것도 안 바뀌었다.
        Assert.NotNull(ChapterWorkbookReader.Read(path).FindEpisode("main05.02"));
    }

    [Fact]
    public void 에피소드_삭제는_그_간선도_함께_지운다()
    {
        string path = Copy();

        Assert.True(ChapterWorkbookWriter.RemoveEpisode(path, "branch05.02A").Written);

        ChapterGraphModel reread = ChapterWorkbookReader.Read(path);

        Assert.Null(reread.FindEpisode("branch05.02A"));
        Assert.DoesNotContain(reread.Edges, edge =>
            edge.FromEpisodeId == "branch05.02A" || edge.ToEpisodeId == "branch05.02A");
        Assert.Empty(reread.Errors);  // 유령 간선이 없다
    }

    // ── 간선·조건 ───────────────────────────────────────────────────────────

    [Fact]
    public void 간선_추가와_삭제가_왕복한다()
    {
        string path = Copy();

        Assert.True(ChapterWorkbookWriter.AddEdge(
            path, "main05.01", "main05.03", optionLabel: "지름길").Written);

        ChapterGraphModel afterAdd = ChapterWorkbookReader.Read(path);
        ChapterEdge added = afterAdd.Edges.Single(edge =>
            edge.FromEpisodeId == "main05.01" && edge.ToEpisodeId == "main05.03");
        Assert.Equal("지름길", added.OptionLabel);

        Assert.False(ChapterWorkbookWriter.AddEdge(path, "main05.01", "main05.03").Written); // 중복 거부

        Assert.True(ChapterWorkbookWriter.RemoveEdge(path, "main05.01", "main05.03").Written);
        Assert.DoesNotContain(ChapterWorkbookReader.Read(path).Edges, edge =>
            edge.FromEpisodeId == "main05.01" && edge.ToEpisodeId == "main05.03");
    }

    [Fact]
    public void 조건_추가와_수정이_왕복한다()
    {
        string path = Copy();

        Assert.True(ChapterWorkbookWriter.AddCondition(path, "새조건", "trust >= 1", "설명").Written);
        Assert.False(ChapterWorkbookWriter.AddCondition(path, "새조건", "trust >= 2").Written); // 중복 거부

        Assert.True(ChapterWorkbookWriter.UpdateCondition(path, "새조건", "trust >= 2").Written);

        ChapterCondition condition = ChapterWorkbookReader.Read(path).FindCondition("새조건")!;
        Assert.Equal("trust >= 2", condition.Expression);
        Assert.True(condition.IsValid);
    }

    // ── 새 챕터 ─────────────────────────────────────────────────────────────

    [Fact]
    public void 새_챕터_워크북이_규격대로_생기고_리더가_읽는다()
    {
        string folder = Path.Combine(_directory, "chapters");

        Assert.True(ChapterWorkbookWriter.EnsureChapterWorkbook(
            folder, "ch06", [("trust", "신뢰"), ("anger", "분노")]));
        Assert.False(ChapterWorkbookWriter.EnsureChapterWorkbook(folder, "ch06")); // 덮어쓰지 않는다

        ChapterGraphModel model = ChapterWorkbookReader.Read(Path.Combine(folder, "ch06.xlsx"));

        Assert.Equal(2, model.Stats.Count);
        Assert.Empty(model.Episodes);
        Assert.Empty(model.Errors);  // 5시트가 다 있어 시트 누락 오류가 없다
    }
}
