using Avalonia.Controls;
using Avalonia.VisualTree;
using Vn.App.Services;
using Vn.App.Views;
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
}
