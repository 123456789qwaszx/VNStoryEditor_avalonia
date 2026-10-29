using Vn.Authoring.Definition;
using Vn.Authoring.Model;

namespace Vn.Authoring.Chapters;

/// <summary>한 번의 출력 결과 — 낸 것과 미룬 것. 대상은 챕터(<c>ch01</c>) 또는 대본(<c>ch01/ep01</c>)이다.</summary>
/// <param name="Deferred">
/// 지금 갱신하지 못한 워크북들. <b>실패가 아니라 미룸이다</b> (§5.3) — 원본은 프로젝트라
/// 값은 이미 안전하고, 못 한 것은 그 파일을 지금 쓰는 일뿐이다.
/// </param>
public sealed record WorkbookEmitRun(
    IReadOnlyList<string> Written,
    IReadOnlyList<(string Target, string Failure)> Deferred)
{
    public static WorkbookEmitRun Empty { get; } = new([], []);

    public bool AllWritten => Deferred.Count == 0;

    /// <summary>상태줄에 덧붙일 말. 다 냈으면 빈 문자열이다 — <b>잘된 일은 조용하다</b>.</summary>
    public string Notice() => Deferred.Count switch
    {
        0 => string.Empty,
        1 => $" ⚠ {Deferred[0].Failure}",
        _ => $" ⚠ 워크북 {Deferred.Count}개를 지금 갱신하지 못했습니다 — {Deferred[0].Failure}"
    };
}

