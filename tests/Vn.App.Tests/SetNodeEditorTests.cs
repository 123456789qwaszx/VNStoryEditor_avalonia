using Avalonia.Controls;
using Avalonia.VisualTree;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
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

    // ── 기획자 화자의 캐릭터키 (2026-08-17 소유자 보고) ────────────────────
    //
    // "기획자가 추가한 화자가 있는데, 거기서 표정을 추가하려고 했지만 characterId가
    // 없다면서 아래쪽에 메시지만 나오고 원했던 기능은 되지가 않아."

    [Fact]
    public void 챕터_화자_시트의_캐릭터키가_표정_단추까지_온다() => HeadlessUi.Run(() =>
    {
        // `화자` 시트에는 `캐릭터키` 칸이 원래 있었는데, 세션이 이름만 추려 담고 그 값을
        // 버렸다 — 그래서 시트에 적어도 툴은 정의 파일만 보고 "없다"고 했다.
        using var chapters = new TempChapters(("라루", "laru"));

        var session = new AuthoringSession();
        session.SupplyChapterVocabulary(ChapterLibrary.Load(chapters.Folder));

        Assert.Equal("laru", session.ChapterSpeakerCharacterIds["라루"]);

        Grid row = PlannerRow(session, "라루");

        // 줄에도 보이고(둘째 칸), 표정 단추가 살아 있다.
        Assert.Equal("laru", row.Children.OfType<TextBox>().ElementAt(1).Text);
        Assert.True(row.Children.OfType<Button>().First().IsEnabled);
    });

    [Fact]
    public void 캐릭터키가_비면_표정_단추가_잠기고_채울_칸을_알려_준다() => HeadlessUi.Run(() =>
    {
        // 전에는 단추가 살아 있어서 누르면 메시지만 뜨고 아무 일도 안 났다.
        using var chapters = new TempChapters(("이름만", null));

        var session = new AuthoringSession();
        session.SupplyChapterVocabulary(ChapterLibrary.Load(chapters.Folder));

        Assert.DoesNotContain("이름만", session.ChapterSpeakerCharacterIds.Keys);

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

    private sealed class TempChapters : IDisposable
    {
        public TempChapters(params (string Name, string? CharacterId)[] speakers)
        {
            Folder = Path.Combine(
                Path.GetTempPath(), "vn-speaker-key", Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Folder);
            ChapterWorkbookWriter.EnsureChapterWorkbook(Folder, "ch01", [("trust", "신뢰")]);

            foreach ((string name, string? characterId) in speakers)
            {
                ChapterWorkbookWriter.AddSpeaker(
                    Path.Combine(Folder, "ch01.xlsx"), name, characterId, null);
            }
        }

        public string Folder { get; }

        public void Dispose()
        {
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, recursive: true);
            }
        }
    }
}
