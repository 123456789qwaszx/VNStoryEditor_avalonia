using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.VisualTree;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// Gate A 1번 — "견본 워크북을 프로젝트에 넣으면 그래프가 엑셀에 적힌 X·Y 위치와 간선 관계
/// 그대로 그려진다"를 <b>사람 눈 없이</b> 닫는다.
///
/// 창을 띄우고 좌표를 찍어 클릭하는 방식은 쓰지 않는다 — 창이 앞으로 올라왔는지 확인할 수 없어
/// 남의 창을 누르게 된다. 헤드리스로 진짜 시각 트리를 만들고 배치를 돌린 뒤, 그려진 것을
/// 이름으로 확인한다.
/// </summary>
public sealed class ChapterGraphViewRenderTests
{
    private const double CardWidth = 190;
    private const double CardHeight = 74;

    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    [Fact]
    public void 견본_챕터가_6노드_5간선으로_그려진다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (Canvas canvas, _) = Render(project);

        Assert.Equal(6, NodeCards(canvas).Count);
        // 간선 하나가 선분 여럿(포트 꺾임)일 수 있다 — 표식(Tag)의 종류로 센다.
        Assert.Equal(5, canvas.Children.OfType<Line>()
            .Where(line => line.Tag is string)
            .Select(line => (string)line.Tag!)
            .Distinct(StringComparer.Ordinal)
            .Count());
    });

    [Fact]
    public void 그려진_노드는_에피소드가_진_자리에_선다() => HeadlessUi.Run(() =>
    {
        // ⛔ <b>v3까지 이 테스트의 이름은 「깊이 열에 선다」였다</b> — 판을 그릴 때마다
        //    깊이 배치를 다시 계산했고, 엑셀의 X·Y는 내보내기에나 쓰였다. v4(2026-09-18
        //    소유자: *"자유롭게 노드의 위치를 조절할 수 있게"*)가 그것을 뒤집었다:
        //    이제 <b>사람이 적어 둔 자리 그대로</b> 선다.
        //
        //    그래서 여기 적힌 숫자는 배치 산식이 아니라 <b>견본 워크북의 X 열</b>이다:
        //    01=0 · 02=220 · A=440 · 03=440 · end=680. 깊이 배치라면 03은 660(A 다음 열)에
        //    섰을 텐데, 사람은 A와 같은 열에 두었고 이제 그 뜻이 이긴다.
        using var project = new TempProject(SamplePath);
        (Canvas canvas, _) = Render(project);

        IReadOnlyDictionary<string, (double X, double Y)> placed = Placements(canvas);

        Assert.Equal(220, placed["main05.02"].X - placed["main05.01"].X);
        Assert.Equal(440, placed["branch05.02A"].X - placed["main05.01"].X);
        Assert.Equal(440, placed["main05.03"].X - placed["main05.01"].X);
        Assert.Equal(680, placed["main05.end"].X - placed["main05.01"].X);

        // 세로도 워크북 그대로다 — A는 본줄 위(−120), 03은 아래(+120).
        Assert.Equal(-120, placed["branch05.02A"].Y - placed["main05.01"].Y);
        Assert.Equal(120, placed["main05.03"].Y - placed["main05.01"].Y);

        // 간선 없는 부착 노드는 본줄 아래에 따로 선다 (워크북이 그렇게 적어 두었다).
        Assert.True(
            placed["attach05.02s"].Y > placed["main05.01"].Y,
            $"attach05.02s(Y={placed["attach05.02s"].Y})는 본류(Y={placed["main05.01"].Y}) 아래여야 한다");
    });

    [Fact]
    public void 음수_자리도_판_안으로_들어온다() => HeadlessUi.Run(() =>
    {
        // ⛔ v3의 깊이 배치는 언제나 0 이상을 냈지만 손으로 적은 워크북은 아니다 —
        //    견본의 `branch05.02A`가 Y=−120이다. 그대로 그리면 캔버스 위쪽 바깥에 서고,
        //    스크롤이 닿지 않아 <b>아예 못 만지는 카드</b>가 된다.
        using var project = new TempProject(SamplePath);
        (Canvas canvas, _) = Render(project);

        IReadOnlyDictionary<string, (double X, double Y)> placed = Placements(canvas);

        Assert.All(placed.Values, position =>
        {
            Assert.True(position.X >= 0, $"X={position.X}는 판 밖이다");
            Assert.True(position.Y >= 0, $"Y={position.Y}는 판 밖이다");
        });

        // 밀되 <b>거리는 그대로</b>다 — 사람이 짠 모양이 바뀌면 미는 뜻이 없다.
        Assert.Equal(240, placed["main05.03"].Y - placed["branch05.02A"].Y);
    });

    [Fact]
    public void 그려진_간선이_엑셀의_관계_그대로다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (Canvas canvas, _) = Render(project);

        string[] drawn = canvas.Children.OfType<Line>()
            .Where(line => line.Tag is string)   // 히트 선(무표식)은 제외
            .Select(line => (string)line.Tag!)
            .Distinct(StringComparer.Ordinal)    // 포트 꺾임 = 같은 간선의 선분 여럿
            .OrderBy(tag => tag, StringComparer.Ordinal)
            .ToArray();

        // 표식은 라벨까지 담는다 (2026-08-15 — 간선 신원 = 출발·도착·라벨).
        Assert.Equal(
            [
                "branch05.02A→main05.03 [계속]",
                "main05.01→main05.02 [계속]",
                "main05.02→branch05.02A [라루의 제안을 듣는다]",
                "main05.02→main05.03 [혼자 문을 연다]",
                "main05.03→main05.end [계속]"
            ],
            drawn);
    });

    [Fact]
    public void 간선은_카드_아래의_포트에서_도착_카드_위의_점으로_간다() => HeadlessUi.Run(() =>
    {
        // ⛔ <b>v4까지는 오른변 → 왼변이었다.</b> 연출 그래프는 정확히 반대(선택지가 아래,
        //    분기가 오른쪽)였고, 소유자가 그것을 짚었다 (2026-09-18): *"여기는 그게 반대로
        //    분기가 아래, 선택지가 우측이다보니 헷갈립니다."* 판이 둘인데 같은 것이 다른
        //    변에서 나가면 손이 매번 헷갈린다.
        using var project = new TempProject(SamplePath);
        (Canvas canvas, _) = Render(project);

        IReadOnlyDictionary<string, (double X, double Y)> placed = Placements(canvas);
        Line edge = canvas.Children.OfType<Line>()
            .Single(line => (string?)line.Tag == "main05.01→main05.02 [계속]");

        // 출발: 아래변의 첫 칸. 칸이 셋이면 카드 너비의 1/4 자리다.
        Assert.Equal(placed["main05.01"].X + (CardWidth / 4), edge.StartPoint.X, 3);
        Assert.Equal(placed["main05.01"].Y + CardHeight + 5, edge.StartPoint.Y, 3);

        // 도착: 위변 <b>가운데 점 하나</b> — 들어오는 길은 전부 여기로 모인다.
        Assert.Equal(placed["main05.02"].X + (CardWidth / 2), edge.EndPoint.X, 3);
        Assert.Equal(placed["main05.02"].Y - 8, edge.EndPoint.Y, 3);
    });

    [Fact]
    public void 선택지_간선은_꺾이지_않고_직선_하나로_간다() => HeadlessUi.Run(() =>
    {
        // 2026-08-23 소유자 보고 — "간선을 수직선으로 그어주다보니 … 선이 완전히 겹쳐
        // 어디로 이어지는지 확인하기가 힘들어". 예전에는 포트 간선이 직교 3구간이었고
        // 꺾이는 x가 출발·도착의 중간이라, 같은 열로 가는 길들이 세로 구간을 공유했다.
        using var project = new TempProject(SamplePath);
        (Canvas canvas, _) = Render(project);

        foreach (string tag in new[]
                 {
                     "main05.02→branch05.02A [라루의 제안을 듣는다]",
                     "main05.02→main05.03 [혼자 문을 연다]"
                 })
        {
            Assert.Single(
                canvas.Children.OfType<Line>().Where(line => (string?)line.Tag == tag),
                line => line.StartPoint != line.EndPoint);
        }
    });

    [Fact]
    public void 한_에피소드에서_나가는_길들은_서로_다른_방향으로_뻗는다() => HeadlessUi.Run(() =>
    {
        // 이것이 소유자가 실제로 겪은 문제다. 겹치지 않는다는 말의 뜻은 "기울기가 다르다"이고,
        // 직선이면 그것이 저절로 성립한다 — 포트마다 출발 y가 다르고 도착마다 자리가 다르다.
        using var project = new TempProject(SamplePath);
        (Canvas canvas, _) = Render(project);

        (double Dx, double Dy)[] directions = canvas.Children.OfType<Line>()
            .Where(line => (string?)line.Tag is { } tag && tag.StartsWith("main05.02→", StringComparison.Ordinal))
            .Select(line => (
                Dx: line.EndPoint.X - line.StartPoint.X,
                Dy: line.EndPoint.Y - line.StartPoint.Y))
            .ToArray();

        Assert.Equal(2, directions.Length);

        // 두 방향이 평행하면(외적 0) 화면에서 한 줄로 보인다 — 그것이 예전 모습이었다.
        double cross = (directions[0].Dx * directions[1].Dy) - (directions[0].Dy * directions[1].Dx);

        Assert.True(
            Math.Abs(cross) > 1.0,
            $"두 길이 같은 방향으로 뻗는다(외적 {cross:0.###}) — 화면에서 겹쳐 보인다");
    });

    [Fact]
    public void 도달_불가가_원인_조건과_함께_검증_보고에_선다() => HeadlessUi.Run(() =>
    {
        // 규칙 개정 — 도달성 증명(G7)이 뷰에 붙으면서, 견본 챕터의 실제 도달 불가가
        // 화면에 뜬다. 에피소드 워크북이 없으면 스탯이 오르지 않으므로 신뢰높음(trust >= 3)이
        // 영원히 닫히고 branch05.02A에 닿을 수 없다 — 저작 시점에 잡히는 것이 이 레이어의 목적이다.
        using var project = new TempProject(SamplePath);

        // 대본은 채워 둔다 — 빈 노드는 2026-08-25부터 그 자체가 오류라, 안 채우면
        // 이 테스트가 재려는 도달 불가 한 건이 그 더미에 묻힌다.
        EpisodeWorkbookFixture.Fill(project.EpisodesFolder);
        (Canvas canvas, ChapterGraphView view) = Render(project);

        var expander = view.FindControl<Expander>("DiagnosticsExpander")!;

        // ⚠ 2026-08-24 — <b>저절로 펼쳐지지 않는다</b> (소유자: "그것까지 꺼줘"). 예전에는
        // 오류가 있으면 열고 없으면 닫아서, 사람이 접어 둔 것을 저장할 때마다 다시 열었다.
        // 이제 그 칸은 사람만 만지고, 알림은 머리글의 표식이 든다.
        Assert.False(expander.IsExpanded);

        // 표식이 유일한 알림 창구다 — 관례대로 오류는 빨강, 경고는 노랑이다.
        Assert.Contains("🔴 오류 1", (string)expander.Header!);

        // 경고 4건 — 스탯이 game.definition.json(기본값은 variables가 비어 있다)에 없다는
        // 것(`스탯` 시트가 "읽기전용 미러"라는 규격 그대로의 보고)이 스탯 수만큼이다.
        // v12 — 문구 없는 간선이 사라지면서 그 경고 하나가 빠졌다(견본이 문구를 갖는다).
        // 2026-08-25 — `복도지남` 깃발이 늘면서 미러 경고가 하나 더 는다(스탯 3 → 4).
        Assert.Contains("🟡 경고 4", (string)expander.Header!);

        // 접힌 채로도 사람이 열면 목록이 그대로 있다 — 원할 때 언제든 확인한다.
        expander.IsExpanded = true;
        Relayout(view);

        // 원인 조건까지 짚는다.
        var panel = view.FindControl<StackPanel>("DiagnosticsPanel")!;
        Assert.Contains(panel.Children.OfType<TextBlock>(), block =>
            block.Text?.Contains("branch05.02A") == true &&
            block.Text.Contains("trust >= 3"));

        // 도달 불가 노드는 그래프에서도 ⚠로 선다.
        Border card = canvas.Children.OfType<Border>()
            .Single(border => (border.Tag as string) == "branch05.02A");

        Assert.Contains(((StackPanel)card.Child!).Children.OfType<StackPanel>().Single()
            .Children.OfType<TextBlock>(), mark => mark.Text == "⚠");
    });

    [Fact]
    public void 노드_카드는_배치_뒤_실제_크기를_갖는다() => HeadlessUi.Run(() =>
    {
        // "그려졌다"의 최소 조건 — 배치가 돌아 카드가 0×0이 아니어야 한다.
        using var project = new TempProject(SamplePath);
        (Canvas canvas, _) = Render(project);

        Assert.All(NodeCards(canvas), card =>
        {
            Assert.Equal(CardWidth, card.Bounds.Width);
            // 카드는 보이는 선택지 칸 수만큼 아래로 자란다 (포트 줄 18px).
            Assert.True(card.Bounds.Height >= CardHeight,
                $"카드 높이 {card.Bounds.Height} < 기본 {CardHeight}");
        });
    });

    [Fact]
    public void 챕터_워크북이_없으면_어디에_넣으라고_알려_준다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(samplePath: null);
        (Canvas canvas, ChapterGraphView view) = Render(project);

        var empty = view.FindControl<TextBlock>("EmptyText")!;

        Assert.Empty(canvas.Children);
        Assert.True(empty.IsVisible);
        Assert.Contains(ChapterLibrary.FolderName, empty.Text);

        // 프로젝트는 있는데 챕터가 없는 첫 화면 — 안내문 아래 판 한가운데에 [＋ 챕터]가
        // 함께 선다. [새 프로젝트]는 아니다: 프로젝트는 이미 있다.
        Assert.True(view.ChapterAddCenterButton.IsVisible);
        Assert.False(view.ProjectNewCenterButton.IsVisible);
    });

    [Fact]
    public void 프로젝트가_없으면_새_프로젝트_단추가_먼저_선다() => HeadlessUi.Run(() =>
    {
        // 2026-08-26 소유자 — "새 프로젝트를 만들지도 않았는데 +챕터버튼부터 나오니
        // 오히려 헷갈려." 첫걸음은 순서다: 프로젝트가 먼저고 챕터는 그 다음이다.
        var session = new AuthoringSession();   // 열지도 저장하지도 않았다 — ProjectPath 없음.
        var view = new ChapterGraphView();
        var window = new Window { Width = 1280, Height = 800, Content = view };
        window.Show();

        try
        {
            view.Attach(session);
            window.Measure(new Avalonia.Size(1280, 800));
            window.Arrange(new Avalonia.Rect(0, 0, 1280, 800));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.True(view.ProjectNewCenterButton.IsVisible);
            Assert.False(view.ChapterAddCenterButton.IsVisible);
        }
        finally
        {
            view.DetachSession();
            window.Close();
        }
    });

    [Fact]
    public void 빈_챕터를_열면_가운데_에피소드_추가_단추가_선다() => HeadlessUi.Run(() =>
    {
        // 셋째 걸음 (2026-08-26 소유자: "챕터를 열었는데, 아무 에피소드가 없을 땐
        // + 에피소드 버튼이 나오도록") — 새 챕터의 에피소드 시트는 머리글뿐이다.
        using var project = new TempProject(samplePath: null);
        string chapters = Path.Combine(
            Path.GetDirectoryName(project.ManifestPath)!, ChapterLibrary.FolderName);
        ChapterWorkbookWriter.EnsureChapterWorkbook(chapters, "ch01");

        (_, ChapterGraphView view) = Render(project);

        Assert.True(view.EpisodeAddCenterButton.IsVisible);
        // 앞의 두 걸음은 이미 지났다 — 프로젝트도 챕터도 있다.
        Assert.False(view.ProjectNewCenterButton.IsVisible);
        Assert.False(view.ChapterAddCenterButton.IsVisible);
    });

    [Fact]
    public void 챕터가_서면_가운데_첫걸음_단추는_사라진다() => HeadlessUi.Run(() =>
    {
        // 이 단추들은 "아무것도 없을 때의 첫걸음"이다 — 판이 그려지는 순간 할 일은
        // 만들기가 아니라 그리기·잇기이고, 만들기는 우측 기둥의 [＋]가 계속 진다.
        using var project = new TempProject(SamplePath);
        (_, ChapterGraphView view) = Render(project);

        Assert.False(view.ChapterAddCenterButton.IsVisible);
        Assert.False(view.ProjectNewCenterButton.IsVisible);
        Assert.False(view.EpisodeAddCenterButton.IsVisible);
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static List<Border> NodeCards(Canvas canvas) =>
        canvas.Children.OfType<Border>().Where(border => border.Tag is string).ToList();

    private static IReadOnlyDictionary<string, (double X, double Y)> Placements(Canvas canvas) =>
        NodeCards(canvas).ToDictionary(
            card => (string)card.Tag!,
            card => (Canvas.GetLeft(card), Canvas.GetTop(card)),
            StringComparer.Ordinal);

    [Fact]
    public void 검증_보고는_늘_서_있되_접히면_한_줄만_먹는다() => HeadlessUi.Run(() =>
    {
        // 2026-08-24 소유자 — "이건 굉장히 중요하고, 상시로 띄워놓는 건 맞는데, 원할 때
        // 언제든 확인은 해야하되, 시각적으로 크게 보일 필요는 없어."
        //
        // Fluent의 Expander 머리글은 최소 48px이라 접혀 있어도 판 아래를 그만큼 늘 먹었다.
        // ⚠ 그 48은 템플릿 안에서 토글에 <b>직접</b> 박히는 값이라 Style 세터로는 못 이긴다
        // (해 보고 알았다). 템플릿이 쳐다보는 리소스를 Expander 자리에서 갈아 끼운다.
        using var project = new TempProject(SamplePath);
        (_, ChapterGraphView view) = Render(project);

        var expander = view.FindControl<Expander>("DiagnosticsExpander")!;

        // 늘 서 있다 — 접히는 것이지 사라지는 것이 아니다.
        Assert.True(expander.IsVisible);

        expander.IsExpanded = false;
        Relayout(view);

        Assert.True(
            expander.Bounds.Height is > 0 and < 32,
            $"접힌 검증 보고가 {expander.Bounds.Height:F0}px를 먹는다 — 한 줄이어야 한다");
    });

    /// <summary>바꾼 뒤 다시 재고 배치한다 — 안 하면 Bounds가 옛 값이다.</summary>
    private static void Relayout(ChapterGraphView view)
    {
        if (TopLevel.GetTopLevel(view) is Window window)
        {
            window.Measure(new Avalonia.Size(window.Width, window.Height));
            window.Arrange(new Avalonia.Rect(0, 0, window.Width, window.Height));
        }

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static (Canvas Canvas, ChapterGraphView View) Render(TempProject project)
    {
        var session = new AuthoringSession();
        session.Open(project.ManifestPath);

        var view = new ChapterGraphView();
        var window = new Window { Width = 1280, Height = 800, Content = view };
        window.Show();

        view.Attach(session);

        // 대본은 이제 사람이 들여온다 (R-D) — 노드가 없으면 "빈 노드" 오류가 더미로 쌓여
        // 이 클래스가 재려는 진단이 그 속에 묻힌다.
        view.ImportEpisodes();

        // 배치를 실제로 돌린다 — 이걸 하지 않으면 Bounds가 전부 0이고 "그려졌다"가 거짓이 된다.
        window.Measure(new Avalonia.Size(1280, 800));
        window.Arrange(new Avalonia.Rect(0, 0, 1280, 800));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 캔버스는 스크롤 안이라 창 배치만으로는 자기 크기까지 펴지지 않는다. 그린 판 전체를
        // 대상으로 한 번 더 돌려야 카드가 실제 크기를 갖는다.
        Canvas canvas = view.FindControl<Canvas>("GraphCanvas")!;
        canvas.Measure(new Avalonia.Size(canvas.Width, canvas.Height));
        canvas.Arrange(new Avalonia.Rect(0, 0, canvas.Width, canvas.Height));

        project.Ui.Own(view, window);

        return (canvas, view);
    }

    /// <summary>견본을 chapters/ 아래 둔 임시 프로젝트. 뷰가 실제로 읽는 자리 그대로다.</summary>
    private sealed class TempProject : IDisposable
    {
        private readonly string _directory;

        /// <summary>이 테스트가 띄운 화면. 폴더를 지우기 <b>전에</b> 닫는다.</summary>
        public OpenChapterViews Ui { get; } = new();

        public TempProject(string? samplePath)
        {
            _directory = Path.Combine(
                Path.GetTempPath(), "vn-chapter-render", Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(_directory);

            if (samplePath is not null)
            {
                Directory.CreateDirectory(Path.Combine(_directory, ChapterLibrary.FolderName));
                File.Copy(samplePath, Path.Combine(_directory, ChapterLibrary.FolderName, "ch05.xlsx"));
            }

            ManifestPath = Path.Combine(_directory, "project" + ProjectManifestJson.FileExtension);
            ProjectStore.Save(ManifestPath, new StoryProject { Title = "렌더 검증" });
        }

        public string ManifestPath { get; }

        /// <summary>그 챕터의 대본 폴더 — episodes/{ChapterId}/ (2026-08-16 챕터별 격리).</summary>
        public string EpisodesFolder => Path.Combine(_directory, "episodes", "ch05");

        public void Dispose()
        {
            Ui.CloseAll();

            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
