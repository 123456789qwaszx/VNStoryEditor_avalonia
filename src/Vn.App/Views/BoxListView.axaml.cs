using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Vn.Core.Story;

namespace Vn.App.Views;

/// <summary>
/// 노드 본문을 박스 목록으로 보여준다. 읽기 전용이다.
///
/// 박스 하나가 라인 하나이고, 그 앞에 쌓인 명령이 같은 박스에 들어간다.
/// 표시만 하며 여기서 Yarn을 다시 조립하지 않는다 — <see cref="StoryLine"/>은 손실 압축이라
/// 그것으로 원본을 복원할 수 없다.
/// </summary>
public partial class BoxListView : UserControl
{
    public BoxListView()
    {
        InitializeComponent();
    }

    /// <summary>박스를 고르면 그 라인을 알린다. 무엇을 할지는 듣는 쪽이 정한다.</summary>
    public event EventHandler<StoryLine>? LineSelected;

    public void Show(IReadOnlyList<StoryLine> lines)
    {
        Boxes.ItemsSource = lines
            .Select(line => new BoxItem(line))
            .ToList();
    }

    public void Clear()
    {
        Boxes.ItemsSource = null;
    }

    private void OnBoxSelected(object? sender, SelectionChangedEventArgs e)
    {
        // 목록을 비울 때도 이 이벤트가 온다.
        if (Boxes.SelectedItem is BoxItem item)
        {
            LineSelected?.Invoke(this, item.Line);
        }
    }
}

/// <summary>
/// 박스 하나를 그리는 데 필요한 값들.
///
/// <see cref="StoryLine"/>을 그대로 바인딩하지 않는 이유는 화면이 필요로 하는 것이
/// "명령이 있는가", "태그를 어떻게 붙여 쓸 것인가" 같은 표시용 값이기 때문이다.
/// 그것을 컨버터로 만들면 XAML에만 존재하는 로직이 생긴다. 여기서 미리 계산해 둔다.
/// </summary>
public sealed class BoxItem
{
    private const int IndentPerDepth = 16;

    public BoxItem(StoryLine line)
    {
        Line = line;
    }

    public StoryLine Line { get; }

    public string Speaker => Line.Speaker ?? string.Empty;

    public string Text => Line.Text;

    public bool IsOption => Line.IsOption;

    public bool HasCommands => Line.CommandsSincePreviousLine.Count > 0;

    // 명령은 원본 문자열 그대로 보여준다. 줄·열·깊이는 분기 트리가 쓸 값이라 화면에 내지 않는다.
    public string Commands =>
        string.Join(
            Environment.NewLine,
            Line.CommandsSincePreviousLine.Select(command => command.Raw));

    public bool HasHashtags => Line.Hashtags.Count > 0;

    public string Hashtags =>
        string.Join(" ", Line.Hashtags.Select(tag => $"#{tag}"));

    public Thickness Indent => new(Line.Depth * IndentPerDepth, 0, 0, 0);
}
