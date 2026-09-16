using Vn.Authoring.Editing;

namespace Vn.Authoring.Chapters;

/// <summary>
/// <b>에피소드를 다른 챕터로 옮기고, 옛 자리의 대본 워크북을 민다</b>
/// (2026-09-16 소유자: <i>"옮길 때 옛 파일을 밀어줘"</i>).
///
/// ⛔ <b>왜 밀어야 하나</b>: 대본 워크북은 <c>episodes/{챕터}/{에피소드}.xlsx</c>에 살고
/// <b>산출물</b>이다(R-F). 챕터가 바뀌면 다음 저장이 새 자리에 내는데, 옛 자리의 파일은
/// 아무도 안 지운다 — 그대로 두면 <b>같은 에피소드의 원고가 두 폴더에 남고</b> 사람은 어느
/// 쪽이 지금 것인지 알 수 없다.
///
/// ⚠ <b>지우지 않고 민다.</b> 산출물이라도 사람이 열어 보던 파일이라, 이 저장소의 다른
/// 지우는 작업과 같은 규칙을 쓴다 — <c>.bak</c>은 <b>직전</b> 상태를 담는 자리이지 이력을
/// 쌓는 자리가 아니다(<see cref="ChapterDeleter"/>·<see cref="ChapterRenamer"/>).
///
/// ⚠ <b>파일이 먼저다</b> — <see cref="EpisodeRenamer"/>와 같은 차례다. 엑셀이 붙들고 있으면
/// 여기서 전부 멈춘다. 모델만 옮겨 놓고 파일에서 막히면, 원고가 옛 챕터 폴더에 남은 채
/// 에피소드는 새 챕터에 앉는 <b>어중간한 상태</b>가 된다.
/// </summary>
public static class EpisodeMover
{
    /// <param name="EdgesCut">챕터를 가로지르게 되어 걷힌 간선 수.</param>
    /// <param name="Archived">밀어 둔 워크북들의 <c>.bak</c> 이름.</param>
    public sealed record Result(
        bool Moved,
        string? Failure,
        int EdgesCut = 0,
        IReadOnlyList<string>? Archived = null)
    {
        public static Result Fail(string reason) => new(false, reason);

        public IReadOnlyList<string> Backups => Archived ?? [];
    }

    public static Result Move(
        ProjectEditor editor,
        string? projectManifestPath,
        string fromChapterId,
        IReadOnlyCollection<string> episodeIds,
        string toChapterId,
        string? sceneId = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(episodeIds);

        // 같은 챕터 안이면 파일이 있던 자리 그대로다 — 장면만 다시 긋는다.
        if (string.Equals(fromChapterId, toChapterId, StringComparison.Ordinal))
        {
            return new Result(
                true, null, editor.MoveEpisodesToChapter(fromChapterId, episodeIds, toChapterId, sceneId));
        }

        string? folder = EpisodeLibrary.FolderFor(projectManifestPath, fromChapterId);

        // 옮긴 파일들 — 되돌릴 때 쓴다. (민 자리, 원래 자리)
        var pushed = new List<(string Backup, string Origin)>();

        foreach (string episodeId in episodeIds)
        {
            if (folder is null || EpisodeLibrary.FindExisting(folder, episodeId) is not { } origin)
            {
                continue;
            }

            string backup = origin + ChapterDeleter.BackupSuffix;

            try
            {
                File.Move(origin, backup, overwrite: true);
                pushed.Add((backup, origin));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                Restore(pushed);

                return Result.Fail(
                    $"'{episodeId}'의 대본 파일을 치우지 못했습니다(엑셀이 열고 있을 수 있습니다): " +
                    exception.Message);
            }
        }

        int cut;

        try
        {
            cut = editor.MoveEpisodesToChapter(fromChapterId, episodeIds, toChapterId, sceneId);
        }
        catch (InvalidOperationException exception)
        {
            Restore(pushed);
            return Result.Fail(exception.Message);
        }

        return new Result(
            true, null, cut, pushed.Select(item => Path.GetFileName(item.Backup)).ToList());
    }

    private static void Restore(List<(string Backup, string Origin)> pushed)
    {
        foreach ((string backup, string origin) in pushed)
        {
            try
            {
                File.Move(backup, origin, overwrite: true);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // 되돌리기도 막혔다 — 원고는 `.bak`에 있고 이름이 사람에게 이미 닿는다.
            }
        }
    }
}
