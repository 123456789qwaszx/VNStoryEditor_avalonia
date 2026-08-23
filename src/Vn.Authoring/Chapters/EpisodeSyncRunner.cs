using Vn.Authoring.Definition;
using Vn.Authoring.Editing;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 한 번의 에피소드 동기화가 남긴 것. <b>화면에 무엇을 말할지는 여기 없다</b> —
/// 무슨 일이 있었는지만 담고, 말하는 것은 셸의 몫이다.
/// </summary>
/// <param name="BoardFileId">이 챕터의 판. 동기화가 아무것도 못 했으면 null이다.</param>
/// <param name="Notices">사람에게 알릴 것 — 대본 이행·구판 입양. 오류가 아니라 사건이다.</param>
/// <param name="WorkbooksCreated">대본 파일을 새로 만들었나 — 셸이 감시자를 다시 건다.</param>
public sealed record EpisodeSyncRun(
    string? BoardFileId,
    IReadOnlyList<EpisodeSyncReport> Reports,
    IReadOnlyList<ChapterDiagnostic> BoardWarnings,
    IReadOnlyList<string> Notices,
    bool WorkbooksCreated)
{
    public static EpisodeSyncRun Nothing { get; } = new(null, [], [], [], false);

    /// <summary>
    /// 반영을 돌린 워크북 수.
    ///
    /// ⛔ <b>"뭔가 달라졌다"의 눈금으로 쓰지 말 것.</b> <c>Applied</c>는 "반영을 돌렸다"는
    /// 뜻이라 <b>같은 워크북을 두 번 돌려도 참이다</b>
    /// (`EpisodeSyncServiceTests.같은_워크북을_두_번_동기화하면_두_번째는_변경이_없다`).
    /// 셸이 이것으로 "다시 그려라"를 방송하다가, 아무것도 안 바뀐 동기화가 매번 열려 있는
    /// 편집 화면을 파괴했다 (2026-08-24). 그 질문의 답은 <c>ProjectEditor.Revision</c>이다.
    /// </summary>
    public int Applied => Reports.Count(report => report.Applied);

    public int Rejected => Reports.Sum(report => report.RejectionCount);

    /// <summary>
    /// 상태줄에 낼 한 줄. <b>짚을 것이 없으면 null이다</b> (2026-08-24 소유자: "동기화가
    /// 몇개 반영됬는지 표기할 필요는 없어. 그런 동기화 문구들은 굳이 표시가 안 되도록").
    ///
    /// 동기화는 <b>사람이 시켜서 도는 일이 아니다</b> — 챕터를 고르거나 워크북이 저장될
    /// 때마다 저절로 돈다. 그런 일이 잘됐다고 매번 상태줄을 차지하면, 정작 사람이 방금
    /// 누른 것에 대한 답이 그 아래 깔린다(실제로 깔렸다 — 내보내기 결론이 동기화 문구에
    /// 덮이던 자리가 이미 문서에 적혀 있다).
    ///
    /// ⛔ <b>거부·경고는 그대로 말한다.</b> 조용한 무반영이 최악이다(G3-1) — 여기까지
    /// 침묵하면 작가는 자기 대사가 왜 안 나오는지 영영 모른다. 검증 보고와 같은 선이다
    /// (`ChapterGraphView.HasSomethingToSay`).
    /// </summary>
    public string? StatusMessage => Rejected == 0
        ? null
        : $"동기화 거부·경고 {Rejected}건 — 아래 검증 보고를 확인하세요.";
}

