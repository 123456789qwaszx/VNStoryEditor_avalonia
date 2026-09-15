using ClosedXML.Excel;

namespace Vn.App.Tests;

/// <summary>
/// 에피소드 워크북을 <b>대사 한 줄과 함께</b> 세운다 (2026-08-25).
///
/// ⚠ <b>왜 필요해졌나</b> — 재생할 줄이 하나도 없는 대사 노드는 Yarn이 YarnProject에 아예
/// 넣지 않는다. 그래서 챕터 검증이 그것을 오류로 세우고 내보내기를 막는다. 견본 워크북만
/// 복사한 프로젝트는 <b>에피소드 대본이 하나도 없어서</b> 그 오류에 전부 걸린다 —
/// 예전에는 조용히 통과하던 자리라 테스트들이 빈 채로 살아 있었다.
///
/// 여기서 채우는 것은 <b>한 줄</b>이다. 내용은 아무래도 좋고, 노드가 살아남는 것이 요점이다.
/// </summary>
internal static class EpisodeWorkbookFixture
{
    /// <summary>견본 챕터(ch05)의 에피소드 전부.</summary>
    internal static readonly string[] SampleEpisodes =
    [
        "main05.01", "main05.02", "branch05.02A", "main05.03", "attach05.02s", "main05.end"
    ];

    internal static void Fill(string episodesFolder, params string[] episodeIds)
    {
        Directory.CreateDirectory(episodesFolder);

        foreach (string episodeId in episodeIds.Length > 0 ? episodeIds : SampleEpisodes)
        {
            string path = Path.Combine(episodesFolder, episodeId + ".xlsx");

            using var book = new XLWorkbook();
            IXLWorksheet sheet = book.AddWorksheet("대본");

            // v15 4열 (2026-09-16 — R-C). 리더가 <b>머리글로 시트를 찾으므로</b> 한 글자도
            // 달라선 안 된다 — 어긋나면 "시트 없음"이 되어 이 워크북이 통째로 안 읽힌다.
            string[] headers = ["인덱스", "LineId", "화자", "내용"];

            for (int column = 0; column < headers.Length; column++)
            {
                sheet.Cell(1, column + 1).SetValue(headers[column]);
            }

            sheet.Cell(2, 1).SetValue("10");
            sheet.Cell(2, 2).SetValue($"ln_{episodeId.Replace('.', '_')}");
            sheet.Cell(2, 3).SetValue("윌로");
            sheet.Cell(2, 4).SetValue("한 줄");

            book.SaveAs(path);
        }
    }
}
