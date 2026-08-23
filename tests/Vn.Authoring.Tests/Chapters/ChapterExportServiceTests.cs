using System.Text.Json;
using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 증명 캐시와 내보내기 — 2026-08-23에 `ChapterGraphView` 3,835줄에서 나온 규칙들.
///
/// <b>이 파일이 이 리팩터의 목적이다.</b> 여기 붙드는 것들은 그동안 헤드리스 UI 스위트
/// (41초)를 통해서만 간접적으로 닿았고, 그래서 <b>규칙으로 읽히지 않았다.</b> 실제로
/// 물린 적이 있다 — "동기화는 고른 챕터만, 내보내기는 전 챕터"라는 비대칭이 코드비하인드에
/// 묻혀 있어, 저작 관문을 걸려던 시도가 뒤늦게 그것을 알았다.
/// </summary>
public sealed class ChapterExportServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "vn-chapter-export-service", Guid.NewGuid().ToString("N"));

    private string ProjectPath => Path.Combine(_root, "예제.vnproject.json");

    public ChapterExportServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ── 증명 캐시 ───────────────────────────────────────────────────────────

    [Fact]
    public void 디스크가_그대로면_다시_증명하지_않는다()
    {
        // 챕터 하나 증명에 200ms 가까이 든다(에피소드마다 엑셀을 열고 상태공간을 훑는다).
        // 화면은 갱신할 때마다 이것을 처음부터 다시 돌리고 있었다.
        var service = new ChapterExportService();
        ChapterEntry entry = Entry("ch01", Sound);

        service.ValidationFor(entry, ProjectPath);
        service.ValidationFor(entry, ProjectPath);
        service.ValidationFor(entry, ProjectPath);

        Assert.Equal(1, service.ValidationComputeCount);
    }

    [Fact]
    public void 워크북_내용이_바뀌면_다시_증명한다()
    {
        // ⚠ 지문이 시각·크기가 아니라 **내용 해시**인 이유가 여기 있다. 화면은 자기가
        // 워크북을 쓰고 그 자리에서 곧바로 다시 읽는다 — 두 사건이 같은 시각 눈금에
        // 들어가고 길이까지 같으면, 시각·크기 지문은 "안 바뀌었다"고 거짓말한다.
        var service = new ChapterExportService();
        ChapterEntry entry = Entry("ch01", Sound);

        service.ValidationFor(entry, ProjectPath);

        // 길이는 같고 내용만 다르게 — 크기 지문이라면 여기서 속는다.
        File.WriteAllBytes(entry.Path, [9, 9, 9, 9]);

        service.ValidationFor(entry, ProjectPath);

        Assert.Equal(2, service.ValidationComputeCount);
    }

    [Fact]
    public void 챕터마다_따로_기억한다()
    {
        var service = new ChapterExportService();

        service.ValidationFor(Entry("ch01", Sound), ProjectPath);
        service.ValidationFor(Entry("ch02", Sound), ProjectPath);
        service.ValidationFor(Entry("ch01", Sound), ProjectPath);   // 이미 안다

        Assert.Equal(2, service.ValidationComputeCount);
    }

    // ── 내보내기 ────────────────────────────────────────────────────────────

    [Fact]
    public void 고른_챕터만이_아니라_전부_나간다()
    {
        // 2026-08-17 소유자 — 고른 챕터만 내보내면 나머지는 누른 순간의 낡은 판으로 굳는다.
        var service = new ChapterExportService();

        ChapterExportRun run = service.ExportAll(
            [Entry("ch01", Sound), Entry("ch02", Sound), Entry("ch03", Sound)],
            ProjectPath);

        Assert.True(run.AllExported, string.Join(" / ", run.Refused.Concat(run.Failed)));
        Assert.Null(run.Notice);

        foreach (string id in new[] { "ch01", "ch02", "ch03" })
        {
            Assert.True(
                File.Exists(ChapterExportService.ExportPathFor(ProjectPath, id)),
                $"{id}이 안 나갔다");
        }
    }

    [Fact]
    public void 검증에_걸린_챕터는_기존_파일을_손대지_않는다()
    {
        // ⛔ G8. 거부되면 파일을 안 건드린다 — 낡은 파일이 남더라도 쓰레기로 덮는 것보다
        // 낫다. 런타임이 읽는 것은 언제나 "한 번은 옳았던" 판이어야 한다.
        var service = new ChapterExportService();
        string path = ChapterExportService.ExportPathFor(ProjectPath, "ch01");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ \"한번은\": \"옳았던 판\" }");

        ChapterExportRun run = service.ExportAll([Entry("ch01", Unreachable)], ProjectPath);

        Assert.Contains("ch01", run.Refused);
        Assert.Equal("{ \"한번은\": \"옳았던 판\" }", File.ReadAllText(path));
    }

    [Fact]
    public void 같은_글이면_파일을_다시_쓰지_않는다()
    {
        // 다시 읽을 때마다 파일을 두드리면 클라우드 동기화가 계속 깨어나고, 바뀐 것이
        // 없는데 시각만 새로 찍힌다.
        var service = new ChapterExportService();
        ChapterEntry entry = Entry("ch01", Sound);

        service.ExportAll([entry], ProjectPath);

        string path = ChapterExportService.ExportPathFor(ProjectPath, "ch01");
        DateTime firstWrite = new FileInfo(path).LastWriteTimeUtc;

        // 시각 눈금이 확실히 갈리도록 뒤로 밀어 두고 다시 낸다 — 안 쓰면 이 값이 남는다.
        File.SetLastWriteTimeUtc(path, firstWrite.AddMinutes(-5));
        DateTime marked = new FileInfo(path).LastWriteTimeUtc;

        service.ExportAll([entry], ProjectPath);

        Assert.Equal(marked, new FileInfo(path).LastWriteTimeUtc);
    }

    [Fact]
    public void 못_나간_것만_말한다()
    {
        var service = new ChapterExportService();

        ChapterExportRun run = service.ExportAll(
            [Entry("ch01", Sound), Entry("ch02", Unreachable)],
            ProjectPath);

        Assert.Equal(["ch02"], run.Refused);
        Assert.Empty(run.Failed);

        string notice = Assert.IsType<string>(run.Notice);
        Assert.Contains("ch02", notice);
        Assert.DoesNotContain("ch01", notice);   // 잘 나간 것은 말하지 않는다
    }

    [Fact]
    public void 프로젝트를_아직_저장하지_않았으면_아무것도_안_한다()
    {
        // 저장 전에는 산출물이 놓일 자리가 없다. 실패가 아니라 할 일이 없는 것이다.
        ChapterExportRun run = new ChapterExportService()
            .ExportAll([Entry("ch01", Sound)], projectPath: null);

        Assert.True(run.AllExported);
        Assert.Null(run.Notice);
    }

    [Fact]
    public void 증명을_두_번_돌리지_않는다()
    {
        // 2026-08-18 — 화면이 보고 패널을 세우려고 한 번 증명하는데, 내보내기가 안에서
        // 또 증명해 챕터 하나당 200ms를 두 번 치르고 있었다. 그래서 이 둘이 한 객체다.
        var service = new ChapterExportService();
        ChapterEntry entry = Entry("ch01", Sound);

        service.ValidationFor(entry, ProjectPath);
        service.ExportAll([entry], ProjectPath);

        Assert.Equal(1, service.ValidationComputeCount);
    }

    [Fact]
    public void 내보낸_JSON이_실제로_읽히는_모양이다()
    {
        // 캐시·장부만 붙들고 내용은 안 보면, 나가긴 나가는데 쓸모없는 파일이 될 수 있다.
        var service = new ChapterExportService();

        service.ExportAll([Entry("ch01", Sound)], ProjectPath);

        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(ChapterExportService.ExportPathFor(ProjectPath, "ch01")));

        Assert.Equal("ch01", document.RootElement.GetProperty("ChapterId").GetString());
        Assert.Equal("ep1", document.RootElement.GetProperty("StartEpisodeId").GetString());
    }

    // ── ⑧ 저작 관문 — `대사엔트리`가 실재하는 대사노드를 가리키나 ─────────────

    [Fact]
    public void 한_번도_안_연_챕터는_검사하지_않는다()
    {
        // ⚠ 이 관문이 오탐을 내면 오늘 잘 나가던 챕터가 전부 막힌다. 동기화는 **고른
        // 챕터 하나만** 돌고 내보내기는 **전 챕터**를 도는 비대칭이 그 위험의 원천이다.
        // 판 자체가 없으면 그 챕터는 아직 안 연 것이지 잘못된 것이 아니다.
        var editor = new ProjectEditor(new StoryProject());

        ChapterExportRun run = new ChapterExportService()
            .ExportAll([Entry("ch01", Sound)], ProjectPath, editor.Project);

        Assert.True(run.AllExported, string.Join(" / ", run.Refused));
    }

    [Fact]
    public void 판은_섰지만_아직_동기화_전이면_검사하지_않는다()
    {
        // 챕터 목록을 클릭하면 판이 먼저 서고 노드는 동기화가 만든다. 그 사이가 있다.
        var editor = new ProjectEditor(new StoryProject());
        editor.EnsureChapterBoard("ch01");

        ChapterExportRun run = new ChapterExportService()
            .ExportAll([Entry("ch01", Sound)], ProjectPath, editor.Project);

        Assert.True(run.AllExported, string.Join(" / ", run.Refused));
    }

    [Fact]
    public void 판에_노드가_있는데_하나가_빠지면_거부한다()
    {
        // 노드가 하나라도 있으면 그 챕터는 한 번은 동기화됐다는 뜻이고, 그때부터 빠진
        // 이름은 진짜 빠진 것이다 — 이대로 내보내면 진행 JSON이 없는 노드를 부르고
        // 로드·검증·증명은 통과하는데 재생만 안 된다.
        var editor = new ProjectEditor(new StoryProject());
        string fileId = editor.EnsureChapterBoard("ch01");
        editor.AddDialogueNode(fileId, name: "ep1");     // ep2는 없다

        var service = new ChapterExportService();
        ChapterExportRun run = service.ExportAll([Entry("ch01", Sound)], ProjectPath, editor.Project);

        Assert.Equal(["ch01"], run.Refused);

        ChapterDiagnostic problem = Assert.Single(
            service.ValidationFor(Entry("ch01", Sound), ProjectPath, editor.Project).All,
            item => item.Code == ChapterDiagnosticCode.DialogueEntryNodeMissing);

        Assert.Equal(ChapterDiagnosticSeverity.Error, problem.Severity);
        Assert.Contains("ep2", problem.Message);
        Assert.Contains("이 챕터를 한 번 고르면", problem.Message);   // 고치는 법까지 말한다
    }

    [Fact]
    public void 노드가_생기면_캐시가_깨져_다시_증명한다()
    {
        // ⛔ 이것이 없으면 **"고쳤는데 계속 거부한다"**가 된다. 지문이 파일만 보면 동기화가
        // 노드를 만들어도 캐시가 옛 결론을 그대로 준다 — 캐시가 만드는 가장 나쁜 거짓말이
        // 여기서 한 번 더 나올 자리다.
        var editor = new ProjectEditor(new StoryProject());
        string fileId = editor.EnsureChapterBoard("ch01");
        editor.AddDialogueNode(fileId, name: "ep1");

        var service = new ChapterExportService();
        ChapterEntry entry = Entry("ch01", Sound);

        Assert.Contains("ch01", service.ExportAll([entry], ProjectPath, editor.Project).Refused);

        // 동기화가 빠진 노드를 만든다 — 디스크는 한 글자도 안 바뀌었다.
        editor.AddDialogueNode(fileId, name: "ep2");

        ChapterExportRun after = service.ExportAll([entry], ProjectPath, editor.Project);

        Assert.True(after.AllExported, string.Join(" / ", after.Refused));
        Assert.Equal(2, service.ValidationComputeCount);   // 파일이 그대로여도 다시 증명했다
    }

    [Fact]
    public void 판을_안_넘기면_이_검사를_하지_않는다()
    {
        // 콘솔·테스트처럼 판을 볼 수 없는 자리에서도 나머지 검증은 그대로 돌아야 한다.
        var editor = new ProjectEditor(new StoryProject());
        editor.AddDialogueNode(editor.EnsureChapterBoard("ch01"), name: "ep1");

        Assert.True(new ChapterExportService()
            .ExportAll([Entry("ch01", Sound)], ProjectPath)
            .AllExported);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────
    //
    // 지문은 **디스크의 바이트**로, 검증은 **메모리의 모델**로 몬다. 둘이 갈라져 있어
    // 캐시 동작과 내보내기 동작을 따로 흔들 수 있다 — 엑셀을 실제로 굽지 않으므로 빠르다.

    private ChapterEntry Entry(string chapterId, Func<string, ChapterGraphModel> build)
    {
        string path = Path.Combine(_root, chapterId + ".xlsx");
        File.WriteAllBytes(path, [1, 2, 3, 4]);

        return new ChapterEntry(chapterId, path, build(chapterId), null);
    }

    /// <summary>ep1 → ep2. 오류 없이 나간다.</summary>
    private static ChapterGraphModel Sound(string chapterId) => new(
        chapterId,
        string.Empty,
        [
            new ChapterEpisode("ep1", "첫 화", "", "Main", "ep1", 0, 0, null, null, 2),
            new ChapterEpisode("ep2", "둘째", "", "Main", "ep2", 200, 0, null, null, 3)
        ],
        [new ChapterEdge("ep1", "ep2", null, null, null, 2)],
        [],
        [new ChapterStat("trust", "신뢰", Initial: 1, Minimum: 0, Maximum: 5, SourceRow: 2)],
        [],
        []);

    /// <summary>ep3로 들어오는 길이 없다 — 도달성 증명이 오류로 잡는다(D3).</summary>
    private static ChapterGraphModel Unreachable(string chapterId) => new(
        chapterId,
        string.Empty,
        [
            new ChapterEpisode("ep1", "첫 화", "", "Main", "ep1", 0, 0, null, null, 2),
            new ChapterEpisode("ep2", "둘째", "", "Main", "ep2", 200, 0, null, null, 3),
            new ChapterEpisode("ep3", "닿지 않는 화", "", "Main", "ep3", 400, 0, null, null, 4)
        ],
        [new ChapterEdge("ep1", "ep2", null, null, null, 2)],
        [],
        [new ChapterStat("trust", "신뢰", Initial: 1, Minimum: 0, Maximum: 5, SourceRow: 2)],
        [],
        []);
}
