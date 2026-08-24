using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Definition;
using Vn.Authoring.Serialization;
using Vn.Authoring.Model;

namespace Vn.App.Tests;

/// <summary>
/// 챕터 설정 노드 화면 — 아이템·능력 편집 (2026-08-17).
/// </summary>
public sealed class SetNodeEditorTests
{
    // ── 종류는 태어날 때 정해진다 (2026-08-23 소유자) ──────────────────────
    //
    // "조건 추가와 변수 추가할 시에 현재는 bool과 int 타입이 서로 오고가고 있는데 …
    // 오히려 직관적이지 않아서 불편하다." 종류 드롭다운을 없애고 추가 단추를 갈랐다.
    //
    // 이 규칙이 2026-08-17의 버그(능력→아이템에서 초기값이 `false`로 넘어오던 것)를
    // <b>구조적으로</b> 없앤다 — 값 공간이 섞일 자리 자체가 사라졌다.

    [Fact]
    public void 종류는_만들_때_정해지고_행에서_바뀌지_않는다() => HeadlessUi.Run(() =>
    {
        (SetNodeEditor editor, AuthoringSession session, SetNode node, Window window) = Show();

        Click(editor, "AddItemButton");
        Click(editor, "AddAbilityButton");

        var after = (SetNode)session.Project.FindNode(node.Id)!;

        // 아이템은 숫자 0에서, 능력은 Off에서 태어난다 — 각자 자기 값 공간의 기본값이다.
        Assert.Equal(
            [VariableAssignment.FloatType, VariableAssignment.BoolType],
            after.Assignments.Select(item => item.Type));
        Assert.Equal(["0", "false"], after.Assignments.Select(item => item.Value));

        editor.Rebuild();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 종류를 바꾸는 콤보가 아예 없다. 조건 탭의 대상 드롭다운과 달리 여기에는
        // 고를 것이 하나도 없어야 한다.
        Assert.Empty(editor.FindControl<StackPanel>("AssignmentHost")!
            .GetVisualDescendants()
            .OfType<ComboBox>());

        window.Close();
    });

    [Fact]
    public void 표에_머리글과_순번이_선다() => HeadlessUi.Run(() =>
    {
        // 소유자 — "index 순번과 구분을 엑셀처럼 가진채로 잘 정리되어서 가독성 좋게".
        (SetNodeEditor editor, AuthoringSession session, SetNode node, Window window) = Show();

        session.Editor.SetAssignments(node.Id,
        [
            new VariableAssignment { Variable = "약초", Value = "0", Type = VariableAssignment.FloatType },
            new VariableAssignment { Variable = "밧줄", Value = "0", Type = VariableAssignment.FloatType }
        ]);

        editor.Rebuild();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        List<string> texts = editor.FindControl<StackPanel>("AssignmentHost")!
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(block => block.Text ?? string.Empty)
            .ToList();

        Assert.Contains("#", texts);
        Assert.Contains("이름", texts);
        Assert.Contains("초기값", texts);

        // 순번은 1부터 — 표 안에서만 뜻이 있는 번호다.
        Assert.Contains("1", texts);
        Assert.Contains("2", texts);

        // 표가 몇 줄인지 제목이 말한다.
        Assert.Contains(texts, text => text.StartsWith("아이템 (개수) (2)", StringComparison.Ordinal));

        window.Close();
    });

