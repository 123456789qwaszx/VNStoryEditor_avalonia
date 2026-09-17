using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 에피소드 하나를 걷는 <b>유일한 자리</b> (2026-09-18).
///
/// ⛔ <b>2026-09-18까지 이 규율은 [챕터 그래프] 화면 안에 살았다.</b> 그래서 [대본] 탭에
/// 같은 기능을 붙이려면 <b>사본을 뜨는 수밖에</b> 없었고, 사본은 갈린다 — 바로 앞의
/// 「에피소드를 만드는 길」이 정확히 그렇게 갈려 있었다(화면 하나가 카드 만들기를 잊었다).
/// 그 일을 다시 하지 않으려고 먼저 꺼낸다.
///
/// <b>세 자리를 함께 본다.</b>
///
/// <list type="number">
///   <item><c>episodes/{챕터}/{에피소드}.xlsx</c> — 대본 워크북을 <c>.bak</c>으로 민다</item>
///   <item>챕터의 에피소드 행과 그것을 쓰는 간선·픽스처 참조 (<see cref="ProjectEditor.RemoveEpisode"/>)</item>
///   <item>판 위의 <b>카드</b> — 비어 있으면 지우고, 글이 있으면 떼어만 낸다</item>
/// </list>
///
/// <b>순서가 규격이다.</b> 파일을 <b>먼저</b> 민다 — 행은 지웠는데 원고가 폴더에 남으면
/// 지운 에피소드의 파일이 유물로 쌓인다(2026-08-26 소유자가 겪은 그 버그다). 그리고 행
/// 지우기가 실패하면 <b>민 파일을 되돌린다</b>: 행은 있는데 원고가 <c>.bak</c>인 반쪽이
/// 더 나쁘다.
///
/// ⚠ <b>글이 든 카드는 안 지운다.</b> 연출을 넣어 둔 카드를 툴이 임의로 지우면 되돌릴
/// 자리가 없다. 떼어 내면 그 에피소드를 더는 사칭하지 않고, 살릴지 지울지는 사람이 판에서
/// 정한다 (<see cref="ProjectEditor.DetachEpisodeMark"/>).
/// </summary>
public static class EpisodeDeleter
{
    /// <param name="WorkbookBackup">대본 워크북이 밀려간 <c>.bak</c> 파일 이름(없었으면 null).</param>
    /// <param name="CardRemoved">비어 있어 함께 지운 카드의 이름(안 지웠으면 null).</param>
    /// <param name="CardDetachedAs">글이 있어 떼어만 낸 카드의 <b>새 이름</b>(안 뗐으면 null).</param>
    public sealed record Result(
        bool Ok,
        string? Failure,
        string? WorkbookBackup = null,
        string? CardRemoved = null,
        string? CardDetachedAs = null)
    {
        public static Result Fail(string reason) => new(false, reason);

        /// <summary>사람에게 할 말 — 화면이 제 사정(내보내기 결과)을 뒤에 붙인다.</summary>
        public string Describe(string episodeId)
        {
            string text = $"'{episodeId}'과 그 간선·픽스처 참조를 지웠습니다.";

            if (WorkbookBackup is { } backup)
            {
                text += $" 대본 파일은 {System.IO.Path.GetFileName(backup)}으로 밀어 두었습니다.";
            }

            if (CardRemoved is { } removed)
            {
                text += $" 연출 그래프의 빈 노드 '{removed}'도 함께 지웠습니다.";
            }
            else if (CardDetachedAs is { } detached)
            {
                text += $" ⚠ 연출 그래프의 카드는 내용이 있어 '{detached}'으로 떼어 냈습니다 — " +
                    "살릴지 지울지는 판에서 정해 주세요.";
            }

            return text;
        }
    }

    /// <param name="projectManifestPath">
    /// 프로젝트 파일 경로. 없으면(아직 저장 안 함) 대본 워크북 단계를 건너뛴다 — 밀 파일이 없다.
    /// </param>
    public static Result Delete(
        ProjectEditor editor, string? projectManifestPath, string chapterId, string episodeId)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (string.IsNullOrWhiteSpace(episodeId))
        {
            return Result.Fail("지울 에피소드 이름이 비어 있습니다.");
        }

