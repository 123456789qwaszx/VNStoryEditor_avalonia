using ClosedXML.Excel;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 구판 챕터 워크북을 2026-08-16 규격으로 한 번에 이행한다.
///
/// 바뀐 것: ① 에피소드의 `인덱스` 열 삭제 ② 간선의 `스탯변화`가 C열로, `선택지 라벨`은
/// `선택지`로 ③ 조건의 `조건식` 한 칸이 `스탯`·`연산자`·`값` 세 칸으로(분해 불가한 식은
/// 스탯 칸에 원문 그대로 — 리더의 탈출구와 짝) ④ 스탯에 `타입` 열(선택) ⑤ 데이터 없는
/// 픽스처 시트 제거(임시 제거 — 데이터가 있으면 지우지 않는다).
///
/// <b>이행이 필요 없으면 파일에 손대지 않는다</b> — 챕터를 열 때마다 불려도 쓰기는 구판을
/// 처음 만난 그 한 번뿐이라 폴더 감시가 맴돌지 않는다. 쓰기 전 원본은 <c>.bak</c>으로 남는다.
/// </summary>
public static class ChapterWorkbookMigrator
{
    public sealed record MigrationResult(bool Migrated, string? Failure)
    {
        public static MigrationResult NotNeeded { get; } = new(false, null);
    }

    public static MigrationResult Migrate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // 필요 여부는 읽기로만 판정한다 — 최신 파일을 다시 저장하는 낭비를 만들지 않는다.
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var probe = new XLWorkbook(stream);

            if (!NeedsMigration(probe))
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

            MigrateEpisodes(workbook);
            MigrateEdges(workbook);
            MigrateEdgeLabelsToChoiceSheet(workbook); // v6 — 선택지 문구 → 선택지 시트
            MigrateConditions(workbook);
            MigrateStats(workbook);
            RemoveEmptyFixtures(workbook);
            ReapplyDropdowns(workbook);

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

    private static bool NeedsMigration(XLWorkbook workbook)
    {
        IXLWorksheet? fixtures = Find(workbook, ChapterSheetNames.Fixtures);

        return Header(workbook, ChapterSheetNames.Episodes, 3) == "인덱스" ||
               Header(workbook, ChapterSheetNames.Edges, 3) == "선택지 라벨" ||
               Header(workbook, ChapterSheetNames.Edges, 4) == "선택지" || // v5 — 문구가 아직 간선에 있다
               (Find(workbook, ChapterSheetNames.Edges) is not null &&
                Find(workbook, ChapterSheetNames.Choices) is null) ||
               Header(workbook, ChapterSheetNames.Conditions, 2) == "조건식" ||
               (Find(workbook, ChapterSheetNames.Stats) is not null &&
                Header(workbook, ChapterSheetNames.Stats, 6) != "타입") ||
               (fixtures is not null && fixtures.RowsUsed().Count() <= 1);
    }

