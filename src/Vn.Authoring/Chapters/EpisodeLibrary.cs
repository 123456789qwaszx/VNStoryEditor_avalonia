using ClosedXML.Excel;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 프로젝트의 `episodes/` 폴더와 에피소드 워크북의 생성 (G5).
///
/// <b>이 레이어에서 엑셀 파일을 만드는 유일한 자리다.</b> 노드를 클릭했는데 워크북이 없으면
/// 여기서 §3.2 규격의 빈 워크북을 만들어 준다 — 기획자가 열한 개의 머리글을 손으로 칠 이유가 없다.
///
/// 만든 뒤에는 손대지 않는다. 내용의 소유자는 사람이고, 툴이 다시 쓰는 것은
/// <see cref="EpisodeSyncService"/>의 LineId 되쓰기(B열) 하나뿐이다.
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
    /// 워크북이 없으면 §3.2 규격의 빈 워크북을 만든다. 있으면 아무것도 하지 않는다 —
    /// <b>기존 파일은 절대 덮어쓰지 않는다.</b>
    /// </summary>
    /// <returns>새로 만들었으면 true.</returns>
    public static bool EnsureWorkbook(string folder, string episodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(episodeId);

        string path = PathFor(folder, episodeId);

        if (File.Exists(path))
        {
            return false;
        }

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

        // LineId 열(B)은 툴 소유다(§3.2). 회색으로 칠하고 잠근 뒤 시트를 보호한다 —
        // 사람이 실수로 신원을 고치는 길을 엑셀 단에서 막는다. 나머지 열은 자유롭게 편집된다.
        sheet.Columns(1, headers.Length).Style.Protection.SetLocked(false);
        sheet.Column(2).Style.Protection.SetLocked(true);
        sheet.Column(2).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F1F3F4"));
        sheet.Row(1).Style.Protection.SetLocked(true);
        sheet.Protect();

        // 10·20·30 방식(G-5)의 첫 자리. 빈 파일보다 시작점이 있는 파일이 규격을 가르친다.
        sheet.Cell(2, 1).SetValue(10);

        sheet.Column(9).Width = 50;   // 내용
        sheet.Column(11).Width = 24;  // 메모

        workbook.SaveAs(path);
        return true;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
