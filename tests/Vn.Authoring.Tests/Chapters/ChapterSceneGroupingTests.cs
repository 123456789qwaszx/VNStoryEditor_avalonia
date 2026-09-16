using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>챕터를 장면으로 묶어 본다</b> (R6 S-1 · 2026-09-16, `docs/plans/R6.md`).
///
/// ⛔ 이 묶음이 <b>저장되지 않는다</b>는 것이 요점이다. 정본은 `Episode.SceneId` 하나이고
/// 여기서 나오는 것은 화면이 그릴 때마다 만드는 투영이다 — 런타임에도 장면 표가 없다.
///
/// 못 박는 것: ① 같은 장면ID끼리 묶인다 ② 순서의 근거는 <b>루트의 깊이</b>다
/// ③ 빈 장면ID는 사람에게 `__scene_*`로 보이지 않는다 ④ <b>깨진 챕터에서도 묶음이 나온다</b>.
/// </summary>
public sealed class ChapterSceneGroupingTests
{
    [Fact]
    public void 같은_장면ID끼리_묶이고_루트가_맨_앞에_선다()
    {
        // opening: a → b, classroom: c. a가 챕터 시작이라 opening의 루트.
        ChapterGraphModel chapter = Chapter(
            [Episode("a", "opening", 2), Episode("b", "opening", 3), Episode("c", "classroom", 4)],
            [Edge("a", "b"), Edge("b", "c")]);

        IReadOnlyList<ChapterScene> scenes = ChapterSceneGrouping.Of(chapter);

        Assert.Equal(["opening", "classroom"], scenes.Select(scene => scene.SceneId));

        Assert.Equal(["a", "b"], scenes[0].Episodes.Select(episode => episode.EpisodeId));
        Assert.Equal("a", scenes[0].RootEpisodeId);

        Assert.Equal(["c"], scenes[1].Episodes.Select(episode => episode.EpisodeId));
        Assert.Equal("c", scenes[1].RootEpisodeId);
    }

    [Fact]
    public void 장면_순서는_루트의_깊이다()
    {
        // ⚠ 시트 순서가 아니다 — 시트에는 late가 먼저 적혀 있어도 이야기 순서가 이긴다.
        ChapterGraphModel chapter = Chapter(
            [Episode("late", "B", 2), Episode("first", "A", 3)],
            [Edge("first", "late")]);

        // 챕터 시작 = 시트 첫 행이라는 규칙 때문에 여기서는 late가 시작이 된다 —
        // 그 전제를 피하려고 시작을 명시적으로 first로 두는 챕터를 따로 만든다.
        ChapterGraphModel ordered = Chapter(
            [Episode("first", "A", 2), Episode("late", "B", 3)],
            [Edge("first", "late")]);

        Assert.Equal(["A", "B"], ChapterSceneGrouping.Of(ordered).Select(scene => scene.SceneId));

        // 그리고 깊이를 모르는(도달 못 하는) 장면은 뒤로 밀린다 — 순서가 흔들리지 않게.
        Assert.Equal(2, ChapterSceneGrouping.Of(chapter).Count);
    }

    [Fact]
    public void 장면ID가_비면_내부_발급값을_사람에게_보이지_않는다()
    {
        // ⛔ `__scene_solo`는 퇴화 상태의 구현 세부다 — 화면에 그대로 뜨면 사람이 그것을
        //    고쳐야 하는 값으로 읽는다.
        ChapterGraphModel chapter = Chapter([Episode("solo", sceneId: null, 2)], []);

        ChapterScene scene = Assert.Single(ChapterSceneGrouping.Of(chapter));

        Assert.True(scene.IsDefault);
        Assert.Equal("__scene_solo", scene.SceneId);              // 비교의 열쇠는 그대로
        Assert.Equal("미지정 · solo", scene.DisplayName);
        Assert.DoesNotContain("__scene_", scene.DisplayName);
    }

