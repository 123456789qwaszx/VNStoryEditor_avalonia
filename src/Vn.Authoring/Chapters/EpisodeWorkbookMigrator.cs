using System.Globalization;
using ClosedXML.Excel;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 구판 대본 워크북(9열 — 인덱스·LineId·유형·태그·조건라벨·IN·OUT·화자·내용)을
/// v10(6열 — 인덱스·LineId·유형·조건라벨·화자·내용)으로 한 번에 이행한다.
///
/// <b>구간을 제자리로 옮긴다.</b> 구판은 <c>IF</c>의 <c>IN</c>이 표 아래쪽 어딘가의 구간
/// (<c>INPUT</c>…<c>OUT</c>)을 가리켰다. 이행은 그 구간의 행들을 <c>IF</c> 바로 아래로
/// 끌어와 <c>ENDIF</c> 행으로 닫는다 — 산출 결과는 같고, 범위가 눈에 보이게 된다.
///
/// <b>인덱스는 절대 다시 매기지 않는다.</b> 그 번호가 줄의 신원이고, 프로젝트의
/// <c>ExcelLineMap</c>이 그 번호로 LineId를 붙들고 있다. 다시 매기면 그 줄에 달아 둔 연출이
/// 통째로 끊긴다. 그래서 옮긴 뒤 번호가 뒤죽박죽이 되는데, v10에서 오름차순이 권고로
/// 내려간 것이 바로 이 때문이다(읽는 순서는 행 순서다).
///
/// <b>CHOICE·OPTION은 옮기지 않고 남긴다.</b> 선택지의 주인은 v9부터 챕터 `선택지`·`간선`
/// 시트다. 툴이 임의로 없애면 사람이 쓴 문구가 사라지므로, 유형을 그대로 두어 리더가
/// 그 행에서 "선택지 시트로 옮기세요"라고 말하게 한다. 그 옵션이 가리키던 구간의 행들도
/// 옵션 바로 아래로 끌어와 둔다 — 사라지는 대사가 없어야 한다.
///
/// <b>이행이 필요 없으면 파일에 손대지 않는다.</b> 쓰기 전 원본은 <c>.bak</c>으로 남는다.
/// </summary>
public static class EpisodeWorkbookMigrator
{
    public sealed record MigrationResult(bool Migrated, string? Failure)
    {
        public static MigrationResult NotNeeded { get; } = new(false, null);
    }

    private static readonly string[] LegacyHeaders =
        ["인덱스", "LineId", "유형", "태그", "조건라벨", "IN", "OUT", "화자", "내용"];

    private static readonly string[] Headers =
        ["인덱스", "LineId", "유형", "조건라벨", "화자", "내용"];

    private const int TemplateRows = 500;

    public static MigrationResult Migrate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var probe = new XLWorkbook(stream);

