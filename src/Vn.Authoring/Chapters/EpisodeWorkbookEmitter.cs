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
/// ⚠ <b>지금은 v14 6열 그대로 낸다</b>(유형·조건라벨·LineId는 비운 채). 규격을 여기서
/// 줄이지 않는 이유는 <see cref="EpisodeWorkbookReader"/>가 <b>여섯 머리글이 모두 맞아야</b>
/// 시트를 찾기 때문이다 — 지금 줄이면 이미터가 낸 파일을 리더가 못 읽고, 그러면
/// <b>임포트라는 안전망이 R-B에서 끊긴다</b>(지시서 §9). 조건 열 폐지는 R-C의 일이고,
/// 그때 리더·이행기와 함께 움직인다.
///
/// ⚠ <b>1행 경고 배너도 R-C로 미뤘다</b>(지시서 §4.4의 둘째 항목). 리더의 머리글 행이
/// 1행이라, 배너를 넣으려면 머리글이 2행으로 내려가고 그것이 곧 규격 변경이다. 지금은
/// 시트 보호와 문서 속성으로만 알린다.
/// </summary>
public static class EpisodeWorkbookEmitter
{
    private const int HeaderRow = 1;

    /// <summary>
    /// ⚠ <see cref="EpisodeWorkbookReader"/>·<see cref="EpisodeWorkbookWriter"/>의 배열과
    /// <b>한 글자도 달라선 안 된다</b> — 셋 다 이 머리글로 시트를 찾는다.
    /// </summary>
    private static readonly string[] Headers =
        ["유형", "조건라벨", "인덱스", "LineId", "화자", "내용"];

    private const int ColumnIndex = 3;
    private const int ColumnLineId = 4;
    private const int ColumnSpeaker = 5;
    private const int ColumnText = 6;

    private const string SheetName = "대본";

    private const string OutputNotice =
        "이 파일은 VnTool이 만든 산출물입니다. 여기서 고친 것은 반영되지 않고 다음 저장에 덮어쓰입니다.";

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
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(lines);

        // 임시 파일은 <b>같은 폴더</b>에 둔다 — File.Move가 볼륨을 넘으면 원자적이지 않다.
        string temporary = path + ".tmp";

        try
        {
            string? folder = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            // ⚠ <b>ClosedXML의 경로 SaveAs는 확장자를 검사한다</b> — `.tmp`로 끝나는 이름을
            // 거부한다(`Extension 'tmp' is not supported`). 그렇다고 임시 이름을 `.xlsx`로
            // 두면 폴더를 훑는 자리들이 그것을 대본으로 센다. 그래서 <b>스트림으로 받아
            // 바이트로 쓴다</b> — 스트림 오버로드에는 검사할 확장자가 없고, 파일 이름은
            // 우리가 고를 수 있게 된다.
            byte[] bytes;

            using (var memory = new MemoryStream())
            {
                using (XLWorkbook workbook = Build(lines))
                {
                    workbook.SaveAs(memory);
                }

                bytes = memory.ToArray();
            }

            File.WriteAllBytes(temporary, bytes);

            // 백업은 교체 직전에 — 임시 파일이 만들어지지 못한 경우까지 .bak을 굴리면
            // 아무 일도 없었는데 되돌릴 자리만 낡는다.
            if (File.Exists(path))
            {
                File.Copy(path, path + ".bak", overwrite: true);
            }

            File.Move(temporary, path, overwrite: true);

            return ChapterWriteResult.Ok;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Discard(temporary);

            return ChapterWriteResult.Locked(
                $"엑셀이 '{Path.GetFileName(path)}'를 열고 있어 툴이 쓰지 못했습니다 — " +
                "엑셀에서 그 파일을 닫으면 다시 냅니다.");
        }
        catch (Exception exception)
        {
            Discard(temporary);

            return ChapterWriteResult.Locked($"대본 워크북을 내지 못했습니다: {exception.Message}");
        }
    }

    private static XLWorkbook Build(IReadOnlyList<EmittedEpisodeLine> lines)
    {
        var workbook = new XLWorkbook();

        try
        {
            workbook.Properties.Comments = OutputNotice;

            // 시트 이름은 고정 "대본" — 에피소드 Id로 지으면 개명 때 탭 이름만 낡는다
            // (EnsureWorkbook과 같은 이유). 리더는 머리글로 찾으므로 이름은 아무래도 좋다.
            IXLWorksheet sheet = workbook.AddWorksheet(SheetName);

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

    /// <summary>임시 파일을 남기지 않는다. 지우다 실패해도 그것 때문에 결과가 바뀌지는 않는다.</summary>
    private static void Discard(string temporary)
    {
        try
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 원래 실패의 사유를 덮지 않는다.
        }
    }
}
