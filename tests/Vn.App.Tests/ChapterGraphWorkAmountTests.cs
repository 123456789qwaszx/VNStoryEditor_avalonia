using Avalonia;
using Avalonia.Controls;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;
using Path = System.IO.Path;

namespace Vn.App.Tests;

/// <summary>
/// 챕터 화면이 <b>얼마나 일하는가</b> (2026-08-18 성능).
///
/// 소유자 보고 — "노드가 조금만 많아져도 엄청 렉이 심하고". 재 보니 노드 60개에서 첫
/// 화면이 58초였고, 그중 판을 그리는 값은 2.6ms였다. 나머지는 전부 <b>같은 일을 여러 번</b>
/// 한 값이었다. 시간(ms)은 기계마다 달라 고정할 수 없으므로, 여기서는 <b>일의 횟수</b>를
/// 건다 — 느려지는 회귀는 언제나 "몇 번 하는가"가 먼저 늘어난다.
/// </summary>
public sealed class ChapterGraphWorkAmountTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-work-amount", Guid.NewGuid().ToString("N"));

    private string ManifestPath => Path.Combine(_directory, "p" + ProjectManifestJson.FileExtension);

    public ChapterGraphWorkAmountTests()
    {
        Directory.CreateDirectory(_directory);

        string chapters = Path.Combine(_directory, ChapterLibrary.FolderName);
        ChapterWorkbookWriter.EnsureChapterWorkbook(chapters, "ch01", [("trust", "신뢰")]);
        string path = Path.Combine(chapters, "ch01.xlsx");

        string previous = string.Empty;

        for (int index = 0; index < 6; index++)
        {
            string id = $"ep{index}";
            ChapterWorkbookWriter.AddEpisode(path, id, title: $"제목{index}", index, 0);

            if (previous.Length > 0)
            {
                ChapterWorkbookWriter.AddEdge(path, previous, id);
            }

            previous = id;
        }

        ProjectStore.Save(ManifestPath, new StoryProject { Title = "일의 양" });
    }

    /// <summary>이 클래스가 띄운 화면. 폴더를 지우기 <b>전에</b> 닫는다.</summary>
    private readonly OpenChapterViews _ui = new();

    public void Dispose()
    {
        _ui.CloseAll();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 상태줄_한_줄이_프로젝트_변경으로_방송되지_않는다() => HeadlessUi.Run(() =>
    {
        // 이것이 58초의 뿌리였다: 상태를 적으면 프로젝트가 바뀌었다고 방송됐고, 챕터
        // 화면은 그 신호에 워크북을 전부 다시 읽었다. 그 재읽기가 다시 상태를 적었다.
        var session = new AuthoringSession();
        session.Open(ManifestPath);

        int projectChanges = 0;
        int statusChanges = 0;
        session.Changed += (_, _) => projectChanges++;
        session.StatusChanged += (_, _) => statusChanges++;

        session.SetStatus("에피소드 3개를 반영했습니다.");

        Assert.Equal(0, projectChanges);
        Assert.Equal(1, statusChanges);
        Assert.Equal("에피소드 3개를 반영했습니다.", session.StatusMessage);
    });

    [Fact]
    public void 몰려_오는_변경은_재읽기_한_번으로_합쳐진다() => HeadlessUi.Run(() =>
    {
        // 동기화 한 번이 프로젝트 변경을 수십 개 낸다. 예전에는 그 하나하나가 전체
        // 재읽기를 예약해서, 마지막 한 번 말고는 전부 버려질 그림을 그렸다.
        (ChapterGraphView view, AuthoringSession session, Window window) = Show();

        int reloads = 0;
        view.EntriesReloaded += _ => reloads++;

        for (int index = 0; index < 20; index++)
        {
            session.Editor.AddStoryFile($"판{index}");
        }

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, reloads);

        window.Close();
    });

    [Fact]
    public void 파일이_그대로면_다시_증명하지_않는다() => HeadlessUi.Run(() =>
    {
        // 검증은 대본 워크북을 전부 열고 상태공간을 훑는다 — 챕터 하나에 200ms 가까이.
        // 디스크가 그대로면 결과가 달라질 수 없다.
        (ChapterGraphView view, _, Window window) = Show();

        int before = view.ValidationComputeCount;

        for (int index = 0; index < 5; index++)
        {
            view.RefreshFromDisk();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal(before, view.ValidationComputeCount);

        window.Close();
    });

    [Fact]
    public void 워크북이_바뀌면_다시_증명한다() => HeadlessUi.Run(() =>
    {
        // 지문이 낡은 결과를 붙들면 "고쳤는데 오류가 그대로"가 된다 — 캐시가 만드는
        // 가장 나쁜 거짓말이다.
        (ChapterGraphView view, _, Window window) = Show();

        string path = Path.Combine(_directory, ChapterLibrary.FolderName, "ch01.xlsx");
        ChapterWorkbookWriter.AddEpisode(path, "새에피소드", title: "새것", 9, 9);

        int before = view.ValidationComputeCount;

        view.RefreshFromDisk();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(
            view.ValidationComputeCount > before,
            "워크북이 바뀌었으면 다시 증명해야 한다");

        window.Close();
    });

    [Fact]
    public void 우리가_쓴_저장으로는_감시자가_판을_다시_만들지_않는다() => HeadlessUi.Run(() =>
    {
        // 감시자는 남의 저장과 우리 저장을 구별하지 못한다. 툴이 쓴 자리는 그 자리에서
        // 이미 화면을 맞췄으므로, 250ms 뒤 오는 알림은 같은 그림을 한 번 더 그리라는
        // 주문이다 — 그 순간 사람이 누르고 있던 카드가 파괴된다.
        (ChapterGraphView view, _, Window window) = Show();

        int before = view.CanvasDrawCount;

        for (int index = 0; index < 5; index++)
        {
            view.ReloadIfDiskChanged();
            view.SyncEpisodesIfDiskChanged();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal(before, view.CanvasDrawCount);

        window.Close();
    });

    [Fact]
    public void 남이_엑셀에서_저장하면_감시자가_판을_다시_만든다() => HeadlessUi.Run(() =>
    {
        // 지문이 남의 저장까지 삼키면 "엑셀에서 고쳤는데 툴이 그대로"가 된다 — Gate A가
        // 지키던 바로 그것이다.
        (ChapterGraphView view, _, Window window) = Show();

        ChapterWorkbookWriter.AddEpisode(
            Path.Combine(_directory, ChapterLibrary.FolderName, "ch01.xlsx"),
            "밖에서온것", title: "남의 저장", 9, 9);

        int before = view.CanvasDrawCount;

        view.ReloadIfDiskChanged();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(
            view.CanvasDrawCount > before,
            "디스크가 실제로 바뀌었으면 판을 다시 만들어야 한다");

        window.Close();
    });

    [Fact]
    public void 손을_뗀_뷰는_더_이상_일하지_않는다() => HeadlessUi.Run(() =>
    {
        // 흔들리던 `ChapterGraphEditingTests`의 뿌리 (2026-08-18). 어셈블리 하나가
        // <b>디스패처 하나</b>를 나눠 쓰는데, 창을 안 닫고 끝낸 테스트의 뷰가 그대로 살아
        // 있었다. 그 뷰의 감시자는 이미 지워진 임시 폴더를 보고 있고, 거기서 난 사건이
        // <b>다음 테스트의</b> RunJobs()에서 깨어나 없는 프로젝트를 다시 읽었다.
        // 그래서 매번 다른 테스트가 깨졌고, 혼자 돌리면 통과했다.
        (ChapterGraphView view, AuthoringSession session, Window window) = Show();

        view.DetachSession();

        int drawsAfterDetach = view.CanvasDrawCount;
        int reloadsAfterDetach = 0;
        view.EntriesReloaded += _ => reloadsAfterDetach++;

        // 손을 뗐으니 세션이 무슨 말을 해도 이 뷰는 듣지 않는다.
        for (int index = 0; index < 10; index++)
        {
            session.Editor.AddStoryFile($"판{index}");
        }

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(drawsAfterDetach, view.CanvasDrawCount);
        Assert.Equal(0, reloadsAfterDetach);

        window.Close();
    });

    [Fact]
    public void 두_번_붙였어도_한_번_떼면_완전히_떨어진다() => HeadlessUi.Run(() =>
    {
        // 붙기만 하고 뗄 줄 모르던 뷰라, 두 번 붙으면 구독이 두 벌 쌓였다. 한 벌만
        // 떼어지면 <b>뗐다고 믿는 뷰가 계속 듣는다</b> — 이 테스트가 막는 것이 그것이다.
        //
        // ⚠ "재읽기가 한 번만 돈다"로는 이걸 못 잡는다. QueueReload가 한 차례에 하나로
        // 합치기 때문에 구독이 둘이어도 재읽기는 하나다 — 합치기가 이중 구독을 가려 준다.
        // 그래서 <b>뗀 뒤에</b> 본다.
        (ChapterGraphView view, AuthoringSession session, Window window) = Show();

        view.Attach(session);
        view.DetachSession();

        int reloads = 0;
        view.EntriesReloaded += _ => reloads++;

        session.Editor.AddStoryFile("판");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, reloads);

        window.Close();
    });

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
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        _ui.Own(view, window);

        return (view, session, window);
    }
}
