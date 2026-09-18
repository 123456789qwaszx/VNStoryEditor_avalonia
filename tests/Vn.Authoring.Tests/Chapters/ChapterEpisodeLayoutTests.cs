using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>자리의 주인은 사람이다</b> (v4 · 2026-09-18 소유자: *"챕터그래프도 연출그래프와
/// 동일하게 자유롭게 노드의 위치를 조절할 수 있게"*).
///
/// v3까지는 그릴 때마다 깊이 배치를 다시 계산했고, 그 근거는 <i>"드래그가 없으므로 사람이
/// 지킬 배치도 없다"</i>였다. 드래그가 생기면서 전제가 뒤집혔다 — 이 묶음이 재는 것은
/// <b>뒤집힌 뒤에 새로 생긴 두 가지 의무</b>다:
///
/// <list type="number">
///   <item>한 동작으로 여러 카드를 옮겼으면 <b>되돌리기도 한 번</b>이다</item>
///   <item>아직 자리가 없는 챕터(엑셀에서 들여와 전부 0,0)는 <b>한 번 심어</b> 준다</item>
/// </list>
/// </summary>
public sealed class ChapterEpisodeLayoutTests
{
    private readonly ProjectEditor _editor = new(new StoryProject());

    private ChapterDocument Chapter => _editor.FindChapter("ch01")!;

    public ChapterEpisodeLayoutTests()
    {
        _editor.EnsureChapter("ch01");
        _editor.AddEpisode("ch01", "ep01", "복도", 0, 0);
        _editor.AddEpisode("ch01", "ep02", "옥상", 0, 0);
        _editor.AddEpisode("ch01", "ep03", "지하", 0, 0);
    }

    [Fact]
    public void 한_번에_옮기면_되돌리기도_한_번이다()
    {
        // ⭐ 요점은 이것 하나다. MoveEpisode를 세 번 부르면 [자동 정렬] 한 번을 무르는 데
        //    Ctrl+Z를 세 번 눌러야 한다 — 사람이 한 동작으로 한 일은 한 번에 돌아와야 한다.
        _editor.MoveEpisodes("ch01", new Dictionary<string, (double X, double Y)>
        {
            ["ep01"] = (10, 20),
            ["ep02"] = (30, 40),
            ["ep03"] = (50, 60)
        });

        Assert.Equal([(10.0, 20.0), (30.0, 40.0), (50.0, 60.0)],
            Chapter.Episodes.Select(episode => (episode.X, episode.Y)));

        _editor.Undo();

        Assert.All(_editor.FindChapter("ch01")!.Episodes,
            episode => Assert.Equal((0.0, 0.0), (episode.X, episode.Y)));
    }

    [Fact]
    public void 자리를_옮기는_것은_구조_변경이_아니다()
    {
        ProjectChangeKind? kind = null;
        _editor.Changed += (_, args) => kind = args.Kind;

        _editor.MoveEpisodes("ch01", new Dictionary<string, (double X, double Y)>
        {
            ["ep01"] = (1.239, 2.341)
        });

        Assert.Equal(ProjectChangeKind.NodeMetadata, kind);
        Assert.Equal((1.24, 2.34), (Chapter.Episodes[0].X, Chapter.Episodes[0].Y));
    }

    [Fact]
    public void 모르는_에피소드가_섞여_있으면_아무것도_안_옮긴다()
    {
        // ⚠ 반쯤 옮기고 던지면 판이 어중간해지고, 조용히 건너뛰면 "정렬했는데 하나가
        //   제자리"를 사람이 버그로 읽는다. 둘 다 아니고 — 통째로 거절한다.
        Assert.ThrowsAny<InvalidOperationException>(() =>
            _editor.MoveEpisodes("ch01", new Dictionary<string, (double X, double Y)>
            {
                ["ep01"] = (10, 20),
                ["없는에피소드"] = (30, 40)
            }));

        Assert.All(Chapter.Episodes,
            episode => Assert.Equal((0.0, 0.0), (episode.X, episode.Y)));

        // ⚠ 되돌리기에도 아무것도 안 쌓였다 — 무른 것은 세 번째 에피소드를 세운 일이지
        //    실패한 옮기기가 아니다. 거절한 동작이 목록을 차지하면 Ctrl+Z가 한 번 헛돈다.
        _editor.Undo();

        Assert.Equal(["ep01", "ep02"],
            _editor.FindChapter("ch01")!.Episodes.Select(episode => episode.EpisodeId));
    }

    [Fact]
    public void 겹쳐_있으면_심을_자리다()
    {
        // 엑셀에서 들여온 챕터가 정확히 이 모양이다 — X·Y를 아무도 안 채웠다.
        Assert.True(ChapterBranchPlanner.NeedsSeeding(Chapter.ToGraphModel("ch01.xlsx")));

        _editor.MoveEpisodes("ch01", new Dictionary<string, (double X, double Y)>
        {
            ["ep01"] = (0, 0),
            ["ep02"] = (220, 0),
            ["ep03"] = (440, 0)
        });

        // ⭐ 한 번만 참이다 — 심고 나면 다시 심지 않는다. 그래야 사람이 옮긴 자리를
        //    그리기가 덮지 않는다.
        Assert.False(ChapterBranchPlanner.NeedsSeeding(Chapter.ToGraphModel("ch01.xlsx")));
    }

    [Fact]
    public void 에피소드가_하나뿐이면_원점이어도_심지_않는다()
    {
        // (0,0)이 "안 채워졌다"는 뜻은 아니다 — 겹치는 것이 없으면 고칠 것도 없다.
        var lone = new ProjectEditor(new StoryProject());
        lone.EnsureChapter("ch02");
        lone.AddEpisode("ch02", "ep01", "혼자", 0, 0);

        Assert.False(ChapterBranchPlanner.NeedsSeeding(
            lone.FindChapter("ch02")!.ToGraphModel("ch02.xlsx")));
    }
}
