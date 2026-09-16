using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Chapters;

/// <summary>
/// <b>프로젝트의 대사노드 → 대본 워크북</b> (R-E · 2026-09-16, 지시서 §4·§6.2).
///
/// <b>여기가 뒤집기의 도착점이다.</b> 지시서의 한 문장 — *"엑셀에 쓴 것이 툴에 반영되는
/// 것이 아니라, 툴에 쓴 것이 엑셀을 채운다"* — 에서 <b>채운다</b>에 해당하는 자리다.
///
/// 하는 일은 잇는 것뿐이다: 노드의 줄들을 <c>(화자, 내용)</c> 순서로 거둬
/// <see cref="EpisodeWorkbookEmitter"/>에 넘긴다. 번호 매기기·원자적 쓰기·<c>.bak</c>·
/// 읽기 전용 표기는 전부 이미터가 이미 한다(R-B).
///
/// ⚠ <b>인덱스는 여기서 다시 매겨진다</b>(10·20·30). 워크북이 원본이던 시절에는 그 번호가
/// 줄의 신원이라 절대 못 건드렸지만, 이제 신원은 프로젝트의 <c>LineId</c>이고 인덱스는
/// <b>출력 열</b>일 뿐이다(지시서 §4.2). 그래서 삽입·삭제라는 난제가 함께 사라졌다.
/// </summary>
public static class EpisodeScriptOutput
{
    /// <summary>
    /// 그 노드의 대본을 <paramref name="path"/>에 낸다.
    ///
    /// ⚠ 실패(엑셀이 잡고 있음)는 <b>예외가 아니라 사유</b>다. 부르는 쪽은 그것을 사람에게
    /// 말하고 편집은 계속 놔둬야 한다 — 프로젝트가 원본이므로 글은 이미 안전하다(§5.3).
    /// </summary>
    public static ChapterWriteResult Write(StoryProject project, DialogueNode node, string path)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(node);

        return EpisodeWorkbookEmitter.Emit(path, EpisodeWorkbookEmitter.Number(Lines(project, node)));
    }

    /// <summary>
    /// 그 노드가 내보낼 줄들. <b>본문은 줄이 아니라 로케일이 갖는다</b> — LineId를 키로 쓰는
    /// 덕분에 언어를 더해도 논리가 가리키는 Id가 안 바뀐다(<c>ScriptDocument</c>).
    ///
    /// ⚠ 비어 있는 화자는 <b>지문</b>이라 그대로 비워 낸다(오류가 아니다).
    /// </summary>
    private static IEnumerable<(string? Speaker, string Text)> Lines(
        StoryProject project, DialogueNode node)
    {
        if (node.ScriptId is not { } scriptId ||
            project.FindScript(scriptId) is not { } script)
        {
            return [];
        }

        ScriptLocale primary = script.Locales
            .Single(locale => string.Equals(locale.Locale, script.PrimaryLocale, StringComparison.Ordinal));

        return script.ActiveLines
            .Select(line => primary.Find(line.Id))
            .Select(line => (Speaker: string.IsNullOrWhiteSpace(line.Speaker) ? null : line.Speaker, line.Text))
            .ToList();
    }
}