    [Fact]
    public void 조건_드롭다운은_같은_종류만_담고_열_때마다_다시_읽는다() => HeadlessUi.Run(() =>
    {
        // 소유자 보고 (2026-08-23) — "아이템을 +추가를 한 뒤에 능력을 적고나서 조건에
        // 반영시키려면 다른 노드에 갔다와야 하는게 불편해." 아이템·능력 편집은 Content
        // 변경이라 이 화면을 다시 만들지 않으므로(타이핑 중 컨트롤 파괴 방지), 조건 행의
        // 후보가 행을 만들 때의 것으로 굳어 있었다.
        (SetNodeEditor editor, AuthoringSession session, SetNode node, Window window) = Show();

        var herb = new VariableAssignment
        {
            Variable = "약초",
            Value = "0",
            Type = VariableAssignment.FloatType
        };

        session.Editor.SetAssignments(node.Id, [herb]);
        session.Editor.AddCondition(node.Id, "약초있음", "$약초 >= 1");

        editor.Rebuild();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ComboBox target = editor.FindControl<StackPanel>("ConditionHost")!
            .GetVisualDescendants()
            .OfType<ComboBox>()
            .First();

        Assert.Equal(["약초"], target.ItemsSource!.Cast<string>());
        Assert.Equal("약초", target.SelectedItem);

        // 아이템 하나와 능력 하나를 더한다 — 화면을 다시 만드는 손은 여기에도, 앱에도 없다.
        session.Editor.SetAssignments(node.Id,
        [
            herb,
            new VariableAssignment { Variable = "밧줄", Value = "0", Type = VariableAssignment.FloatType },
            new VariableAssignment { Variable = "자물쇠따기", Value = "false", Type = VariableAssignment.BoolType }
        ]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(["약초"], target.ItemsSource!.Cast<string>()); // 아직 굳어 있다

        // 여는 순간이 곧 "지금 무엇이 있나"를 묻는 순간이다.
        target.IsDropDownOpen = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // ⚠ 아이템 조건의 대상에는 <b>아이템만</b> 온다 — 능력을 고를 수 있으면 행이
        // 부호·수치를 든 채 능력을 가리키게 된다.
        Assert.Equal(["약초", "밧줄"], target.ItemsSource!.Cast<string>());

        // 고르고 있던 값은 살아남는다 — 목록을 갈아 끼우다 조건이 풀리면 안 된다.
        Assert.Equal("약초", target.SelectedItem);

        window.Close();
    });

    [Fact]
    public void 조건_추가는_종류별로_갈리고_대상이_없으면_잠긴다() => HeadlessUi.Run(() =>
    {
        // 빈 식으로 태어나면 그 조건이 아이템의 것인지 능력의 것인지 데이터가 말하지 못한다
        // (ConditionDefinition은 이름과 식뿐이다). 그래서 첫 후보를 물려 완성된 채로 만든다.
        (SetNodeEditor editor, AuthoringSession session, SetNode node, Window window) = Show();

        Assert.False(editor.FindControl<Button>("AddItemConditionButton")!.IsEnabled);
        Assert.False(editor.FindControl<Button>("AddAbilityConditionButton")!.IsEnabled);

        session.Editor.SetAssignments(node.Id,
        [
            new VariableAssignment { Variable = "약초", Value = "0", Type = VariableAssignment.FloatType }
        ]);

        editor.Rebuild();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(editor.FindControl<Button>("AddItemConditionButton")!.IsEnabled);
        Assert.False(editor.FindControl<Button>("AddAbilityConditionButton")!.IsEnabled);

        Click(editor, "AddItemConditionButton");

        var after = (SetNode)session.Project.FindNode(node.Id)!;
        ConditionDefinition made = Assert.Single(after.Conditions);

        Assert.Equal("$약초 >= 1", made.Expression);
        Assert.Equal(string.Empty, made.Name); // 이름은 사람이 적는다 (W47)

        window.Close();
    });

    [Fact]
    public void 칸이_서로_겹치지_않는다() => HeadlessUi.Run(() =>
    {
        // 소유자 보고 (2026-08-23) — "지금 선택칸들끼리 서로 겹쳐있네."
        //
        // ⚠ Avalonia는 칸을 넘는 컨트롤을 <b>잘라내지 않는다.</b> Fluent ComboBox는 최소
        // 폭이 64px쯤인데 `부호` 칸에 50px만 주자, 템플릿 테두리가 x=-7..57로 배치되어
        // 양옆 7px씩 이웃 위로 그려졌다. 열 폭은 컨트롤의 최소 폭보다 넉넉해야 한다.
        (SetNodeEditor editor, AuthoringSession session, SetNode node, Window window) = Show();

        session.Editor.SetAssignments(node.Id,
        [
            new VariableAssignment { Variable = "약초", Value = "0", Type = VariableAssignment.FloatType },
            new VariableAssignment { Variable = "자물쇠따기", Value = "false", Type = VariableAssignment.BoolType }
        ]);
        session.Editor.AddCondition(node.Id, "약초있음", "$약초 >= 1");
        session.Editor.AddCondition(node.Id, "열림", "$자물쇠따기 == true");

        editor.Rebuild();

        var spilled = new List<string>();
        var tabs = editor.FindControl<TabControl>("SectionTabs")!;

        // ⚠ 고른 탭만 배치된다 — 나머지 탭의 폭은 0이라 그냥 훑으면 <b>검사가 통째로
        // 헛돈다</b>(이 고정이 처음에 조건 탭만 보고 있었다). 하나씩 골라 가며 잰다.
        foreach ((int index, string host) in new[]
                 {
                     (0, "ConditionHost"), (1, "AssignmentHost"), (2, "SpeakerHost")
                 })
        {
            tabs.SelectedIndex = index;

            // 실제 곁기둥 폭(GraphEditorView의 460)에서 잰다 — 겹침은 폭에 달린 일이다.
            window.Width = 460;
            window.Measure(new Size(460, 900));
            window.Arrange(new Rect(0, 0, 460, 900));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            // 표의 줄 격자만 본다 — 컨트롤 템플릿 안의 격자는 원래 겹쳐 산다(팝업 등).
            foreach (Grid grid in editor.FindControl<StackPanel>(host)!
                         .GetVisualDescendants()
                         .OfType<Grid>()
                         .Where(candidate => candidate.Name == SetNodeEditor.TableRowName))
            {
                if (grid.Bounds.Width <= 0)
                {
                    continue;
                }

                List<Control> drawn = grid.Children.OfType<Control>()
                    .Where(cell => cell.Bounds.Width > 0)
                    .OrderBy(cell => cell.Bounds.X)
                    .ToList();

                foreach (Control cell in drawn)
                {
                    // ① 격자 밖으로 그리는가 (칸이 컨트롤 최소 폭보다 좁을 때 생긴다)
                    if (cell.Bounds.X < -0.5 || cell.Bounds.Right > grid.Bounds.Width + 0.5)
                    {
                        spilled.Add(
                            $"{host}: {cell.GetType().Name}가 표 밖으로 " +
                            $"x={cell.Bounds.X:F0} r={cell.Bounds.Right:F0} (표 {grid.Bounds.Width:F0})");
                    }
                }

                // ② 이웃끼리 겹치는가 — 소유자가 본 것이 이쪽이다.
                for (int next = 1; next < drawn.Count; next++)
                {
                    if (drawn[next - 1].Bounds.Right > drawn[next].Bounds.X + 0.5)
                    {
                        spilled.Add(
                            $"{host}: {drawn[next - 1].GetType().Name}(r=" +
                            $"{drawn[next - 1].Bounds.Right:F0})와 {drawn[next].GetType().Name}(x=" +
                            $"{drawn[next].Bounds.X:F0})가 겹침");
                    }
                }
            }
        }

        Assert.True(spilled.Count == 0, string.Join(" / ", spilled));

        window.Close();
    });

    [Fact]
    public void 이름_칸은_모든_탭에서_같은_자리에_선다() => HeadlessUi.Run(() =>
    {
        // 소유자 (2026-08-23) — "뒤쪽은 몰라도 첫번째칸이 어긋나면 가독성이 너무 심하게
        // 훼손된다." 이름이 유일한 별(*) 열이므로, 나머지 열의 합이 표마다 같으면 어떤
        // 폭에서도 첫 칸이 제자리에 선다(SetNodeEditor의 규칙 ③).
        (SetNodeEditor editor, AuthoringSession session, SetNode node, Window window) = Show();

        session.Editor.SetAssignments(node.Id,
        [
            new VariableAssignment { Variable = "약초", Value = "0", Type = VariableAssignment.FloatType },
            new VariableAssignment { Variable = "자물쇠따기", Value = "false", Type = VariableAssignment.BoolType }
        ]);
        session.Editor.AddCondition(node.Id, "약초있음", "$약초 >= 1");
        session.Editor.AddCondition(node.Id, "열림", "$자물쇠따기 == true");

        editor.Rebuild();

        var tabs = editor.FindControl<TabControl>("SectionTabs")!;
        var widths = new List<(string Host, double Width)>();

        foreach ((int index, string host) in new[]
                 {
                     (0, "ConditionHost"), (1, "AssignmentHost"), (2, "SpeakerHost")
                 })
        {
            tabs.SelectedIndex = index;
            window.Width = 460;
            window.Measure(new Size(460, 900));
            window.Arrange(new Rect(0, 0, 460, 900));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            foreach (Grid grid in editor.FindControl<StackPanel>(host)!
                         .GetVisualDescendants()
                         .OfType<Grid>()
                         .Where(candidate => candidate.Name == SetNodeEditor.TableRowName))
            {
                // 둘째 <b>열</b>이 이름 칸이다(첫째는 순번). 컨트롤이 아니라 열을 잰다 —
                // 컨트롤 폭은 제 여백만큼 작아서, 여백을 바꾸면 뜻 없이 흔들린다.
                if (grid.Bounds.Width > 0)
                {
                    widths.Add((host, grid.ColumnDefinitions[1].ActualWidth));
                }
            }
        }

        Assert.NotEmpty(widths);
        Assert.True(
            widths.Select(item => Math.Round(item.Width)).Distinct().Count() == 1,
            "이름 칸 폭: " + string.Join(" / ", widths.Select(item => $"{item.Host}={item.Width:F0}")));

        window.Close();
    });

    [Fact]
    public void 세_구역이_탭으로_갈렸다() => HeadlessUi.Run(() =>
    {
        // 소유자 — 조건은 스크롤 한참 아래의 아이템·능력을 참조하는데, 둘이 같은 기둥에
        // 있으면 하나를 보려고 다른 하나를 늘 지나쳐야 했다.
        (SetNodeEditor editor, _, _, Window window) = Show();

        var tabs = editor.FindControl<TabControl>("SectionTabs")!;

        Assert.Equal(
            ["조건", "아이템 · 능력", "화자"],
            tabs.Items.Cast<TabItem>().Select(tab => tab.Header as string));

        window.Close();
    });

    [Fact]
    public void 아이템을_개명하면_조건의_대상_칸이_그_자리에서_새_이름을_읽는다() => HeadlessUi.Run(() =>
    {
        // 소유자 (2026-08-24) — "이름을 바꿨을 때 … 연결이 계속 이어지도록."
        //
        // 개명 전파가 조건식을 이미 갈았지만 그 변경은 Content라 화면을 다시 만들지 않는다
        // (다시 만들면 이름 칸이 초점을 잃는 순간 컨트롤이 사라져 다음 클릭이 먹히지 않는다).
        // 그래서 행은 그대로 두고 <b>칸의 값만</b> 갈아 끼운다 — 안 그러면 데이터는 이어졌는데
        // 화면에는 빈 드롭다운("아이템")이 서서 끊어진 것처럼 보인다.
        (SetNodeEditor editor, AuthoringSession session, SetNode node, Window window) = Show();

        session.Editor.SetAssignments(node.Id,
        [
            new VariableAssignment { Variable = "열쇠", Value = "0", Type = VariableAssignment.FloatType }
        ]);
        session.Editor.AddCondition(node.Id, "열쇠있음", "$열쇠 >= 1");

        editor.Rebuild();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ComboBox target = editor.FindControl<StackPanel>("ConditionHost")!
            .GetVisualDescendants()
            .OfType<ComboBox>()
            .First();

        Assert.Equal("열쇠", target.SelectedItem);

        // 사람이 이름 칸을 고치고 초점을 옮긴다 — 그것이 곧 커밋이다.
        var tabs = editor.FindControl<TabControl>("SectionTabs")!;
        tabs.SelectedIndex = 1;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        AutoCompleteBox name = editor.FindControl<StackPanel>("AssignmentHost")!
            .GetVisualDescendants()
            .OfType<AutoCompleteBox>()
            .First();

        name.Focus();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        name.Text = "보물열쇠";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        editor.FindControl<TextBox>("NameBox")!.Focus();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var after = (SetNode)session.Project.FindNode(node.Id)!;

        Assert.Equal("$보물열쇠 >= 1", after.Conditions[0].Expression);   // 데이터가 이어졌고
        Assert.Equal(["보물열쇠"], target.ItemsSource!.Cast<string>());    // 화면도 그것을 본다
        Assert.Equal("보물열쇠", target.SelectedItem);

        window.Close();
    });

    /// <summary>빈 설정 노드 하나를 띄운다 — 조건·아이템·화자 탭이 모두 붙어 있다.</summary>
    private static (SetNodeEditor Editor, AuthoringSession Session, SetNode Node, Window Window) Show()
    {
        var session = new AuthoringSession();
        string fileId = session.EnsureChapterBoard("ch01");
        SetNode node = session.Project.FindFile(fileId)!.Nodes.OfType<SetNode>().Single();

        var editor = new SetNodeEditor();
        editor.Attach(session);
        var window = new Window { Content = editor, Width = 900, Height = 700 };
        window.Show();
        editor.Show(node.Id);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return (editor, session, node, window);
    }

    private static void Click(SetNodeEditor editor, string name)
    {
        editor.FindControl<Button>(name)!
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    // ── 기획자 화자의 캐릭터키 (2026-08-17 소유자 보고) ────────────────────
    //
    // "기획자가 추가한 화자가 있는데, 거기서 표정을 추가하려고 했지만 characterId가
    // 없다면서 아래쪽에 메시지만 나오고 원했던 기능은 되지가 않아."
    //
    // ⚠ 원천이 바뀌었다 (2026-08-23) — 챕터 `화자` 시트가 폐지되고 [화자] 탭 →
    // `game.definition.json` 하나가 됐다. 고정하는 계약은 그대로다: 캐릭터키가 화면의
    // 표정 단추까지 와야 하고, 없으면 <b>어디를 채우면 되는지</b>를 말해야 한다.

    [Fact]
    public void 등록_화자의_캐릭터키가_표정_단추까지_온다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(("라루", "laru"));

        AuthoringSession session = project.OpenSession();

        Grid row = PlannerRow(session, "라루");

        // 줄에도 보이고(둘째 칸), 표정 단추가 살아 있다.
        Assert.Equal("laru", row.Children.OfType<TextBox>().ElementAt(1).Text);
        Assert.True(row.Children.OfType<Button>().First().IsEnabled);
    });

