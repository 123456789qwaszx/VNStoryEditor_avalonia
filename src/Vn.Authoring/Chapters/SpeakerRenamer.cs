using Vn.Authoring.Editing;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 화자 개명 한 판의 결과.
/// </summary>
/// <param name="Applied">개명을 실제로 시작했는가. false면 <b>아무것도 건드리지 않았다</b>.</param>
/// <param name="WorkbookCells">대본 워크북에서 바꾼 화자 칸 수.</param>
/// <param name="WorkbookFiles">한 칸이라도 바뀐 워크북 수.</param>
/// <param name="ScriptLines">프로젝트의 대본 줄에서 바꾼 수(모든 locale 합계).</param>
/// <param name="Blocked">
/// 쓰지 못한 워크북의 사유. <paramref name="Applied"/>가 false면 <b>시작 전에</b> 막힌
/// 것이고(파일 무접촉), true인데 비어 있지 않으면 시작한 뒤에 막힌 것이다(그 파일만 옛 이름).
/// </param>
public sealed record SpeakerRenameOutcome(
    bool Applied,
    int WorkbookCells,
    int WorkbookFiles,
    int ScriptLines,
    IReadOnlyList<string> Blocked);

/// <summary>
/// <b>화자 개명이 참조를 끌고 간다</b> (2026-08-24 소유자: "화자의 이름을 편집한 경우도
/// 연결이 계속 이어지도록").
///
/// 화자의 신원은 <b>[화자] 탭 목록의 그 줄</b>이고 이름은 그것의 표시다. 그런데 이름을 붙드는
/// 쪽 — 대본 엑셀의 화자 칸(E열)과 프로젝트 대본의 <c>LocalizedLine.Speaker</c> — 은 <b>문자열
/// 하나</b>뿐이다. 등록부에서만 갈면 그 줄들이 전부 미등록이 되어 초상화 매핑이 끊기고, 공백
/// 있는 이름은 파서가 산문으로 읽어 대사와 합쳐지기까지 한다(2026-08-23 실사례).
///
/// <b>순서가 규격이다</b> — 워크북이 먼저, 등록부가 나중이다. 2026-08-15 에피소드 개명이 배운
/// 것과 같다: 남의 파일을 못 고치면 <b>시작도 하지 않는다</b>. 반대로 등록부를 먼저 갈면,
/// 엑셀이 잡고 있어 못 고친 워크북이 다음 동기화에서 "미등록 화자"로 되살아난다.
///
/// <b>⚠ locale은 각자다</b> (로컬라이징 대비 — <see cref="ProjectEditor.RenameSpeaker"/>).
/// </summary>
public static class SpeakerRenamer
{
    /// <summary>
    /// 프로젝트 전체에서 화자 이름을 갈아 끼운다. 워크북 → 프로젝트 대본 순서로 돌고,
    /// <b>등록부(`game.definition.json`)는 부르는 쪽이 저장한다</b> — 이 함수가 성공을
    /// 돌려준 뒤에만.
    /// </summary>
    /// <param name="chapters">현재 읽힌 챕터들. 대본 워크북의 자리는 여기서 나온다.</param>
    public static SpeakerRenameOutcome Rename(
        ProjectEditor editor,
        string? projectPath,
        IReadOnlyList<ChapterEntry> chapters,
        string oldName,
        string newName)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(chapters);

        string from = (oldName ?? string.Empty).Trim();
        string to = (newName ?? string.Empty).Trim();

        if (from.Length == 0 || to.Length == 0 ||
            string.Equals(from, to, StringComparison.Ordinal))
        {
            return new SpeakerRenameOutcome(true, 0, 0, 0, Array.Empty<string>());
        }

        IReadOnlyList<string> workbooks = EpisodeWorkbooks(projectPath, chapters);

        // ⛔ 시작 전에 전부 닿을 수 있는지 본다. 하나라도 막혀 있으면 <b>한 칸도</b> 고치지
        //    않는다 — 반쯤 개명된 프로젝트에는 되돌릴 손잡이가 없다.
        //
        // 못 읽은 챕터도 막는다: 그 챕터의 에피소드 목록을 모르므로 <b>대본이 몇 개 남았는지
        // 조차</b> 말할 수 없다. 조용히 건너뛰면 그 대본들만 옛 이름으로 남아, 다음 동기화가
        // "미등록 화자"를 뿜을 때까지 아무도 모른다.
        List<string> locked = chapters
            .Where(entry => entry.Model is null)
            .Select(entry =>
                $"챕터 '{entry.ChapterId}'을 읽지 못해 화자 이름을 바꾸지 않았습니다 — " +
                (entry.OpenFailure ?? "그 워크북을 열 수 없습니다") +
                " 그 챕터의 대본이 옛 이름으로 남으면 미등록 화자가 됩니다.")
            .Concat(workbooks
                .Where(ChapterWorkbookWriter.IsLockedByAnotherApp)
                .Select(path =>
                    $"엑셀이 '{Path.GetFileName(path)}'를 열고 있어 화자 이름을 바꾸지 못합니다 — " +
                    "엑셀에서 그 파일을 닫고 다시 시도해 주세요."))
            .ToList();

        if (locked.Count > 0)
        {
            return new SpeakerRenameOutcome(false, 0, 0, 0, locked);
        }

        int cells = 0;
        int files = 0;
        var blocked = new List<string>();

        foreach (string path in workbooks)
        {
            (ChapterWriteResult result, int changed) =
                EpisodeWorkbookWriter.RenameSpeaker(path, from, to);

            if (!result.Written)
            {
                // 빗장을 지난 뒤에 잠긴 경우다(사람이 그 사이에 엑셀로 열었다). 여기서
                // 멈추면 이미 고친 파일들이 갈 곳을 잃으므로, 끝까지 돌고 무엇이 남았는지
                // 정확히 말한다 — 남은 칸은 다음 동기화가 "미등록 화자"로 짚어 준다.
                blocked.Add(result.Failure ?? $"'{Path.GetFileName(path)}'에 쓰지 못했습니다.");
                continue;
            }

            if (changed > 0)
            {
                cells += changed;
                files++;
            }
        }

        int lines = editor.RenameSpeaker(from, to);

        return new SpeakerRenameOutcome(true, cells, files, lines, blocked);
    }

    /// <summary>
    /// 이 프로젝트의 대본 워크북 자리들. 어휘 밀어 넣기(`PushVocabulary`)와 <b>같은 목록</b>을
    /// 본다 — 두 곳이 서로 다른 파일 집합을 돌면 드롭다운과 셀이 어긋난다.
    /// </summary>
    private static IReadOnlyList<string> EpisodeWorkbooks(
        string? projectPath,
        IReadOnlyList<ChapterEntry> chapters)
    {
        var paths = new List<string>();

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return paths;
        }

        foreach (ChapterEntry entry in chapters)
        {
            if (entry.Model is not { } model ||
                EpisodeLibrary.FolderFor(projectPath, entry.ChapterId) is not { } folder)
            {
                continue;
            }

            foreach (ChapterEpisode episode in model.Episodes)
            {
                if (EpisodeLibrary.FindExisting(folder, episode.EpisodeId) is { } path)
                {
                    paths.Add(path);
                }
            }
        }

        return paths;
    }
}
