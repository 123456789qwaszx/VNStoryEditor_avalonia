using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 화자 개명 한 판의 결과.
/// </summary>
/// <param name="ScriptLines">프로젝트의 대본 줄에서 바꾼 수(모든 locale 합계).</param>
/// <param name="WorkbooksWritten">그 줄을 가진 에피소드의 대본 워크북 중 다시 낸 수.</param>
/// <param name="Deferred">
/// 지금 다시 내지 못한 워크북의 사유(엑셀이 잡고 있음). <b>실패가 아니라 미룸이다</b> — 원본은
/// 프로젝트라 이름은 이미 바뀌었고, 못 한 것은 그 파일을 지금 쓰는 일뿐이다. 다음 저장이 다시 낸다.
/// </param>
public sealed record SpeakerRenameOutcome(
    int ScriptLines,
    int WorkbooksWritten,
    IReadOnlyList<string> Deferred);

/// <summary>
/// <b>화자 개명이 참조를 끌고 간다</b> (2026-08-24 소유자: "화자의 이름을 편집한 경우도
/// 연결이 계속 이어지도록").
///
/// 화자의 신원은 <b>[화자] 탭 목록의 그 줄</b>이고 이름은 그것의 표시다. 그런데 이름을 붙드는
/// 쪽 — 프로젝트 대본의 <c>LocalizedLine.Speaker</c> — 은 <b>문자열 하나</b>뿐이다. 등록부에서만
/// 갈면 그 줄들이 전부 미등록이 되어 초상화 매핑이 끊기고, 공백 있는 이름은 파서가 산문으로 읽어
/// 대사와 합쳐지기까지 한다(2026-08-23 실사례).
///
/// <b>엑셀을 고치지 않는다 — 다시 낸다</b> (2026-10-29 · `docs/plans/excel-boundary.md` C4). 그 전에는
/// 여기가 대본 워크북의 화자 칸을 <i>셀 단위로</i> 먼저 고치고(<c>EpisodeWorkbookWriter.RenameSpeaker</c>)
/// 프로젝트를 나중에 바꿨다 — 원본이 엑셀이던 시절의 순서다. 그 Writer는 머리글을 1행에서 찾는데
/// 이미터는 2행에 쓰므로, <b>툴이 낸 파일의 화자 개명은 실패하는 경로</b>였다. 이제 프로젝트의 줄을
/// 바꾸고 그 줄을 가진 에피소드의 워크북을 <b>통째로 다시 낸다</b>. 잠긴 파일은 거부가 아니라 미룸이다.
///
/// <b>⚠ locale은 각자다</b> (로컬라이징 대비 — <see cref="ProjectEditor.RenameSpeaker"/>).
/// </summary>
public static class SpeakerRenamer
{
    /// <summary>
    /// 프로젝트 전체에서 화자 이름을 갈아 끼우고, 바뀐 줄을 가진 에피소드의 워크북을 다시 낸다.
    /// <b>등록부(`game.definition.json`)는 부르는 쪽이 저장한다.</b>
    /// </summary>
    public static SpeakerRenameOutcome Rename(
        ProjectEditor editor,
        string? projectPath,
        string oldName,
        string newName)
    {
        ArgumentNullException.ThrowIfNull(editor);

        string from = (oldName ?? string.Empty).Trim();
        string to = (newName ?? string.Empty).Trim();

        if (from.Length == 0 || to.Length == 0 ||
            string.Equals(from, to, StringComparison.Ordinal))
        {
            return new SpeakerRenameOutcome(0, 0, Array.Empty<string>());
        }

        // 바뀔 줄을 가진 대본 — 개명 <b>전에</b> 센다. 뒤에 세면 옛 이름이 이미 없다.
        HashSet<string> touched = editor.Project.Scripts
            .Where(script => script.Locales.Any(locale => locale.Entries.Values
                .Any(line => string.Equals(line.Speaker, from, StringComparison.Ordinal))))
            .Select(script => script.Id)
            .ToHashSet(StringComparer.Ordinal);

        int lines = editor.RenameSpeaker(from, to);

        if (lines == 0)
        {
            return new SpeakerRenameOutcome(0, 0, Array.Empty<string>());
        }

        int written = 0;
        var deferred = new List<string>();

        // 그 대본을 읽는 에피소드 카드마다 워크북을 다시 낸다 — 판이 챕터의 것일 때만
        // (챕터 아닌 판의 카드는 진행에 안 실리고 워크북도 없다).
        foreach (DialogueNode node in editor.Project.EnumerateNodes().OfType<DialogueNode>())
        {
            if (node.ScriptId is not { } scriptId || !touched.Contains(scriptId) ||
                EpisodeNaming.ChapterOf(editor.Project, node) is not { } chapter ||
                EpisodeLibrary.FolderFor(projectPath, chapter.ChapterId) is not { } folder)
            {
                continue;
            }

            ChapterWriteResult result = EpisodeScriptOutput.Write(
                editor.Project, node, EpisodeLibrary.PathFor(folder, EpisodeNaming.EpisodeIdOf(node)));

            if (result.Written)
            {
                written++;
            }
            else
            {
                deferred.Add(result.Failure ?? "대본 워크북을 다시 내지 못했습니다.");
            }
        }

        return new SpeakerRenameOutcome(lines, written, deferred);
    }
}
