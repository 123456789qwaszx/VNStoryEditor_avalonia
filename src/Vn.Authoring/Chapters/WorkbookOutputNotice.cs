using ClosedXML.Excel;

namespace Vn.Authoring.Chapters;

/// <summary>
/// <b>산출물임을 파일 안에서 말하는 한 줄</b> (지시서 §4.4 · 2026-09-16).
///
/// 규격은 셋을 함께 내라고 한다 — ①시트 보호 ②<b>각 시트 1행에 머리글보다 위로</b> 이 문구
/// ③파일 속성 <c>Comments</c>. 사람이 열어서 고칠 수 있는 파일이 "고쳐도 소용없는 파일"이면
/// 그 사실이 파일 안에 있어야 한다. 보호는 <b>막는 것이 아니라 알리는 것</b>인데, 알리는
/// 문구가 없으면 잠긴 이유가 안 보인다.
///
/// ⛔ <b>그래서 머리글이 2행으로 내려간다.</b> 리더가 행 번호를 상수로 들면 구판 파일(머리글이
/// 1행)이 안 읽히므로, <see cref="HeaderRowOf"/>가 <b>찾아 준다</b> — 1행이 이 문구면 2행,
/// 아니면 1행이다. 두 규격이 한동안 같이 산다: 사람이 손으로 만든 옛 워크북과, 툴이 내는 새 것.
/// </summary>
public static class WorkbookOutputNotice
{
    /// <summary>1행에 적는 말. ⚠ <b>이 글자로 머리글 행을 가리므로</b> 함부로 바꾸지 않는다.</summary>
    public const string Text =
        "⚠ 이 파일은 VnTool이 만든 산출물입니다. 여기서 고친 것은 반영되지 않고 다음 저장에 덮어쓰입니다.";

    /// <summary>파일 속성 <c>Comments</c>에 적는 말 — ⚠ 없이 같은 뜻이다.</summary>
    public const string Property =
        "이 파일은 VnTool이 만든 산출물입니다. 여기서 고친 것은 반영되지 않고 다음 저장에 덮어쓰입니다.";

    /// <summary>
    /// 그 시트의 머리글이 몇 행인가 — <b>구판은 1행, 산출물은 2행</b>.
    ///
    /// ⚠ 1행 A열의 글자 하나로 가른다. 안내문은 툴만 적고 사람이 적을 일이 없으므로
    /// 이것으로 충분하고, 머리글 이름을 시트마다 알 필요가 없어 어느 시트에나 쓴다.
    /// </summary>
    public static int HeaderRowOf(IXLWorksheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        return sheet.Cell(1, 1).GetString().StartsWith("⚠ 이 파일은 VnTool", StringComparison.Ordinal)
            ? 2
            : 1;
    }

    /// <summary>1행에 안내문을 적는다 — 이미터가 머리글을 쓰기 전에 부른다.</summary>
    public static void Write(IXLWorksheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        IXLCell cell = sheet.Cell(1, 1);

        cell.SetValue(Text);
        cell.Style.Font.SetBold(true);
        cell.Style.Font.SetFontColor(XLColor.FromHtml("#9C5700"));
        cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FFF2CC"));
    }
}
