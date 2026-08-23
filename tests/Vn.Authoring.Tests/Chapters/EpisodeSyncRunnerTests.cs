using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 에피소드 동기화의 <b>순서와 정책</b> — 2026-08-23에 `ChapterGraphView`에서 나왔다.
///
/// 각 단계의 일은 이미 `EpisodeSyncService`·`EpisodeLibrary`·`EpisodeWorkbookMigrator`가
/// 갖고 있었고 테스트도 있었다. <b>없던 것은 순서에 대한 테스트</b>다 — 그것이 3,512줄
/// 코드비하인드 안에 살아서 헤드리스 UI로만 닿았기 때문이다.
///
/// ⚠ 여기 테스트는 <b>화면도 세션도 모른다.</b>
/// </summary>
public sealed class EpisodeSyncRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "vn-episode-sync-runner", Guid.NewGuid().ToString("N"));

    private string ProjectPath => Path.Combine(_root, "예제.vnproject.json");

    public EpisodeSyncRunnerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void 판이_없으면_만들고_그_Id를_돌려준다()
    {
        // 챕터 = 판 1:1 (G-1 v2). 동기화가 대사 노드를 놓을 자리를 스스로 보장한다 —
        // 예전에는 이 한 줄 때문에 도메인이 셸의 메서드를 불러야 했다.
        var editor = new ProjectEditor(new StoryProject());

        EpisodeSyncRun run = Run(editor, Chapter("ch01", "ep1", "ep2"));

        Assert.NotNull(run.BoardFileId);
        Assert.Equal("ch01", editor.Project.FindFile(run.BoardFileId!)!.Name);
    }

    [Fact]
    public void 대본이_없는_에피소드에_워크북을_만들어_준다()
    {
        // 2026-08-17 소유자 보고 — 툴의 [＋ 에피소드]는 이미 만들고 있었지만 **엑셀에서
        // 직접 행을 더한 경우**가 남아 있었고, 그게 기본 작업 방식이라 대부분이 그 길이다.
        var editor = new ProjectEditor(new StoryProject());

        EpisodeSyncRun run = Run(editor, Chapter("ch01", "ep1", "ep2"));

        Assert.True(run.WorkbooksCreated, "새 워크북을 만들었다면 셸이 감시자를 다시 걸어야 한다");
        Assert.NotNull(EpisodeLibrary.FindExisting(EpisodesFolder("ch01"), "ep1"));
        Assert.NotNull(EpisodeLibrary.FindExisting(EpisodesFolder("ch01"), "ep2"));
    }

    [Fact]
    public void 두_번_돌려도_워크북을_다시_만들지_않는다()
    {
        // 감시자는 어느 파일이 바뀌었는지 말하지 않으므로 매번 전부 다시 돈다 —
        // 그래서 멱등이 아니면 저장 한 번이 워크북을 덮어쓴다.
        var editor = new ProjectEditor(new StoryProject());
        ChapterEntry entry = Chapter("ch01", "ep1");

        Assert.True(Run(editor, entry).WorkbooksCreated);
        Assert.False(Run(editor, entry).WorkbooksCreated, "있는 파일에는 손대지 않는다");
    }

    [Fact]
    public void 새_워크북이_화자와_조건_목록을_받는다()
    {
        // 만들 때 어휘를 넣지 않으면, 작가가 첫 줄을 쓰는 순간 드롭다운이 비어 있다.
        var editor = new ProjectEditor(new StoryProject());
        var definition = new GameDefinition
        {
            Speakers = { new SpeakerSpec { Name = "라루" }, new SpeakerSpec { Name = "윌로" } }
        };

        EpisodeSyncRun run = EpisodeSyncRunner.Run(
            editor, definition, ProjectPath, Chapter("ch01", "ep1"), []);

        Assert.NotNull(run.BoardFileId);

        // 러너가 쓴 화자 목록은 화면이 [＋ 에피소드]에서 쓰는 것과 **같은 규칙**이어야 한다.
        Assert.Equal(["라루", "윌로"], EpisodeSyncRunner.SpeakerNames(definition));
    }

    [Fact]
    public void 에피소드가_없고_폴더도_없으면_아무것도_안_한다()
    {
        // 폴더가 없다고 곧장 멈추지는 않는다 (2026-08-17) — 에피소드가 하나라도 있으면
        // 대본을 만들어 줄 참이고 그 첫 파일이 폴더를 만든다. 둘 다 없을 때만 돌아간다.
        var editor = new ProjectEditor(new StoryProject());

        EpisodeSyncRun run = Run(editor, Chapter("ch01"));

        Assert.Null(run.BoardFileId);
        Assert.Empty(editor.Project.Files);
    }

    [Fact]
    public void 프로젝트를_아직_저장하지_않았으면_아무것도_안_한다()
    {
        EpisodeSyncRun run = EpisodeSyncRunner.Run(
            new ProjectEditor(new StoryProject()),
            GameDefinition.Empty,
            projectPath: null,
            Chapter("ch01", "ep1"),
            []);

        Assert.Same(EpisodeSyncRun.Nothing, run);
    }

    [Fact]
    public void 볼_워크북이_하나도_없었으면_상태줄에_아무_말도_하지_않는다()
    {
        // 규칙은 "반영이 0이면 조용히"가 아니라 **"본 워크북이 0이면 조용히"**다.
        // 에피소드가 없는 챕터를 고른 것뿐인데 상태줄이 말하면 그게 소음이다.
        Assert.Null(EpisodeSyncRun.Nothing.StatusMessage);
        Assert.Null(new EpisodeSyncRun("sf_1", [], [], [], false).StatusMessage);
    }

    [Fact]
    public void 워크북은_섰지만_아직_안_썼으면_0개로_말한다()
    {
        // ⚠ 갓 만든 워크북도 보고를 하나 낸다(NotYetWritten). 그래서 첫 동기화 직후
        // 상태줄이 "에피소드 0개를 반영했습니다"라고 말한다 — 어색하지만 **지금 동작이고**,
        // 화면에서 규칙을 꺼내는 이 작업이 동작을 바꾸지는 않는다.
        //
        // 바꾸고 싶다면 여기가 그 자리다: 이제 UI 없이 고칠 수 있고, 이 테스트가 갈린다.
        var editor = new ProjectEditor(new StoryProject());

        EpisodeSyncRun run = Run(editor, Chapter("ch01", "ep1"));

        Assert.Single(run.Reports);
        Assert.Equal(0, run.Applied);
        Assert.Equal("에피소드 0개를 반영했습니다.", run.StatusMessage);
    }

    [Fact]
    public void 거부가_있으면_그_수를_함께_말한다()
    {
        // 반영 수만 말하면 "됐다"로 읽힌다 — 거부·경고가 있으면 아래 보고를 보라고 한다.
        var run = new EpisodeSyncRun(
            "sf_1",
            [Report(applied: true, rejections: 0), Report(applied: false, rejections: 2)],
            [], [], false);

        Assert.Equal(1, run.Applied);
        Assert.Equal(2, run.Rejected);
        Assert.Contains("거부·경고 2건", run.StatusMessage);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private EpisodeSyncRun Run(ProjectEditor editor, ChapterEntry entry) =>
        EpisodeSyncRunner.Run(editor, GameDefinition.Empty, ProjectPath, entry, [entry]);

    private string EpisodesFolder(string chapterId) =>
        EpisodeLibrary.FolderFor(ProjectPath, chapterId)!;

    /// <summary>에피소드들을 한 줄로 이은 챕터 — 워크북 파일은 러너가 만든다.</summary>
    private ChapterEntry Chapter(string chapterId, params string[] episodeIds)
    {
        var episodes = episodeIds
            .Select((id, index) =>
                new ChapterEpisode(id, id, "", "Main", id, index * 200, 0, null, null, index + 2))
            .ToList();

        var edges = episodeIds
            .Zip(episodeIds.Skip(1), (from, to) =>
                new ChapterEdge(from, to, null, null, HideWhenLocked: false, null, 2))
            .ToList();

        var model = new ChapterGraphModel(
            chapterId, string.Empty, episodes, edges, [],
            [new ChapterStat("trust", "신뢰", Initial: 0, Minimum: 0, Maximum: 5, SourceRow: 2)],
            [], []);

        return new ChapterEntry(chapterId, Path.Combine(_root, chapterId + ".xlsx"), model, null);
    }

    /// <summary>보고의 모양은 `EpisodeSyncService`가 정한다 — 여기서는 세는 규칙만 본다.</summary>
    private static EpisodeSyncReport Report(bool applied, int rejections) => new(
        EpisodeId: "ep",
        WorkbookPath: "ep.xlsx",
        DialogueNodeId: "nd_1",
        Applied: applied,
        Diagnostics: [],
        Problems: [.. Enumerable.Range(0, rejections).Select(index => $"문제 {index}")],
        Pruned: [],
        IssuedLineIds: []);
}
