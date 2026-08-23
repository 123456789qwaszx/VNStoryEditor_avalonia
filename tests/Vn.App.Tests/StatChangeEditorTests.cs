using Avalonia.Controls;
using Avalonia.Interactivity;
using Vn.App.Views;
using Vn.Authoring.Chapters;

namespace Vn.App.Tests;

/// <summary>
/// `스탯변화` 줄 편집기 (2026-08-17 소유자: "엑셀에서 정의해둔 스탯을 드롭다운으로 선택하고
/// 부호와 값을 넣는거야"). 사람이 고른 것이 시트 문법으로 정확히 옮겨지는지를 본다 —
/// 이 편집기의 유일한 일이 그 번역이다.
/// </summary>
public sealed class StatChangeEditorTests
{
    private static readonly ChapterStat Trust = new("trust", "신뢰", 0, 0, 10, 2);
    private static readonly ChapterStat Anger = new("anger", "분노", 0, 0, 10, 3);

    private static readonly ChapterStat Key =
        new("key", "열쇠", 0, 0, 1, 4, ChapterStatType.Bool);

    [Fact]
    public void 시트_글을_읽어_줄로_세우고_그대로_돌려준다() => HeadlessUi.Run(() =>
    {
        var editor = new StatChangeEditor();
        editor.Load([Trust, Anger], [new StatDelta("trust", 2), new StatDelta("anger", -1)]);

        Assert.Equal(2, Rows(editor).Count);
        Assert.Equal("trust +2; anger -1", editor.ToSheetText());
    });

    [Fact]
    public void 스탯을_고르고_부호와_수치를_넣으면_시트_글이_된다() => HeadlessUi.Run(() =>
    {
        var editor = new StatChangeEditor();
        editor.Load([Trust, Anger], []);

        Assert.Empty(Rows(editor));

        Add(editor);
        Assert.Single(Rows(editor));

        // 드롭다운은 엑셀 `스탯` 시트에 선언된 것만 담는다.
        Assert.Equal(
            ["신뢰 (trust)", "분노 (anger)"],
            (IEnumerable<string>)StatCombo(editor, 0).ItemsSource!);

        StatCombo(editor, 0).SelectedItem = "분노 (anger)";
        SignCombo(editor, 0).SelectedIndex = 1;  // －
        AmountBox(editor, 0).Text = "3";

        Assert.Equal("anger -3", editor.ToSheetText());
    });

    [Fact]
    public void 두_번째_줄은_아직_안_쓴_스탯을_먼저_권한다() => HeadlessUi.Run(() =>
    {
        // 같은 키를 두 줄에 적는 실수가 흔하다 — [＋]가 남은 스탯을 집어 준다.
        var editor = new StatChangeEditor();
        editor.Load([Trust, Anger], [new StatDelta("trust", 1)]);

        Add(editor);

        Assert.Equal("분노 (anger)", StatCombo(editor, 1).SelectedItem);
    });

    [Fact]
    public void bool_스탯은_부호_대신_켬_끔이고_수치칸이_없다() => HeadlessUi.Run(() =>
    {
        // 능력·소지품처럼 참/거짓뿐인 스탯에 `+5`를 적게 두면 뜻을 알 수 없다.
        var editor = new StatChangeEditor();
        editor.Load([Key], [new StatDelta("key", 1)]);

        Assert.Equal(["켬", "끔"], (IEnumerable<string>)SignCombo(editor, 0).ItemsSource!);
        Assert.False(AmountBox(editor, 0).IsVisible);

        SignCombo(editor, 0).SelectedIndex = 1;

        // 2026-08-19 — 이 기대값은 `key -1`이었다. 화면은 켬·끔을 보여 주면서 글로는 증감을
        // 적었고, 리더는 그 글을 "bool에 증감은 안 된다"며 오류로 잡았다. 툴이 제 손으로
        // 만든 값을 제가 거부하고 있었고, 이 테스트가 그 상태를 붙들고 있었다.
        Assert.Equal("key false", editor.ToSheetText());
    });

    [Fact]
    public void 종류가_바뀌면_수치를_물려받지_않는다() => HeadlessUi.Run(() =>
    {
        // 정수 스탯에 적어 둔 +5가 bool 스탯 줄에 그대로 남으면 값 공간(0·1)을 벗어난다.
        var editor = new StatChangeEditor();
        editor.Load([Trust, Key], [new StatDelta("trust", 5)]);

        StatCombo(editor, 0).SelectedItem = "열쇠 (key)";

        // bool로 바뀌었으므로 글도 깃발 표기가 된다 (2026-08-19).
        Assert.Equal("key true", editor.ToSheetText());
    });

    [Fact]
    public void 줄을_지우면_시트_글에서도_사라진다() => HeadlessUi.Run(() =>
    {
        var editor = new StatChangeEditor();
        editor.Load([Trust, Anger], [new StatDelta("trust", 1), new StatDelta("anger", 2)]);

        Remove(editor, 0);

        Assert.Equal("anger +2", editor.ToSheetText());
    });

    [Fact]
    public void 읽기_전용이면_아무것도_못_누른다() => HeadlessUi.Run(() =>
    {
        // 엑셀이 그 챕터를 잡고 있으면 툴 편집이 잠긴다 — 그 스위치를 이 편집기도 같이 탄다.
        var editor = new StatChangeEditor { Editable = false };
        editor.Load([Trust], [new StatDelta("trust", 1)]);

        Assert.False(StatCombo(editor, 0).IsEnabled);
        Assert.False(AddButton(editor).IsEnabled);
    });

    [Fact]
    public void 스탯이_하나도_선언되지_않았으면_엑셀로_보낸다() => HeadlessUi.Run(() =>
    {
        var editor = new StatChangeEditor();
        editor.Load([], []);

        Assert.Contains("`스탯` 시트", editor.Children.OfType<TextBlock>().Single().Text!);
        Assert.Equal(string.Empty, editor.ToSheetText());
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static List<Grid> Rows(StatChangeEditor editor) => editor.Children.OfType<Grid>().ToList();

    private static Button AddButton(StatChangeEditor editor) =>
        editor.Children.OfType<Button>().Single();

    private static void Add(StatChangeEditor editor) =>
        AddButton(editor).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Remove(StatChangeEditor editor, int index) =>
        Rows(editor)[index].Children.OfType<Button>().Single()
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static ComboBox StatCombo(StatChangeEditor editor, int index) =>
        Rows(editor)[index].Children.OfType<ComboBox>().First();

    private static ComboBox SignCombo(StatChangeEditor editor, int index) =>
        Rows(editor)[index].Children.OfType<ComboBox>().Last();

    private static TextBox AmountBox(StatChangeEditor editor, int index) =>
        Rows(editor)[index].Children.OfType<TextBox>().Single();
}