/// <summary>
/// 챕터 하나의 <b>에피소드 워크북을 대사노드로 반영하는 순서</b>. 각 단계의 일은 이미
/// <see cref="EpisodeSyncService"/>·<see cref="EpisodeLibrary"/>·
/// <see cref="EpisodeWorkbookMigrator"/>가 갖고 있고, 여기 있는 것은 <b>순서와 정책</b>이다.
///
/// <b>왜 화면 밖으로 나왔나 (2026-08-23)</b> — 이 순서가 `ChapterGraphView` 3,835줄
/// 안에 살아서 <b>다음 툴이 가져갈 수 없었고</b>, 규칙으로 읽히지도 않았다. 그리고 그
/// 안에서 셸의 것(상태줄·다시 그리기)과 저작의 것(반영·검사)이 섞여 있었다.
///
/// <b>인터페이스를 두지 않는다.</b> 셸이 필요한 것은 넷뿐인데
/// (<c>Editor</c>·<c>Definition</c>·<c>ProjectPath</c>·챕터 목록) 앞의 둘은 이미 도메인이고
/// 뒤의 둘은 값이다. 셸에 <b>되돌려 말해야</b> 하는 것(상태줄·판 다시 그리기)은 전부
/// <em>출력</em>이라 <see cref="EpisodeSyncRun"/>으로 돌려주면 된다 — 이 코드베이스의
/// <c>interface</c> 총 개수는 1이고, 그 결을 지킨다.
/// </summary>
public static class EpisodeSyncRunner
{
    /// <summary>
    /// 감시자는 어느 파일이 바뀌었는지 말하지 않으므로(저장 한 번이 이벤트 여러 개라
    /// 어차피 뭉개진다) 전부 다시 돈다 — 바뀌지 않은 워크북은 "변경 없음"으로 끝나
    /// 비용이 잔잔하다.
    /// </summary>
    /// <param name="entry">고른 챕터. ⚠ 동기화는 <b>이 하나만</b> 돈다 — 내보내기가
    /// 전 챕터를 도는 것과 다르다(<see cref="ChapterExportService.ExportAll"/>).</param>
    /// <param name="allEntries">구판 대본의 주인을 가리는 데 쓴다 — 같은 EpisodeId를
    /// 여러 챕터가 claim하면 옮기지 않는다.</param>
    public static EpisodeSyncRun Run(
        ProjectEditor editor,
        GameDefinition definition,
        string? projectPath,
        ChapterEntry entry,
        IReadOnlyList<ChapterEntry> allEntries)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(allEntries);

        if (entry.Model is not { } model ||
            EpisodeLibrary.FolderFor(projectPath, entry.ChapterId) is not { } folder)
        {
            return EpisodeSyncRun.Nothing;
        }

        var notices = new List<string>();

        // 구판 평면 원고(episodes/{Id}.xlsx)를 이 챕터 폴더로 입양한다 (2026-08-16).
        // 주인이 여럿이면 옮기지 않고 사유를 말한다 — 남의 챕터 원고를 가져오지 않는다.
        AdoptFlatWorkbooks(projectPath, entry, model, allEntries, notices);

        // 폴더가 없다고 여기서 멈추지 않는다 (2026-08-17) — 에피소드가 하나라도 있으면
        // 대본을 만들어 줄 참이고, 그 첫 파일이 폴더를 만든다. 예전에는 여기서 되돌아가서
        // 첫 에피소드가 영영 대본을 못 받았다(폴더는 대본이 생겨야 나고, 대본은 폴더가
        // 있어야 만들었다 — 서로를 기다리는 매듭).
        if (!Directory.Exists(folder) && model.Episodes.Count == 0)
        {
            return EpisodeSyncRun.Nothing with { Notices = notices };
        }

        // 새 노드가 들어갈 판 = 그 챕터의 판 (챕터=판 1:1, G-1 v2). 없으면 만든다 —
        // 왼쪽 챕터 목록 클릭과 같은 규칙 하나를 쓴다.
        string fileId = editor.EnsureChapterBoard(entry.ChapterId);

        List<string> speakers = SpeakerNames(definition);
        List<string> labels = model.Conditions.Select(condition => condition.Label).ToList();

        // 대본이 없는 에피소드에는 여기서 만들어 준다 (2026-08-17 소유자 보고). 툴의
        // [＋ 에피소드]는 이미 만들고 있었지만 **엑셀에서 직접 행을 더한 경우**가 남아
        // 있었다 — 원본이 엑셀이라 대부분이 그 길이다. 여기서
        // 보장하면 어느 길로 들어와도 같다. 없는 파일을 만드는 것뿐이라 단일 writer
        // 원칙과 충돌하지 않는다(있으면 손대지 않는다).
        bool created = false;

        foreach (ChapterEpisode episode in model.Episodes)
        {
            created |= EpisodeLibrary.EnsureWorkbook(folder, episode.EpisodeId, speakers, labels);
        }

