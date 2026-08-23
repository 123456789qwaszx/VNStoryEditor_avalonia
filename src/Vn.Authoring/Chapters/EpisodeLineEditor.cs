using Vn.Authoring.Model;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 엑셀노드의 대사 한 줄이 <b>어느 파일 어느 행에서 왔는가</b>, 그리고 거기에 되쓰는 길
/// (2026-08-24).
///
/// <b>왜 화면 밖에 있나</b> — 노드에서 엑셀 셀까지 가는 길은 네 걸음이고
/// (<c>ExcelEpisodeId</c> → 챕터 → 폴더 → 파일 → 인덱스), 그 걸음은 전부 저작의 지식이다.
/// 편집기 코드비하인드에 두면 다음 툴이 못 가져가고 화면 없이는 시험도 못 한다 —
/// M1에서 <see cref="EpisodeSyncRunner"/>를 꺼낸 것과 같은 이유다.
/// </summary>
/// <param name="WorkbookPath">그 에피소드의 대본 워크북.</param>
/// <param name="Index">A열 값 — 줄의 신원(G-5). 엑셀 행 번호가 아니다.</param>
public sealed record EpisodeLineTarget(string WorkbookPath, int Index);

/// <summary>
/// 연출 그래프에서 고친 대사를 <b>엑셀 셀로 곧장 내보낸다.</b>
///
/// ⛔ <b>노드를 먼저 고치지 않는다.</b> 워크북 쓰기가 성공한 뒤에만 화면의 값이 남을 자격이
/// 있다 — 엑셀이 그 파일을 잡고 있으면 쓰기가 거부되는데, 그때 노드만 고쳐 두면 화면과
/// 파일이 다른 말을 하고 <b>다음 동기화가 사람이 방금 쓴 글을 지운다.</b> 그 순서를
/// 뒤집는 것이 이 기능에서 가장 하기 쉬운 실수다.
/// </summary>
public static class EpisodeLineEditor
{
    /// <summary>
    /// 이 줄이 사는 엑셀 자리. <b>못 찾으면 null이고, 그러면 편집을 열어서는 안 된다</b> —
    /// 되쓸 곳이 없는 편집은 다음 동기화까지만 사는 거짓말이다.
    ///
    /// null이 되는 자리는 셋이다: ① 엑셀노드가 아니거나 ② 프로젝트를 아직 저장하지 않아
    /// 대본 폴더가 없거나 ③ 그 줄의 신원(<c>ExcelLineMap</c>)이 없다 — ③은 워크북에 실려
    /// 오지 않은 줄, 즉 툴이 만든 고아 줄이다.
    /// </summary>
    public static EpisodeLineTarget? Locate(
        StoryProject project, string? projectPath, DialogueNode node, string? lineId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(node);

        if (node.ExcelEpisodeId is not { } episodeId || string.IsNullOrEmpty(lineId))
        {
            return null;
        }

        // 챕터 = 판 1:1 (G-1 v2) — 그 판의 이름이 곧 ChapterId다. 같은 EpisodeId를 여러
        // 챕터가 쓸 수 있으므로(2026-08-16) 챕터를 거치지 않고 파일을 찾으면 안 된다.
        if (project.FindFileContainingNode(node.Id)?.Name is not { Length: > 0 } chapterId ||
            EpisodeLibrary.FolderFor(projectPath, chapterId) is not { } folder ||
            EpisodeLibrary.FindExisting(folder, episodeId) is not { } workbookPath)
        {
            return null;
        }

        // ExcelLineMap은 인덱스 → LineId다. 되쓰려면 그 반대가 필요하다.
        foreach ((int index, string mapped) in node.ExcelLineMap)
        {
            if (string.Equals(mapped, lineId, StringComparison.Ordinal))
            {
                return new EpisodeLineTarget(workbookPath, index);
            }
        }

        return null;
    }

    /// <summary>
    /// 고친 화자·내용을 그 줄의 엑셀 셀에 쓴다. 성공하면 호출자가 노드도 같은 값으로
    /// 맞춘다 — 둘이 같은 말을 하므로 다음 동기화는 아무것도 되돌리지 않는다.
    /// </summary>
    public static ChapterWriteResult Write(
        EpisodeLineTarget target, string? speaker, string? text)
    {
        ArgumentNullException.ThrowIfNull(target);

        return EpisodeWorkbookWriter.SetLine(target.WorkbookPath, target.Index, speaker, text);
    }
}