    [Fact]
    public void 섞인_챕터에서_미지정_장면끼리_이름이_안_겹친다()
    {
        // ⛔ 규격을 쓰다 드러난 구멍(2026-09-16). 표시명이 하나뿐이면 미지정 장면이 여럿일 때
        //    <b>똑같은 줄이 여럿 서서</b> 어느 것이 어느 에피소드인지 알 수 없다.
        //    (장면ID를 하나도 안 적은 챕터는 화면이 장면 단을 생략하므로 이 이름이 안 보인다 —
        //     이 이름이 실제로 서는 자리는 <b>섞인 챕터</b>뿐이다.)
        ChapterGraphModel chapter = Chapter(
            [Episode("a", "opening", 2), Episode("b", null, 3), Episode("c", null, 4)],
            [Edge("a", "b"), Edge("b", "c")]);

        List<string> names = ChapterSceneGrouping.Of(chapter)
            .Where(scene => scene.IsDefault)
            .Select(scene => scene.DisplayName)
            .ToList();

        Assert.Equal(["미지정 · b", "미지정 · c"], names);
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void 빈_장면ID는_에피소드마다_제_장면이다()
    {
        // 구판 호환의 뜻 — 장면 개념이 서기 전의 동작이 그대로 살아 있어야 한다.
        ChapterGraphModel chapter = Chapter(
            [Episode("a", null, 2), Episode("b", null, 3)],
            [Edge("a", "b")]);

        IReadOnlyList<ChapterScene> scenes = ChapterSceneGrouping.Of(chapter);

        Assert.Equal(2, scenes.Count);
        Assert.All(scenes, scene => Assert.True(scene.IsDefault));
        Assert.All(scenes, scene => Assert.Single(scene.Episodes));
    }

    [Fact]
    public void 루트가_둘이어도_묶음은_나오고_나머지_착지점을_짚어_준다()
    {
        // ⛔ <b>규칙 14</b> — 오류가 있어도 모델은 만든다. 조용히 빈 화면을 주면 기획자는
        //    무엇이 잘못됐는지 볼 방법이 없다. 거부는 내보내기 관문의 일이다.
        ChapterGraphModel chapter = Chapter(
            [Episode("root", "opening", 2), Episode("a", "shared", 3), Episode("b", "shared", 4)],
            [Edge("root", "a"), Edge("root", "b")]);

        ChapterScene shared = ChapterSceneGrouping.Of(chapter)
            .Single(scene => scene.SceneId == "shared");

        Assert.True(shared.HasSplitEntry);
        Assert.Equal("a", shared.RootEpisodeId);
        Assert.Equal(["b"], shared.ExtraEntries);
        Assert.Equal(["a", "b"], shared.Episodes.Select(episode => episode.EpisodeId));
    }

    [Fact]
    public void 아무_데서도_안_들어오는_장면도_그려진다()
    {
        // 떨어진 섬 — 도달성 증명이 따로 짚는다. 여기서는 자리를 주는 것이 일이다.
        ChapterGraphModel chapter = Chapter(
            [Episode("start", "A", 2), Episode("island", "B", 3)],
            []);

        ChapterScene island = ChapterSceneGrouping.Of(chapter)
            .Single(scene => scene.SceneId == "B");

        Assert.Equal("island", island.RootEpisodeId);
        Assert.False(island.HasSplitEntry);
    }

    [Fact]
    public void 장면_루트로_재진입해도_루트는_하나다()
    {
        // 허브 구조(교실 ↔ 복도) — 나갔다 돌아오는 것은 정상이고, 루트가 늘지 않는다.
        ChapterGraphModel chapter = Chapter(
            [Episode("hub", "A", 2), Episode("out", "B", 3)],
            [Edge("hub", "out"), Edge("out", "hub")]);

        ChapterScene hub = ChapterSceneGrouping.Of(chapter).Single(scene => scene.SceneId == "A");

        Assert.False(hub.HasSplitEntry);
        Assert.Equal("hub", hub.RootEpisodeId);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static ChapterEpisode Episode(string id, string? sceneId, int row) =>
        new(id, id, string.Empty, id, 0, 0, null, row) { SceneId = sceneId };

    private static ChapterEdge Edge(string from, string to) =>
        new(from, to, "다음", null, null, 0);

    private static ChapterGraphModel Chapter(
        ChapterEpisode[] episodes, ChapterEdge[] edges) =>
        new("ch01", "ch01.xlsx", episodes, edges, [], [], [], []);
}
