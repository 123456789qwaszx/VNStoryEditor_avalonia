using ClosedXML.Excel;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 대본 워크북을 <b>현행 규격 v15로 이행한다</b> (2026-09-16 — R-C,
/// <c>docs/work-orders/tool-owns-workbooks-orders.md</c> §3).
///
/// <b>v15에서 규격이 줄었다</b>: <c>인덱스 · LineId · 화자 · 내용</c> 넷. <c>유형</c>·
/// <c>조건라벨</c>이 폐지됐고, 그 둘이 그리던 조건 블록(<c>IF</c>·<c>ELSEIF</c>·<c>ENDIF</c>)도
/// 함께 사라졌다.
///
/// <b>줄어드는 이행이라 앞의 모든 판이 한 길로 합쳐진다.</b> v10·v13·v14는 <i>같은 여섯
/// 낱말의 순열</i>이었고 구판 9열은 그 위에 세 칸이 더 있었는데, 이제 남길 칸이 넷뿐이므로
/// <b>머리글로 그 넷을 찾아 새 시트에 옮기면</b> 어느 판에서 오든 같은 코드가 처리한다.
/// 짝마다 맞바꾸기를 두던 시절의 경우 수가 통째로 사라졌다.
///
/// ⚠ <b>조건 블록 행은 옮기지 않고 버린다</b> — 그리고 <b>버렸다고 말한다</b>. 이 블록이
/// 쓸 수 있던 조건은 [2] 진행 스탯뿐이고 런타임이 대사에서 그것을 읽는 것을 금지했으므로,
/// 옮겨 봐야 내보내기가 거부한다(R4 이후 실제로 전부 막혀 있었다). 분기가 필요하면 챕터
/// `간선` 시트의 표시조건·해금조건으로 올린다.
///
/// ⚠ <b>인덱스는 절대 다시 매기지 않는다.</b> 그 번호가 줄의 신원이고 프로젝트의
/// <c>ExcelLineMap</c>이 그 번호로 LineId를 붙들고 있다. 다시 매기면 그 줄에 달아 둔 연출이
/// 통째로 끊긴다. 그래서 블록 행을 걷어낸 뒤 번호가 뜨문뜨문해지는데, 오름차순이 v10에서
/// 권고로 내려간 것이 바로 이런 경우를 위해서다(읽는 순서는 행 순서다).
///
/// ⚠ <b>CHOICE·OPTION 행은 남긴다.</b> 선택지의 주인은 v9부터 챕터 `선택지`·`간선` 시트이고,
/// 툴이 임의로 없애면 사람이 쓴 문구가 사라진다. 대사 행으로 옮겨 두면 리더가 내용을 그대로
/// 읽으므로 글이 보존된다 — 사라지는 대사가 없어야 한다.
///
/// <b>이행이 필요 없으면 파일에 손대지 않는다.</b> 쓰기 전 원본은 <c>.bak</c>으로 남는다.
/// </summary>
public static class EpisodeWorkbookMigrator
{
    public sealed record MigrationResult(bool Migrated, string? Failure)
    {
        public static MigrationResult NotNeeded { get; } = new(false, null);
    }

    /// <summary>현행 규격 (v15). 넷 다 <b>대사 줄의 것</b>이다 — 신원 둘, 내용 둘.</summary>
    private static readonly string[] Headers = ["인덱스", "LineId", "화자", "내용"];

    private const int ColumnIndex = 1;
    private const int ColumnLineId = 2;
    private const int ColumnSpeaker = 3;
    private const int ColumnText = 4;

    private const int HeaderRow = 1;
    private const int TemplateRows = 500;

    /// <summary>폐지된 블록 낱말 — 이 행은 옮기지 않는다.</summary>
    private static readonly string[] BlockWords = ["IF", "ELSEIF", "ELSE IF", "ENDIF", "END"];

    public static MigrationResult Migrate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // 내용이 그대로면 판정도 그대로다 (2026-08-24 성능). 대본은 챕터보다 수가 많아
        // 이쪽이 더 컸다 — 실측 64개에 717ms였고 전부 "필요 없음"이었다.
        if (WorkbookMigrationGate.IsKnownCurrent(path))
        {
            return MigrationResult.NotNeeded;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var probe = new XLWorkbook(stream);

            if (FindScriptSheet(probe) is null)
            {
                // 대본 시트를 못 찾으면 이행할 것이 없다 — 리더가 제 말로 짚게 둔다.
                WorkbookMigrationGate.MarkCurrent(path);
                return MigrationResult.NotNeeded;
            }

            if (IsCurrent(FindScriptSheet(probe)!))
            {
                WorkbookMigrationGate.MarkCurrent(path);
                return MigrationResult.NotNeeded;
            }
        }
        catch (Exception exception)
        {
            return new MigrationResult(false,
                $"'{Path.GetFileName(path)}'를 읽지 못해 규격 이행을 건너뜁니다: {exception.Message}");
        }

        try
        {
            using var memory = new MemoryStream();

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                stream.CopyTo(memory);
            }

            File.WriteAllBytes(path + ".bak", memory.ToArray());
            memory.Position = 0;

            using var workbook = new XLWorkbook(memory);

            IXLWorksheet sheet = FindScriptSheet(workbook)!;
            (List<Line> lines, int dropped) = Read(sheet);
            Rewrite(sheet, lines);

            workbook.SaveAs(path);
            WorkbookMigrationGate.MarkCurrent(path);

            return new MigrationResult(true, dropped == 0
                ? null
                : $"'{Path.GetFileName(path)}'의 조건 블록 {dropped}행을 걷었습니다 — 대사에서는 " +
                  "진행 스탯을 읽을 수 없습니다(분기는 챕터 `간선` 시트로). 이전 상태는 .bak입니다.");
        }
        catch (Exception exception)
        {
            return new MigrationResult(false,
                $"'{Path.GetFileName(path)}' 규격 이행에 실패했습니다" +
                $"(파일이 잠겨 있을 수 있습니다): {exception.Message}");
        }
    }

    /// <summary>옮길 대사 한 줄. 인덱스는 <b>그대로 나른다</b>(다시 매기지 않는다).</summary>
    private readonly record struct Line(string Index, string LineId, string Speaker, string Text);

    /// <summary>
    /// 대본 시트를 찾는다 — <b>머리글 이름으로</b>. `인덱스`·`화자`·`내용`이 있으면 어느 판이든
    /// 대본이다(v15는 그 셋을 다 갖고, 구판들도 이름은 같았다). 자리는 보지 않는다:
    /// 자리로 찾으면 순열이 바뀔 때마다 여기가 함께 낡는다.
    /// </summary>
    private static IXLWorksheet? FindScriptSheet(XLWorkbook workbook) =>
        workbook.Worksheets.FirstOrDefault(sheet =>
            Column(sheet, "인덱스") > 0 && Column(sheet, "화자") > 0 && Column(sheet, "내용") > 0);

    /// <summary>이미 v15인가 — 넷이 제자리에 있고 다섯째 머리글이 없다.</summary>
    private static bool IsCurrent(IXLWorksheet sheet)
    {
        for (int column = 1; column <= Headers.Length; column++)
        {
            if (!string.Equals(Text(sheet, HeaderRow, column), Headers[column - 1], StringComparison.Ordinal))
            {
                return false;
            }
        }

        // 다섯째 칸에 머리글이 남아 있으면 구판의 잔해다(유형·조건라벨이 뒤로 밀린 모양).
        return Text(sheet, HeaderRow, Headers.Length + 1).Length == 0;
    }

    /// <summary>머리글 이름으로 열 번호를 찾는다. 없으면 0.</summary>
    private static int Column(IXLWorksheet sheet, string header)
    {
        for (int column = 1; column <= 16; column++)
        {
            if (string.Equals(Text(sheet, HeaderRow, column), header, StringComparison.Ordinal))
            {
                return column;
            }
        }

        return 0;
    }

    /// <summary>
    /// 남길 넷만 거둔다. ⚠ <b>행을 통째로 읽고 나서 쓴다</b> — 같은 시트를 읽으며 쓰면
    /// 아직 안 읽은 칸을 덮는다(v14 순열 이행이 그 함정을 한 번 밟았다).
    /// </summary>
    private static (List<Line> Lines, int Dropped) Read(IXLWorksheet sheet)
    {
        int indexColumn = Column(sheet, "인덱스");
        int lineIdColumn = Column(sheet, "LineId");
        int speakerColumn = Column(sheet, "화자");
        int textColumn = Column(sheet, "내용");
        int kindColumn = Column(sheet, "유형");

        var lines = new List<Line>();
        int dropped = 0;

        foreach (IXLRow row in sheet.RowsUsed().Where(row => row.RowNumber() > HeaderRow))
        {
            int number = row.RowNumber();

            if (kindColumn > 0 &&
                BlockWords.Contains(Text(sheet, number, kindColumn), StringComparer.OrdinalIgnoreCase))
            {
                dropped++;
                continue;
            }

            var line = new Line(
                Text(sheet, number, indexColumn),
                lineIdColumn > 0 ? Text(sheet, number, lineIdColumn) : string.Empty,
                speakerColumn > 0 ? Text(sheet, number, speakerColumn) : string.Empty,
                textColumn > 0 ? Text(sheet, number, textColumn) : string.Empty);

            // 인덱스만 있고 아무것도 안 쓴 행은 템플릿이 깔아 둔 자리다 — 아래에서 다시 깐다.
            if (line.Speaker.Length == 0 && line.Text.Length == 0)
            {
                continue;
            }

            lines.Add(line);
        }

        return (lines, dropped);
    }

    /// <summary>시트를 v15 모양으로 다시 깐다 — 거둔 줄을 순서 그대로 놓는다.</summary>
    private static void Rewrite(IXLWorksheet sheet, List<Line> lines)
    {
        sheet.Clear(XLClearOptions.All);

        for (int column = 1; column <= Headers.Length; column++)
        {
            IXLCell cell = sheet.Cell(HeaderRow, column);
            cell.SetValue(Headers[column - 1]);
            cell.Style.Font.SetBold(true);
            cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E8EAED"));
        }

        int target = HeaderRow;

        foreach (Line line in lines)
        {
            target++;
            Set(sheet, target, ColumnIndex, line.Index);
            Set(sheet, target, ColumnLineId, line.LineId);
            Set(sheet, target, ColumnSpeaker, line.Speaker);
            Set(sheet, target, ColumnText, line.Text);
        }

        // 템플릿 번호를 남은 자리에 다시 깐다 — 인덱스 없는 행은 표의 일부가 아니라서,
        // 시트에서 그냥 아래로 타이핑하면 대사가 조용히 버려지는 함정이 실제로 있었다.
        // ⚠ 거둔 줄의 번호는 건드리지 않는다(신원이다). 그 아래부터 이어 깐다.
        for (int row = target + 1; row <= TemplateRows; row++)
        {
            sheet.Cell(row, ColumnIndex).SetValue((row - HeaderRow) * 10);
        }

        sheet.Column(ColumnLineId).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F1F3F4"));
        sheet.Column(ColumnText).Width = 50;
    }

    private static void Set(IXLWorksheet sheet, int row, int column, string value)
    {
        if (value.Length > 0)
        {
            sheet.Cell(row, column).SetValue(value);
        }
    }

    private static string Text(IXLWorksheet sheet, int row, int column) =>
        column <= 0 ? string.Empty : sheet.Cell(row, column).GetString().Trim();
}
