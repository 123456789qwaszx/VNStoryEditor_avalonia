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

    /// <summary>
    /// 대본 폴더의 뿌리 <c>episodes/</c>. <b>여기에 파일을 바로 두지 않는다</b> —
    /// 워크북은 챕터별 하위 폴더에 산다(<see cref="FolderFor(string?, string)"/>).
    /// 이 경로는 감시(하위 폴더 포함)와 구판 파일 입양에만 쓴다.
    /// </summary>
    public static string? FolderFor(string? projectManifestPath)
    {
        if (string.IsNullOrWhiteSpace(projectManifestPath))
        {
            return null;
        }

        string? root = Path.GetDirectoryName(Path.GetFullPath(projectManifestPath));
        return root is null ? null : Path.Combine(root, FolderName);
    }

    /// <summary>
    /// 그 <b>챕터의</b> 대본 폴더 — <c>episodes/{ChapterId}/</c> (2026-08-16 소유자 보고).
    ///
    /// EpisodeId는 챕터 안에서만 유일하다. 뿌리에 평평하게 두면 다른 챕터의 같은 이름이
    /// 한 파일을 공유해, 이 챕터의 노드를 눌렀는데 저 챕터의 원고가 열린다(실사례).
    /// 챕터 워크북이 <c>chapters/{ChapterId}.xlsx</c>인 것과 같은 결로 나눈다.
    /// </summary>
    public static string? FolderFor(string? projectManifestPath, string chapterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chapterId);

        return FolderFor(projectManifestPath) is { } root
            ? Path.Combine(root, chapterId)
            : null;
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

    /// <summary><see cref="AdoptFlatWorkbook"/> 한 번의 결과. 실패는 예외가 아니라 사유다.</summary>
    /// <param name="Adopted">뿌리에 있던 구판 파일을 이 챕터 폴더로 옮겼으면 참.</param>
    public sealed record FlatAdoption(bool Adopted, string? Problem)
    {
        public static FlatAdoption None { get; } = new(false, null);
    }

    /// <summary>
    /// 구판(평평한 <c>episodes/{Id}.xlsx</c>) 원고를 그 챕터 폴더로 입양한다 (2026-08-16).
    ///
    /// <b>주인이 하나일 때만 옮긴다.</b> 여러 챕터가 같은 EpisodeId를 쓰면 그 파일이 어느
    /// 챕터의 원고인지 알 수 없다 — 임의로 한쪽에 주면 다른 쪽 원고가 사라진 것처럼 보인다.
    /// 그런 경우엔 손대지 않고 사람에게 말한다(규칙 14).
    /// </summary>
    /// <param name="claimants">그 EpisodeId를 쓰는 챕터 수 — 호출자가 챕터 목록에서 센다.</param>
    public static FlatAdoption AdoptFlatWorkbook(
        string root, string chapterId, string episodeId, int claimants)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return FlatAdoption.None;
        }

        string chapterFolder = Path.Combine(root, chapterId);

        // 이 챕터에 이미 원고가 있으면 입양할 것이 없다.
        if (FindExisting(chapterFolder, episodeId) is not null)
        {
            return FlatAdoption.None;
        }

        // 뿌리에 평평하게 놓인 옛 파일 — 하위 폴더는 보지 않는다.
        string? flat = Directory.EnumerateFiles(root, "*.xls*", SearchOption.TopDirectoryOnly)
            .Where(candidate =>
            {
                string name = Path.GetFileName(candidate);

                return !name.StartsWith("~$", StringComparison.Ordinal) &&
                       string.Equals(Normalize(Path.GetFileNameWithoutExtension(name)),
                           Normalize(episodeId), StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (flat is null)
        {
            return FlatAdoption.None;
        }

        if (claimants > 1)
        {
            return new FlatAdoption(false,
                $"'{Path.GetFileName(flat)}'를 어느 챕터의 원고로 볼지 알 수 없습니다 — " +
                $"'{episodeId}'를 쓰는 챕터가 {claimants}개입니다. " +
                $"episodes/{chapterId}/ 로 직접 옮기거나 이름을 나눠 주세요.");
        }

        try
        {
            Directory.CreateDirectory(chapterFolder);
            File.Move(flat, Path.Combine(chapterFolder, Path.GetFileName(flat)));
            return new FlatAdoption(true, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new FlatAdoption(false,
                $"'{Path.GetFileName(flat)}'를 episodes/{chapterId}/ 로 옮기지 못했습니다" +
                $"(엑셀·시트가 열고 있을 수 있습니다): {exception.Message}");
        }
    }

    /// <summary>
    /// 에피소드 개명을 대본 파일이 따라간다. 파일을 옛 이름에 버려두면 원고가 고아가 되고,
    /// 새 이름을 더블클릭하는 순간 빈 워크북이 하나 더 생긴다(실사례). 확장자는 지금 것을
    /// 그대로 유지한다(.xlsm이면 .xlsm으로) — 내용은 건드리지 않는 이동뿐이다.
    /// </summary>
    /// <returns>실패 사유. null이면 성공(옮길 파일이 없던 경우 포함 — 아직 대본 전이면 정상).</returns>
    public static string? RenameWorkbook(string folder, string oldId, string newId)
    {
        string? source = FindExisting(folder, oldId);

        if (source is null)
        {
            return null; // 대본을 아직 안 만들었다 — 옮길 것이 없다.
        }

        if (FindExisting(folder, newId) is { } taken)
        {
            return $"'{Path.GetFileName(taken)}'가 이미 있어 대본 파일을 옮기지 못했습니다. " +
                   "파일을 먼저 정리해 주세요.";
        }

        string target = Path.Combine(folder, newId + Path.GetExtension(source));

        try
        {
            File.Move(source, target);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"대본 파일을 옮기지 못했습니다(엑셀·시트가 열고 있을 수 있습니다): {exception.Message} — " +
                   $"파일을 닫고 '{Path.GetFileName(source)}'를 '{newId}'로 직접 바꿔 주세요.";
        }
    }

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
    /// <param name="speakers">
    /// 챕터 `화자` 시트의 등록 이름들 (2026-08-16). 있으면 화자 열(E)에 드롭다운을 깐다 —
    /// 목록은 숨김 시트가 담고, 검증은 조언일 뿐이라 목록 밖 이름도 그대로 적을 수 있다
    /// (편의 기능이 없다고 원고를 못 쓰게 하지 않는다).
    /// </param>
    /// <param name="conditionLabels">
    /// 챕터 `조건` 시트의 라벨들 (2026-08-17 소유자). 조건라벨 열(D)에 같은 방식으로 깐다 —
    /// 이쪽은 <b>오타가 곧 오류</b>라(리더가 미등록 라벨을 잡는다) 드롭다운의 값이 더 크다.
    /// </param>
    /// <returns>새로 만들었으면 true.</returns>
    public static bool EnsureWorkbook(
        string folder,
        string episodeId,
        IReadOnlyList<string>? speakers = null,
        IReadOnlyList<string>? conditionLabels = null)
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

        // 시트 이름은 고정 "대본" — 에피소드 Id로 지으면 개명 때 파일만 옮겨지고(내용 불변,
        // v4) 탭 이름이 옛 Id로 남아 진단 메시지에 낡은 이름이 찍힌다(실사례). 리더는
        // 어차피 머리글로 시트를 찾으므로 이름은 아무래도 좋다 — 그러면 안 낡는 이름이 낫다.
        IXLWorksheet sheet = workbook.AddWorksheet("대본");

        // 6열 (v14, 2026-08-24) — 왼쪽 두 칸이 <b>제어 행의 메타데이터</b>, 오른쪽 네 칸이
        // <b>대사 줄</b>이다. 리더의 배열과 한 글자도 달라선 안 된다(시트를 찾는 근거다).
        string[] headers = ["유형", "조건라벨", "인덱스", "LineId", "화자", "내용"];

        for (int column = 1; column <= headers.Length; column++)
        {
            IXLCell cell = sheet.Cell(1, column);
            cell.SetValue(headers[column - 1]);
            cell.Style.Font.SetBold(true);
            cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E8EAED"));
        }

        // 유형 드롭다운과 블록 행 빗장은 ApplyVocabulary가 함께 건다 (v13) —
        // 옛 파일에도 같은 손이 닿아야 해서 한 자리에 모았다.

        // 시트 보호는 걸지 않는다 (v4). 보호의 유일한 이유였던 LineId 열을 툴이 더는
        // 쓰지 않는다 — 행 신원은 프로젝트의 ExcelLineMap이 갖는다. 보호가 없으면 구글
        // 시트 같은 외부 편집기가 재저장할 때 깨질 것도 하나 줄어든다. 그 열은 유물로 남아
        // 회색 배경만 유지한다 (v14에서 C→D).
        sheet.Column(LineIdColumn).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F1F3F4"));

        // 인덱스를 미리 다 깔아 준다 (10·20·30 방식, G-5). 작가는 번호를 신경 쓰지 않고
        // 그 옆 칸(화자·내용)만 채우면 된다 — 인덱스 없는 행은 표의 일부가 아니라서,
        // 시트에서 그냥 아래로 타이핑하면 대사가 조용히 버려지는 함정이 실제로 있었다.
        // 사이에 끼울 때만 사람이 15 같은 빈 숫자를 적는다(그래서 십 단위다).
        //
        // ⚠ v14에서 인덱스는 <b>대사 줄만</b> 갖는데 이 깔기는 행을 가리지 않는다 — 아직
        // 아무 유형도 없는 빈 시트라 가릴 수가 없다. 사람이 그 자리에 IF를 치면 번호가
        // 남는데, 그건 <b>툴이 동기화에서 지운다</b>(소유자 결정 —
        // `EpisodeWorkbookWriter.ClearBlockRowIndexes`). 깔기를 그만두는 쪽이 아니라
        // 치우는 쪽을 고른 이유는, 저 함정이 실제로 사람을 물었기 때문이다.
        for (int row = 2; row <= TemplateRows; row++)
        {
            sheet.Cell(row, IndexColumn).SetValue((row - 1) * 10);
        }

        sheet.Column(TextColumn).Width = 50;   // 내용

        ApplyVocabulary(workbook, sheet, speakers, conditionLabels);

        workbook.SaveAs(path);
        return true;
    }

    // ── 어휘 드롭다운 (2026-08-16 화자 · 2026-08-17 조건라벨) ──────────────
    //
    // 챕터 시트에 등록한 낱말이 대본의 그 열에 드롭다운으로 선다: `화자` 시트 → 화자 열(E),
    // `조건` 시트의 라벨 → 조건라벨 열(D). 목록은 워크북 안의 숨김 시트가 담는다 — 외부
    // 파일 참조는 엑셀·구글 시트에서 깨지기 때문이다. 챕터의 목록이 바뀌면
    // <see cref="PushVocabulary"/>가 숨김 시트만 갈아 끼운다. 이것이 v4("만든 뒤 손대지
    // 않는다")의 유일한 예외이며, 예외의 폭은 정확히 숨김 시트와 검증 정의까지다 —
    // <b>대본 행은 절대 쓰지 않는다.</b>

    /// <summary>화자 목록을 담는 숨김 시트. 이 이름이 곧 신원이다 — 갱신이 이 시트만 만진다.</summary>
    public const string SpeakerListSheetName = "화자목록";

    /// <summary>조건 라벨 목록을 담는 숨김 시트 (2026-08-17).</summary>
    public const string ConditionListSheetName = "조건목록";

    // v14 (2026-08-24) — 구조 두 칸이 앞, 대사 네 칸이 뒤.
    private const int KindColumn = 1;         // A열 — §3.2의 유형 (v14에서 B→A)
    private const int ConditionColumn = 2;    // B열 — §3.2의 조건라벨 (v14에서 D→B)
    private const int IndexColumn = 3;        // C열 — 대사 줄의 번호 (v14에서 A→C)
    private const int LineIdColumn = 4;       // D열 — 유물. 사람도 툴도 안 쓴다 (v14에서 C→D)
    private const int SpeakerColumn = 5;      // E열 — §3.2의 화자 (v10에서 H→E)
    private const int TextColumn = 6;         // F열 — §3.2의 내용
    private const int ListRows = 200;         // 드롭다운이 가리키는 범위. 이보다 많으면 나눌 일이다.

    /// <summary>
    /// `유형` 드롭다운의 정본 낱말. <b>규격이 늘면 옛 파일에도 와야 한다</b> — 2026-08-17
    /// 소유자 보고("유형이 여전히 IF와 END밖에 없어")의 정체가 이것이었다. 화자·조건라벨은
    /// 숨김 시트를 갈아 끼우면 따라오는데, 이 목록은 낱말이 파일에 그대로 굳어서 만들 때
    /// 한 번 박히고 끝이었다. 이제 동기화마다 대조해 다르면 다시 건다.
    /// </summary>
    private const string KindList = "\"대사,IF,ELSEIF,ENDIF\"";

    /// <summary>어휘 한 가지 — 숨김 시트 하나와 그것이 조언하는 대본 열 하나.</summary>
    private sealed record Vocabulary(string SheetName, int Column);

    private static readonly Vocabulary Speakers = new(SpeakerListSheetName, SpeakerColumn);
    private static readonly Vocabulary Conditions = new(ConditionListSheetName, ConditionColumn);

    /// <summary>
    /// 숨김 목록 시트를 채우고(없으면 만들고) 그 열에 조언 드롭다운을 건다.
    /// 검증은 경고를 띄우지 않는다 — 목록 밖 값도 사람이 적을 수 있어야 한다(미등록 화자는
    /// 동기화 보고가, 미등록 조건라벨은 리더가 각자 다룬다).
    /// </summary>
    private static void ApplyVocabulary(
        XLWorkbook workbook,
        IXLWorksheet scriptSheet,
        IReadOnlyList<string>? speakers,
        IReadOnlyList<string>? conditionLabels)
    {
        ApplyKindList(scriptSheet);

        if (speakers is { Count: > 0 })
        {
            ApplyList(workbook, scriptSheet, Speakers, speakers);
        }

        if (conditionLabels is { Count: > 0 })
        {
            ApplyList(workbook, scriptSheet, Conditions, conditionLabels);
        }
    }

    /// <summary>
    /// `유형` 드롭다운을 정본 낱말로 다시 건다 — 이미 걸린 것이 있어도 갈아 끼운다.
    /// 화자·조건라벨과 달리 이 목록은 <b>낱말이 파일에 굳으므로</b>, 규격이 늘 때 옛 파일에
    /// 오게 하려면 여기서 밀어 넣는 수밖에 없다. v4의 예외(숨김 시트와 검증 정의)에 든다.
    /// </summary>
    private static void ApplyKindList(IXLWorksheet sheet)
    {
        sheet.DataValidations.Delete(validation => validation.Ranges.Any(range =>
            range.RangeAddress.FirstAddress.ColumnNumber == KindColumn));

        sheet.Range(2, KindColumn, TemplateRows, KindColumn)
            .CreateDataValidation()
            .List(KindList, inCellDropdown: true);

        BlockRowGuard(sheet, IndexColumn,
            "이 행은 IF·ELSEIF·ENDIF입니다 — 인덱스는 플레이어에게 전달되는 " +
            "대사의 순번이라 구조를 그리는 행은 갖지 않습니다(v14).");

        BlockRowGuard(sheet, LineIdColumn,
            "이 행은 IF·ELSEIF·ENDIF입니다 — 라인이 아니라서 LineId를 가질 수 없습니다.");

        BlockRowGuard(sheet, TextColumn,
            "이 행은 IF·ELSEIF·ENDIF입니다 — 블록의 흐름만 그립니다. " +
            "대사는 그 위나 아래의 자기 행에 적어 주세요.");
    }

    /// <summary>
    /// <b>블록 행에는 못 적게</b> 엑셀이 먼저 막는다 (2026-08-24 소유자: "If,ElseIf,EndIf일
    /// 경우 LineId가 생성되면 안돼 … 화자와 내용도 안 적게 막아줘").
    ///
    /// 유형(B열)이 IF·ELSEIF·ENDIF면 그 행의 이 칸은 비어 있어야 한다. 빈칸은 언제나
    /// 통과한다(<c>IgnoreBlanks</c>) — 막는 것은 <em>적는 것</em>뿐이다.
    ///
    /// 걸리는 칸은 셋 — <b>인덱스(C) · LineId(D) · 내용(F)</b>.
    ///
    /// ⚠ <b>화자(E열)에는 못 건다.</b> 엑셀은 한 칸에 검증을 하나만 허용하는데 그 열은
    /// 이미 화자 드롭다운이 쓰고 있다. 둘 중 하나를 골라야 한다면 <b>드롭다운이 이긴다</b> —
    /// 그건 대사 줄마다 쓰는 것이고 이 빗장은 어쩌다 한 번이다. 화자는 리더가 오류로
    /// 짚는다 — 그쪽이 더 센 빗장이다(붙여넣기로도 못 빠져나간다). 여기 검증은
    /// <b>실수를 손에서 막는</b> 앞잡이일 뿐이고, 규칙의 주인은 언제나 리더다.
    /// </summary>
    private static void BlockRowGuard(IXLWorksheet sheet, int column, string message)
    {
        sheet.DataValidations.Delete(validation => validation.Ranges.Any(range =>
            range.RangeAddress.FirstAddress.ColumnNumber == column));

        IXLDataValidation guard = sheet
            .Range(2, column, TemplateRows, column)
            .CreateDataValidation();

        // 유형이 비었거나 `대사`일 때만 적을 수 있다. 행 번호는 상대참조라 엑셀이 행마다 민다.
        guard.Custom($"=OR(${KindLetter}2=\"\",${KindLetter}2=\"대사\")");
        guard.IgnoreBlanks = true;
        guard.ErrorStyle = XLErrorStyle.Stop;
        guard.ErrorTitle = "블록 행입니다";
        guard.ErrorMessage = message;
        guard.ShowErrorMessage = true;
    }

    /// <summary>수식에 쓰는 유형 열의 글자 — 열이 옮겨 가면 여기도 따라온다.</summary>
    private static string KindLetter => XLHelper.GetColumnLetterFromNumber(KindColumn);

    /// <summary>
    /// 유형 드롭다운과 <b>블록 행 빗장</b>이 규격대로 걸려 있는가. 하나라도 없으면 옛
    /// 파일이므로 <see cref="ApplyVocabulary"/>가 다시 건다 — v4의 예외(숨김 시트와 검증
    /// 정의)에 든다.
    /// </summary>
    private static bool KindListMatches(IXLWorksheet sheet) =>
        sheet.DataValidations.Any(validation =>
            validation.Ranges.Any(range =>
                range.RangeAddress.FirstAddress.ColumnNumber == KindColumn) &&
            string.Equals(validation.Value, KindList, StringComparison.Ordinal)) &&
        HasBlockRowGuard(sheet, IndexColumn) &&
        HasBlockRowGuard(sheet, LineIdColumn) &&
        HasBlockRowGuard(sheet, TextColumn);

    private static bool HasDropdown(IXLWorksheet sheet, int column) =>
        sheet.DataValidations.Any(validation =>
            validation.Ranges.Any(range =>
                range.RangeAddress.FirstAddress.ColumnNumber == column) &&
            validation.AllowedValues == XLAllowedValues.List);

    private static bool HasBlockRowGuard(IXLWorksheet sheet, int column) =>
        sheet.DataValidations.Any(validation =>
            validation.Ranges.Any(range =>
                range.RangeAddress.FirstAddress.ColumnNumber == column) &&
            validation.AllowedValues == XLAllowedValues.Custom);

    private static void ApplyList(
        XLWorkbook workbook, IXLWorksheet scriptSheet, Vocabulary vocabulary, IReadOnlyList<string> values)
    {
        IXLWorksheet list = workbook.Worksheets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, vocabulary.SheetName, StringComparison.Ordinal))
            ?? workbook.AddWorksheet(vocabulary.SheetName);

        list.Visibility = XLWorksheetVisibility.Hidden;
        list.Column(1).Clear(XLClearOptions.Contents);

        for (int index = 0; index < values.Count && index < ListRows; index++)
        {
            list.Cell(index + 1, 1).SetValue(values[index]);
        }

        // 그 열에 이미 검증이 있으면 그대로 둔다(범위 참조라 목록 시트만 갈면 따라온다).
        if (scriptSheet.DataValidations.Any(validation =>
                validation.Ranges.Any(existing =>
                    existing.RangeAddress.FirstAddress.ColumnNumber == vocabulary.Column)))
        {
            return;
        }

        IXLDataValidation created = scriptSheet
            .Range(2, vocabulary.Column, TemplateRows, vocabulary.Column)
            .CreateDataValidation();

        created.List($"='{vocabulary.SheetName}'!$A$1:$A${ListRows}", inCellDropdown: true);
        created.ShowErrorMessage = false; // 조언일 뿐 — 목록 밖 값도 사람이 적을 수 있다.
    }

    /// <summary><see cref="PushVocabulary"/> 한 번의 결과. 실패는 예외가 아니라 사유다.</summary>
    public sealed record VocabularyPush(bool Changed, string? Failure)
    {
        public static VocabularyPush Unchanged { get; } = new(false, null);
    }

    /// <summary>
    /// 챕터의 화자·조건라벨 목록을 기존 워크북의 숨김 시트에 반영한다. <b>둘 다 이미 같으면
    /// 파일에 손대지 않는다</b>(수정 시각 불변) — 챕터를 열 때마다 불려도 실제 쓰기는 목록이
    /// 바뀐 그 순간뿐이다. 쓰기 전 원본을 <c>.bak</c>으로 남긴다(원고 파일이므로).
    /// 워크북이 아직 없으면 할 일이 없다 — 생성 때 목록을 받는다.
    ///
    /// 둘을 <b>한 번의 쓰기</b>로 넣는다. 따로 저장하면 원고 파일을 두 번 열고 .bak도 두 번
    /// 덮인다 — 두 번째 .bak은 첫 번째 저장 직후라 되돌릴 자리로 쓸모가 없다.
    /// </summary>
    public static VocabularyPush PushVocabulary(
        string folder,
        string episodeId,
        IReadOnlyList<string> speakers,
        IReadOnlyList<string> conditionLabels)
    {
        ArgumentNullException.ThrowIfNull(speakers);
        ArgumentNullException.ThrowIfNull(conditionLabels);

        if (FindExisting(folder, episodeId) is not { } path)
        {
            return VocabularyPush.Unchanged;
        }

        try
        {
            using var memory = new MemoryStream();

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                stream.CopyTo(memory);
            }

            memory.Position = 0;
            using var workbook = new XLWorkbook(memory);

            IXLWorksheet scriptSheet = FindScriptSheet(workbook);

            if (ListMatches(workbook, scriptSheet, Speakers, speakers) &&
                ListMatches(workbook, scriptSheet, Conditions, conditionLabels) &&
                KindListMatches(scriptSheet))
            {
                return VocabularyPush.Unchanged;
            }

            // 원고 파일을 다시 쓰는 유일한 순간 — 직전 상태를 남긴다.
            File.WriteAllBytes(path + ".bak", memory.ToArray());

            ApplyVocabulary(workbook, scriptSheet, speakers, conditionLabels);
            workbook.SaveAs(path);

            return new VocabularyPush(true, null);
        }
        catch (Exception exception)
        {
            return new VocabularyPush(false,
                $"'{Path.GetFileName(path)}'에 화자·조건 목록을 넣지 못했습니다" +
                $"(엑셀·시트가 열고 있을 수 있습니다): {exception.Message}");
        }
    }

    /// <summary>
    /// 숨김 시트의 목록이 지금 목록과 같고, <b>그 목록을 가리키는 드롭다운도 아직 거기 있는가</b>.
    /// 순서까지 같아야 같다(드롭다운 순서).
    ///
    /// ⛔ 2026-08-25 소유자 보고 — "유형이랑 조건 라벨의 드롭다운이 사라진 상태야."
    ///    숨김 목록만 대조하면, 외부 편집기가 재저장하며 <b>검증만</b> 떨군 파일이
    ///    "이미 맞다"로 통과해 영영 안 고쳐진다 — 목록은 그대로 있으니 대조가 맞다고 답한다.
    ///
    /// ⚠ 넣을 낱말이 없으면 드롭다운도 걸지 않는다(<c>ApplyVocabulary</c>). 그때까지
    ///   드롭다운을 요구하면 달라진 것이 없는데도 열 때마다 원고를 다시 쓰고 `.bak`이 갈린다.
    /// </summary>
    private static bool ListMatches(
        XLWorkbook workbook,
        IXLWorksheet scriptSheet,
        Vocabulary vocabulary,
        IReadOnlyList<string> values)
    {
        if (values.Count > 0 && !HasDropdown(scriptSheet, vocabulary.Column))
        {
            return false;
        }

        IXLWorksheet? list = workbook.Worksheets.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, vocabulary.SheetName, StringComparison.Ordinal));

        if (list is null)
        {
            return values.Count == 0; // 시트도 없고 넣을 것도 없다.
        }

        var current = new List<string>();
        int last = list.Column(1).LastCellUsed()?.Address.RowNumber ?? 0;

        for (int row = 1; row <= last; row++)
        {
            string value = list.Cell(row, 1).GetString().Trim();

            if (value.Length > 0)
            {
                current.Add(value);
            }
        }

        return current.SequenceEqual(values.Take(ListRows), StringComparer.Ordinal);
    }

    /// <summary>대본 시트 = 화자 머리글(E1)이 있는 시트. 없으면 첫 시트 — 검증만 못 걸 뿐이다.</summary>
    private static IXLWorksheet FindScriptSheet(XLWorkbook workbook) =>
        workbook.Worksheets.FirstOrDefault(sheet =>
            string.Equals(sheet.Cell(1, SpeakerColumn).GetString().Trim(), "화자", StringComparison.Ordinal))
        ?? workbook.Worksheets.First();
}
