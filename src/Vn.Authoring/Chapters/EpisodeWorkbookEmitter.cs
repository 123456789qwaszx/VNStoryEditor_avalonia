using ClosedXML.Excel;

namespace Vn.Authoring.Chapters;

/// <summary>대본 워크북에 낼 대사 한 줄. 인덱스는 줄의 신원이다(G-5) — 행 번호가 아니다.</summary>
/// <param name="Index">C열 값. 오름차순일 필요는 없다(규격상 권고).</param>
/// <param name="Speaker">비면 지문이다. 빈 화자는 오류가 아니다.</param>
public sealed record EmittedEpisodeLine(int Index, string? Speaker, string Text);

/// <summary>
/// 프로젝트의 대사를 <b>대본 워크북 한 벌로 낸다</b> (R-B · 2026-09-15 —
/// <c>docs/work-orders/tool-owns-workbooks-orders.md</c>).
///
/// <b>성격이 Writer와 다르다.</b> <see cref="EpisodeWorkbookWriter"/>는 "셀 하나 고치기"이고
/// 원본이 엑셀이라는 전제 위에 섰다. 이쪽은 <b>문서 전체를 낸다</b> — 성격이
/// <c>YarnBundleEmitter</c>와 같아서 이름도 그렇게 간다. 뒤집기가 끝나면
/// 워크북은 <c>exported/</c>와 같은 편, 곧 <b>산출물</b>이다.
///
/// <b>덕분에 난제 둘이 사라진다</b> — 전체를 새로 쓰므로 <b>행 삽입·삭제라는 개념이 없고</b>,
/// 인덱스 충돌도 없다. 지시서가 0-b로 잡았던 일이 이 성격 변경 하나로 증발했다.
///
/// <b>v15 4열을 낸다</b>(<c>인덱스 · LineId · 화자 · 내용</c>). R-B 때는 리더가 여섯
/// 머리글을 다 맞춰야 시트를 찾아서 6열 그대로 냈는데, R-C가 리더·이행기와 함께 규격을
/// 줄이면서 그 유예가 끝났다. <c>LineId</c>는 유물이라 비운 채 낸다(툴이 쓰지 않는다 — v4).
///
/// ⚠ <b>1행 경고 배너도 R-C로 미뤘다</b>(지시서 §4.4의 둘째 항목). 리더의 머리글 행이
/// 1행이라, 배너를 넣으려면 머리글이 2행으로 내려가고 그것이 곧 규격 변경이다. 지금은
/// 시트 보호와 문서 속성으로만 알린다.
/// </summary>
public static class EpisodeWorkbookEmitter
{
    // ⚠ 1행은 안내문이다 (§4.4) — 머리글이 2행으로 내려간다. 리더는 상수로 들지 않고
    //    `WorkbookOutputNotice.HeaderRowOf`로 찾으므로 구판 파일도 그대로 읽힌다.
    private const int HeaderRow = 2;

    /// <summary>
    /// ⚠ <see cref="EpisodeWorkbookReader"/>·<see cref="EpisodeWorkbookWriter"/>의 배열과
    /// <b>한 글자도 달라선 안 된다</b> — 셋 다 이 머리글로 시트를 찾는다.
    /// </summary>
    private static readonly string[] Headers =
        ["인덱스", "LineId", "화자", "내용"];

    private const int ColumnIndex = 1;
    private const int ColumnLineId = 2;
    private const int ColumnSpeaker = 3;
    private const int ColumnText = 4;

    private const string SheetName = "대본";

    // 문구의 주인은 `WorkbookOutputNotice` 하나다 — 두 이미터가 같은 말을 해야 한다.

    /// <summary>줄 순서만 있을 때 인덱스를 매긴다 — 10·20·30(G-5). 사이에 끼울 자리를 남긴다.</summary>
    public static IReadOnlyList<EmittedEpisodeLine> Number(
        IEnumerable<(string? Speaker, string Text)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        return lines
            .Select((line, order) => new EmittedEpisodeLine(
                (order + 1) * 10, line.Speaker, line.Text))
            .ToList();
    }

    /// <summary>
    /// 대본 워크북 한 벌을 <paramref name="path"/>에 낸다. 이미 있으면 통째로 갈아 끼운다.
    ///
    /// <b>원자적이다</b> — 임시 파일을 완성한 뒤 교체하므로 <b>반쯤 쓴 워크북이 남지 않는다</b>
    /// (런타임의 세이브 규율과 같다: <i>"모든 쓰기는 임시 파일을 완성한 뒤 교체한다"</i>).
    /// 엑셀이 그 파일을 잡고 있으면 쓰지 않고 사유만 돌려준다.
    ///
    /// ⚠ 기존 파일은 <c>.bak</c>으로 남긴다. Writer가 .bak을 안 두는 것은 그쪽이 <b>두 칸
    /// 덮어쓰기</b>라서였다 — 이쪽은 문서 전체를 갈아 끼우므로 되돌릴 자리가 필요하다.
    /// </summary>
    public static ChapterWriteResult Emit(string path, IReadOnlyList<EmittedEpisodeLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        // 원자성·백업·잠금 판정의 주인은 WorkbookAtomicWrite 하나다 — 두 이미터가 같은
        // 규칙을 두 벌 갖지 않게 한다.
        return WorkbookAtomicWrite.Replace(path, () => Build(lines));
    }

    private static XLWorkbook Build(IReadOnlyList<EmittedEpisodeLine> lines)
    {
        var workbook = new XLWorkbook();

        try
        {
            workbook.Properties.Comments = WorkbookOutputNotice.Property;

            // 시트 이름은 고정 "대본" — 에피소드 Id로 지으면 개명 때 탭 이름만 낡는다
            // (EnsureWorkbook과 같은 이유). 리더는 머리글로 찾으므로 이름은 아무래도 좋다.
            IXLWorksheet sheet = workbook.AddWorksheet(SheetName);

            // ⛔ 안내문이 먼저다 (§4.4) — 사람이 열었을 때 왜 잠겼는지가 파일 안에 있어야 한다.
            WorkbookOutputNotice.Write(sheet);

            for (int column = 1; column <= Headers.Length; column++)
            {
                IXLCell cell = sheet.Cell(HeaderRow, column);
                cell.SetValue(Headers[column - 1]);
                cell.Style.Font.SetBold(true);
                cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E8EAED"));
            }

            // LineId(D)는 유물이다 — 툴이 쓰지 않는다(v4). 회색만 유지해 옛 파일과 같아 보이게 한다.
            sheet.Column(ColumnLineId).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F1F3F4"));
            sheet.Column(ColumnText).Width = 50;

            for (int order = 0; order < lines.Count; order++)
            {
                EmittedEpisodeLine line = lines[order];
                int row = HeaderRow + 1 + order;

                sheet.Cell(row, ColumnIndex).SetValue(line.Index);

                // 빈 화자는 지문이다 — 빈 칸으로 두는 것이 규격이다(0을 넣지 않는다).
                if (!string.IsNullOrEmpty(line.Speaker))
                {
                    sheet.Cell(row, ColumnSpeaker).SetValue(line.Speaker);
                }

                sheet.Cell(row, ColumnText).SetValue(line.Text);
            }

            sheet.SheetView.FreezeRows(HeaderRow);

            // 막는 것이 아니라 알리는 것이다 — 암호를 걸지 않는다. 사람이 풀고 고칠 수는
            // 있고, 고친 것이 다음 저장에 덮어쓰인다는 사실만 손에 닿게 한다.
            sheet.Protect();

            return workbook;
        }
        catch
        {
            workbook.Dispose();
            throw;
        }
    }

}