            if (FindLegacySheet(probe) is null)
            {
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
            IXLWorksheet sheet = FindLegacySheet(workbook)!;

            Rewrite(sheet, Reorder(Read(sheet)));

            workbook.SaveAs(path);
            return new MigrationResult(true, null);
        }
        catch (Exception exception)
        {
            return new MigrationResult(false,
                $"'{Path.GetFileName(path)}' 규격 이행에 실패했습니다" +
                $"(파일이 잠겨 있을 수 있습니다): {exception.Message}");
        }
    }

    private static IXLWorksheet? FindLegacySheet(XLWorkbook workbook) =>
        workbook.Worksheets.FirstOrDefault(sheet =>
            LegacyHeaders.Select((header, offset) =>
                string.Equals(Text(sheet, 1, offset + 1), header, StringComparison.Ordinal))
                .All(matches => matches));

    // ── 읽기 ────────────────────────────────────────────────────────────────

    /// <summary>구판 행 하나 — 이행에 필요한 만큼만.</summary>
    private sealed record Legacy(
        int Index, string LineId, string Kind, string Tag,
        string ConditionLabel, int? In, string Speaker, string Text);

    private static List<Legacy> Read(IXLWorksheet sheet)
    {
        var rows = new List<Legacy>();

        foreach (IXLRow row in sheet.RowsUsed().Where(item => item.RowNumber() > 1))
        {
            int number = row.RowNumber();
            string rawIndex = Text(sheet, number, 1);

            if (!int.TryParse(rawIndex, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                    out int index))
            {
                continue;
            }

            string kind = Text(sheet, number, 3);
            string tag = Text(sheet, number, 4);
            string label = Text(sheet, number, 5);
            string speaker = Text(sheet, number, 8);
            string text = Text(sheet, number, 9);

            // 인덱스만 있고 아무것도 없는 템플릿 자리는 옮길 것이 없다.
            if (kind.Length + tag.Length + label.Length + speaker.Length + text.Length == 0 &&
                Text(sheet, number, 2).Length == 0 && Text(sheet, number, 6).Length == 0)
            {
                continue;
            }

            int? into = int.TryParse(Text(sheet, number, 6), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out int parsed) ? parsed : null;

            rows.Add(new Legacy(
                index, Text(sheet, number, 2), kind, tag, label, into, speaker, text));
        }

        return rows;
    }

    // ── 옮기기 ──────────────────────────────────────────────────────────────

    /// <summary>새 규격의 행 하나. 인덱스는 구판 그대로다(ENDIF만 새로 받는다).</summary>
    private sealed record Moved(
        int Index, string LineId, string Kind, string ConditionLabel, string Speaker, string Text);

    private static List<Moved> Reorder(List<Legacy> rows)
    {
        // 구간: INPUT에서 시작해 OUT까지(양끝 포함) — 구판 리더와 같은 정의다.
        var sections = new Dictionary<int, List<Legacy>>();
        List<Legacy>? open = null;

        foreach (Legacy row in rows)
        {
            if (row.Tag == "INPUT")
            {
                open = [row];
                sections[row.Index] = open;
                continue;
            }

            if (open is null)
            {
                continue;
            }

            open.Add(row);

            if (row.Tag == "OUT")
            {
                open = null;
            }
        }

        var inSection = sections.Values
            .SelectMany(section => section)
            .Select(row => row.Index)
            .ToHashSet();

        var used = rows.Select(row => row.Index).ToHashSet();
        var placed = new HashSet<int>();
        var result = new List<Moved>();

        foreach (Legacy row in rows.Where(item => !inSection.Contains(item.Index)))
        {
            bool isIf = row.Kind == "IF";
            result.Add(Map(row));

            if (row.In is not { } target ||
                !sections.TryGetValue(target, out List<Legacy>? section) ||
                !placed.Add(target))
            {
                continue;
            }

            result.AddRange(section.Select(Map));

            if (isIf)
            {
                result.Add(new Moved(
                    NextFreeIndex(section[^1].Index, used), string.Empty, "ENDIF", string.Empty,
                    string.Empty, string.Empty));
            }
        }

        // 아무도 안 가리킨 구간도 버리지 않는다 — 구판에서도 산출물엔 없었지만 글은 글이다.
        foreach ((int start, List<Legacy> section) in sections.Where(pair => !placed.Contains(pair.Key)))
        {
            result.AddRange(section.Select(Map));
            placed.Add(start);
        }

        return result;
    }

    private static Moved Map(Legacy row) => new(
        row.Index,
        // IF는 라인이 아니다 — 구판 파일에 남아 있던 LineId는 여기서 턴다.
        row.Kind == "IF" ? string.Empty : row.LineId,
        row.Kind == "IF" ? "IF" : row.Kind,   // CHOICE·OPTION은 그대로 남겨 리더가 말하게 한다
        row.Kind == "IF" ? row.ConditionLabel : row.Kind == "OPTION" ? row.ConditionLabel : string.Empty,
        row.Speaker,
        row.Text);

    /// <summary>ENDIF에 줄 번호 — 구간의 마지막 다음부터 빈 번호를 찾는다(읽기 좋게 가까이).</summary>
    private static int NextFreeIndex(int after, HashSet<int> used)
    {
        int candidate = after + 1;

        while (!used.Add(candidate))
        {
            candidate++;
        }

        return candidate;
    }

    // ── 쓰기 ────────────────────────────────────────────────────────────────

    private static void Rewrite(IXLWorksheet sheet, List<Moved> rows)
    {
        // 통째로 지우고 다시 깐다. 열이 셋 줄고 행 순서가 바뀌므로 부분 수정이 더 위험하다.
        sheet.Clear(XLClearOptions.All);

        for (int column = 1; column <= Headers.Length; column++)
        {
            IXLCell cell = sheet.Cell(1, column);
            cell.SetValue(Headers[column - 1]);
            cell.Style.Font.SetBold(true);
            cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E8EAED"));
        }

        for (int offset = 0; offset < rows.Count; offset++)
        {
            Moved row = rows[offset];
            int number = offset + 2;

            sheet.Cell(number, 1).SetValue(row.Index);
            Set(sheet, number, 2, row.LineId);
            Set(sheet, number, 3, row.Kind);
            Set(sheet, number, 4, row.ConditionLabel);
            Set(sheet, number, 5, row.Speaker);
            Set(sheet, number, 6, row.Text);
        }

        // 남은 자리에 번호를 다시 깔아 둔다 — 새 템플릿과 같은 손맛(그 옆 칸만 채우면 된다).
        int next = rows.Count == 0 ? 10 : rows.Max(row => row.Index) + 10;

        for (int number = rows.Count + 2; number <= TemplateRows; number++)
        {
            sheet.Cell(number, 1).SetValue(next);
            next += 10;
        }

        sheet.Column(2).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F1F3F4"));
        sheet.Column(6).Width = 50;

        sheet.Range(2, 3, TemplateRows, 3).CreateDataValidation().List("\"대사,IF,ELSEIF,ENDIF\"", true);

        // 화자·조건라벨 드롭다운은 열 자리가 바뀌었다(H→E, 신설 D). 이행 뒤 첫 동기화에서
        // EpisodeLibrary.PushVocabulary가 새 자리에 다시 건다 — 여기서는 낡은 검증만 턴다.
        // (Clear가 이미 지웠으므로 할 일은 없고, 이 주석이 그 사실을 대신 말한다.)
    }

    private static void Set(IXLWorksheet sheet, int row, int column, string value)
    {
        if (value.Length > 0)
        {
            sheet.Cell(row, column).SetValue(value);
        }
    }

    private static string Text(IXLWorksheet sheet, int row, int column)
    {
        IXLCell cell = sheet.Cell(row, column);

        return cell.DataType switch
        {
            XLDataType.Blank => string.Empty,
            XLDataType.Number => cell.GetDouble().ToString(CultureInfo.InvariantCulture),
            XLDataType.Boolean => cell.GetBoolean() ? "TRUE" : "FALSE",
            _ => cell.GetString().Trim()
        };
    }
}
