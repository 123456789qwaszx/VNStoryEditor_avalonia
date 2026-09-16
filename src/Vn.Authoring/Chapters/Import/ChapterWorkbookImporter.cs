using Vn.Authoring.Definition;
using Vn.Authoring.Editing;

namespace Vn.Authoring.Chapters.Import;

/// <param name="Applied">전부 반영됐는가. 거부면 <b>아무것도</b> 반영되지 않았다.</param>
/// <param name="ChapterIds">들여온 챕터들. 거부면 비어 있다.</param>
/// <param name="Notices">사람에게 보일 말(이행했다·잠겨서 못 했다). 순서가 일어난 순서다.</param>
public sealed record ChapterProjectImport(
    bool Applied,
    IReadOnlyList<string> ChapterIds,
    IReadOnlyList<ChapterDiagnostic> Diagnostics,
    IReadOnlyList<string> Notices)
{
    public IEnumerable<ChapterDiagnostic> Errors =>
        Diagnostics.Where(item => item.Severity == ChapterDiagnosticSeverity.Error);
}

/// <summary>
/// <b>챕터 워크북 → 프로젝트는 여기를 지난다</b> (R-F · 2026-09-16 —
/// <c>docs/work-orders/tool-owns-workbooks-orders.md</c> §5).
///
/// <see cref="EpisodeWorkbookImporter"/>의 짝이고 규율도 같다. 다른 것은 들여오는 값이다 —
/// 저쪽은 대사 본문, 이쪽은 <b>에피소드 구조·간선·선택지·조건·스탯</b>이다.
///
/// <b><see cref="ChapterImportService"/>와 무엇이 다른가</b> — 그쪽은 R-A가 세운 <i>읽기의
/// 경계</i>이고 지금도 화면이 <b>다시 그릴 때마다</b> 부른다. 이쪽은 그 결과를
/// <b>프로젝트 안으로 들인다</b>. 그래서 이쪽은 한 번만 부른다: 들어온 뒤로 그 워크북은
/// 산출물이고, 다시 읽으면 툴이 쓴 값을 툴이 되읽는 왕복이 살아난다.
///
/// ⛔ <b>부분 성공하지 않는다</b> (§5.2). 못 연 파일이 하나라도 있거나 오류 진단이 하나라도
/// 있으면 <b>아무것도</b> 들여오지 않는다. 절반만 들어온 프로젝트는 어느 챕터가 원본인지
/// 사람이 알 수 없게 만들고, 되돌릴 길은 워크북뿐인데 그 워크북은 곧 산출물이 된다.
///
/// ⛔ <b>합치지 않고 갈아 끼운다.</b> 같은 Id의 챕터가 이미 있으면 통째로 바꾼다 — 행 단위로
/// 맞추려면 신원 매칭이 필요하고, 그것이 곧 <c>EpisodeSyncService</c>가 하던 일이다(R-D에서
/// 철거한 그 기계). 임포트는 <b>한 번</b>이라 맞출 상대가 없어야 맞다.
///
/// ⚠ 이 클래스는 화면·세션을 모른다. 그래야 화면 없이 시험된다.
/// </summary>
public static class ChapterWorkbookImporter
{
    /// <summary>
    /// 프로젝트의 <c>chapters/</c> 폴더를 프로젝트 안으로 들여온다. <b>명시적 동작이다</b> —
    /// 파일 감시가 부르지 않는다(§5.2).
    ///
    /// 폴더가 없으면 빈 성공이다 — 아직 챕터를 안 만든 프로젝트이지 오류가 아니다.
    /// </summary>
    public static ChapterProjectImport Run(
        ProjectEditor editor,
        string? projectManifestPath,
        GameDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(editor);

        // 1단계 — 읽는다. 이행도 여기서 한다(구판 파일은 읽기 전에 규격을 맞춘다).
        ChapterImport read = ChapterImportService.Run(projectManifestPath, definition);

        var diagnostics = new List<ChapterDiagnostic>();
        var documents = new List<ChapterDocument>();

        foreach (ChapterEntry entry in read.Entries)
        {
            if (entry.Model is not { } model)
            {
                // 못 연 파일 — 데이터 오류가 아니라 접근 실패다. 그래도 관문은 같다:
                // 무엇이 안 들어왔는지 모르는 채로 절반을 들이지 않는다.
                diagnostics.Add(new ChapterDiagnostic(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.ChapterFileUnreadable,
                    entry.Path,
                    Sheet: null, Row: null, Column: null,
                    $"'{entry.ChapterId}'을 열지 못해 들여오지 않았습니다. {entry.OpenFailure}"));
                continue;
            }

            diagnostics.AddRange(model.Diagnostics);
            documents.Add(ChapterDocument.From(model));
        }

        // ⛔ 여기가 §5.2의 관문이다 — 하나라도 깨졌으면 아무것도 들여오지 않는다.
        if (diagnostics.Any(item => item.Severity == ChapterDiagnosticSeverity.Error))
        {
            return new ChapterProjectImport(
                Applied: false, Array.Empty<string>(), diagnostics, read.Notices);
        }

        // 2단계 — 반영. 여기서부터는 실패할 것이 없다.
        editor.ReplaceChapters(documents);

        return new ChapterProjectImport(
            Applied: true,
            documents.Select(document => document.ChapterId).ToList(),
            diagnostics,
            read.Notices);
    }
}
