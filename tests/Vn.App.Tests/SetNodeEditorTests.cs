using Avalonia.Controls;
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
    [Fact]
    public void 능력에서_아이템으로_바꾸면_초기값이_숫자가_된다() => HeadlessUi.Run(() =>
    {
        // 소유자 보고 (2026-08-17) — "능력에서 아이템으로 바꾸면 아이템의 초기값이 false로
        // 적히는 문제가 있어." 값 공간이 종류마다 다른데 안 쓰는 칸의 값이 그대로 넘어왔다.
        var session = new AuthoringSession();
        string fileId = session.EnsureChapterBoard("ch01");
        SetNode node = session.Project.FindFile(fileId)!.Nodes.OfType<SetNode>().Single();

        session.Editor.SetAssignments(node.Id,
        [
            new VariableAssignment
            {
                Variable = "자물쇠따기",
                Value = "false",
                Type = VariableAssignment.BoolType
            }
        ]);

        var editor = new SetNodeEditor();
        editor.Attach(session);
        var window = new Window { Content = editor, Width = 900, Height = 700 };
        window.Show();
        editor.Show(node.Id);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 종류 콤보 — 아이템·능력 행의 첫 칸이다.
        ComboBox kind = editor.FindControl<StackPanel>("AssignmentHost")!
            .GetVisualDescendants()
            .OfType<ComboBox>()
            .First(box => box.ItemsSource?.Cast<object>().Any(item =>
                item as string == "능력 (보유)") == true);

        Assert.Equal("능력 (보유)", kind.SelectedItem);

        kind.SelectedItem = "아이템 (개수)";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        VariableAssignment saved = Assert.Single(
            session.Project.FindNode(node.Id) is SetNode after ? after.Assignments : []);

        Assert.Equal(VariableAssignment.FloatType, saved.Type);
        Assert.Equal("0", saved.Value); // `false`가 아니라 숫자에서 시작한다
        Assert.Equal("자물쇠따기", saved.Variable);

        window.Close();
    });

    [Fact]
    public void 조건_드롭다운은_열_때마다_아이템_목록을_다시_읽는다() => HeadlessUi.Run(() =>
    {
        // 소유자 보고 (2026-08-23) — "아이템을 +추가를 한 뒤에 능력을 적고나서 조건에
        // 반영시키려면 다른 노드에 갔다와야 하는게 불편해." 아이템·능력 편집은 Content
        // 변경이라 이 화면을 다시 만들지 않으므로(타이핑 중 컨트롤 파괴 방지), 조건 행의
        // 후보가 행을 만들 때의 것으로 굳어 있었다.
        var session = new AuthoringSession();
        string fileId = session.EnsureChapterBoard("ch01");
        SetNode node = session.Project.FindFile(fileId)!.Nodes.OfType<SetNode>().Single();

        var herb = new VariableAssignment
        {
            Variable = "약초",
            Value = "0",
            Type = VariableAssignment.FloatType
        };

        session.Editor.SetAssignments(node.Id, [herb]);
        session.Editor.AddCondition(node.Id, "약초있음", "$약초 >= 1");

        var editor = new SetNodeEditor();
        editor.Attach(session);
        var window = new Window { Content = editor, Width = 900, Height = 700 };
        window.Show();
        editor.Show(node.Id);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ComboBox target = editor.FindControl<StackPanel>("ConditionHost")!
            .GetVisualDescendants()
            .OfType<ComboBox>()
            .First();

        Assert.Equal(["약초"], target.ItemsSource!.Cast<string>());
        Assert.Equal("약초", target.SelectedItem);

        // 능력 한 줄을 더한다 — 화면을 다시 만드는 손은 여기에도, 앱에도 없다.
        session.Editor.SetAssignments(node.Id,
        [
            herb,
            new VariableAssignment
            {
                Variable = "자물쇠따기",
                Value = "false",
                Type = VariableAssignment.BoolType
            }
        ]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(["약초"], target.ItemsSource!.Cast<string>()); // 아직 굳어 있다

        // 여는 순간이 곧 "지금 무엇이 있나"를 묻는 순간이다.
        target.IsDropDownOpen = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(["약초", "자물쇠따기"], target.ItemsSource!.Cast<string>());

        // 고르고 있던 값은 살아남는다 — 목록을 갈아 끼우다 조건이 풀리면 안 된다.
        Assert.Equal("약초", target.SelectedItem);

        window.Close();
    });

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
