namespace Vn.Authoring.Chapters;

/// <summary>
/// 챕터 안의 장면 하나 — <b>저장된 것이 아니라 묶어 본 것</b>이다.
/// </summary>
/// <param name="SceneId">
/// 비교의 열쇠 = <see cref="ChapterEpisode.EffectiveSceneId"/>. 빈 칸이면
/// <c>__scene_{EpisodeId}</c>가 들어 있다 — <b>사람에게 보이는 값이 아니다</b>
/// (<see cref="DisplayName"/>을 쓴다).
/// </param>
/// <param name="IsDefault">
/// 장면ID를 안 적어 퇴화한 장면인가. 참이면 <b>에피소드 하나가 곧 장면 하나</b>이고,
/// 그것이 장면 개념이 서기 전의 동작이다(구판 호환).
/// </param>
/// <param name="RootEpisodeId">
/// 밖에서 들어오는 자리. 롤백이 되돌아갈 곳과 이어하기가 재개할 곳이 여기다.
/// </param>
/// <param name="ExtraEntries">
/// ⛔ <b>규칙 위반의 증거</b> — 루트 말고 밖에서 또 들어오는 자리들. 비어 있어야 정상이다
/// (<c>ChapterInvariants.VerifySceneEntries</c>). 비어 있지 않아도 <b>묶음은 나온다</b> —
/// 화면이 짚어 줘야 사람이 고칠 자리를 안다.
/// </param>
public sealed record ChapterScene(
    string SceneId,
    string DisplayName,
    bool IsDefault,
    string RootEpisodeId,
    IReadOnlyList<ChapterEpisode> Episodes,
    IReadOnlyList<string> ExtraEntries)
{
    /// <summary>밖에서 들어오는 자리가 둘 이상인가 — 장면이 어디서 시작하는지가 흐려진 상태.</summary>
    public bool HasSplitEntry => ExtraEntries.Count > 0;
}

/// <summary>
/// <b>챕터를 장면으로 묶어 본다</b> (R6 · 2026-09-16, <c>docs/plans/R6.md</c>).
///
/// ⛔ <b>장면은 엔티티가 아니다.</b> 정본은 <see cref="ChapterEpisode.SceneId"/> 하나이고,
/// 런타임에도 저장되는 장면 표가 없다 — <c>SceneProgression</c>은 <b>실행할 때</b> 현재
/// 에피소드의 장면ID로 만들어진다. 이 클래스가 내는 것은 <b>투영</b>이라, 들고 있다가
/// 나중에 쓰면 안 된다(그 순간 정본이 둘이 된다). 화면이 그릴 때마다 부른다.
///
/// ⚠ <b>새 불변식을 만들지 않는다.</b> 루트 판정은 <see cref="ChapterGraphModel.IsSceneRoot"/>
/// 그대로이고, 그것은 코어의 <c>VerifySceneEntries</c>와 같은 규칙이다 — 챕터 시작도 밖에서
/// 들어오는 길로 세고, 장면 안 간선은 안 센다.
///
/// ⚠ <b>깨진 챕터에서도 묶음이 나온다.</b> 리더의 규율 그대로다 — <i>"오류가 있어도 모델은
/// 만든다, 조용히 빈 화면을 주지 않는다"</i>(규칙 14). 루트가 둘이면 첫 자리를 루트로 삼고
/// 나머지를 <see cref="ChapterScene.ExtraEntries"/>에 실어 화면이 짚게 한다.
/// </summary>
public static class ChapterSceneGrouping
{
    /// <summary>장면ID를 안 적은 장면의 이름 — 내부 발급값을 사람에게 보이지 않는다.</summary>
    public const string DefaultSceneName = "에피소드별 장면(기본)";

    /// <summary>
    /// 장면 순서는 <b>루트의 깊이</b>다 — 챕터 시작에서 먼 순서가 곧 이야기 순서다.
    /// 깊이가 같거나 도달할 수 없으면 <b>시트에 적힌 순서</b>로 안정 정렬한다
    /// (같은 챕터를 두 번 열었을 때 자리가 흔들리면 안 된다).
    /// </summary>
    public static IReadOnlyList<ChapterScene> Of(ChapterGraphModel chapter)
    {
        ArgumentNullException.ThrowIfNull(chapter);

        IReadOnlyDictionary<string, int> depths = ChapterBranchPlanner.Depths(chapter);

        // 시트 순서 = 사람이 정한 순서. 도달 못 하는 에피소드의 마지막 기준이 된다.
        var sheetOrder = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int index = 0; index < chapter.Episodes.Count; index++)
        {
            sheetOrder[chapter.Episodes[index].EpisodeId] = index;
        }

        int Depth(ChapterEpisode episode) =>
            depths.TryGetValue(episode.EpisodeId, out int value) ? value : int.MaxValue;

        int Order(ChapterEpisode episode) => sheetOrder[episode.EpisodeId];

        var scenes = new List<ChapterScene>();

        foreach (IGrouping<string, ChapterEpisode> group in chapter.Episodes
                     .GroupBy(episode => episode.EffectiveSceneId, StringComparer.Ordinal))
        {
            List<ChapterEpisode> ordered = group
                .OrderBy(Depth)
                .ThenBy(Order)
                .ToList();

            // 밖에서 들어오는 자리들. 정상이면 하나다.
            List<ChapterEpisode> entries = ordered
                .Where(chapter.IsSceneRoot)
                .ToList();

            // 아무 데서도 안 들어오는 장면 = 떨어진 섬이다. 도달성 증명이 따로 짚으므로
            // 여기서는 그리기만 한다 — 첫 에피소드를 자리로 삼는다.
            ChapterEpisode root = entries.FirstOrDefault() ?? ordered[0];

            // 루트를 맨 앞으로 — 장면이 어디서 시작하는지가 목록의 첫 줄이어야 한다.
            List<ChapterEpisode> episodes = ordered
                .OrderBy(episode => ReferenceEquals(episode, root) ? 0 : 1)
                .ToList();

            bool isDefault = string.IsNullOrWhiteSpace(
                group.First(episode =>
                    string.Equals(episode.EffectiveSceneId, group.Key, StringComparison.Ordinal)).SceneId);

            scenes.Add(new ChapterScene(
                group.Key,
                isDefault ? DefaultSceneName : group.Key,
                isDefault,
                root.EpisodeId,
                episodes,
                entries.Skip(1).Select(episode => episode.EpisodeId).ToList()));
        }

        return scenes
            .OrderBy(scene => Depth(Find(chapter, scene.RootEpisodeId)))
            .ThenBy(scene => Order(Find(chapter, scene.RootEpisodeId)))
            .ToList();
    }

    private static ChapterEpisode Find(ChapterGraphModel chapter, string episodeId) =>
        chapter.FindEpisode(episodeId)!;
}
