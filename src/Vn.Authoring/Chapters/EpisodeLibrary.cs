using System.Text;
using ClosedXML.Excel;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 프로젝트의 `episodes/` 폴더와 에피소드 워크북의 생성 (G5).
///
/// <b>이 레이어에서 엑셀 파일을 만드는 유일한 자리다.</b> 워크북이 없으면 §3.2 규격의
/// 빈 워크북을 만들어 준다 — 기획자가 열한 개의 머리글을 손으로 칠 이유가 없다.
///
/// <b>만든 뒤에는 절대 손대지 않는다 (v4).</b> 대본 파일의 writer는 사람뿐이다 —
/// LineId도 B열이 아니라 프로젝트의 <c>ExcelLineMap</c>에 기록된다. 그래서 구글 시트든
/// 엑셀이든 어떤 편집기와도 쓰기 충돌이 없다.
/// </summary>
public static class EpisodeLibrary
{
    public const string FolderName = "episodes";

    /// <summary>드롭다운·검증을 걸어 두는 행 수. 한 에피소드가 이걸 넘으면 나누는 게 맞다.</summary>
    private const int TemplateRows = 500;

    public static string? FolderFor(string? projectManifestPath)
    {
        if (string.IsNullOrWhiteSpace(projectManifestPath))
        {
            return null;
        }

        string? root = Path.GetDirectoryName(Path.GetFullPath(projectManifestPath));
        return root is null ? null : Path.Combine(root, FolderName);
    }

    public static string PathFor(string folder, string episodeId) =>
        Path.Combine(folder, episodeId + ".xlsx");

    /// <summary>
    /// 폴더에 이미 있는 그 에피소드의 워크북. 없으면 null.
    ///
    /// <b><see cref="File.Exists(string)"/> 하나만 믿지 않는다.</b> 두 가지 실사례 때문이다:
    /// ① 클라우드 동기화가 한글 파일 이름을 분해형(NFD)으로 바꿔 놓아 조합형(NFC) 경로로는
    /// 못 찾는다. ② 구글 시트가 .xlsx를 저장하며 <b>.xlsm으로 개명한다</b> — 매크로는 없이
    /// 선언만 그렇게 쓴다(컨테이너 해부로 확인). 못 찾으면 "없구나" 하고 빈 워크북을 하나 더
    /// 만들게 되므로, 폴더를 훑어 이름을 정규화해 맞추고 .xlsm도 같은 워크북으로 받는다.
    /// v4에서 툴은 이 파일을 읽기만 하므로 .xlsm이어도 아무 문제가 없다.
    ///
    /// 같은 이름이 여럿이면(빈 .xlsx 유물 + 진짜 .xlsm) <b>가장 최근에 저장된 것</b>이 원고다.
    /// </summary>
    public static string? FindExisting(string folder, string episodeId)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return null;
        }

        string wanted = Normalize(episodeId);

        return Directory.EnumerateFiles(folder, "*.xls*")
            .Where(candidate =>
            {
                string name = Path.GetFileName(candidate);
                string extension = Path.GetExtension(name);

                return !name.StartsWith("~$", StringComparison.Ordinal) &&
                       (string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase)) &&
                       string.Equals(Normalize(Path.GetFileNameWithoutExtension(name)), wanted,
                           StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>이름 비교의 단일 규칙 — 조합형으로 모으고 앞뒤 공백을 턴다.</summary>
    private static string Normalize(string value) =>
        value.Trim().Normalize(NormalizationForm.FormC);

    /// <summary>툴이 읽지 못하는 스프레드시트 형식 (.xlsm은 v4.1부터 정식으로 읽는다).</summary>
    private static readonly string[] OtherSpreadsheetExtensions = [".xlsb", ".xls", ".ods"];

    /// <summary>
    /// 같은 이름인데 <b>읽을 수 없는 형식</b>인 파일(예: <c>.ods</c>). 조용히 무시하면 툴은
    /// "워크북이 없구나" 하며 빈 것을 새로 만들고, 사람의 원고는 화면 어디에도 나타나지
    /// 않는다. 찾아서 말해 주기 위한 자리다(규칙 14).
    /// </summary>
    public static string? FindOtherFormat(string folder, string episodeId)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return null;
        }

        string wanted = Normalize(episodeId);

        foreach (string candidate in Directory.EnumerateFiles(folder))
        {
            string name = Path.GetFileName(candidate);

            if (name.StartsWith("~$", StringComparison.Ordinal) ||
                !OtherSpreadsheetExtensions.Contains(
                    Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(Normalize(Path.GetFileNameWithoutExtension(name)), wanted,
                    StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// 워크북이 없으면 §3.2 규격의 빈 워크북을 만든다. 있으면 아무것도 하지 않는다 —
    /// <b>기존 파일은 절대 덮어쓰지 않는다.</b>
    /// </summary>
    /// <returns>새로 만들었으면 true.</returns>
    public static bool EnsureWorkbook(string folder, string episodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(episodeId);

        if (FindExisting(folder, episodeId) is not null)
        {
            return false;
        }

        string path = PathFor(folder, episodeId);

        Directory.CreateDirectory(folder);

        using var workbook = new XLWorkbook();
        IXLWorksheet sheet = workbook.AddWorksheet(Truncate(episodeId, 31)); // 엑셀 시트 이름 상한

        string[] headers =
            ["인덱스", "LineId", "유형", "태그", "조건라벨", "IN", "OUT", "화자", "내용", "스탯변화", "메모"];

        for (int column = 1; column <= headers.Length; column++)
        {
            IXLCell cell = sheet.Cell(1, column);
            cell.SetValue(headers[column - 1]);
            cell.Style.Font.SetBold(true);
            cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E8EAED"));
        }

        // 유형·태그는 규격의 낱말만 받는다 — 오타가 검증기까지 가기 전에 엑셀이 막는다.
        sheet.Range(2, 3, TemplateRows, 3).CreateDataValidation()
            .List("\"IF,CHOICE,OPTION\"", true);
        sheet.Range(2, 4, TemplateRows, 4).CreateDataValidation()
            .List("\"INPUT,OUT\"", true);

        // 시트 보호는 걸지 않는다 (v4). 보호의 유일한 이유였던 B열(LineId)을 툴이 더는
        // 쓰지 않는다 — 행 신원은 프로젝트의 ExcelLineMap이 갖는다. 보호가 없으면 구글
        // 시트 같은 외부 편집기가 재저장할 때 깨질 것도 하나 줄어든다. B열은 유물로 남아
        // 회색 배경만 유지한다(과거 파일과 열 배치가 같아야 규격 문서가 하나로 통한다).
        sheet.Column(2).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F1F3F4"));

        // 인덱스를 미리 다 깔아 준다 (10·20·30 방식, G-5). 작가는 번호를 신경 쓰지 않고
        // 그 옆 칸(화자·내용)만 채우면 된다 — 인덱스 없는 행은 표의 일부가 아니라서,
        // 시트에서 그냥 아래로 타이핑하면 대사가 조용히 버려지는 함정이 실제로 있었다.
        // 사이에 끼울 때만 사람이 15 같은 빈 숫자를 적는다(그래서 십 단위다).
        for (int row = 2; row <= TemplateRows; row++)
        {
            sheet.Cell(row, 1).SetValue((row - 1) * 10);
        }

        sheet.Column(9).Width = 50;   // 내용
        sheet.Column(11).Width = 24;  // 메모

        workbook.SaveAs(path);
        return true;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
