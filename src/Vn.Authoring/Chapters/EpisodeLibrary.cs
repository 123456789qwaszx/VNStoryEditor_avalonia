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
    /// 챕터 `화자` 시트의 등록 이름들 (2026-08-16). 있으면 화자 열(H)에 드롭다운을 깐다 —
    /// 목록은 숨김 시트가 담고, 검증은 조언일 뿐이라 목록 밖 이름도 그대로 적을 수 있다
    /// (편의 기능이 없다고 원고를 못 쓰게 하지 않는다).
    /// </param>
    /// <returns>새로 만들었으면 true.</returns>
    public static bool EnsureWorkbook(string folder, string episodeId, IReadOnlyList<string>? speakers = null)
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

        // 9열 (2026-08-14 소유자 개정) — 스탯변화·메모 폐지. 대사 중 A계층 조작은 설계 미스.
        string[] headers =
            ["인덱스", "LineId", "유형", "태그", "조건라벨", "IN", "OUT", "화자", "내용"];

        for (int column = 1; column <= headers.Length; column++)
        {
            IXLCell cell = sheet.Cell(1, column);
            cell.SetValue(headers[column - 1]);
            cell.Style.Font.SetBold(true);
            cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E8EAED"));
        }

        // 유형·태그는 규격의 낱말만 받는다 — 오타가 검증기까지 가기 전에 엑셀이 막는다.
        // CHOICE/OPTION은 새 템플릿에서 뺐다 (2026-08-16 소유자 — 선택지는 챕터의 `선택지`
        // 시트에서 만든다). 옛 파일의 CHOICE/OPTION은 여전히 읽히되 검증이 옮기라고 말한다.
        sheet.Range(2, 3, TemplateRows, 3).CreateDataValidation()
            .List("\"IF\"", true);
        sheet.Range(2, 4, TemplateRows, 4).CreateDataValidation()
            .List("\"INPUT,OUT\"", true);

        // 시트 보호는 걸지 않는다 (v4). 보호의 유일한 이유였던 B열(LineId)을 툴이 더는
        // 쓰지 않는다 — 행 신원은 프로젝트의 ExcelLineMap이 갖는다. 보호가 없으면 구글
        // 시트 같은 외부 편집기가 재저장할 때 깨질 것도 하나 줄어든다. B열은 유물로 남아
        // 회색 배경만 유지한다(과거 파일과 열 배치가 같아야 규격 문서가 하나로 통한다).
        sheet.Column(2).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F1F3F4"));

        // 인덱스를 미리 다 깔아 준다 (10·20·30 방식, G-5). 작가는 번호를 신경 쓰지 않고
        // 그 옆 칸(화자·내용)만 채우면 된다 — 인덱스 없는 행은 표의 일부가 아니라서,
        // 시트에서 그냥 아래로 타이핑하면 대사가 조용히 버려지는 함정이 실제로 있었다.
        // 사이에 끼울 때만 사람이 15 같은 빈 숫자를 적는다(그래서 십 단위다).
        for (int row = 2; row <= TemplateRows; row++)
        {
            sheet.Cell(row, 1).SetValue((row - 1) * 10);
        }

        sheet.Column(9).Width = 50;   // 내용

        if (speakers is { Count: > 0 })
        {
            ApplySpeakerList(workbook, sheet, speakers);
        }

        workbook.SaveAs(path);
        return true;
    }

    // ── 화자 드롭다운 (2026-08-16 소유자 지시) ──────────────────────────────
    //
    // 챕터 `화자` 시트에 등록한 이름이 대본의 화자 열(H)에 드롭다운으로 선다.
    // 목록은 워크북 안의 숨김 시트가 담는다 — 외부 파일 참조는 엑셀·구글 시트에서
    // 깨지기 때문이다. 챕터의 목록이 바뀌면 <see cref="PushSpeakerList"/>가 숨김
    // 시트만 갈아 끼운다. 이것이 v4("만든 뒤 손대지 않는다")의 유일한 예외이며,
    // 예외의 폭은 정확히 숨김 시트와 검증 정의까지다 — <b>대본 행은 절대 쓰지 않는다.</b>

    /// <summary>화자 목록을 담는 숨김 시트. 이 이름이 곧 신원이다 — 갱신이 이 시트만 만진다.</summary>
    public const string SpeakerListSheetName = "화자목록";

    private const int SpeakerColumn = 8;      // H열 — §3.2의 화자
    private const int SpeakerListRows = 200;  // 드롭다운이 가리키는 범위. 화자가 이보다 많으면 나눌 일이다.

    /// <summary>
    /// 숨김 목록 시트를 채우고(없으면 만들고) 화자 열에 조언 드롭다운을 건다.
    /// 검증은 경고를 띄우지 않는다 — 목록 밖 이름(미등록 화자)은 동기화 보고가 다룬다.
    /// </summary>
    private static void ApplySpeakerList(
        XLWorkbook workbook, IXLWorksheet scriptSheet, IReadOnlyList<string> speakers)
    {
        IXLWorksheet list = workbook.Worksheets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, SpeakerListSheetName, StringComparison.Ordinal))
            ?? workbook.AddWorksheet(SpeakerListSheetName);

        list.Visibility = XLWorksheetVisibility.Hidden;
        list.Column(1).Clear(XLClearOptions.Contents);

        for (int index = 0; index < speakers.Count && index < SpeakerListRows; index++)
        {
            list.Cell(index + 1, 1).SetValue(speakers[index]);
        }

        // 화자 열에 이미 검증이 있으면 그대로 둔다(범위 참조라 목록 시트만 갈면 따라온다).
        IXLRange range = scriptSheet.Range(2, SpeakerColumn, TemplateRows, SpeakerColumn);

        if (!scriptSheet.DataValidations.Any(validation =>
                validation.Ranges.Any(existing =>
                    existing.RangeAddress.FirstAddress.ColumnNumber == SpeakerColumn)))
        {
            IXLDataValidation validation = range.CreateDataValidation();
            validation.List($"='{SpeakerListSheetName}'!$A$1:$A${SpeakerListRows}", inCellDropdown: true);
            validation.ShowErrorMessage = false; // 조언일 뿐 — 목록 밖 이름도 원고다.
        }
    }

    /// <summary><see cref="PushSpeakerList"/> 한 번의 결과. 실패는 예외가 아니라 사유다.</summary>
    public sealed record SpeakerListPush(bool Changed, string? Failure)
    {
        public static SpeakerListPush Unchanged { get; } = new(false, null);
    }

    /// <summary>
    /// 챕터의 화자 목록을 기존 워크북의 숨김 시트에 반영한다. <b>목록이 이미 같으면 파일에
    /// 손대지 않는다</b>(수정 시각 불변) — 챕터를 열 때마다 불려도 실제 쓰기는 목록이 바뀐
    /// 그 순간뿐이다. 쓰기 전 원본을 <c>.bak</c>으로 남긴다(원고 파일이므로).
    /// 워크북이 아직 없으면 할 일이 없다 — 생성 때 목록을 받는다.
    /// </summary>
    public static SpeakerListPush PushSpeakerList(
        string folder, string episodeId, IReadOnlyList<string> speakers)
    {
        ArgumentNullException.ThrowIfNull(speakers);

        if (FindExisting(folder, episodeId) is not { } path)
        {
            return SpeakerListPush.Unchanged;
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

            if (SpeakerListMatches(workbook, speakers))
            {
                return SpeakerListPush.Unchanged;
            }

            IXLWorksheet scriptSheet = FindScriptSheet(workbook);

            // 원고 파일을 다시 쓰는 유일한 순간 — 직전 상태를 남긴다.
            File.WriteAllBytes(path + ".bak", memory.ToArray());

            ApplySpeakerList(workbook, scriptSheet, speakers);
            workbook.SaveAs(path);

            return new SpeakerListPush(true, null);
        }
        catch (Exception exception)
        {
            return new SpeakerListPush(false,
                $"'{Path.GetFileName(path)}'에 화자 목록을 넣지 못했습니다" +
                $"(엑셀·시트가 열고 있을 수 있습니다): {exception.Message}");
        }
    }

    /// <summary>숨김 시트의 목록이 지금 목록과 같은가 — 순서까지 같아야 같다(드롭다운 순서).</summary>
    private static bool SpeakerListMatches(XLWorkbook workbook, IReadOnlyList<string> speakers)
    {
        IXLWorksheet? list = workbook.Worksheets.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, SpeakerListSheetName, StringComparison.Ordinal));

        if (list is null)
        {
            return speakers.Count == 0; // 시트도 없고 화자도 없다 — 넣을 것이 없다.
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

        return current.SequenceEqual(speakers.Take(SpeakerListRows), StringComparer.Ordinal);
    }

    /// <summary>대본 시트 = 화자 머리글(H1)이 있는 시트. 없으면 첫 시트 — 검증만 못 걸 뿐이다.</summary>
    private static IXLWorksheet FindScriptSheet(XLWorkbook workbook) =>
        workbook.Worksheets.FirstOrDefault(sheet =>
            string.Equals(sheet.Cell(1, SpeakerColumn).GetString().Trim(), "화자", StringComparison.Ordinal))
        ?? workbook.Worksheets.First();
}
