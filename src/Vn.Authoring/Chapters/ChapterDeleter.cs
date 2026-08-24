using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 챕터 제거의 <b>유일한 자리</b> (2026-08-25 소유자: "챕터를 만들 수는 있는데, 삭제할
/// 방법이 없어 … 당연하지만, 그렇게 하면 연출그래프에 있던 것도 모두 자동으로 제거되도록").
///
/// ⛔ <b>예전에는 이 기능을 일부러 두지 않았다</b>(<see cref="ChapterRenamer"/>의 옛 주석) —
/// 워크북 삭제는 되돌릴 수 없으니 사람이 폴더에서 지우라는 것이었다. 그런데 그 길은
/// <b>절반만 지운다</b>: 파일은 사라지는데 판(<c>StoryFile</c>)과 그 위의 노드는 프로젝트에
/// 그대로 남아, 이름이 겹치는 번들과 "부르는 노드가 YarnProject에 없다"로 뒤늦게 터졌다
/// (2026-08-25에 유령 노드로 하루를 썼다). 사람이 손으로 지울 수 없는 쪽이 남는 것이
/// 문제였으므로, 답은 기능을 막는 것이 아니라 <b>네 자리를 함께 걷는 것</b>이다.
///
/// 개명과 같은 넷을 본다:
///
/// <list type="number">
///   <item><c>chapters/{Id}.xlsx</c> — 챕터 워크북</item>
///   <item><c>episodes/{Id}/</c> — 그 챕터의 대본 폴더</item>
///   <item>판(<c>StoryFile</c>)과 그 위의 노드 전부 — 챕터 = 판 1:1 (G-1 v2)</item>
///   <item>조건 공급 설정노드 <c>챕터 {Id} 조건</c> — 그 판 위에 서므로 ③에 딸려 걷힌다</item>
/// </list>
///
/// <b>원고는 지우지 않고 <c>.bak</c>으로 밀어 둔다.</b> 이 저장소에서 지우는 종류의 작업은
/// 늘 직전 상태를 같은 이름의 <c>.bak</c>으로 남긴다(되돌리기가 없기 때문이다). 챕터 하나는
/// 몇 달치 원고일 수 있어서 더더욱 그렇다 — 사람이 확인한 뒤 <c>.bak</c>을 지우면 된다.
/// 판 쪽은 편집기의 되돌리기 한 번으로 통째로 돌아온다(<see cref="ProjectEditor.RemoveStoryFile"/>).
///
/// <b>반쯤 지운 챕터를 남기지 않는다.</b> 막을 것은 파일에 손대기 <b>전에</b> 전부 보고,
/// 대본 폴더를 못 밀면 워크북을 되돌린다 — 워크북은 없는데 원고는 남은 상태가 가장 나쁘다.
/// </summary>
public static class ChapterDeleter
{
    /// <param name="WorkbookBackup">워크북이 밀려간 <c>.bak</c> 파일 이름.</param>
    /// <param name="EpisodesBackup">대본 폴더가 밀려간 <c>.bak</c> 폴더 이름(없었으면 null).</param>
    /// <param name="NodesRemoved">판과 함께 걷힌 노드 수 — 연출 그래프에서 사라지는 것이다.</param>
    /// <param name="StaleExport">
    /// 그 챕터로 내보낸 진행 JSON이 남아 있으면 그 파일 이름. <b>지우지 않는다</b> —
    /// 산출물이라도 남의 폴더(개발자가 가져가는 자리)에 있는 파일이다. 대신 <b>이름을 말해</b>
    /// 사람이 지우게 한다: 그냥 두면 런타임이 <b>없는 챕터를 계속 싣는다</b>.
    /// </param>
    public sealed record Result(
        bool Deleted,
        string? Failure,
        string? WorkbookBackup = null,
        string? EpisodesBackup = null,
        int NodesRemoved = 0,
        string? StaleExport = null)
    {
        public static Result Fail(string reason) => new(false, reason);
    }

    /// <summary>지우는 종류의 작업이 직전 상태를 남기는 꼬리표 — 저장소 공통 규약이다.</summary>
    public const string BackupSuffix = ".bak";

    /// <summary>
    /// 챕터 하나를 걷는다 — 워크북·대본 폴더는 <c>.bak</c>으로 밀고, 판과 그 위의 노드는
    /// 프로젝트에서 제거한다.
    /// </summary>
    /// <param name="projectManifestPath">
    /// 프로젝트 파일 경로. <c>chapters/</c>와 <c>episodes/</c>가 여기서 갈라진다 —
    /// 폴더 둘을 따로 받으면 서로 다른 프로젝트를 섞을 수 있다.
    /// </param>
    public static Result Delete(
        ProjectEditor editor, string? projectManifestPath, string chapterId)
    {
        ArgumentNullException.ThrowIfNull(editor);

        chapterId = (chapterId ?? string.Empty).Trim();

        if (chapterId.Length == 0)
        {
            return Result.Fail("챕터 이름이 비어 있습니다.");
        }

        if (ChapterLibrary.FolderFor(projectManifestPath) is not { } chapters)
        {
            return Result.Fail("프로젝트를 먼저 저장해야 합니다.");
        }

        StoryFile? board = editor.Project.Files.FirstOrDefault(file =>
            string.Equals(file.Name, chapterId, StringComparison.Ordinal));

        // ⚠ 막을 것을 <b>파일에 손대기 전에</b> 본다. 워크북을 밀고 나서 "판을 못 지운다"를
        //    만나면, 파일은 사라졌는데 연출 그래프에는 그대로 남는 — 이 기능이 고치려던
        //    바로 그 상태가 된다.
        if (board is not null && editor.Project.Files.Count <= 1)
        {
            return Result.Fail(
                $"'{chapterId}'는 마지막 판이라 지울 수 없습니다 — 새 노드가 갈 자리가 " +
                "없어집니다. 다른 챕터를 하나 만든 뒤에 지워 주세요.");
        }

        // .xlsm도 챕터다(구글 시트가 그렇게 저장하는 실사례가 있다) — 읽을 때 받아 준
        // 것을 지울 때 못 찾으면, 목록에는 보이는데 안 지워지는 챕터가 된다.
        //
        // ⚠ <b>확장자를 다시 본다.</b> `ch05.xls*`는 지난번에 남긴 `ch05.xlsx.bak`도 잡는데,
        //    열거 순서는 정해져 있지 않다. 그것을 집으면 <c>.bak.bak</c>을 만들고 정작
        //    원고는 그 자리에 남아 — <b>지웠다고 말한 챕터가 다음 새로고침에 되살아난다</b>.
        string? workbook = Directory.Exists(chapters)
            ? Directory.EnumerateFiles(chapters, chapterId + ".xls*")
                .FirstOrDefault(candidate => Path.GetExtension(candidate) is { } extension &&
                    (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase)))
            : null;

        if (workbook is null && board is null)
        {
            return Result.Fail($"챕터 '{chapterId}'를 찾지 못했습니다.");
        }

        string? workbookBackup = null;

        if (workbook is not null)
        {
            try
            {
                string target = workbook + BackupSuffix;
                File.Move(workbook, target, overwrite: true);
                workbookBackup = Path.GetFileName(target);
            }
            catch (Exception exception)
            {
                return Result.Fail(
                    $"챕터 워크북을 치우지 못했습니다(엑셀이 열고 있을 수 있습니다): " +
                    $"{exception.Message}");
            }
        }

        string? episodesBackup;

        if (TryArchiveEpisodes(projectManifestPath, chapterId, out episodesBackup) is { } failure)
        {
            // ⛔ 되돌린다. 워크북만 사라지면 그 챕터의 원고는 폴더에 살아 있는데 툴에서는
            //    보이지 않는다 — 사람이 잃은 줄 알게 되는 상태다.
            if (workbook is not null && workbookBackup is not null)
            {
                TryRestore(Path.Combine(chapters, workbookBackup), workbook);
            }

            return Result.Fail(failure);
        }

        int nodesRemoved = 0;

        if (board is not null)
        {
            // 판이 걷히면 그 위의 노드가 전부 걷힌다 — 에피소드 노드도, 자유 씬도,
            // 조건 공급 배관(`챕터 {Id} 조건`)도 이 판에 서 있다. 그것이 소유자가 말한
            // "연출그래프에 있던 것도 모두 자동으로 제거"다.
            nodesRemoved = board.Nodes.Count;
            editor.RemoveStoryFile(board.Id);
        }

        string stale = ChapterExportService.ExportPathFor(projectManifestPath!, chapterId);

        return new Result(
            true,
            null,
            workbookBackup,
            episodesBackup,
            nodesRemoved,
            File.Exists(stale) ? Path.GetFileName(stale) : null);
    }

    /// <summary>
    /// 대본 폴더를 <c>{Id}.bak/</c>으로 민다. 성공이면 null, 실패면 사유다.
    /// 이미 있던 <c>.bak</c>은 갈린다 — <c>.bak</c>은 <b>직전</b> 상태를 담는 자리이지
    /// 이력을 쌓는 자리가 아니다(이 저장소의 다른 <c>.bak</c>과 같은 규칙이다).
    /// </summary>
    private static string? TryArchiveEpisodes(
        string? projectManifestPath, string chapterId, out string? backupName)
    {
        backupName = null;

        if (EpisodeLibrary.FolderFor(projectManifestPath, chapterId) is not { } folder ||
            !Directory.Exists(folder))
        {
            return null;
        }

        string target = folder + BackupSuffix;

        try
        {
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.Move(folder, target);
            backupName = Path.GetFileName(target);
            return null;
        }
        catch (Exception exception)
        {
            return $"대본 폴더를 치우지 못해 챕터를 지우지 않았습니다(엑셀이 대본을 열고 " +
                   $"있을 수 있습니다): {exception.Message}";
        }
    }

    /// <summary>되돌리기의 되돌리기 — 여기서 또 실패하면 말할 것이 없으므로 조용히 넘긴다.</summary>
    private static void TryRestore(string backup, string original)
    {
        try
        {
            File.Move(backup, original, overwrite: true);
        }
        catch
        {
            // 사유는 이미 위에서 사람에게 갔다. 여기서 덮어쓰면 원인이 가려진다.
        }
    }
}