        if (editor.FindChapter(chapterId) is null)
        {
            return Result.Fail($"챕터 '{chapterId}'를 찾지 못했습니다.");
        }

        // ① 원고부터 민다 — 엑셀이 붙들고 있으면 여기서 멈춘다(행은 안 건드린다).
        (string? backup, string? original, string? failure) =
            EpisodeLibrary.FolderFor(projectManifestPath, chapterId) is { } folder
                ? EpisodeLibrary.ArchiveWorkbook(folder, episodeId)
                : (null, null, null);

        if (failure is not null)
        {
            return Result.Fail(failure);
        }

        // ② 행과 그것을 쓰는 것들.
        try
        {
            editor.RemoveEpisode(chapterId, episodeId);
        }
        catch when (Restore(backup, original))
        {
            throw;   // `Restore`는 언제나 false다 — 되돌려 놓고 예외는 그대로 올린다.
        }

        // ③ 카드.
        (string? removed, string? detached) = TakeCard(editor, chapterId, episodeId);

        return new Result(true, null, backup, removed, detached);
    }

    /// <summary>
    /// 그 에피소드의 카드를 거둔다 — 비었으면 지우고, 글이 있으면 떼어만 낸다.
    ///
    /// ⚠ <b>이 챕터의 판에서만</b> 찾는다. 프로젝트 전체를 이름으로 훑으면 다른 챕터에 같은
    /// Id가 있을 때 남의 카드를 건드린다.
    /// </summary>
    private static (string? Removed, string? DetachedAs) TakeCard(
        ProjectEditor editor, string chapterId, string episodeId)
    {
        StoryFile? board = editor.Project.Files.FirstOrDefault(file =>
            string.Equals(file.Name, chapterId, StringComparison.Ordinal));

        if (board?.Nodes.OfType<DialogueNode>().FirstOrDefault(node =>
                string.Equals(EpisodeNaming.EpisodeIdOf(node), episodeId, StringComparison.Ordinal))
            is not { } card)
        {
            return (null, null);
        }

        if (NothingWritten(editor.Project, card))
        {
            string name = card.Name;
            editor.RemoveNode(card.Id);

            return (name, null);
        }

        return (null, editor.DetachEpisodeMark(card.Id));
    }

    /// <summary>
    /// 이 카드에 <b>지킬 글이 하나도 없는가</b>.
    ///
    /// ⛔ <b>"줄이 있는가"로 재면 안 된다.</b> 갓 만든 카드는 빈 줄 하나를 달고 태어나므로
    /// (<c>NewDialogueNodeCore</c>), 줄 수로 재면 <b>방금 만든 에피소드를 지울 때마다</b>
    /// `(떼어냄)` 카드가 남는다 — 이 규칙이 막으려던 유령이 바로 그것이다.
    ///
    /// ⚠ 내보내기 쪽의 「빈 노드」 판정(<c>ActiveLines.Any()</c>)과 <b>다른 질문이다</b>.
    /// 그쪽은 <i>"재생할 줄이 있는가"</i>이고 빈 줄도 재생되는 한 줄이다. 여기는
    /// <i>"사람이 쓴 것이 있는가"</i>다 — 빈 줄은 쓴 것이 아니다.
    /// </summary>
    private static bool NothingWritten(Model.StoryProject project, DialogueNode card)
    {
        if (project.FindScript(card.ScriptId) is not { } script)
        {
            return true;
        }

        Script.ScriptLocale primary = script.RequireLocale(script.PrimaryLocale);

        return !script.ActiveLines.Any(line =>
            primary.Find(line.Id) is { } text &&
            (!string.IsNullOrWhiteSpace(text.Text) || !string.IsNullOrWhiteSpace(text.Speaker)));
    }

    /// <summary>민 파일을 제자리로. <b>언제나 false</b>를 돌려준다 — 예외를 삼키지 않는다.</summary>
    private static bool Restore(string? backup, string? original)
    {
        if (backup is null || original is null)
        {
            return false;
        }

        try
        {
            System.IO.File.Move(backup, original, overwrite: true);
        }
        catch (Exception exception) when (
            exception is System.IO.IOException or UnauthorizedAccessException)
        {
            // 여기서 또 실패하면 조용히 넘긴다 — 사유는 올라가는 예외가 말한다.
        }

        return false;
    }
}
