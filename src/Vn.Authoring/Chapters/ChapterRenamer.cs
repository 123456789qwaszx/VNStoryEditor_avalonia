using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 챕터 개명의 <b>유일한 자리</b> (2026-08-24 소유자 보고: "챕터의 이름을 바꾸니까,
/// 에피소드들의 대화가 없다고 나오고, 엑셀도 새로운게 열리네. 또 연출그래프에서도
/// 조건노드가 이전의 챕터를 받고 있어").
///
/// <b>순서가 규칙이다 — 모델 먼저, 파일은 뒤에</b> (2026-10-29 · `docs/plans/excel-boundary.md` C2).
/// 개명은 <see cref="ProjectEditor.RenameChapter"/> 한 번이다: <c>Project.Chapters</c>의 Id · 판 이름 ·
/// 조건 공급 노드 이름이 한 변경으로 바뀌고 되돌리기 한 번에 돌아온다. 그 전에는 여기가 워크북
/// 파일·대본 폴더·판·배관을 각각 옮기면서 정작 <b>챕터 모델의 Id는 안 바꿨다</b> — 저장하고 다시
/// 열면 챕터는 옛 이름, 판은 새 이름이었다.
///
/// <b>파일은 산출물이다.</b> <c>chapters/{Id}.xlsx</c>와 <c>episodes/{Id}/</c>는 프로젝트에서 다시
/// 낼 수 있으므로, 옮기는 것은 <i>친절</i>이지 개명의 성립 조건이 아니다. 못 옮기면(엑셀이 잡고
/// 있다 · 새 이름의 폴더가 이미 있다) 모델을 되돌리지 않고 <see cref="Result.FilesNotMoved"/>로
/// 사유를 말한다 — 다음 출력이 새 이름으로 내고, 옛 파일은 사람이 치운다. 덮어쓰거나 지우지는
/// 않는다(남의 폴더의 파일일 수 있다).
/// </summary>
public static class ChapterRenamer
{
    /// <param name="EpisodesMoved">대본 폴더가 실제로 옮겨졌으면 참(없었으면 거짓).</param>
    /// <param name="SupplyRenamed">조건 공급 노드의 이름이 따라갔으면 참.</param>
    /// <param name="StaleExport">
    /// 옛 이름으로 내보낸 진행 JSON이 남아 있으면 그 파일 이름. <b>지우지 않는다</b> —
    /// 산출물이라도 남의 폴더(개발자가 가져가는 자리)에 있는 파일이다. 대신 <b>이름을 말해</b>
    /// 사람이 지우게 한다: 그냥 두면 런타임이 없는 챕터를 계속 싣는다.
    /// <see cref="ChapterDeleter"/>도 같은 규칙을 쓴다.
    /// </param>
    /// <param name="FilesNotMoved">
    /// 워크북이나 대본 폴더를 새 이름으로 옮기지 못한 사유. 모델은 이미 바뀌었다 — 옛 이름의
    /// 파일이 그대로 남아 있고, 다음 출력이 새 이름으로 낸다.
    /// </param>
    public sealed record Result(
        bool Renamed,
        string? Failure,
        bool EpisodesMoved = false,
        bool SupplyRenamed = false,
        string? StaleExport = null,
        string? FilesNotMoved = null)
    {
        public static Result Fail(string reason) => new(false, reason);
    }

    /// <summary>
    /// 챕터 Id를 바꾸고(모델), 그 이름의 워크북·대본 폴더를 따라 옮긴다(파일, 되면).
    /// </summary>
    /// <param name="projectManifestPath">
    /// 프로젝트 파일 경로. <c>chapters/</c>와 <c>episodes/</c>가 여기서 갈라진다 —
    /// 폴더 둘을 따로 받으면 서로 다른 프로젝트를 섞을 수 있다. 아직 저장 안 한 프로젝트(null)면
    /// 옮길 파일이 없으므로 모델만 바꾼다.
    /// </param>
    public static Result Rename(
        ProjectEditor editor, string? projectManifestPath, string oldId, string newId)
    {
        ArgumentNullException.ThrowIfNull(editor);

        oldId = (oldId ?? string.Empty).Trim();
        newId = (newId ?? string.Empty).Trim();

        if (oldId.Length == 0 || newId.Length == 0)
        {
            return Result.Fail("챕터 이름이 비어 있습니다.");
        }

        if (string.Equals(oldId, newId, StringComparison.Ordinal))
        {
            return Result.Fail("이름이 같습니다.");
        }

        if (editor.FindChapter(oldId) is null)
        {
            return Result.Fail($"챕터 '{oldId}'가 프로젝트에 없습니다.");
        }

        if (editor.FindChapter(newId) is not null)
        {
            return Result.Fail($"챕터 '{newId}'가 이미 있습니다.");
        }

        // ① 모델 — 챕터 Id · 판 · 배관이 한 변경으로.
        ChapterRenameEffect effect = editor.RenameChapter(oldId, newId);

        // ② 파일 — 산출물을 따라 옮긴다. 못 옮겨도 ①은 그대로다.
        var notMoved = new List<string>();
        bool episodesMoved = false;

        if (ChapterLibrary.FolderFor(projectManifestPath) is { } chapters)
        {
            if (File.Exists(Path.Combine(chapters, oldId + ".xlsx")))
            {
                ChapterWriteResult moved = ChapterWorkbookWriter.RenameChapterWorkbook(chapters, oldId, newId);

                if (!moved.Written)
                {
                    notMoved.Add($"챕터 워크북을 옮기지 못했습니다: {moved.Failure}");
                }
            }

            string? episodesOld = EpisodeLibrary.FolderFor(projectManifestPath, oldId);
            string? episodesNew = EpisodeLibrary.FolderFor(projectManifestPath, newId);

            if (episodesOld is not null && episodesNew is not null && Directory.Exists(episodesOld))
            {
                if (Directory.Exists(episodesNew))
                {
                    // 덮어쓰지 않는다 — 그 폴더의 파일이 누구의 것인지 모른다.
                    notMoved.Add(
                        $"'{newId}'의 대본 폴더가 이미 있어 옛 폴더 '{oldId}'를 옮기지 않았습니다 — " +
                        "다음 저장이 새 이름으로 대본을 냅니다. 옛 폴더는 확인하고 치워 주세요.");
                }
                else
                {
                    try
                    {
                        Directory.Move(episodesOld, episodesNew);
                        episodesMoved = true;
                    }
                    catch (Exception exception)
                    {
                        notMoved.Add(
                            $"대본 폴더를 옮기지 못했습니다(엑셀이 대본을 열고 있을 수 있습니다): " +
                            $"{exception.Message} 다음 저장이 새 이름으로 대본을 냅니다.");
                    }
                }
            }
        }

        string? stale = projectManifestPath is null
            ? null
            : ChapterExportService.ExportPathFor(projectManifestPath, oldId);

        return new Result(
            true, null,
            episodesMoved,
            effect.SupplyRenamed,
            stale is not null && File.Exists(stale) ? Path.GetFileName(stale) : null,
            notMoved.Count == 0 ? null : string.Join(" ", notMoved));
    }
}
