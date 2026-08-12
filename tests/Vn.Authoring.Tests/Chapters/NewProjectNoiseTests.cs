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

    private static byte[] ReadTemplateBytes(string folder)
    {
        EpisodeLibrary.EnsureWorkbook(folder, "__template");
        string path = EpisodeLibrary.PathFor(folder, "__template");
        byte[] bytes = File.ReadAllBytes(path);
        File.Delete(path);
        return bytes;
    }
}
