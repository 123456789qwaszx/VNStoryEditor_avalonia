using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.Authoring.Chapters;

/// <summary>
/// <b>에피소드 Id를 바꾸고 그 이름을 지고 있는 것들을 함께 옮긴다</b>
/// (<see cref="ChapterRenamer"/>·<see cref="ChapterDeleter"/>와 같은 결).
///
/// ⛔ <b>왜 서비스로 나왔나</b> (2026-09-16): 이 규율이 <c>ChapterGraphView</c> 안에만
/// 있었는데 [대본] 탭 탐색기에서도 이름을 고치게 되면서 부르는 자리가 둘이 됐다. 사본을
/// 뜨면 <b>한쪽만 대본 파일을 옮기거나 한쪽만 노드를 따라가게</b> 되고, 그 어긋남은
/// 원고가 고아가 되는 모양으로 나타난다(2026-08-15 실사례가 그것이었다).
///
/// 함께 따라가는 것 넷:
/// <list type="bullet">
/// <item><b>간선·픽스처</b> — <see cref="ProjectEditor.RenameEpisode"/>가 한다.</item>
/// <item><b>대본 워크북</b> — <c>episodes/{챕터}/{Id}.xlsx</c>.</item>
/// <item><b>대사 노드</b> — 이름과 엑셀 표식. 새로 만들지 않고 이름만 바꾸므로 줄·연출·
///   행 신원(<c>ExcelLineMap</c>)이 전부 보존된다.</item>
/// <item><b>대사엔트리</b> — 규약(= Id)을 따르던 것만.</item>
/// </list>
/// </summary>
public static class EpisodeRenamer
{
    /// <param name="ScriptMoved">대본 워크북이 실제로 옮겨졌으면 참(없었으면 거짓).</param>
    /// <param name="NodeRenamed">연출 그래프의 대사 노드가 따라갔으면 참.</param>
    public sealed record Result(
        bool Renamed,
        string? Failure,
        bool ScriptMoved = false,
        bool NodeRenamed = false)
    {
        public static Result Fail(string reason) => new(false, reason);
    }

    public static Result Rename(
        ProjectEditor editor,
        string? projectManifestPath,
        string chapterId,
        string oldId,
        string newId)
    {
        ArgumentNullException.ThrowIfNull(editor);

        oldId = (oldId ?? string.Empty).Trim();
        newId = (newId ?? string.Empty).Trim();

        if (oldId.Length == 0 || newId.Length == 0 ||
            string.Equals(oldId, newId, StringComparison.Ordinal))
        {
            return Result.Fail("바뀐 이름이 없습니다.");
        }

        string? episodes = EpisodeLibrary.FolderFor(projectManifestPath, chapterId);

        // 새 이름의 대본 파일이 이미 있으면 <b>시작도 하지 않는다</b> — 챕터만 개명된 채
        // 원고가 옛 이름에 남는 어중간한 상태를 만들지 않는다.
        if (episodes is not null &&
            EpisodeLibrary.FindExisting(episodes, oldId) is not null &&
            EpisodeLibrary.FindExisting(episodes, newId) is not null)
        {
            return Result.Fail(
                $"'{newId}' 이름의 대본 파일이 이미 있어 개명하지 않았습니다. 파일을 먼저 정리해 주세요.");
        }

        // 대본 파일을 <b>먼저</b> 옮긴다 — 엑셀이 잠그고 있으면 여기서 전부 멈춘다. 챕터만
        // 개명된 채 원고가 옛 이름에 남으면, 새 이름을 여는 순간 빈 워크북이 생겨 원고가
        // 고아가 된다(실사례 2026-08-15: new02 원고가 남고 빈 rrr.xlsx가 생겼다).
        bool moved = false;

        if (episodes is not null && EpisodeLibrary.FindExisting(episodes, oldId) is not null)
        {
            if (EpisodeLibrary.RenameWorkbook(episodes, oldId, newId) is { } failure)
            {
                return Result.Fail($"개명하지 않았습니다 — {failure}");
            }

            moved = true;
        }

        try
        {
            editor.RenameEpisode(chapterId, oldId, newId);
        }
        catch (InvalidOperationException exception)
        {
            // 챕터 쪽이 막혔다(같은 Id·폐지된 cleared: 참조) — 옮긴 대본 파일을 되돌린다.
            if (moved && episodes is not null)
            {
                EpisodeLibrary.RenameWorkbook(episodes, newId, oldId);
            }

            return Result.Fail(exception.Message);
        }

        return new Result(true, null, moved, RenameNode(editor, chapterId, oldId, newId));
    }

    /// <summary>
    /// 대사 노드도 따라간다 — 규약(대사엔트리 = Id)을 따르던 노드만.
    ///
    /// ⚠ <b>이 챕터의 판에서만</b> 찾는다 (2026-08-25). 프로젝트 전체를 이름으로 훑으면
    /// 다른 챕터에 같은 Id가 있을 때 남의 노드를 개명한다 — 그쪽 판에서는 에피소드와
    /// 이름이 갈려 유령이 되고, 그 유령이 이름 중복으로 내보내기를 막는다.
    /// </summary>
    private static bool RenameNode(
        ProjectEditor editor, string chapterId, string oldId, string newId)
    {
        if (editor.Project.Files
                .FirstOrDefault(file => string.Equals(file.Name, chapterId, StringComparison.Ordinal))
                ?.Nodes.OfType<DialogueNode>()
                .FirstOrDefault(node =>
                    string.Equals(node.MarkedEpisodeId, oldId, StringComparison.Ordinal) ||
                    string.Equals(node.Name, oldId, StringComparison.Ordinal))
            is not { } node)
        {
            return false;
        }

        // 엑셀 표식도 함께 간다 — 옛 Id로 남으면 연출 그래프가 챕터 밖 노드로 보고 레일을 끊는다.
        if (node.MarkedEpisodeId is not null)
        {
            node.MarkedEpisodeId = newId;
        }

        editor.RenameNode(node.Id, newId);
        return true;
    }
}
