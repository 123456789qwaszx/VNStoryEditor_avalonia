using ClosedXML.Excel;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 테스트가 쓰는 워크북 작성기.
///
/// 오류 케이스마다 이진 견본 파일을 저장소에 넣으면 무엇이 잘못됐는지 파일을 열어 봐야 안다.
/// 시트 내용을 테스트 코드 안에 두면 "이 표에서 이 오류가 나온다"가 한눈에 읽힌다.
///
/// 쓰기도 리더와 같은 ClosedXML로 한다. 손수 조립한 OOXML로 시험용 파일을 만들면 실제 엑셀이
/// 저장하는 파일과 다른 물건(공유 문자열 없음·서식 없음)을 시험하게 되고, 리더가 진짜 파일에서
/// 깨지는 걸 못 잡는다.
/// </summary>
internal static class XlsxTestWorkbook
{
    /// <summary>시트 이름 → 행 목록(행은 셀 문자열 배열). null 셀은 빈 칸이다.</summary>
    public static string Write(string directory, string fileName, params (string Name, string?[][] Rows)[] sheets)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);

        using var workbook = new XLWorkbook();

        foreach ((string name, string?[][] rows) in sheets)
        {
            IXLWorksheet sheet = workbook.AddWorksheet(name);

            for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                string?[] cells = rows[rowIndex];

                for (int columnIndex = 0; columnIndex < cells.Length; columnIndex++)
                {
                    string? value = cells[columnIndex];

                    if (string.IsNullOrEmpty(value))
                    {
                        continue;
                    }

                    // 전부 문자열로 넣는다. 규격의 숫자 칸(X·Y·초기값)이 텍스트로 들어와도
                    // 읽혀야 하고, 그 관용이 실제 기획자의 엑셀에서 필요하다.
                    sheet.Cell(rowIndex + 1, columnIndex + 1).SetValue(value);
                }
            }
        }

        workbook.SaveAs(path);
        return path;
    }
}
