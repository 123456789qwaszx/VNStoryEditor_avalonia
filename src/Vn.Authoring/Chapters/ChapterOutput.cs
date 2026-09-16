using Vn.Authoring.Definition;
using Vn.Authoring.Model;

namespace Vn.Authoring.Chapters;

/// <summary>한 번의 출력 결과 — 낸 것과 미룬 것.</summary>
/// <param name="Deferred">
/// 지금 갱신하지 못한 챕터들. <b>실패가 아니라 미룸이다</b> (§5.3) — 원본은 프로젝트라
/// 글은 이미 안전하고, 못 한 것은 그 파일을 지금 쓰는 일뿐이다.
/// </param>
public sealed record ChapterEmitRun(
    IReadOnlyList<string> Written,
    IReadOnlyList<(string ChapterId, string Failure)> Deferred)
{
    public static ChapterEmitRun Empty { get; } = new([], []);

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
/// <b>프로젝트의 챕터 → 워크북</b> (R-F · 2026-09-16, 지시서 §4·§5.3).
///
/// <see cref="EpisodeScriptOutput"/>의 짝이고, 뒤집기의 도착점도 같다 — 툴에 쓴 것이
/// 엑셀을 채운다. 다른 것은 내는 값이다: 저쪽은 대사, 이쪽은 <b>에피소드 구조·간선·
/// 선택지·조건·스탯</b>이다.
///
/// <b>왜 화면 밖인가</b> — 출력이 화면의 일이면 <b>화면을 지나지 않는 편집에서 파일이
/// 낡는다</b>. 실제 사례가 되돌리기다: <c>Undo</c>는 프로젝트를 되돌리지만 화면의 편집
/// 창구를 지나지 않으므로, 워크북만 되돌리기 <em>전</em> 상태로 남았다.
///
/// ⛔ <b>미룬 것을 잊지 않는다</b> (§5.3). 엑셀이 잡고 있어 못 낸 챕터를 <see cref="Pending"/>에
/// 들고 있다가, 잠금이 풀렸다는 신호에 <see cref="Retry"/>로 다시 낸다. 잠금의 뜻이
/// <i>"막는다"</i>에서 <i>"그 파일을 지금 갱신하지 못했다"</i>로 바뀐 것이 이 자리다.
///
/// ⚠ 화면·세션을 모른다. 그래야 화면 없이 시험된다.
/// </summary>
public sealed class ChapterOutput
{
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);

    /// <summary>아직 못 낸 챕터들 — 잠금이 풀리면 여기부터 다시 낸다.</summary>
    public IReadOnlyCollection<string> Pending => _pending;

    /// <summary>
    /// 챕터 하나를 낸다 — 편집 한 번의 뒤에 붙는 길.
    /// </summary>
    public ChapterEmitRun Emit(
        StoryProject project, string? projectManifestPath, string chapterId, GameDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        return Run(project, projectManifestPath, [chapterId], definition);
    }

    /// <summary>
    /// 프로젝트의 챕터를 <b>전부</b> 낸다 — 저장의 뒤에 붙는 길 (§6.2: "저장 = 프로젝트에
    /// 저장 + 워크북 재출력").
    ///
    /// ⚠ <b>이것이 낡음을 막는 그물이다.</b> 편집마다 내는 것은 빠른 길이고, 저장 때 전부
    /// 내는 것은 <b>어느 길로 고쳤든</b> 파일이 따라오게 한다.
    /// </summary>
    public ChapterEmitRun EmitAll(
        StoryProject project, string? projectManifestPath, GameDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        return Run(
            project,
            projectManifestPath,
            project.Chapters.Select(chapter => chapter.ChapterId).ToList(),
            definition);
    }

    /// <summary>
    /// 미뤄 둔 것만 다시 낸다 — 엑셀이 파일을 놓았다는 신호에 부른다.
    ///
    /// 여전히 잠겨 있으면 그대로 미뤄 둔 채 돌아온다. 사람에게 또 말하지는 않는다 —
    /// 잠금이 움직일 때마다 같은 말을 되풀이하면 그것이 소음이 된다.
    /// </summary>
    public ChapterEmitRun Retry(
        StoryProject project, string? projectManifestPath, GameDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        return _pending.Count == 0
            ? ChapterEmitRun.Empty
            : Run(project, projectManifestPath, _pending.ToList(), definition);
    }

    private ChapterEmitRun Run(
        StoryProject project,
        string? projectManifestPath,
        IReadOnlyList<string> chapterIds,
        GameDefinition? definition)
    {
        if (ChapterLibrary.FolderFor(projectManifestPath) is not { } folder)
        {
            // 아직 저장 안 한 프로젝트 — 낼 자리가 없다. 미루는 것도 아니다(잠긴 게 아니다).
            return ChapterEmitRun.Empty;
        }

        var written = new List<string>();
        var deferred = new List<(string, string)>();

        foreach (string chapterId in chapterIds)
        {
            if (project.Chapters.FirstOrDefault(chapter =>
                    string.Equals(chapter.ChapterId, chapterId, StringComparison.Ordinal))
                is not { } chapter)
            {
                // 지워진 챕터 — 미룰 일도 없다. 파일은 삭제 경로가 따로 걷는다.
                _pending.Remove(chapterId);
                continue;
            }

            Directory.CreateDirectory(folder);

            string path = Path.Combine(folder, chapterId + ".xlsx");
            ChapterWriteResult result = ChapterWorkbookEmitter.Emit(
                path, chapter.ToGraphModel(path, definition));

            if (result.Written)
            {
                _pending.Remove(chapterId);
                written.Add(chapterId);
                continue;
            }

            _pending.Add(chapterId);
            deferred.Add((chapterId, result.Failure!));
        }

        return new ChapterEmitRun(written, deferred);
    }
}