/// <summary>
/// <b>프로젝트 → 워크북</b>, 챕터와 대본을 <b>한 조정자</b>가 낸다 (R-F · 2026-09-16 지시서 §4·§5.3,
/// 2026-10-29 `docs/plans/excel-boundary.md` C5).
///
/// 뒤집기의 도착점이다 — 툴에 쓴 것이 엑셀을 채운다. 챕터 워크북(<see cref="ChapterWorkbookEmitter"/>)은
/// 에피소드 구조·간선·선택지·조건·스탯을, 대본 워크북(<see cref="EpisodeScriptOutput"/>)은 그 챕터 판의
/// 카드마다 대사를 낸다.
///
/// <b>왜 하나인가</b> (C5) — 2026-10-29까지 챕터만 여기를 지났고 대본은 대본 탭이 저장 직후 직접 썼다.
/// 그래서 대본은 못 낸 것을 <b>기억하지 못했고</b>, 저장도 대본을 다시 내지 않았다 — 되돌리기나 화자
/// 개명처럼 화면을 지나지 않는 편집에서 대본 워크북이 낡았다. 이제 둘이 같은 Pending/Retry/EmitAll을
/// 지난다.
///
/// <b>왜 화면 밖인가</b> — 출력이 화면의 일이면 <b>화면을 지나지 않는 편집에서 파일이 낡는다</b>.
/// 실제 사례가 되돌리기다: <c>Undo</c>는 프로젝트를 되돌리지만 화면의 편집 창구를 지나지 않는다.
///
/// ⛔ <b>미룬 것을 잊지 않는다</b> (§5.3). 엑셀이 잡고 있어 못 낸 대상을 <see cref="Pending"/>에
/// 들고 있다가, 잠금이 풀렸다는 신호에 <see cref="Retry"/>로 다시 낸다. 잠금의 뜻이
/// <i>"막는다"</i>에서 <i>"그 파일을 지금 갱신하지 못했다"</i>로 바뀐 것이 이 자리다.
///
/// ⚠ 화면·세션을 모른다. 그래야 화면 없이 시험된다.
/// </summary>
public sealed class WorkbookOutput
{
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);

    /// <summary>아직 못 낸 대상들(챕터 <c>ch01</c> · 대본 <c>ch01/ep01</c>) — 잠금이 풀리면 여기부터 다시 낸다.</summary>
    public IReadOnlyCollection<string> Pending => _pending;

    /// <summary>대본 대상의 열쇠 — 챕터와 에피소드를 함께 든다(EpisodeId는 챕터 안에서만 유일하다).</summary>
    public static string ScriptTarget(string chapterId, string episodeId) => $"{chapterId}/{episodeId}";

    /// <summary>
    /// 챕터 워크북 하나를 낸다 — 챕터 편집 한 번의 뒤에 붙는 길. 대본은 안 낸다: 간선 하나 고칠 때마다
    /// 그 챕터의 대본을 전부 열어 보는 값을 치르지 않는다(대본은 <see cref="EmitScript"/>·<see cref="EmitAll"/>).
    /// </summary>
    public WorkbookEmitRun Emit(
        StoryProject project, string? projectManifestPath, string chapterId, GameDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        return Run(project, projectManifestPath, [chapterId], definition);
    }

    /// <summary>
    /// 대본 하나를 낸다 — 대본 탭이 글을 저장한 직후, 에피소드를 더한 직후, 화자 개명이 줄을 바꾼 직후에
    /// 붙는 길. 챕터 아닌 판의 카드는 낼 자리가 없으므로 아무것도 하지 않는다.
    /// </summary>
    public WorkbookEmitRun EmitScript(StoryProject project, string? projectManifestPath, DialogueNode node)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(node);

        return EpisodeNaming.ChapterOf(project, node) is { } chapter
            ? Run(project, projectManifestPath, [ScriptTarget(chapter.ChapterId, EpisodeNaming.EpisodeIdOf(node))], null)
            : WorkbookEmitRun.Empty;
    }

    /// <summary>
    /// 프로젝트의 챕터와 대본을 <b>전부</b> 낸다 — 저장의 뒤에 붙는 길 (§6.2: "저장 = 프로젝트에
    /// 저장 + 워크북 재출력").
    ///
    /// ⚠ <b>이것이 낡음을 막는 그물이다.</b> 편집마다 내는 것은 빠른 길이고, 저장 때 전부
    /// 내는 것은 <b>어느 길로 고쳤든</b> 파일이 따라오게 한다. 같은 내용의 대본은 다시 쓰지 않는다
    /// (<see cref="EpisodeScriptOutput"/>).
    /// </summary>
    public WorkbookEmitRun EmitAll(
        StoryProject project, string? projectManifestPath, GameDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        return Run(
            project,
            projectManifestPath,
            project.Chapters.SelectMany(chapter => ChapterAndScripts(project, chapter.ChapterId)).ToList(),
            definition);
    }

    /// <summary>
    /// 미뤄 둔 것만 다시 낸다 — 엑셀이 파일을 놓았다는 신호에 부른다.
    ///
    /// 여전히 잠겨 있으면 그대로 미뤄 둔 채 돌아온다. 사람에게 또 말하지는 않는다 —
    /// 잠금이 움직일 때마다 같은 말을 되풀이하면 그것이 소음이 된다.
    /// </summary>
    public WorkbookEmitRun Retry(
        StoryProject project, string? projectManifestPath, GameDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        return _pending.Count == 0
            ? WorkbookEmitRun.Empty
            : Run(project, projectManifestPath, _pending.ToList(), definition);
    }

    /// <summary>챕터 열쇠 하나 + 그 챕터 판의 카드마다 대본 열쇠.</summary>
    private static List<string> ChapterAndScripts(StoryProject project, string chapterId)
    {
        var targets = new List<string> { chapterId };

        if (EpisodeNaming.BoardOf(project, chapterId) is { } board)
        {
            targets.AddRange(board.Nodes.OfType<DialogueNode>()
                .Where(node => node.ScriptId is not null)
                .Select(node => ScriptTarget(chapterId, EpisodeNaming.EpisodeIdOf(node))));
        }

        return targets;
    }

    private WorkbookEmitRun Run(
        StoryProject project,
        string? projectManifestPath,
        IReadOnlyList<string> targets,
        GameDefinition? definition)
    {
        if (ChapterLibrary.FolderFor(projectManifestPath) is not { } chapters)
        {
            // 아직 저장 안 한 프로젝트 — 낼 자리가 없다. 미루는 것도 아니다(잠긴 게 아니다).
            return WorkbookEmitRun.Empty;
        }

        var written = new List<string>();
        var deferred = new List<(string, string)>();

        foreach (string target in targets)
        {
            int slash = target.IndexOf('/');

            ChapterWriteResult? result = slash >= 0
                ? EmitScriptTarget(project, projectManifestPath, target[..slash], target[(slash + 1)..])
                : EmitChapterTarget(project, chapters, target, definition);

            if (result is null)
            {
                // 지워진 챕터·카드 — 미룰 일도 없다. 파일은 삭제 경로가 따로 걷는다.
                _pending.Remove(target);
                continue;
            }

            if (result.Written)
            {
                _pending.Remove(target);
                written.Add(target);
                continue;
            }

            _pending.Add(target);
            deferred.Add((target, result.Failure!));
        }

        return new WorkbookEmitRun(written, deferred);
    }

    private static ChapterWriteResult? EmitChapterTarget(
        StoryProject project, string chapters, string chapterId, GameDefinition? definition)
    {
        if (project.Chapters.FirstOrDefault(chapter =>
                string.Equals(chapter.ChapterId, chapterId, StringComparison.Ordinal))
            is not { } chapter)
        {
            return null;
        }

        Directory.CreateDirectory(chapters);

        string path = Path.Combine(chapters, chapterId + ".xlsx");

        return ChapterWorkbookEmitter.Emit(path, chapter.ToGraphModel(path, definition));
    }

    private static ChapterWriteResult? EmitScriptTarget(
        StoryProject project, string? projectManifestPath, string chapterId, string episodeId)
    {
        if (EpisodeNaming.CardFor(project, chapterId, episodeId) is not { ScriptId: not null } node ||
            EpisodeLibrary.FolderFor(projectManifestPath, chapterId) is not { } folder)
        {
            return null;
        }

        return EpisodeScriptOutput.Write(project, node, EpisodeLibrary.PathFor(folder, episodeId));
    }
}
