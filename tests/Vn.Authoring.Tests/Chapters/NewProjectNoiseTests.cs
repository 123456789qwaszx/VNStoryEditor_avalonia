using System.Text;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 소유자 보고 — "완전 새 프로젝트인데 검증 보고가 동기화 거부·경고를 계속 띄운다."
/// 갓 만든 챕터·에피소드는 <b>아직 아무것도 잘못하지 않았다</b>. 그 상태에서 뜨는 것은
/// 사람이 고칠 것이 아니라 소음이다.
/// </summary>
public sealed class NewProjectNoiseTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-new-project", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 갓_만든_챕터와_빈_에피소드는_고칠_것이_없다()
    {
        string chapters = Path.Combine(_directory, "chapters");
        string episodes = Path.Combine(_directory, "episodes");

        // 툴이 하는 그대로: [＋ 챕터] → [＋ 에피소드] → 노드 더블클릭(빈 워크북 생성).
        ChapterWorkbookWriter.EnsureChapterWorkbook(
            chapters, "ch01", [("trust", "신뢰"), ("anger", "분노")]);
        string chapterPath = Path.Combine(chapters, "ch01.xlsx");

        ChapterWorkbookWriter.AddEpisode(chapterPath, "ep01", title: string.Empty, 0, 0);
        EpisodeLibrary.EnsureWorkbook(episodes, "ep01");

        ChapterGraphModel model = ChapterWorkbookReader.Read(chapterPath);
        ChapterValidationResult validation = ChapterValidator.Validate(model, episodes);

        string[] shown = validation.All
            .Where(item => item.Severity != ChapterDiagnosticSeverity.Info)
            .Select(item => $"[{item.Severity}] {item.Code}: {item.Message}")
            .ToArray();

        Assert.Empty(shown);
    }

    [Fact]
    public void 이름이_분해형으로_저장된_워크북을_같은_파일로_본다()
    {
        // 실제 사고 — 구글 드라이브가 한글 파일 이름을 분해형(NFD)으로 바꿔 놓자
        // File.Exists가 거짓을 말했고, 툴이 같은 이름으로 보이는 빈 워크북을 하나 더 만들어
        // 사람이 쓴 대사가 다른 파일에 남았다.
        string episodes = Path.Combine(_directory, "episodes");
        Directory.CreateDirectory(episodes);

        const string episodeId = "복도장면";

        // 드라이브가 내려놓은 것처럼 분해형 이름으로 파일을 둔다.
        string decomposed = Path.Combine(
            episodes, episodeId.Normalize(NormalizationForm.FormD) + ".xlsx");
        File.WriteAllBytes(decomposed, ReadTemplateBytes(episodes));

        // 조합형 Id로 물어도 그 파일을 찾아낸다 —
        Assert.NotNull(EpisodeLibrary.FindExisting(episodes, episodeId));

        // — 그래서 새로 만들지 않는다. 폴더에는 여전히 워크북이 하나뿐이다.
        Assert.False(EpisodeLibrary.EnsureWorkbook(episodes, episodeId));
        Assert.Single(Directory.EnumerateFiles(episodes, "*.xlsx"));
    }

    [Fact]
    public void xlsm으로_개명된_워크북도_같은_에피소드다()
    {
        // 실제 사고의 최종 처리 (v4.1) — 구글 시트는 .xlsx를 저장하며 .xlsm으로 개명한다.
        // 매크로는 없고 선언만 그렇다(컨테이너 해부로 확인). 툴은 읽기만 하므로(v4)
        // .xlsm을 정식으로 받는다 — 빈 워크북을 새로 만들지 않고, 그 파일을 그대로 읽는다.
        string episodes = Path.Combine(_directory, "episodes");
        Directory.CreateDirectory(episodes);

        File.WriteAllBytes(Path.Combine(episodes, "ep01.xlsm"), ReadTemplateBytes(episodes));

        Assert.NotNull(EpisodeLibrary.FindExisting(episodes, "ep01"));   // 같은 워크북이다
        Assert.Null(EpisodeLibrary.FindOtherFormat(episodes, "ep01"));   // "다른 형식" 아님
        Assert.False(EpisodeLibrary.EnsureWorkbook(episodes, "ep01"));   // 새로 만들지 않는다
        Assert.Single(Directory.EnumerateFiles(episodes, "*.xls*"));
    }

    [Fact]
    public void 빈_xlsx_유물과_xlsm이_같이_있으면_최근에_저장된_쪽이_원고다()
    {
        // 사고 당시의 실제 폴더 모습 — 옛 빌드가 만든 빈 .xlsx 옆에 진짜 원고 .xlsm.
        string episodes = Path.Combine(_directory, "episodes");
        Directory.CreateDirectory(episodes);

        EpisodeLibrary.EnsureWorkbook(episodes, "ep02");                     // 빈 유물
        string manuscript = Path.Combine(episodes, "ep02.xlsm");
        File.WriteAllBytes(manuscript, ReadTemplateBytes(episodes));
        File.SetLastWriteTimeUtc(manuscript, DateTime.UtcNow.AddMinutes(5)); // 더 최근

        Assert.Equal(manuscript, EpisodeLibrary.FindExisting(episodes, "ep02"));
    }

    [Fact]
    public void 템플릿은_인덱스가_미리_깔려_있어_그냥_아래로_타이핑하면_전부_읽힌다()
    {
        // 실사례 — 시트에서 여러 줄을 썼는데 첫 줄만 잡혔다. 인덱스 없는 행은 표의 일부가
        // 아니라서, 템플릿이 첫 행에만 10을 깔아 주면 둘째 줄부터 전부 버려졌다.
        string episodes = Path.Combine(_directory, "episodes");
        EpisodeLibrary.EnsureWorkbook(episodes, "ep_type");
        string path = EpisodeLibrary.PathFor(episodes, "ep_type");

        // 작가가 하는 그대로: 번호는 건드리지 않고 화자·내용만 세 줄 채운다.
        using (var workbook = new ClosedXML.Excel.XLWorkbook(path))
        {
            var sheet = workbook.Worksheets.First();
            sheet.Cell(2, 5).SetValue("라루"); sheet.Cell(2, 6).SetValue("첫 줄");
            sheet.Cell(3, 5).SetValue("윌로"); sheet.Cell(3, 6).SetValue("둘째 줄");
            sheet.Cell(4, 5).SetValue("라루"); sheet.Cell(4, 6).SetValue("셋째 줄");
            workbook.Save();
        }

        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(path);
        EpisodeFlattenResult flattened = EpisodeFlattener.Flatten(
            model, new Dictionary<string, ChapterCondition>());

        Assert.Equal(3, flattened.Lines.Count);           // 세 줄 전부 산출된다
        Assert.Contains("윌로: 둘째 줄", flattened.Text);
        Assert.Empty(flattened.Errors);
    }

    [Fact]
    public void 인덱스_없는_행에_대사가_있으면_조용히_버리지_않고_경고한다()
    {
        string episodes = Path.Combine(_directory, "episodes");
        EpisodeLibrary.EnsureWorkbook(episodes, "ep_warn");
        string path = EpisodeLibrary.PathFor(episodes, "ep_warn");

        using (var workbook = new ClosedXML.Excel.XLWorkbook(path))
        {
            var sheet = workbook.Worksheets.First();
            sheet.Cell(3, 1).Clear();                     // 인덱스를 지우고
            sheet.Cell(3, 6).SetValue("버려질 뻔한 대사"); // 내용만 남긴다
            workbook.Save();
        }

        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(path);

        Assert.Contains(model.Diagnostics, item =>
            item.Severity == ChapterDiagnosticSeverity.Warning &&
            item.Message.Contains("A열에 번호를 적어 주세요"));
    }

    [Fact]
    public void 대본_파일_개명은_xlsm_확장자를_그대로_따라간다()
    {
        // 개명은 이동일 뿐 내용을 건드리지 않는다 — 구글이 .xlsm으로 바꿔 둔 파일이면
        // .xlsm 그대로 새 이름이 된다.
        string episodes = Path.Combine(_directory, "episodes");
        Directory.CreateDirectory(episodes);
        File.WriteAllBytes(Path.Combine(episodes, "ep_old.xlsm"), ReadTemplateBytes(episodes));

        Assert.Null(EpisodeLibrary.RenameWorkbook(episodes, "ep_old", "ep_new"));

        Assert.True(File.Exists(Path.Combine(episodes, "ep_new.xlsm")));
        Assert.False(File.Exists(Path.Combine(episodes, "ep_old.xlsm")));

        // 없는 파일 개명은 실패가 아니다(아직 대본 전) · 겹치면 사유를 말한다.
        Assert.Null(EpisodeLibrary.RenameWorkbook(episodes, "ep_none", "ep_x"));
        EpisodeLibrary.EnsureWorkbook(episodes, "ep_taken");
        Assert.Contains("이미 있어", EpisodeLibrary.RenameWorkbook(episodes, "ep_new", "ep_taken"));
    }

    [Fact]
    public void 템플릿의_빈_행은_블록_뒤에_있어도_오류가_아니다()
    {
        // 실사례 — 템플릿이 인덱스를 500행까지 깔아 두자, 그 빈 자리들이 대사로 세어져
        // 멀쩡한 시트에 오류가 났다. 인덱스만 있는 행은 표의 일부가 아니다.
        string episodes = Path.Combine(_directory, "episodes");
        EpisodeLibrary.EnsureWorkbook(episodes, "ep_block");
        string path = EpisodeLibrary.PathFor(episodes, "ep_block");

        using (var workbook = new ClosedXML.Excel.XLWorkbook(path))
        {
            var sheet = workbook.Worksheets.First();
            // 대사 → 조건 블록 → (템플릿이 깔아 둔 빈 행 수백 개)
            sheet.Cell(2, 5).SetValue("윌로"); sheet.Cell(2, 6).SetValue("첫 줄");   // 10
            sheet.Cell(3, 2).SetValue("IF"); sheet.Cell(3, 4).SetValue("신뢰높음");  // 20
            sheet.Cell(4, 5).SetValue("라루"); sheet.Cell(4, 6).SetValue("조건 안"); // 30
            sheet.Cell(5, 2).SetValue("ENDIF");                                      // 40
            workbook.Save();
        }

        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(path, ["신뢰높음"]);

        Assert.Empty(model.Errors);
        Assert.Equal(4, model.Rows.Count);
    }

    private static byte[] ReadTemplateBytes(string folder)
    {
        EpisodeLibrary.EnsureWorkbook(folder, "__template");
        string path = EpisodeLibrary.PathFor(folder, "__template");
        byte[] bytes = File.ReadAllBytes(path);
        File.Delete(path);
        return bytes;
    }
}
