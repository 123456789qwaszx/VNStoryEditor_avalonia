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

    [Fact]
    public void 잠긴_워크북에는_쓰지_않고_사유를_돌려준다()
    {
        string path = Copy();
        byte[] before = File.ReadAllBytes(path);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ChapterWriteResult result = ChapterWorkbookWriter.UpdateEpisode(path, "main05.02", title: "잠금 시험");

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

        // 대사엔트리 = EpisodeId 자동 규약(v3) — 기획자가 관리할 값이 아니라서 툴이 채운다.
        Assert.Equal("main05.04", added.DialogueEntry);
        Assert.Empty(reread.Errors);
    }

    [Fact]
    public void 개명하면_규약을_따르던_대사엔트리가_함께_따라간다()
    {
        string path = Copy();
        Assert.True(ChapterWorkbookWriter.AddEpisode(path, "ep_x", "새 화", 0, 300).Written);

        Assert.True(ChapterWorkbookWriter.RenameEpisode(path, "ep_x", "ep_y").Written);

        // 규약(엔트리 = Id)을 따르던 행은 따라가고 —
        Assert.Equal("ep_y", ChapterWorkbookReader.Read(path).FindEpisode("ep_y")!.DialogueEntry);

        // 사람이 규약과 다르게 적어 둔 엔트리는 건드리지 않는다.
        Assert.True(ChapterWorkbookWriter.UpdateEpisode(path, "ep_y", dialogueEntry: "Story_custom").Written);
        Assert.True(ChapterWorkbookWriter.RenameEpisode(path, "ep_y", "ep_z").Written);
        Assert.Equal("Story_custom",
            ChapterWorkbookReader.Read(path).FindEpisode("ep_z")!.DialogueEntry);
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

    // ── 다음 에피소드 (분기 저작) ───────────────────────────────────────────

    [Fact]
    public void 다음_에피소드는_받은_자리에_서고_간선이_이어진다()
    {
        // 자리 계산은 ChapterBranchPlanner의 일이다(깊이 기반) — 작성기는 받은 좌표를 그대로 쓴다.
        string path = Copy();

        Assert.True(ChapterWorkbookWriter.AddNextEpisode(path, "main05.end", "ep_a", "갈래 A", 1100, 0).Written);
        Assert.True(ChapterWorkbookWriter.AddNextEpisode(path, "main05.end", "ep_b", "갈래 B", 1100, 110, "B를 고른다").Written);

        ChapterGraphModel reread = ChapterWorkbookReader.Read(path);

        Assert.Equal((1100d, 0d), (reread.FindEpisode("ep_a")!.X, reread.FindEpisode("ep_a")!.Y));
        Assert.Equal((1100d, 110d), (reread.FindEpisode("ep_b")!.X, reread.FindEpisode("ep_b")!.Y));

        // 간선이 함께 이어졌고 라벨이 실렸다.
        Assert.Contains(reread.Edges, edge =>
            edge.FromEpisodeId == "main05.end" && edge.ToEpisodeId == "ep_b" &&
            edge.OptionLabel == "B를 고른다");
        Assert.Contains(reread.Edges, edge =>
            edge.FromEpisodeId == "main05.end" && edge.ToEpisodeId == "ep_a" && edge.IsPlainAdvance);
    }

    [Fact]
    public void 다음_에피소드의_중복_Id는_행도_간선도_남기지_않는다()
    {
        // 원자성 — 행 추가와 간선 연결이 한 저장이다. 절반만 성공한 워크북은 없다.
        string path = Copy();

        ChapterWriteResult result = ChapterWorkbookWriter.AddNextEpisode(
            path, "main05.end", "main05.02", "겹침", 1100, 0);

        Assert.False(result.Written);

        ChapterGraphModel reread = ChapterWorkbookReader.Read(path);
        Assert.DoesNotContain(reread.Edges, edge =>
            edge.FromEpisodeId == "main05.end" && edge.ToEpisodeId == "main05.02");
    }

    [Fact]
    public void 간선_속성_편집이_왕복한다()
    {
        string path = Copy();

        Assert.True(ChapterWorkbookWriter.UpdateEdge(
            path, "main05.02", "main05.03",
            optionLabel: "몰래 문을 연다",
            conditionLabel: "분노누적",
            hideWhenLocked: true,
            lockedMessage: "아직은 화가 부족하다").Written);

        ChapterEdge edge = ChapterWorkbookReader.Read(path).Edges.Single(candidate =>
            candidate.FromEpisodeId == "main05.02" && candidate.ToEpisodeId == "main05.03");

        Assert.Equal("몰래 문을 연다", edge.OptionLabel);
        Assert.Equal("분노누적", edge.ConditionLabel);
        Assert.True(edge.HideWhenLocked);
        Assert.Equal("아직은 화가 부족하다", edge.LockedMessage);

        // 빈 문자열 = 지우기. 조건을 떼면 일반 통행이 된다.
        Assert.True(ChapterWorkbookWriter.UpdateEdge(
            path, "main05.02", "main05.03", conditionLabel: "").Written);
        Assert.Null(ChapterWorkbookReader.Read(path).Edges.Single(candidate =>
            candidate.FromEpisodeId == "main05.02" && candidate.ToEpisodeId == "main05.03").ConditionLabel);
    }

    // ── 안전망·개명 ─────────────────────────────────────────────────────────

    [Fact]
    public void 파괴적_쓰기는_직전_상태를_bak으로_남긴다()
    {
        // 툴 편집에는 Ctrl+Z가 없다 — 지우는 종류의 쓰기는 .bak을 굴려 되돌릴 길을 남긴다.
        string path = Copy();
        byte[] before = File.ReadAllBytes(path);

        Assert.True(ChapterWorkbookWriter.RemoveEpisode(path, "branch05.02A").Written);

        string backup = path + ".bak";
        Assert.True(File.Exists(backup));
        Assert.Equal(before, File.ReadAllBytes(backup));  // 지우기 직전 상태 그대로

        // 속성 편집 같은 비파괴 쓰기는 백업을 만들지 않는다 — 굴림이 덮어쓰지 않게.
        File.Delete(backup);
        Assert.True(ChapterWorkbookWriter.UpdateEpisode(path, "main05.01", title: "새 제목").Written);
        Assert.False(File.Exists(backup));
    }

    [Fact]
    public void 챕터_개명은_파일을_옮기고_중복은_거부한다()
    {
        string folder = Path.Combine(_directory, "chapters");
        Directory.CreateDirectory(folder);
        File.Copy(SamplePath, Path.Combine(folder, "ch05.xlsx"));

        ChapterWriteResult result = ChapterWorkbookWriter.RenameChapterWorkbook(folder, "ch05", "ch05b");

        Assert.True(result.Written, result.Failure);
        Assert.False(File.Exists(Path.Combine(folder, "ch05.xlsx")));
        Assert.NotNull(ChapterWorkbookReader.Read(Path.Combine(folder, "ch05b.xlsx")).FindEpisode("main05.01"));

        // 대상이 이미 있으면 덮어쓰지 않고 거부한다.
        File.Copy(SamplePath, Path.Combine(folder, "ch05.xlsx"));
        Assert.False(ChapterWorkbookWriter.RenameChapterWorkbook(folder, "ch05", "ch05b").Written);
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

    [Fact]
    public void xlsm으로_개명된_챕터도_읽고_쓸_수_있다()
    {
        // 구글 시트가 챕터 파일을 .xlsm으로 개명해도(매크로 없이 선언만) 챕터는 살아 있어야
        // 하고, 툴의 구조 편집(간선·에피소드)도 그 파일에 그대로 이어져야 한다.
        string path = Path.Combine(_directory, "ch05.xlsm");
        File.Copy(SamplePath, path);

        Assert.NotNull(ChapterWorkbookReader.Read(path).FindEpisode("main05.01"));

        Assert.True(ChapterWorkbookWriter.UpdateEpisode(path, "main05.02", title: "xlsm에서 고침").Written);
        Assert.Equal("xlsm에서 고침", ChapterWorkbookReader.Read(path).FindEpisode("main05.02")!.Title);
    }

    [Fact]
    public void 조건_라벨_열에_조건_시트를_가리키는_드롭다운이_깔린다()
    {
        // 조건 라벨은 엑셀에서 적는다(v3) — 손으로 적으면 오타가 유령 참조가 되므로,
        // 목록을 엑셀 단에서 강제한다. 정적 목록이 아니라 범위 참조라 조건이 늘면 따라온다.
        string folder = Path.Combine(_directory, "chapters");
        ChapterWorkbookWriter.EnsureChapterWorkbook(folder, "ch07", [("trust", "신뢰")]);

        using var workbook = new XLWorkbook(Path.Combine(folder, "ch07.xlsx"));

        IXLWorksheet episodes = workbook.Worksheet(ChapterSheetNames.Episodes);
        IXLWorksheet edges = workbook.Worksheet(ChapterSheetNames.Edges);

        // 표시조건(H) · 해금조건(I) · 간선의 조건(D)
        AssertConditionList(episodes, 2, 8);
        AssertConditionList(episodes, 2, 9);
        AssertConditionList(edges, 2, 4);
    }

    private static void AssertConditionList(IXLWorksheet sheet, int row, int column)
    {
        IXLDataValidation? validation = sheet.Cell(row, column).GetDataValidation();

        Assert.NotNull(validation);
        Assert.Equal(XLAllowedValues.List, validation.AllowedValues);
        Assert.Contains(ChapterSheetNames.Conditions, validation.Value);
    }
}