        var reports = new List<EpisodeSyncReport>();

        foreach (ChapterEpisode episode in model.Episodes)
        {
            if (EpisodeLibrary.FindExisting(folder, episode.EpisodeId) is not { } path)
            {
                continue;
            }

            // 구판 9열 대본을 v10 블록 규격으로 (2026-08-17). 필요 없는 파일에는 손대지
            // 않으므로 매 동기화마다 불려도 쓰기는 구판을 처음 만난 그 한 번뿐이다.
            EpisodeWorkbookMigrator.MigrationResult migration =
                EpisodeWorkbookMigrator.Migrate(path);

            if (migration.Migrated)
            {
                notices.Add(
                    $"'{Path.GetFileName(path)}'를 새 대본 규격(IF~END 블록)으로 이행했습니다" +
                    "(이전 상태는 .bak). 엑셀이 열려 있었다면 닫았다 다시 열어 주세요.");
            }
            else if (migration.Failure is { } failure)
            {
                notices.Add(failure);
            }

            reports.Add(EpisodeSyncService.Sync(editor, definition, fileId, path, model));
        }

        // 챕터 조건을 판의 모든 대사 노드(자유 노드 포함)에 공급한다 — 작가가 조건
        // 드롭다운에서 A 계층 라벨을 바로 고른다. 멱등이라 매번 불러도 안전하다.
        EpisodeSyncService.SupplyChapterConditionsToBoard(editor, definition, fileId, model);

        // 가드레일 — 자유 노드의 스탯 set, 엑셀노드로 향하는 출구. 막지 않고 크게 말한다.
        // (v12에서 "빈 연출" 경고가 빠졌다 — `연출` 칸 자체가 폐지됐다.)
        var warnings = new List<ChapterDiagnostic>();
        warnings.AddRange(EpisodeSyncService.WarnFreeNodeStatWrites(editor, fileId, model));
        warnings.AddRange(EpisodeSyncService.WarnExitsIntoExcelNodes(editor, fileId, model));

        return new EpisodeSyncRun(fileId, reports, warnings, notices, created);
    }

    /// <summary>새 대본이 받을 화자 — 챕터를 가리지 않는 프로젝트 목록 하나다.</summary>
    public static List<string> SpeakerNames(GameDefinition definition) => definition.Speakers
        .Select(speaker => speaker.Name)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .ToList();

    /// <summary>
    /// 구판 평면 대본(<c>episodes/{Id}.xlsx</c>)을 그 챕터 폴더로 옮긴다 (2026-08-16).
    /// EpisodeId를 여러 챕터가 쓰고 있으면 어느 원고인지 알 수 없으므로 손대지 않고 말한다.
    /// </summary>
    private static void AdoptFlatWorkbooks(
        string? projectPath,
        ChapterEntry entry,
        ChapterGraphModel model,
        IReadOnlyList<ChapterEntry> allEntries,
        List<string> notices)
    {
        if (EpisodeLibrary.FolderFor(projectPath) is not { } root || !Directory.Exists(root))
        {
            return;
        }

        var problems = new List<string>();
        int adopted = 0;

        foreach (ChapterEpisode episode in model.Episodes)
        {
            // 그 이름을 쓰는 챕터 수 — 하나여야 주인이 분명하다.
            int claimants = allEntries.Count(candidate =>
                candidate.Model?.FindEpisode(episode.EpisodeId) is not null);

            EpisodeLibrary.FlatAdoption adoption = EpisodeLibrary.AdoptFlatWorkbook(
                root, entry.ChapterId, episode.EpisodeId, claimants);

            if (adoption.Adopted)
            {
                adopted++;
            }
            else if (adoption.Problem is { } problem)
            {
                problems.Add(problem);
            }
        }

        if (problems.Count > 0)
        {
            notices.Add(problems[0] +
                (problems.Count > 1 ? $" (외 {problems.Count - 1}건)" : string.Empty));
        }
        else if (adopted > 0)
        {
            notices.Add(
                $"대본 {adopted}개를 episodes/{entry.ChapterId}/ 로 옮겼습니다 — " +
                "챕터마다 같은 이름을 따로 쓸 수 있습니다.");
        }
    }
}
