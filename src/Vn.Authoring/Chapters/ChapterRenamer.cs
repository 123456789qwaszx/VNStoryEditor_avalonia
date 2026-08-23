using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 챕터 개명의 <b>유일한 자리</b> (2026-08-24 소유자 보고: "챕터의 이름을 바꾸니까,
/// 에피소드들의 대화가 없다고 나오고, 엑셀도 새로운게 열리네. 또 연출그래프에서도
/// 조건노드가 이전의 챕터를 받고 있어").
///
/// <b>무엇이 잘못됐었나</b> — 개명이 <b>워크북 파일 하나만</b> 옮겼다. 그런데 챕터 Id를
/// 이름에 지고 있는 것은 그것 말고도 셋이 더 있다:
///
/// <list type="number">
///   <item><c>chapters/{Id}.xlsx</c> — 챕터 워크북</item>
///   <item><c>episodes/{Id}/</c> — <b>그 챕터의 대본 폴더</b> (2026-08-16에 챕터별로 갈렸다)</item>
///   <item>판(<c>StoryFile.Name</c>) — 챕터 = 판 1:1 (G-1 v2)</item>
///   <item>조건 공급 설정노드 <c>챕터 {Id} 조건</c> — A계층 배관</item>
/// </list>
///
/// ②를 안 옮기니 새 이름의 폴더가 <b>비어 있고</b>, 툴은 "대사가 없다"고 말하며 노드를
/// 더블클릭하면 <b>빈 워크북을 새로 만들어</b> 열었다 — 원고는 옛 폴더에 그대로 살아 있는데.
/// ④를 안 바꾸니 옛 이름의 배관이 판에 남고, 다음 동기화가 새 이름으로 <b>하나 더</b>
/// 만들어 조건이 둘로 갈렸다.
///
/// <b>그래서 이 클래스가 생겼다.</b> 개명은 네 자리를 <b>함께</b> 옮기는 한 동작이고,
/// 그 동작의 주인은 하나여야 한다 — 예전에는 파일 이동이 <see cref="ChapterWorkbookWriter"/>에,
/// 판 이름이 셸에 있었고, 갈라져 있었기 때문에 나머지 둘을 아무도 챙기지 않았다.
///
/// <b>반쯤 바뀐 챕터를 남기지 않는다.</b> 파일 이동이 둘이라 뒤엣것이 실패할 수 있다
/// (엑셀이 대본을 잡고 있으면 폴더가 안 옮겨진다). 그때는 앞엣것을 <b>되돌리고</b>
/// 사유만 돌려준다 — 워크북은 새 이름인데 원고는 옛 이름인 상태가 가장 나쁘다.
/// </summary>
public static class ChapterRenamer
{
    /// <param name="EpisodesMoved">대본 폴더가 실제로 옮겨졌으면 참(없었으면 거짓).</param>
    /// <param name="SupplyRenamed">조건 공급 노드의 이름이 따라갔으면 참.</param>
    /// <param name="StaleExport">
    /// 옛 이름으로 내보낸 진행 JSON이 남아 있으면 그 파일 이름. <b>지우지 않는다</b> —
    /// 산출물이라도 남의 폴더에 있는 파일이고, 되돌릴 수 없는 삭제는 툴이 대신하지 않는다
    /// (챕터 삭제를 안 두는 것과 같은 이유다). 대신 <b>이름을 말해</b> 사람이 지우게 한다:
    /// 그냥 두면 런타임이 없는 챕터를 계속 싣는다.
    /// </param>
    public sealed record Result(
        bool Renamed,
        string? Failure,
        bool EpisodesMoved = false,
        bool SupplyRenamed = false,
        string? StaleExport = null)
    {
        public static Result Fail(string reason) => new(false, reason);
    }

    /// <summary>
    /// 챕터 Id를 바꾸고 그 이름을 지고 있는 것 넷을 함께 옮긴다.
    /// </summary>
    /// <param name="projectManifestPath">
    /// 프로젝트 파일 경로. <c>chapters/</c>와 <c>episodes/</c>가 여기서 갈라진다 —
    /// 폴더 둘을 따로 받으면 서로 다른 프로젝트를 섞을 수 있다.
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

        if (ChapterLibrary.FolderFor(projectManifestPath) is not { } chapters)
        {
            return Result.Fail("프로젝트를 먼저 저장해야 합니다.");
        }

        string? episodesOld = EpisodeLibrary.FolderFor(projectManifestPath, oldId);
        string? episodesNew = EpisodeLibrary.FolderFor(projectManifestPath, newId);

        // ⚠ 옮기기 <b>전에</b> 막을 것을 전부 본다. 절반쯤 옮기고 나서 "이미 있습니다"를
        // 만나면 되돌릴 것이 늘어난다.
        if (episodesOld is not null && episodesNew is not null &&
            Directory.Exists(episodesOld) && Directory.Exists(episodesNew))
        {
            return Result.Fail(
                $"'{newId}'의 대본 폴더가 이미 있습니다 — 옛 원고를 덮어쓸 수 없어 멈췄습니다. " +
                "그 폴더를 먼저 치우거나 다른 이름을 골라 주세요.");
        }

        ChapterWriteResult moved = ChapterWorkbookWriter.RenameChapterWorkbook(chapters, oldId, newId);

        if (!moved.Written)
        {
            return Result.Fail(moved.Failure!);
        }

        bool episodesMoved = false;

        if (episodesOld is not null && episodesNew is not null && Directory.Exists(episodesOld))
        {
            try
            {
                Directory.Move(episodesOld, episodesNew);
                episodesMoved = true;
            }
            catch (Exception exception)
            {
                // ⛔ 되돌린다. 워크북만 새 이름이 되면 그 챕터는 "대사가 하나도 없는 챕터"로
                // 보이고, 노드를 열면 빈 워크북이 새로 생겨 원고가 둘로 갈린다 — 이 건의
                // 원래 증상 그대로다.
                ChapterWorkbookWriter.RenameChapterWorkbook(chapters, newId, oldId);

                return Result.Fail(
                    $"대본 폴더를 옮기지 못해 이름을 되돌렸습니다(엑셀이 대본을 열고 있을 수 " +
                    $"있습니다): {exception.Message}");
            }
        }

        // 판이 따라간다 — 챕터 = 판 1:1. 판이 아직 없었다면 옮길 것도 없다.
        StoryFile? board = editor.Project.Files.FirstOrDefault(file =>
            string.Equals(file.Name, oldId, StringComparison.Ordinal));

        if (board is not null)
        {
            editor.RenameStoryFile(board.Id, newId);
        }

        string stale = ChapterExportService.ExportPathFor(projectManifestPath!, oldId);

        return new Result(
            true, null,
            episodesMoved,
            RenameConditionSupply(editor, oldId, newId),
            File.Exists(stale) ? Path.GetFileName(stale) : null);
    }

    /// <summary>
    /// A계층 조건 배관(<c>챕터 {Id} 조건</c>)의 이름을 따라 바꾼다.
    ///
    /// 안 바꾸면 옛 이름의 노드가 판에 남고, 다음 동기화가
    /// <see cref="EpisodeSyncService.ConditionSupplyNodeName"/>으로 <b>새것을 하나 더</b>
    /// 만든다. 그러면 같은 챕터의 조건이 노드 둘에 갈려 앉고, 작가 화면에는 옛 챕터의
    /// 라벨이 그대로 보인다(소유자 보고의 그 화면이다).
    ///
    /// ⚠ 새 이름의 배관이 이미 있으면 <b>손대지 않는다</b> — 이름이 겹치면 어느 쪽이 그
    /// 챕터의 배관인지 정해지지 않는다. 합치는 것은 조건을 옮기는 일이라 개명의 몫이 아니고,
    /// 그 상태에서도 동기화가 새 이름 쪽에 마저 공급하므로 조용히 깨지지는 않는다.
    /// </summary>
    private static bool RenameConditionSupply(ProjectEditor editor, string oldId, string newId)
    {
        string oldName = EpisodeSyncService.ConditionSupplyNodeName(oldId);
        string newName = EpisodeSyncService.ConditionSupplyNodeName(newId);

        List<SetNode> supplies = editor.Project.EnumerateNodes().OfType<SetNode>().ToList();

        if (supplies.Any(node => string.Equals(node.Name, newName, StringComparison.Ordinal)))
        {
            return false;
        }

        SetNode? supply = supplies.FirstOrDefault(node =>
            string.Equals(node.Name, oldName, StringComparison.Ordinal));

        if (supply is null)
        {
            return false;
        }

        editor.RenameNode(supply.Id, newName);
        return true;
    }
}