    /// <summary>
    /// v6 (2026-08-16 소유자) — 선택지의 정본이 간선의 문구 칸에서 `선택지` 시트로 옮겨 간다.
    /// 간선 D열의 문구를 선택지 칸(출발·도착·인덱스·대본)으로 옮기고, D열은 `선택지수`가 된다.
    /// 문구 없던 간선도 칸 하나(빈 대본 = 보이지 않는 기본)를 받는다.
    /// </summary>
    private static void MigrateEdgeLabelsToChoiceSheet(XLWorkbook workbook)
    {
        if (Find(workbook, ChapterSheetNames.Edges) is not { } edges)
        {
            return;
        }

        bool labelColumn = Header(workbook, ChapterSheetNames.Edges, 4) == "선택지";
        IXLWorksheet? choices = Find(workbook, ChapterSheetNames.Choices);

        if (!labelColumn && choices is not null)
        {
            return; // 이미 v6다.
        }

        if (choices is null)
        {
            choices = workbook.AddWorksheet(ChapterSheetNames.Choices);
            string[] headers = ["출발", "도착", "인덱스", "대본", "메모"];

            for (int column = 1; column <= headers.Length; column++)
            {
                IXLCell cell = choices.Cell(1, column);
                cell.SetValue(headers[column - 1]);
                cell.Style.Font.SetBold(true);
                cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E8EAED"));
            }
        }

        var nextIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        int slotRow = (choices.LastRowUsed()?.RowNumber() ?? 1) + 1;

        foreach (IXLRow row in edges.RowsUsed().Skip(1))
        {
            string from = row.Cell(1).GetString().Trim();
            string to = row.Cell(2).GetString().Trim();

            if (from.Length == 0 || to.Length == 0)
            {
                continue;
            }

            string text = labelColumn ? row.Cell(4).GetString().Trim() : string.Empty;

            // 이미 칸이 있는 간선(재이행)은 건너뛴다 — 사람이 적은 대본을 두 번 만들지 않는다.
            bool hasSlot = choices.RowsUsed().Skip(1).Any(slot =>
                string.Equals(slot.Cell(1).GetString().Trim(), from, StringComparison.Ordinal) &&
                string.Equals(slot.Cell(2).GetString().Trim(), to, StringComparison.Ordinal));

            if (!hasSlot)
            {
                int index = (nextIndex.TryGetValue(from, out int last) ? last : 0) + 10;
                nextIndex[from] = index;

                choices.Cell(slotRow, 1).SetValue(from);
                choices.Cell(slotRow, 2).SetValue(to);
                choices.Cell(slotRow, 3).SetValue(index);

                if (text.Length > 0)
                {
                    choices.Cell(slotRow, 4).SetValue(text);
                }

                slotRow++;
            }

            if (labelColumn)
            {
                row.Cell(4).SetValue(1); // 문구 → 선택지수
            }
        }

        if (labelColumn)
        {
            edges.Cell(1, 4).SetValue("선택지수");
        }
    }

    private static void MigrateEpisodes(XLWorkbook workbook)
    {
        if (Find(workbook, ChapterSheetNames.Episodes) is not { } sheet ||
            Header(workbook, ChapterSheetNames.Episodes, 3) != "인덱스")
        {
            return;
        }

        // 인덱스는 안 쓰인다(소유자) — 열째로 지운다. `도달불가 허용`(옛 L)이 K로 따라온다.
        sheet.Column(3).Delete();
    }

    private static void MigrateEdges(XLWorkbook workbook)
    {
        if (Find(workbook, ChapterSheetNames.Edges) is not { } sheet ||
            Header(workbook, ChapterSheetNames.Edges, 3) != "선택지 라벨")
        {
            return;
        }

        // 옛 모양: 출발|도착|선택지 라벨|조건|잠금시 숨김|잠금 안내문|(스탯변화)
        bool hadStatColumn = string.Equals(
            sheet.Cell(1, 7).GetString().Trim(), "스탯변화", StringComparison.Ordinal);

        sheet.Column(3).InsertColumnsBefore(1); // 새 C — 스탯변화 자리. 나머지가 한 칸 밀린다.
        sheet.Cell(1, 3).SetValue("스탯변화");
        sheet.Cell(1, 4).SetValue("선택지");

        if (hadStatColumn)
        {
            // 옛 스탯변화(밀려서 지금 H=8)를 C로 옮기고 그 열은 지운다.
            foreach (IXLRow row in sheet.RowsUsed().Skip(1))
            {
                string value = row.Cell(8).GetString();

                if (value.Trim().Length > 0)
                {
                    row.Cell(3).SetValue(value);
                }
            }

            sheet.Column(8).Delete();
        }
    }

