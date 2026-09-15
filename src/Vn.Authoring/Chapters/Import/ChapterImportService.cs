using Vn.Authoring.Definition;

namespace Vn.Authoring.Chapters.Import;

/// <summary>워크북에서 챕터를 한 번 들여온 결과. 알릴 말은 <see cref="Notices"/>가 든다.</summary>
/// <param name="Notices">
/// 사람에게 보일 말(이행했다·잠겨서 못 했다). <b>순서가 일어난 순서다</b> — 상태줄은
/// 마지막 하나만 보이지만, 목록으로 받아 두면 부르는 쪽이 로그로도 쓸 수 있다.
/// </param>
public sealed record ChapterImport(
    IReadOnlyList<ChapterEntry> Entries,
    IReadOnlyList<string> Notices);

/// <summary>
/// <b>워크북 → 모델은 여기를 지난다</b> (R-A · 2026-09-15 —
/// <c>docs/work-orders/tool-owns-workbooks-orders.md</c>).
///
/// <b>왜 화면 밖으로 꺼냈나</b> — 뒤집기의 위험은 전부 <i>"읽던 것을 안 읽게 되는 순간"</i>에
/// 있는데, 그 읽기가 <c>ChapterGraphView.Reload()</c>(3,860줄 코드비하인드) 안에서 이행·
/// 화면 구성과 한 덩어리였다. 무엇이 임포트이고 무엇이 그리기인지 밖에서 보이지 않으면
/// 철거(R-D) 때 무엇을 걷어야 하는지도 보이지 않는다. <b>경계를 이름으로 세운다.</b>
///
/// ⚠ <b>아직은 부르는 쪽이 매번 부른다.</b> 이 타입이 서는 것만으로 재읽기가 사라지지는
/// 않는다 — 감시자를 떼고 이것을 <b>명시적 [가져오기] 한 번</b>으로 바꾸는 것이 R-A의
/// 다음 조각이다. 지금 바뀐 것은 <b>자리</b>이지 <b>횟수</b>가 아니다.
///
/// ⚠ 이 클래스는 화면·세션을 모른다. 그래야 다음 툴이 가져갈 수 있고 화면 없이 시험된다
/// (<c>EpisodeSyncRunner</c>·<c>EpisodeLineEditor</c>를 꺼낸 것과 같은 이유).
/// </summary>
public static class ChapterImportService
{
    /// <summary>
    /// 프로젝트의 <c>chapters/</c> 폴더를 들여온다. 폴더가 없으면 빈 결과다(오류가 아니다 —
    /// 아직 챕터를 안 만든 프로젝트다).
    ///
    /// 순서가 규격이다: <b>이행 먼저, 읽기 나중.</b> 구판 파일을 읽고 나서 이행하면 그
    /// 회차의 모델이 옛 규격으로 서고, 다음 회차에야 새 규격이 보인다.
    /// </summary>
    /// <param name="projectManifestPath">프로젝트 매니페스트 경로. null이면 빈 결과다.</param>
    public static ChapterImport Run(string? projectManifestPath, GameDefinition? definition = null)
    {
        var notices = new List<string>();

        if (ChapterLibrary.FolderFor(projectManifestPath) is not { } folder ||
            !Directory.Exists(folder))
        {
            return new ChapterImport(Array.Empty<ChapterEntry>(), notices);
        }

        Migrate(folder, notices);

        return new ChapterImport(ChapterLibrary.Load(folder, definition), notices);
    }

    /// <summary>
    /// 구판 워크북 규격 이행 (2026-08-16). 필요 없는 파일에는 손대지 않으므로 매번 불러도
    /// 쓰기는 구판을 처음 만난 그 한 번뿐이다. 실패(잠금)는 말로 알리고, 리더가 구판 그대로
    /// 읽으며 머리글 경고를 세운다 — <b>이행이 막혀도 읽기는 계속된다.</b>
    /// </summary>
    private static void Migrate(string folder, List<string> notices)
    {
        // `~$`는 엑셀이 여는 순간 만드는 잠금 파일이다 — 워크북이 아니다.
        foreach (string workbook in Directory.EnumerateFiles(folder, "*.xlsx")
                     .Where(file => !Path.GetFileName(file)
                         .StartsWith("~$", StringComparison.Ordinal)))
        {
            ChapterWorkbookMigrator.MigrationResult migration =
                ChapterWorkbookMigrator.Migrate(workbook);

            if (migration.Migrated)
            {
                notices.Add(
                    $"'{Path.GetFileName(workbook)}'를 새 시트 규격으로 이행했습니다" +
                    "(이전 상태는 .bak). 엑셀이 열려 있었다면 닫았다 다시 열어 주세요.");
            }
            else if (migration.Failure is { } failure)
            {
                notices.Add(failure);
            }
        }
    }
}