    [Fact]
    public void 캐릭터키가_비면_표정_단추가_잠기고_채울_칸을_알려_준다() => HeadlessUi.Run(() =>
    {
        // 전에는 단추가 살아 있어서 누르면 메시지만 뜨고 아무 일도 안 났다.
        using var project = new TempProject(("이름만", null));

        AuthoringSession session = project.OpenSession();

        Button expressions = PlannerRow(session, "이름만").Children.OfType<Button>().First();

        Assert.False(expressions.IsEnabled);

        Assert.Contains("`캐릭터키`", (string)ToolTip.GetTip(expressions)!);
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>그 기획자 화자 줄 — `[이름][캐릭터키][표정]`.</summary>
    private static Grid PlannerRow(AuthoringSession session, string speakerName)
    {
        string fileId = session.EnsureChapterBoard("ch01");
        SetNode node = session.Project.FindFile(fileId)!.Nodes.OfType<SetNode>().Single();

        var editor = new SetNodeEditor();
        editor.Attach(session);
        var window = new Window { Content = editor, Width = 900, Height = 700 };
        window.Show();
        editor.Show(node.Id);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return editor.FindControl<StackPanel>("SpeakerHost")!
            .GetVisualDescendants()
            .OfType<Grid>()
            .First(candidate => candidate.Children.OfType<TextBox>()
                .Any(box => box.Text == speakerName));
    }

    /// <summary>
    /// 화자가 등록된 프로젝트 하나 — 값은 `game.definition.json`에 산다 (2026-08-23).
    /// 저장은 실제 창구(<see cref="AuthoringSession.SaveSpeakers"/>)를 지난다.
    /// </summary>
    private sealed class TempProject : IDisposable
    {
        private readonly string _folder;

        public TempProject(params (string Name, string? CharacterId)[] speakers)
        {
            _folder = Path.Combine(
                Path.GetTempPath(), "vn-speaker-key", Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(_folder);
            ManifestPath = Path.Combine(_folder, "p" + ProjectManifestJson.FileExtension);
            ProjectStore.Save(ManifestPath, new StoryProject { Title = "화자" });

            _speakers = speakers
                .Select(item => new SpeakerSpec
                {
                    Name = item.Name,
                    CharacterId = item.CharacterId ?? string.Empty
                })
                .ToList();
        }

        private readonly List<SpeakerSpec> _speakers;

        public string ManifestPath { get; }

        public AuthoringSession OpenSession()
        {
            var session = new AuthoringSession();
            session.Open(ManifestPath);
            session.SaveSpeakers(_speakers);
            return session;
        }

        public void Dispose()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
    }
}
