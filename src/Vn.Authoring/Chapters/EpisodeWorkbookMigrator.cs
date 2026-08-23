using System.Globalization;
using ClosedXML.Excel;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 옛 대본 워크북을 지금 규격(v13 — 인덱스·<b>유형</b>·LineId·조건라벨·화자·내용)으로 옮긴다.
///
/// 두 길이 있다:
///   ① <b>구판 9열</b>(인덱스·LineId·유형·태그·조건라벨·IN·OUT·화자·내용) — 통째로 다시 깐다.
///   ② <b>v10~v12의 6열</b>(LineId가 유형보다 앞) — <b>두 열만 맞바꾼다</b>(2026-08-24).
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

    /// <summary>지금 규격 (v13, 2026-08-24) — <b>유형이 LineId보다 앞</b>.</summary>
    private static readonly string[] Headers =
        ["인덱스", "유형", "LineId", "조건라벨", "화자", "내용"];

    /// <summary>
    /// v10~v12의 6열 — 같은 여섯 칸인데 <b>LineId가 유형보다 앞</b>이었다.
    /// 2026-08-24에 순서가 뒤집혔고(소유자), 그 파일들은 두 열만 맞바꾸면 된다.
    /// </summary>
    private static readonly string[] HeadersV10 =
        ["인덱스", "LineId", "유형", "조건라벨", "화자", "내용"];

    private const int ColumnIndex = 1;
    private const int ColumnKind = 2;
    private const int ColumnLineId = 3;
    private const int ColumnText = 6;

    private const int TemplateRows = 500;

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

            if (FindLegacySheet(probe) is null && FindV10Sheet(probe) is null)
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

            // 구판 9열은 통째로 다시 깐다. v10 6열은 <b>두 열만 맞바꾸면</b> 되므로
            // 행을 건드리지 않는다 — 옮길 이유가 없는 것을 옮기면 그만큼 틀릴 자리가 는다.
            if (FindLegacySheet(workbook) is { } legacy)
            {
                Rewrite(legacy, Reorder(Read(legacy)));
            }
            else
            {
                SwapKindAndLineId(FindV10Sheet(workbook)!);
            }

            workbook.SaveAs(path);

            // 방금 쓴 그 내용으로 판정을 기록한다 — 이행 직후 한 번 더 파고들 이유가 없다.
            WorkbookMigrationGate.MarkCurrent(path);
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
        FindByHeaders(workbook, LegacyHeaders);

    private static IXLWorksheet? FindV10Sheet(XLWorkbook workbook) =>
        FindByHeaders(workbook, HeadersV10);

    private static IXLWorksheet? FindByHeaders(XLWorkbook workbook, string[] headers) =>
        workbook.Worksheets.FirstOrDefault(sheet =>
            headers.Select((header, offset) =>
                string.Equals(Text(sheet, 1, offset + 1), header, StringComparison.Ordinal))
                .All(matches => matches));

    /// <summary>
    /// v10 → v13 — <b>유형과 LineId 두 열만 맞바꾼다</b> (2026-08-24).
    ///
    /// 행은 건드리지 않는다. 인덱스가 줄의 신원이고 프로젝트의 <c>ExcelLineMap</c>이 그
    /// 번호로 연출을 붙들고 있으므로, 옮길 이유가 없는 것을 옮기면 그만큼 틀릴 자리가 는다.
    ///
    /// ⚠ 서식도 함께 간다 — 회색 배경은 <b>LineId를 따라</b> C열로. 그 회색이 "여기는
    /// 유물이니 손대지 마세요"라는 표시라서, 열만 옮기고 색을 두면 유형 칸이 유물처럼 보인다.
    /// 드롭다운·빗장은 다음 어휘 밀기가 새 자리에 다시 건다(`EpisodeLibrary.PushVocabulary`).
    /// </summary>
    private static void SwapKindAndLineId(IXLWorksheet sheet)
    {
        int last = sheet.LastRowUsed()?.RowNumber() ?? 1;

        for (int row = 1; row <= Math.Max(last, TemplateRows); row++)
        {
            string lineId = Text(sheet, row, ColumnKind);       // 옛 B열 = LineId
            string kind = Text(sheet, row, ColumnLineId);       // 옛 C열 = 유형

            // ⚠ <b>덮어쓰기가 아니라 맞바꾸기다</b> — 빈 값도 써야 한다. `Set`은 빈 값을
            // 건너뛰므로(새로 깔 때는 그게 맞다) 여기서 쓰면 옛 값이 그 자리에 남는다:
            // 대사 행은 유형이 비어 있어서 B에 LineId가 <b>그대로 남고</b> C에도 같은 값이
            // 복사됐다. 견본 워크북에서 실제로 그렇게 났다.
            Overwrite(sheet, row, ColumnKind, kind);
            Overwrite(sheet, row, ColumnLineId, lineId);
        }

        // 머리글은 굵게·회색으로 다시 — 위 루프가 글자만 옮겼다.
        for (int column = 1; column <= Headers.Length; column++)
        {
            IXLCell cell = sheet.Cell(1, column);
            cell.SetValue(Headers[column - 1]);
            cell.Style.Font.SetBold(true);
            cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E8EAED"));
        }

        // 옛 자리(B)의 회색을 걷고 새 자리(C)에 입힌다.
        sheet.Column(ColumnKind).Style.Fill.SetBackgroundColor(XLColor.NoColor);
        sheet.Column(ColumnLineId).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F1F3F4"));

        // 낡은 자리의 검증을 턴다 — 새 자리의 것은 어휘 밀기가 건다.
        sheet.DataValidations.Delete(validation => validation.Ranges.Any(range =>
            range.RangeAddress.FirstAddress.ColumnNumber is ColumnKind or ColumnLineId));
    }

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
            Set(sheet, number, ColumnKind, row.Kind);       // v13 — 유형이 앞이다
            Set(sheet, number, ColumnLineId, row.LineId);
            Set(sheet, number, 4, row.ConditionLabel);
            Set(sheet, number, 5, row.Speaker);
            Set(sheet, number, ColumnText, row.Text);
        }

        // 남은 자리에 번호를 다시 깔아 둔다 — 새 템플릿과 같은 손맛(그 옆 칸만 채우면 된다).
        int next = rows.Count == 0 ? 10 : rows.Max(row => row.Index) + 10;

        for (int number = rows.Count + 2; number <= TemplateRows; number++)
        {
            sheet.Cell(number, 1).SetValue(next);
            next += 10;
        }

        sheet.Column(ColumnLineId).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F1F3F4"));
        sheet.Column(ColumnText).Width = 50;

        sheet.Range(2, ColumnKind, TemplateRows, ColumnKind)
            .CreateDataValidation().List("\"대사,IF,ELSEIF,ENDIF\"", true);

        // 화자·조건라벨 드롭다운은 열 자리가 바뀌었다(H→E, 신설 D). 이행 뒤 첫 동기화에서
        // EpisodeLibrary.PushVocabulary가 새 자리에 다시 건다 — 여기서는 낡은 검증만 턴다.
        // (Clear가 이미 지웠으므로 할 일은 없고, 이 주석이 그 사실을 대신 말한다.)
    }

    /// <summary>값이 있을 때만 적는다 — 새 표를 깔 때의 손이다(빈칸은 안 건드린다).</summary>
    private static void Set(IXLWorksheet sheet, int row, int column, string value)
    {
        if (value.Length > 0)
        {
            sheet.Cell(row, column).SetValue(value);
        }
    }

    /// <summary>
    /// 빈 값도 <b>그대로 적는다</b> — 자리를 맞바꿀 때의 손이다.
    /// <see cref="Set"/>을 쓰면 빈 쪽이 옛 값을 남겨 두 칸이 같아진다.
    /// </summary>
    private static void Overwrite(IXLWorksheet sheet, int row, int column, string value)
    {
        if (value.Length == 0)
        {
            sheet.Cell(row, column).Clear(XLClearOptions.Contents);
            return;
        }

        sheet.Cell(row, column).SetValue(value);
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
