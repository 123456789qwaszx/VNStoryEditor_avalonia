using System.Globalization;
using ClosedXML.Excel;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 챕터 워크북 한 벌을 <b>프로젝트 모델에서 낸다</b> (R-B · 2026-09-15 —
/// <c>docs/work-orders/tool-owns-workbooks-orders.md</c>).
///
/// <see cref="EpisodeWorkbookEmitter"/>와 짝이고 규칙도 같다 — 원자적 교체·<c>.bak</c>·잠금
/// 판정은 <see cref="WorkbookAtomicWrite"/> 하나가 갖는다. <b>성격이 Writer와 다르다</b>:
/// <see cref="ChapterWorkbookWriter"/>는 셀 하나를 고치는 외과수술이고, 이쪽은 문서 전체를
/// 낸다. 뒤집기가 끝나면 워크북은 산출물이다.
///
/// ⚠ <b>규격을 여기서 줄이지 않는다</b> — 리더의 머리글 배열과 한 글자도 달라선 안 된다.
/// 이미터가 낸 파일을 리더가 되읽을 수 있어야 임포트라는 되돌림 경로가 산다(지시서 §9).
///
/// ⚠ <b>조건은 원문 식으로 낸다</b> — <c>스탯·연산자·값</c> 세 칸으로 쪼개지 않고 `스탯`
/// 칸에 식을 통째로 적는다. 리더가 <i>"연산자·값을 비우면 스탯 칸 전체가 원문 조건식"</i>을
/// 이미 받아들이므로(복합식 탈출구), 쪼갰다 합치는 왕복에서 값이 상하지 않는다. 쪼개기는
/// 사람이 엑셀에서 고르기 쉬우라고 있는 것이고, 산출물에는 그 이유가 없다.
/// </summary>
public static class ChapterWorkbookEmitter
{
    // ⚠ 1행은 안내문이다 (§4.4) — 머리글이 2행으로 내려간다. 리더는 상수로 들지 않고
    //    `WorkbookOutputNotice.HeaderRowOf`로 찾으므로 구판 파일도 그대로 읽힌다.
    private const int HeaderRow = 2;

    // ⚠ 아래 다섯 배열은 <see cref="ChapterWorkbookReader"/>의 것과 같아야 한다 —
    //    리더가 머리글로 시트의 데이터 블록을 찾는다.
    private static readonly string[] EpisodeHeaders =
        ["EpisodeId", "대사엔트리", "제목", "장면ID", "이벤트키", "X", "Y", "메모"];

    private static readonly string[] EdgeHeaders =
        ["출발", "도착", "스탯변화", "선택지", "표시조건", "해금조건", "잠금 안내문", "자동"];

    private static readonly string[] ConditionHeaders = ["라벨", "스탯", "연산자", "값", "설명"];

    private static readonly string[] StatHeaders = ["타입", "스탯키", "표시명", "초기값", "최소", "최대"];

    private static readonly string[] ChoiceHeaders = ["인덱스", "대본", "메모"];

    // 문구의 주인은 `WorkbookOutputNotice` 하나다 — 두 이미터가 같은 말을 해야 한다.

    /// <summary>챕터 워크북 한 벌을 <paramref name="path"/>에 낸다. 이미 있으면 통째로 갈아 끼운다.</summary>
    public static ChapterWriteResult Emit(string path, ChapterGraphModel chapter)
    {
        ArgumentNullException.ThrowIfNull(chapter);

        return WorkbookAtomicWrite.Replace(path, () => Build(chapter));
    }

    private static XLWorkbook Build(ChapterGraphModel chapter)
    {
        var workbook = new XLWorkbook();

        try
        {
            workbook.Properties.Comments = WorkbookOutputNotice.Property;

            // 시트 순서가 곧 작업 순서다 — 구조(에피소드·간선)가 앞, 사전(선택지·조건)이
            // 가운데, 값(스탯)이 뒤. 리더는 이름으로 찾으므로 순서는 사람을 위한 것이다.
            WriteEpisodes(workbook.AddWorksheet(ChapterSheetNames.Episodes), chapter);
            WriteEdges(workbook.AddWorksheet(ChapterSheetNames.Edges), chapter);
            WriteChoices(workbook.AddWorksheet(ChapterSheetNames.Choices), chapter);
            WriteConditions(workbook.AddWorksheet(ChapterSheetNames.Conditions), chapter);
            WriteStats(workbook.AddWorksheet(ChapterSheetNames.Stats), chapter);

            // 겉모습(머리글 색·격자·고정·필터·열 너비)의 주인은 한 곳이다 — 만들 때와
            // 이행할 때가 같은 함수를 부르는 그 규율에 이미터도 낀다.
            ChapterWorkbookWriter.ApplyChapterChrome(workbook);

            foreach (IXLWorksheet sheet in workbook.Worksheets)
            {
                // 막는 것이 아니라 알리는 것이다 — 암호는 걸지 않는다.
                sheet.Protect();
            }

            return workbook;
        }
        catch
        {
            workbook.Dispose();
            throw;
        }
    }

    private static void WriteEpisodes(IXLWorksheet sheet, ChapterGraphModel chapter)
    {
        Header(sheet, EpisodeHeaders);

        int row = HeaderRow;

        foreach (ChapterEpisode episode in chapter.Episodes)
        {
            row++;
            Text(sheet, row, 1, episode.EpisodeId);
            Text(sheet, row, 2, episode.DialogueEntry);
            Text(sheet, row, 3, episode.Title);
            Text(sheet, row, 4, episode.SceneId);
            Text(sheet, row, 5, episode.EventKey);
            sheet.Cell(row, 6).SetValue(episode.X);
            sheet.Cell(row, 7).SetValue(episode.Y);
            Text(sheet, row, 8, episode.Memo);
        }
    }

    private static void WriteEdges(IXLWorksheet sheet, ChapterGraphModel chapter)
    {
        Header(sheet, EdgeHeaders);

        int row = HeaderRow;

        foreach (ChapterEdge edge in chapter.Edges)
        {
            row++;
            Text(sheet, row, 1, edge.FromEpisodeId);
            Text(sheet, row, 2, edge.ToEpisodeId);
            Text(sheet, row, 3, StatDeltaParser.Format(edge.StatChanges));
            Text(sheet, row, 4, edge.OptionLabel);
            Text(sheet, row, 5, edge.VisibleConditionLabel);
            Text(sheet, row, 6, edge.ConditionLabel);
            Text(sheet, row, 7, edge.LockedMessage);

            // ⚠ 자동은 <b>명시값</b>이다 (R2) — 비우면 "추측하지 않는다"는 규칙이 깨진다.
            //    리더가 TRUE/FALSE·1/0을 받으므로 글자로 적는다.
            Text(sheet, row, 8, edge.Auto ? "TRUE" : "FALSE");
        }
    }

    private static void WriteChoices(IXLWorksheet sheet, ChapterGraphModel chapter)
    {
        Header(sheet, ChoiceHeaders);

        int row = HeaderRow;

        foreach (ChapterChoiceOption choice in chapter.ChoiceOptions)
        {
            row++;
            Text(sheet, row, 1, choice.Index);
            Text(sheet, row, 2, choice.Text);
            Text(sheet, row, 3, choice.Memo);
        }
    }

    private static void WriteConditions(IXLWorksheet sheet, ChapterGraphModel chapter)
    {
        Header(sheet, ConditionHeaders);

        int row = HeaderRow;

        foreach (ChapterCondition condition in chapter.Conditions)
        {
            row++;
            Text(sheet, row, 1, condition.Label);

            // 원문 식 그대로 (위 ⚠ 참조). 연산자·값은 비운다.
            Text(sheet, row, 2, condition.Expression);
            Text(sheet, row, 5, condition.Description);
        }
    }

    private static void WriteStats(IXLWorksheet sheet, ChapterGraphModel chapter)
    {
        Header(sheet, StatHeaders);

        int row = HeaderRow;

        foreach (ChapterStat stat in chapter.Stats)
        {
            row++;

            // 빈 타입이 곧 int다 — 기본값을 굳이 적지 않는다(사람이 읽을 때 잡음이 된다).
            if (stat.Type == ChapterStatType.Bool)
            {
                Text(sheet, row, 1, "bool");
            }

            Text(sheet, row, 2, stat.Key);
            Text(sheet, row, 3, stat.DisplayName);
            sheet.Cell(row, 4).SetValue(stat.Initial);
            sheet.Cell(row, 5).SetValue(stat.Minimum);
            sheet.Cell(row, 6).SetValue(stat.Maximum);
        }
    }

    /// <summary>
    /// `스탯변화` 문법으로 되돌린다 — <see cref="StatDeltaParser"/>가 읽는 그 모양이다
    /// (`trust +2; met_willow true`). ⚠ 깃발은 증감이 아니라 <b>지정</b>이라 부호를 안 쓴다.
    /// </summary>
    private static void Header(IXLWorksheet sheet, string[] headers)
    {
        // ⛔ 안내문이 먼저다 (§4.4) — 겉모습을 입히는 쪽이 이 줄을 보고 머리글 행을 가른다.
        WorkbookOutputNotice.Write(sheet);

        for (int column = 1; column <= headers.Length; column++)
        {
            sheet.Cell(HeaderRow, column).SetValue(headers[column - 1]);
        }
    }

    /// <summary>빈 값은 <b>안 적는다</b> — 빈 문자열을 넣으면 리더가 "쓴 칸"으로 본다.</summary>
    private static void Text(IXLWorksheet sheet, int row, int column, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            sheet.Cell(row, column).SetValue(value);
        }
    }
}
