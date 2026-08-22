using Avalonia;
using Avalonia.Controls;
using ClosedXML.Excel;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;
using Path = System.IO.Path;

namespace Vn.App.Tests;

/// <summary>
/// <b>[화자] 탭 = 프로젝트의 캐스트</b> (2026-08-23 소유자).
///
/// *"챕터 엑셀을 눌러보면, 엑셀 내 어떤 것에서도 화자를 사용하지 않는다. 즉 애초부터
/// 챕터엑셀에 화자가 들어갈 이유가 전혀없다. 화자는 툴 내부에서, 직접 정의해서 쓰는 게 맞는
/// 것이였다. … 이 화자탭에서 화자 추가 삭제를 하는 것이 구조적으로 옳다. 여기서 추가한
/// 화자는 게임 project에 저장되며, 모든 챕터가 공유해서 쓴다."*
///
/// 못 박는 것 넷: ① 탭에서 더하면 정의 파일에 남고 <b>모든 챕터의 대본</b> 드롭다운에 선다
/// ② ✕로 지우면 목록에서 빠진다 ③ 구판 `화자` 시트는 한 번 옮겨지고 사라진다
/// ④ 어휘가 그대로면 대본 워크북을 하나도 열지 않는다(§성능 규칙).
/// </summary>
public sealed class SpeakerTabTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-speaker-tab", Guid.NewGuid().ToString("N"));

    private readonly OpenChapterViews _ui = new();

    private string ManifestPath => Path.Combine(_directory, "p" + ProjectManifestJson.FileExtension);

    private string ChaptersFolder => Path.Combine(_directory, ChapterLibrary.FolderName);

    public SpeakerTabTests()
    {
        Directory.CreateDirectory(_directory);

        // 챕터 둘 — "모든 챕터가 공유해서 쓴다"를 볼 수 있는 최소 모양.
        Chapter("ch01", "ep01");
        Chapter("ch02", "ep02");

        ProjectStore.Save(ManifestPath, new StoryProject { Title = "캐스트" });
    }

    public void Dispose()
    {
        _ui.CloseAll();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 탭에서_더한_화자가_모든_챕터의_대본에_선다() => HeadlessUi.Run(() =>
    {
        (ChapterGraphView view, AuthoringSession session, _) = Show();

        Add(view, "라루", "laru");

        // ① 프로젝트에 남았다 — 메모리와 디스크 양쪽.
        SpeakerSpec saved = Assert.Single(session.Definition.Speakers);
        Assert.Equal("라루", saved.Name);
        Assert.Equal("laru", saved.CharacterId);
        Assert.Contains("라루", File.ReadAllText(GameDefinition.PathFor(ManifestPath)));

        // ② 챕터를 가리지 않고 대본 드롭다운에 섰다 — 이것이 이 구조의 요점이다.
        Assert.Equal(["라루"], SpeakerList("ch01", "ep01"));
        Assert.Equal(["라루"], SpeakerList("ch02", "ep02"));
    });

    [Fact]
    public void 지우면_목록에서_빠진다() => HeadlessUi.Run(() =>
    {
        (ChapterGraphView view, AuthoringSession session, _) = Show();

        Add(view, "라루", "laru");
        Add(view, "윌로", null);

        // 줄의 ✕ — 첫 줄(라루)을 지운다.
        Button remove = Rows(view)[0].Children.OfType<Button>().Single();
        remove.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.RunJobs();

        SpeakerSpec left = Assert.Single(session.Definition.Speakers);
        Assert.Equal("윌로", left.Name);

        // 대본 드롭다운도 따라간다 — 툴만 줄고 엑셀이 옛 목록을 들고 있으면 안 된다.
        Assert.Equal(["윌로"], SpeakerList("ch01", "ep01"));
    });

    [Fact]
    public void 같은_이름은_두_번_안_선다() => HeadlessUi.Run(() =>
    {
        (ChapterGraphView view, AuthoringSession session, _) = Show();

        Add(view, "라루", "laru");
        Add(view, "라루", "raru2");

        Assert.Single(session.Definition.Speakers);
        Assert.Contains("이미 있습니다", session.StatusMessage);
    });

    [Fact]
    public void 구판_화자_시트는_한_번_옮겨지고_사라진다() => HeadlessUi.Run(() =>
    {
        // 이행 — 이미 시트에 적어 둔 이름을 잃지 않는다. 순서가 규격이다: 시트를 지운 뒤에
        // 정의 파일에 쓴다(반대로 하면 잠긴 워크북이 지운 이름을 되살린다).
        WriteLegacySpeakerSheet("ch01", ("늙은 상인", "merchant"));

        (ChapterGraphView view, AuthoringSession session, _) = Show();

        SpeakerSpec moved = Assert.Single(session.Definition.Speakers);
        Assert.Equal("늙은 상인", moved.Name);
        Assert.Equal("merchant", moved.CharacterId);

        // 시트는 사라졌고, 원본은 .bak에 있다.
        string chapter = Path.Combine(ChaptersFolder, "ch01.xlsx");
        Assert.False(ChapterWorkbookReader.Read(chapter).HasSpeakerSheet);
        Assert.True(File.Exists(chapter + ".bak"));

        // 옮겨 온 이름도 다른 챕터의 대본에서 고를 수 있다.
        Assert.Equal(["늙은 상인"], SpeakerList("ch02", "ep02"));

        // 두 번째 재읽기는 아무것도 안 옮긴다 — 시트가 없으니 옮길 것이 없다.
        view.RefreshFromDisk();
        Dispatcher.RunJobs();
        Assert.Single(session.Definition.Speakers);
    });

    [Fact]
    public void 어휘가_그대로면_대본_워크북을_열지_않는다() => HeadlessUi.Run(() =>
    {
        // §성능 규칙 — 밀기는 대본 워크북을 전부 열어 본다. 그 값이 프로젝트 전체가 됐으므로
        // 지문이 같은 동안에는 한 파일도 만지지 않아야 한다.
        (ChapterGraphView view, _, _) = Show();

        Add(view, "라루", "laru");

        DateTime first = File.GetLastWriteTimeUtc(EpisodePath("ch01", "ep01"));
        DateTime second = File.GetLastWriteTimeUtc(EpisodePath("ch02", "ep02"));

        for (int index = 0; index < 5; index++)
        {
            view.RefreshFromDisk();
            Dispatcher.RunJobs();
        }

        Assert.Equal(first, File.GetLastWriteTimeUtc(EpisodePath("ch01", "ep01")));
        Assert.Equal(second, File.GetLastWriteTimeUtc(EpisodePath("ch02", "ep02")));
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static class Dispatcher
    {
        public static void RunJobs() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>탭의 한 줄 폼을 채우고 [＋ 추가]를 누른다 — 사람이 하는 그대로.</summary>
    private static void Add(ChapterGraphView view, string name, string? characterId)
    {
        view.FindControl<TextBox>("SpeakerNameBox")!.Text = name;
        view.FindControl<TextBox>("SpeakerCharacterIdBox")!.Text = characterId ?? string.Empty;
        view.FindControl<Button>("SpeakerAddButton")!
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Dispatcher.RunJobs();
    }

    private static List<Grid> Rows(ChapterGraphView view) =>
        view.FindControl<StackPanel>("SpeakerListPanel")!.Children.OfType<Grid>().ToList();

    /// <summary>그 챕터 대본의 숨김 목록 시트에 적힌 화자들 — 드롭다운이 가리키는 바로 그것.</summary>
    private List<string> SpeakerList(string chapterId, string episodeId)
    {
        using var workbook = new XLWorkbook(EpisodePath(chapterId, episodeId));

        IXLWorksheet? list = workbook.Worksheets.FirstOrDefault(sheet =>
            sheet.Name == EpisodeLibrary.SpeakerListSheetName);

        if (list is null)
        {
            return [];
        }

        int last = list.Column(1).LastCellUsed()?.Address.RowNumber ?? 0;

        return Enumerable.Range(1, last)
            .Select(row => list.Cell(row, 1).GetString().Trim())
            .Where(value => value.Length > 0)
            .ToList();
    }

    private string EpisodePath(string chapterId, string episodeId) =>
        Path.Combine(_directory, EpisodeLibrary.FolderName, chapterId, episodeId + ".xlsx");

    private void Chapter(string chapterId, string episodeId)
    {
        ChapterWorkbookWriter.EnsureChapterWorkbook(ChaptersFolder, chapterId, [("trust", "신뢰")]);
        ChapterWorkbookWriter.AddEpisode(
            Path.Combine(ChaptersFolder, chapterId + ".xlsx"), episodeId, title: episodeId, 0, 0);

        // 대본은 이미 있다 — 화자를 정하기 전에 쓰기 시작한 원고가 실제 상황이다.
        EpisodeLibrary.EnsureWorkbook(
            Path.Combine(_directory, EpisodeLibrary.FolderName, chapterId), episodeId);
    }

    /// <summary>구판 워크북 흉내 — 툴에는 이 시트를 만드는 길이 더 이상 없다.</summary>
    private void WriteLegacySpeakerSheet(string chapterId, params (string Name, string? CharacterId)[] rows)
    {
        string path = Path.Combine(ChaptersFolder, chapterId + ".xlsx");

        using var workbook = new XLWorkbook(path);
        IXLWorksheet sheet = workbook.AddWorksheet(ChapterSheetNames.Speakers);

        sheet.Cell(1, 1).SetValue("이름");
        sheet.Cell(1, 2).SetValue("캐릭터키");
        sheet.Cell(1, 3).SetValue("메모");

        for (int index = 0; index < rows.Length; index++)
        {
            sheet.Cell(index + 2, 1).SetValue(rows[index].Name);
            sheet.Cell(index + 2, 2).SetValue(rows[index].CharacterId ?? string.Empty);
        }

        workbook.SaveAs(path);
    }

    private (ChapterGraphView View, AuthoringSession Session, Window Window) Show()
    {
        var session = new AuthoringSession();
        session.Open(ManifestPath);

        var view = new ChapterGraphView();
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        view.Attach(session);

        window.Measure(new Size(900, 600));
        window.Arrange(new Rect(0, 0, 900, 600));
        Dispatcher.RunJobs();

        _ui.Own(view, window);

        return (view, session, window);
    }
}