    private static void MigrateConditions(XLWorkbook workbook)
    {
        if (Find(workbook, ChapterSheetNames.Conditions) is not { } sheet ||
            Header(workbook, ChapterSheetNames.Conditions, 2) != "조건식")
        {
            return;
        }

        // 옛 모양: 라벨|조건식|설명 → 새 모양: 라벨|스탯|연산자|값|설명
        sheet.Column(3).InsertColumnsBefore(2); // 설명이 C→E로 밀린다.
        sheet.Cell(1, 2).SetValue("스탯");
        sheet.Cell(1, 3).SetValue("연산자");
        sheet.Cell(1, 4).SetValue("값");

        foreach (IXLRow row in sheet.RowsUsed().Skip(1))
        {
            string expression = row.Cell(2).GetString().Trim();

            if (expression.Length == 0)
            {
                continue;
            }

            if (ConditionExpressionParser.TryDecomposeSingle(
                    expression, out string statKey, out string operatorText, out string valueText))
            {
                row.Cell(2).SetValue(statKey);
                row.Cell(3).SetValue(operatorText);

                if (valueText.Length > 0)
                {
                    row.Cell(4).SetValue(valueText);
                }
            }
            // 분해 불가(cleared:·복합식) — 스탯 칸에 원문 그대로 남는다. 리더의 탈출구가 읽는다.
        }
    }

    private static void MigrateStats(XLWorkbook workbook)
    {
        if (Find(workbook, ChapterSheetNames.Stats) is not { } sheet ||
            Header(workbook, ChapterSheetNames.Stats, 6) == "타입")
        {
            return;
        }

        // F가 표에 들어오면서, F 옆(G)에 적어 둔 안내문이 "표 안의 모르는 열"이 된다 —
        // 표 밖의 글은 빈 칸 하나 뒤여야 하므로(리더 규칙) H로 한 칸 민다.
        string note = sheet.Cell(1, 7).GetString();

        if (note.Trim().Length > 0 && sheet.Cell(1, 8).GetString().Trim().Length == 0)
        {
            sheet.Cell(1, 8).SetValue(note);
            sheet.Cell(1, 7).Clear(XLClearOptions.Contents);
        }

        IXLCell cell = sheet.Cell(1, 6);
        cell.SetValue("타입");
        cell.Style.Font.SetBold(true);
        cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E8EAED"));
    }

    /// <summary>데이터가 없는(머리글뿐인) 픽스처 시트만 지운다 — 임시 제거이지 데이터 파기가 아니다.</summary>
    private static void RemoveEmptyFixtures(XLWorkbook workbook)
    {
        if (Find(workbook, ChapterSheetNames.Fixtures) is { } sheet &&
            sheet.RowsUsed().Count() <= 1)
        {
            workbook.Worksheets.Delete(sheet.Name);
        }
    }

    /// <summary>
    /// 검증(드롭다운)은 열 이동을 따라오지 못한다 — 전부 걷어내고 최신 규격으로 다시 깐다.
    /// 정의는 <see cref="ChapterWorkbookWriter.ApplyChapterDropdowns"/> 하나뿐이다(사본 금지).
    /// </summary>
    private static void ReapplyDropdowns(XLWorkbook workbook)
    {
        IXLWorksheet? episodes = Find(workbook, ChapterSheetNames.Episodes);
        IXLWorksheet? edges = Find(workbook, ChapterSheetNames.Edges);
        IXLWorksheet? conditions = Find(workbook, ChapterSheetNames.Conditions);
        IXLWorksheet? stats = Find(workbook, ChapterSheetNames.Stats);
        IXLWorksheet? choices = Find(workbook, ChapterSheetNames.Choices);

        if (episodes is null || edges is null || conditions is null || stats is null)
        {
            return; // 시트가 빠진 깨진 워크북 — 드롭다운보다 리더 진단이 먼저다.
        }

        foreach (IXLWorksheet? sheet in new[] { episodes, edges, conditions, stats, choices })
        {
            sheet?.DataValidations.Delete(_ => true);
        }

        ChapterWorkbookWriter.ApplyChapterDropdowns(episodes, edges, conditions, stats, choices);
    }

    private static IXLWorksheet? Find(XLWorkbook workbook, string name) =>
        workbook.Worksheets.FirstOrDefault(sheet =>
            string.Equals(sheet.Name, name, StringComparison.Ordinal));

    private static string Header(XLWorkbook workbook, string sheetName, int column) =>
        Find(workbook, sheetName)?.Cell(1, column).GetString().Trim() ?? string.Empty;
}
