using Avalonia.Controls;
using ClosedXML.Excel;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Chapters.Import;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;
using Path = System.IO.Path;

namespace Vn.App.Tests;

/// <summary>
/// <b>뒤집기를 한 바퀴 걷는다</b> (R-A~R-F · 2026-09-16).
///
/// 지시서의 한 문장 — *"엑셀에 쓴 것이 툴에 반영되는 것이 아니라, 툴에 쓴 것이 엑셀을
/// 채운다"* — 을 <b>빈 프로젝트에서 시작해</b> 끝까지 밟는다. 단계별 테스트는 각자 제 조각만
/// 보므로, 조각들이 <b>서로 맞물리는지</b>는 이렇게 걸어 봐야 안다.
///
/// 밟는 길: 새 프로젝트 → 챕터 → 에피소드 → [대본] 탭에서 글 → 두 워크북이 나온다.
/// ⛔ 이 길 어디에도 <b>엑셀을 읽는 걸음이 없다</b>.
/// </summary>
public sealed class InvertedRoundTripTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-round-trip", Guid.NewGuid().ToString("N"));

    private string ManifestPath => Path.Combine(_directory, "p" + ProjectManifestJson.FileExtension);

    private string ChapterPath => Path.Combine(_directory, ChapterLibrary.FolderName, "ch01.xlsx");

    public InvertedRoundTripTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    // ⛔ <b>화면을 걸어 보는 판은 2026-09-16에 뺐다.</b> [챕터 그래프]와 [대본] 탭을 함께
    //    띄워 한 바퀴 도는 테스트를 썼는데, 헤드리스에서 <b>메모리를 32GB까지 먹었다</b>
    //    (소유자가 두 번 끄고 알려 줬다).
    //
    //    재 보니 <b>제품 상태는 멀쩡했다</b> — 캔버스 310×194, 가장 긴 글이 11자
    //    (`[장면] ep01`). 터지는 자리는 Avalonia의
    //    <c>TextFormatterImpl.FormatLineFromCache</c>(글자 모양 캐시)이고, `ScriptBox`의
    //    글꼴을 기본으로 바꿔도 같았다. 기존 421개(대본 탭 21개 포함)는 그대로 초록이다.
    //
    //    ⚠ 원인을 제품 코드로 짚지 못했으므로 <b>고쳤다고 적지 않는다.</b> 다만 사람의
    //    기계를 먹는 테스트를 저장소에 남길 수는 없어 뺀다. 화면을 걷는 확인은 <b>손으로</b>
    //    한다(`docs/handoff/r-f-handoff.md` §9).
    //
    //    다시 쓰려는 사람에게: `DOTNET_GCHeapHardLimit=20000000`을 걸면 512MB에서 먼저
    //    죽으므로 기계가 아니라 그 프로세스가 대신 넘어진다.

    [Fact]
    public void 툴이_낸_챕터_워크북이_제가_산출물임을_말한다() => HeadlessUi.Run(() =>
    {
        // §4.4 — 사람이 열어서 고칠 수 있는 파일이 "고쳐도 소용없는 파일"이면, 그 사실이
        //        파일 안에 있어야 한다. 화면 없이 확인한다.
        var session = new AuthoringSession();
        session.Open(Save());

        session.Editor.EnsureChapter("ch01");
        session.Editor.AddEpisode("ch01", "ep01", "복도", 0, 0);
        session.Save();

        AssertIsOutput(ChapterPath);
    });

    [Fact]
    public void 낸_워크북을_다시_들여와도_같은_것이_들어온다() => HeadlessUi.Run(() =>
    {
        // ⛔ <b>되돌리는 길이 살아 있는가</b> (지시서 §9). 이미터가 낸 파일을 리더가 되읽을
        //    수 있어야 임포트라는 되돌림 경로가 산다 — 규격을 이미터에서 줄이면 여기서 깨진다.
        var session = new AuthoringSession();
        session.Open(Save());

        session.Editor.EnsureChapter("ch01");
        session.Editor.AddEpisode("ch01", "ep01", "복도", 0, 0);
        session.Editor.AddEpisode("ch01", "ep02", "옥상", 200, 0);
        session.Editor.AddEdge("ch01", "ep01", "ep02", optionLabel: "올라간다");
        session.Save();

        Assert.True(File.Exists(ChapterPath));

        // 프로젝트를 <b>버리고</b> 파일에서 다시 들여온다 — 지시서 §9가 말하는 되돌림이다.
        var fresh = new Vn.Authoring.Editing.ProjectEditor(new StoryProject());

        ChapterProjectImport import = ChapterWorkbookImporter.Run(
            fresh, ManifestPath);

        Assert.True(import.Applied, string.Join(" / ", import.Diagnostics.Select(item => item.Message)));

        ChapterDocument back = Assert.Single(fresh.Project.Chapters);

        Assert.Equal(["ep01", "ep02"], back.Episodes.Select(episode => episode.EpisodeId));
        Assert.Equal(["복도", "옥상"], back.Episodes.Select(episode => episode.Title));
        Assert.Equal([("ep01", "ep02", "올라간다")],
            back.Edges.Select(edge => (edge.FromEpisodeId, edge.ToEpisodeId, edge.OptionLabel)));
    });

    /// <summary>
    /// §4.4 — 읽기 전용임을 <b>파일이 말한다</b>. 셋이 함께 서야 한다.
    ///
    /// ⚠ 이 테스트가 처음 돌았을 때 ②가 <b>없었다</b>(2026-09-16). 규격에 적혀 있었지만
    /// 두 이미터 다 머리글을 1행에 쓰고 있었고, 아무도 알아채지 못한 채 R-B부터 살아 있었다.
    /// 한 바퀴를 걸어 보지 않으면 "각자 제 조각은 맞는데 합이 규격이 아닌" 자리는 안 보인다.
    /// </summary>
    private static void AssertIsOutput(string path)
    {
        using var workbook = new XLWorkbook(path);

        // ③ 파일 속성.
        Assert.Contains("산출물", workbook.Properties.Comments);

        foreach (IXLWorksheet sheet in workbook.Worksheets)
        {
            // ① 시트 보호 — 막는 것이 아니라 알리는 것이라 암호가 없다.
            Assert.True(sheet.Protection.IsProtected, $"'{sheet.Name}' 시트가 보호돼 있어야 한다");

            // ② 머리글보다 위 한 줄 — 잠긴 이유가 파일 안에서 보인다.
            Assert.Contains("산출물", sheet.Cell(1, 1).GetString());
            Assert.Equal(2, WorkbookOutputNotice.HeaderRowOf(sheet));
        }
    }

    /// <summary>빈 프로젝트를 저장해 자리를 잡는다 — 저장돼야 챕터가 살 폴더가 정해진다.</summary>
    private string Save(string? path = null)
    {
        string target = path ?? ManifestPath;
        ProjectStore.Save(target, new StoryProject { Title = "한 바퀴" });
        return target;
    }

    private static void Click(Control view, string name)
    {
        view.FindControl<Button>(name)!
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }
}
