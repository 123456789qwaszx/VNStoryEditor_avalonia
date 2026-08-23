using Avalonia.Controls;
using Avalonia.VisualTree;
using ClosedXML.Excel;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// <b>엑셀에서 고친 대사가 연출 그래프까지 오는가</b> — 창 전체를 지나는 한 줄기
/// (2026-08-24 소유자 보고: "챕터그래프에서 대사노드의 엑셀을 열어서 고칠 경우, 연출그래프의
/// 동일한 엑셀노드에 반영이 안 되네").
///
/// ⚠ <b>이 사슬에는 여태 끝에서 끝까지 가는 테스트가 없었다.</b> 조각마다 테스트는 있었지만
/// (감시자·동기화·반영), 창을 세우고 탭을 오가며 <em>사람이 하는 순서 그대로</em> 밟는
/// 것은 없었다. 그래서 신고를 받고도 어디가 끊겼는지 코드만 읽어서는 답이 안 나왔다.
///
/// ⚠ <b>처음 만든 넷은 전부 통과했다</b>(감시자·탭 오가기·엑셀이 붙든 채·구조 변경).
/// 전부 <em>한 챕터짜리</em>였기 때문이다 — 챕터를 둘로 놓고서야 결함이 드러났다
/// (`지금_안_고른_챕터의_대본도_반영된다`). 통과하는 재현은 <b>재현이 아니라 범위의
/// 증언</b>이라는 것을 여기 남긴다: 다음에도 "안 된다"는 신고를 받으면, 통과하는 경로를
/// 늘리는 것이 곧 남은 경우를 좁히는 일이다.
/// </summary>
public sealed class ExcelToPresentationGraphTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "vn-excel-to-graph", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // 붙들고 있던 손이 늦게 놓을 수 있다 — 임시 폴더라 남아도 해가 없다.
            }
        }
    }

    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    [Fact]
    public void 엑셀에서_고친_대사가_연출_그래프_편집기에_온다() => HeadlessUi.Run(() =>
    {
        // 사람의 순서 그대로: 연출 그래프에서 그 노드를 열어 두고 → 챕터 그래프로 가서
        // 엑셀을 고치고 → 다시 연출 그래프로 돌아온다.
        World world = Open();

        world.SelectNodeInPresentationGraph();
        Assert.Contains("복도는 조용했다.", world.EditorText());

        world.ShowChapterGraph();
        world.EditWorkbook(sheet => sheet.Cell(2, 6).SetValue("엑셀에서 방금 고친 대사"));
        world.WaitForWatcher("엑셀에서 방금 고친 대사");
        world.ShowPresentationGraph();

        Assert.Contains("엑셀에서 방금 고친 대사", world.EditorText());
        Assert.DoesNotContain("복도는 조용했다.", world.EditorText());
    });

    [Fact]
    public void 엑셀이_파일을_붙들고_있어도_온다() => HeadlessUi.Run(() =>
    {
        // ⚠ 엑셀은 <b>저장했다고 파일을 놓지 않는다</b> — 곁에 ~$ 잠금 파일도 둔다.
        // 자동 테스트가 여태 흉내내지 않던 자리라 여기서 못 박는다.
        World world = Open();
        world.SelectNodeInPresentationGraph();

        world.EditWorkbook(sheet => sheet.Cell(2, 6).SetValue("붙들린 채로 고친 대사"));

        File.WriteAllText(Path.Combine(world.EpisodesFolder, "~$main05.02.xlsx"), "excel");

        using (new FileStream(
                   world.WorkbookPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            world.WaitForWatcher("붙들린 채로 고친 대사");
        }

        Assert.Contains("붙들린 채로 고친 대사", world.EditorText());
    });

    [Fact]
    public void 엑셀에서_바꾼_갈래가_카드의_포트에_온다() => HeadlessUi.Run(() =>
    {
        // 본문만이 아니라 <b>구조</b>도 와야 한다 — IF 갈래의 조건 라벨은 카드의 포트다.
        World world = Open();
        world.SelectNodeInPresentationGraph();

        Assert.Contains("신뢰높음", world.PortLabels());

        world.EditWorkbook(sheet =>
        {
            foreach (IXLRow row in sheet.RowsUsed())
            {
                // v14 — 조건라벨은 B열(2)이다.
                if (row.Cell(2).GetString().Trim() == "신뢰높음")
                {
                    row.Cell(2).SetValue("지쳐있음");
                }
            }
        });

        world.WaitForWatcher(text => !world.PortLabels().Contains("신뢰높음"));

        Assert.Contains("지쳐있음", world.PortLabels());
        Assert.DoesNotContain("신뢰높음", world.PortLabels());
    });

    [Fact]
    public void 연출_그래프_탭을_한_번도_안_열어도_반영된다() => HeadlessUi.Run(() =>
    {
        // 편집기가 화면에 없는 동안 온 변경이 <b>나중에 열 때</b> 보여야 한다 — 숨은 탭이
        // 갱신을 놓치면 여기서 옛 글이 뜬다.
        World world = Open();

        world.EditWorkbook(sheet => sheet.Cell(2, 6).SetValue("탭을 열기 전에 고친 대사"));
        world.WaitForWatcher("탭을 열기 전에 고친 대사");

        world.SelectNodeInPresentationGraph();

        Assert.Contains("탭을 열기 전에 고친 대사", world.EditorText());
    });

    [Fact]
    public void 지금_안_고른_챕터의_대본도_반영된다() => HeadlessUi.Run(() =>
    {
        // ⛔ <b>이것이 소유자가 겪은 그 결함이다</b> (2026-08-24). 반영은 <em>고른 챕터</em>
        // 하나만 돌았는데, 연출 그래프는 <b>모든 판의 노드를 함께</b> 보여 준다. 그래서
        // 다른 챕터의 대사노드는 그 챕터를 다시 고르기 전까지 영영 낡은 글을 들고 있었다 —
        // 챕터가 둘 이상인 프로젝트에서는 늘 그랬고, 그래서 "반영이 안 되네"가 나왔다.
        //
        // 위 넷은 전부 통과했었다(한 챕터짜리라서). 챕터를 둘로 놓고서야 재현됐다.
        World world = Open(secondChapter: true);

        world.VisitChapter("ch06");
        world.VisitChapter("ch05");   // ← 지금 고른 챕터는 ch05다

        Assert.Contains("복도는 조용했다.", world.ProjectTextIn("ch06"));

        world.EditWorkbook(
            Path.Combine(world.EpisodesRoot, "ch06", "main05.02.xlsx"),
            sheet => sheet.Cell(2, 6).SetValue("안 고른 챕터에서 고친 대사"));

        world.WaitFor(() =>
            world.ProjectTextIn("ch06").Contains("안 고른 챕터에서 고친 대사", StringComparison.Ordinal));

        Assert.Contains("안 고른 챕터에서 고친 대사", world.ProjectTextIn("ch06"));

        // 고른 챕터가 밀려나지도 않았다 — 둘 다 살아 있어야 한다.
        Assert.Contains("복도는 조용했다.", world.ProjectTextIn("ch05"));
    });

    [Fact]
    public void 판이_없는_챕터는_미리_만들지_않는다() => HeadlessUi.Run(() =>
    {
        // ⚠ 고침의 범위 — 따라잡는 것은 <b>판이 이미 선</b> 챕터뿐이다. 전부 돌면
        // `EnsureChapterBoard`가 아무도 안 연 챕터의 판까지 미리 만들어, 고치려던 것보다
        // 큰 변화가 된다.
        World world = Open(secondChapter: true);

        world.VisitChapter("ch05");   // ch06은 한 번도 안 골랐다

        Assert.False(
            world.HasBoard("ch06"),
            "안 연 챕터의 판을 미리 만들면 프로젝트에 빈 판이 쌓인다");
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    private World Open(bool secondChapter = false)
    {
        Directory.CreateDirectory(Path.Combine(_root, ChapterLibrary.FolderName));

        string[] chapters = secondChapter ? ["ch05", "ch06"] : ["ch05"];

        foreach (string chapter in chapters)
        {
            File.Copy(SamplePath, Path.Combine(_root, ChapterLibrary.FolderName, chapter + ".xlsx"));

            string folder = Path.Combine(_root, "episodes", chapter);
            Directory.CreateDirectory(folder);
            File.Copy(SamplePath, Path.Combine(folder, "main05.02.xlsx"));
        }

        string episodes = Path.Combine(_root, "episodes", "ch05");

        string manifest = Path.Combine(_root, "p" + ProjectManifestJson.FileExtension);
        ProjectStore.Save(manifest, new StoryProject { Title = "관통 검증" });

        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        window.SessionProbe.Open(manifest);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        window.FindControl<ChapterGraphView>("ChapterGraph")!.SyncEpisodes();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return new World(window, episodes);
    }

    private sealed class World(MainWindow window, string episodesFolder)
    {
        public string EpisodesFolder => episodesFolder;

        public string EpisodesRoot => Path.GetDirectoryName(episodesFolder)!;

        public string WorkbookPath => Path.Combine(episodesFolder, "main05.02.xlsx");

        /// <summary>그 챕터를 한 번 고른다 — 판이 서고 대사노드가 선다.</summary>
        public void VisitChapter(string chapterId)
        {
            ShowChapterGraph();
            window.FindControl<ChapterGraphView>("ChapterGraph")!.SelectChapter(chapterId);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        public bool HasBoard(string chapterId) => Session.Project.Files
            .Any(file => string.Equals(file.Name, chapterId, StringComparison.Ordinal));

        /// <summary>그 챕터 판의 엑셀노드가 들고 있는 글.</summary>
        public string ProjectTextIn(string chapterId)
        {
            DialogueNode? node = Session.Project.Files
                .FirstOrDefault(file => file.Name == chapterId)?.Nodes
                .OfType<DialogueNode>()
                .FirstOrDefault(item => item.ExcelEpisodeId == "main05.02");

            return node is null
                ? "<노드 없음>"
                : string.Join(" | ", Session.Project.FindScript(node.ScriptId)!.Locales
                    .SelectMany(locale => locale.Entries.Values)
                    .Select(line => line.Text));
        }

        public void EditWorkbook(string path, Action<IXLWorksheet> edit)
        {
            using var book = new XLWorkbook(path);

            edit(book.Worksheets
                .First(candidate => candidate.Cell(1, 1).GetString().Trim() == "유형"));

            book.SaveAs(path);
        }

        /// <summary>진짜 감시자를 기다린다 — 조건이 참이 될 때까지.</summary>
        public void WaitFor(Func<bool> until)
        {
            // 위 WaitForWatcher의 ⚠ 그대로 — 전체 스위트에서는 넉넉해야 한다.
            for (int tick = 0; tick < 200; tick++)
            {
                Thread.Sleep(100);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                if (until())
                {
                    return;
                }
            }

            Assert.Fail("감시자가 변경을 물어오지 않았다");
        }

        private AuthoringSession Session => window.SessionProbe;

        private DialogueNode Node => Session.Project.EnumerateNodes().OfType<DialogueNode>()
            .Single(item => item.ExcelEpisodeId == "main05.02");

        public void ShowChapterGraph() => SelectTab(0);

        public void ShowPresentationGraph() => SelectTab(1);

        public void SelectNodeInPresentationGraph()
        {
            ShowPresentationGraph();
            Session.Select(Node.Id);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        /// <summary>엑셀이 저장한 것처럼 워크북을 고친다.</summary>
        public void EditWorkbook(Action<IXLWorksheet> edit)
        {
            using var book = new XLWorkbook(WorkbookPath);

            edit(book.Worksheets
                .First(candidate => candidate.Cell(1, 1).GetString().Trim() == "유형"));

            book.SaveAs(WorkbookPath);
        }

        /// <summary>
        /// ⚠ <b>진짜 감시자</b>를 기다린다 — `SyncEpisodesIfDiskChanged`를 직접 치지 않는다.
        /// 그 자리를 치면 감시자가 죽어 있어도 테스트가 통과한다.
        /// </summary>
        public void WaitForWatcher(string expected) =>
            WaitForWatcher(_ => ProjectText().Contains(expected, StringComparison.Ordinal));

        public void WaitForWatcher(Func<string, bool> until)
        {
            // ⚠ 넉넉해야 한다. 감시자는 250ms 디바운스에 파일 사건을 기다리는데, 전체
            // 스위트가 함께 돌 때는 어셈블리 하나가 <b>디스패처를 나눠 쓰고</b> 디스크도
            // 붐빈다 — 6초로 뒀더니 혼자서는 늘 통과하면서 전체 실행에서 한 번 넘어졌다.
            // 사슬이 끊기면 어차피 여기서 실패하므로, 기다림을 늘려도 잡는 힘은 그대로다.
            for (int tick = 0; tick < 200; tick++)
            {
                Thread.Sleep(100);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                if (until(ProjectText()))
                {
                    return;
                }
            }

            Assert.Fail($"감시자가 변경을 물어오지 않았다 — 지금 대본: {ProjectText()}");
        }

        /// <summary>화면이 아니라 <b>프로젝트</b>의 글 — 반영이 왔는지의 근거.</summary>
        private string ProjectText() => string.Join(" | ",
            Session.Project.FindScript(Node.ScriptId)!.Locales
                .SelectMany(locale => locale.Entries.Values)
                .Select(line => line.Text));

        /// <summary>연출 그래프 곁기둥의 대사 편집기가 <b>지금 보여 주는</b> 줄들.</summary>
        public string EditorText()
        {
            if (window.GetVisualDescendants().OfType<DialogueNodeEditor>().FirstOrDefault()
                    ?.FindControl<StackPanel>("LineHost") is not { } host)
            {
                return "<편집기가 화면에 없다>";
            }

            return string.Join(" | ", host.GetVisualDescendants().OfType<TextBox>()
                .Where(box => box.FindAncestorOfType<AutoCompleteBox>() is null)
                .Select(box => box.Text ?? string.Empty));
        }

        /// <summary>판 위 카드의 갈래 포트 이름들 — 구조가 왔는지의 근거.</summary>
        public string PortLabels() => string.Join(" | ",
            window.FindControl<GraphEditorView>("Graph")!
                .FindControl<Canvas>("GraphCanvas")!
                .GetVisualDescendants().OfType<TextBlock>()
                .Select(block => block.Text ?? string.Empty));

        private void SelectTab(int index)
        {
            window.FindControl<TabControl>("MainTabs")!.SelectedIndex = index;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }
}
