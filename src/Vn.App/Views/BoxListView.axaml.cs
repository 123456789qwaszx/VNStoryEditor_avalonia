using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Vn.Core.Story;

namespace Vn.App.Views;

/// <summary>
/// 노드 본문을 분기 블록 트리로 보여준다. 읽기 전용이다.
///
/// 박스 하나가 라인 하나이고, 그 앞에 쌓인 명령이 같은 박스에 들어간다.
/// 선택지와 조건은 갈래 카드로 그리고, 갈래 안에 다시 박스와 카드가 들어간다.
///
/// 표시만 한다. 여기서 Yarn을 다시 조립하지 않는다 — 트리도 <see cref="StoryLine"/>과 같은
/// 손실 압축이라 원본을 복원할 수 없다.
/// </summary>
public partial class BoxListView : UserControl
{
    public BoxListView()
    {
        InitializeComponent();
    }

    /// <summary>박스를 고르면 그 라인을 알린다. 무엇을 할지는 듣는 쪽이 정한다.</summary>
    public event EventHandler<StoryLine>? LineSelected;

    public void Show(IReadOnlyList<StoryElement> body)
    {
        Elements.ItemsSource = BuildChildren(body);
    }

    public void Clear()
    {
        Elements.ItemsSource = null;
    }

    /// <summary>
    /// 트리 한 층을 화면 항목으로 바꾼다. 갈래 카드가 자기 자식에 대해 다시 부른다.
    /// </summary>
    internal static List<object> BuildChildren(IReadOnlyList<StoryElement> body)
    {
        var items = new List<object>();

        foreach (StoryElement element in body)
        {
            switch (element)
            {
                case StoryLineElement lineElement:
                    items.Add(new BoxItem(lineElement.Line));
                    break;

                case StoryBlockElement blockElement:
                    items.Add(new BlockItem(blockElement.Block));
                    break;
            }
        }

        return items;
    }

    private void OnBoxPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: BoxItem item })
        {
            LineSelected?.Invoke(this, item.Line);
        }
    }
}

/// <summary>
/// 화면이 필요로 하는 값을 미리 계산해 둔다.
/// 컨버터로 만들면 XAML에만 존재하는 로직이 생긴다.
/// </summary>
public sealed class BoxItem
{
    public BoxItem(StoryLine line)
    {
        Line = line;
    }

    public StoryLine Line { get; }

    public string Speaker => Line.Speaker ?? string.Empty;

    public string Text => Line.Text;

    public bool HasCommands => Line.CommandsSincePreviousLine.Count > 0;

    // 명령은 원본 문자열 그대로 보여준다. 줄·열·깊이는 화면에 내지 않는다.
    public string Commands =>
        string.Join(
            Environment.NewLine,
            Line.CommandsSincePreviousLine.Select(command => command.Raw));

    public bool HasHashtags => Line.Hashtags.Count > 0;

    public string Hashtags =>
        string.Join(" ", Line.Hashtags.Select(tag => $"#{tag}"));
}

/// <summary>이야기가 갈라지는 지점. 갈래 카드를 모아 놓은 것.</summary>
public sealed class BlockItem
{
    private const int IndentPerDepth = 16;

    public BlockItem(StoryBlock block)
    {
        Block = block;

        Branches = block.Branches
            .Select(branch => new BranchItem(block.Kind, branch))
            .ToList();
    }

    public StoryBlock Block { get; }

    public IReadOnlyList<BranchItem> Branches { get; }

    public string Title => Block.Kind == StoryBlockKind.Option
        ? "선택지"
        : "조건";

    // 중첩된 블록만 들여쓴다. 최상위는 깊이 0이라 여백이 없다.
    public Thickness Indent => new(Block.Depth * IndentPerDepth, 0, 0, 0);
}

/// <summary>갈래 하나. 접히는 카드로 그린다.</summary>
public sealed class BranchItem
{
    public BranchItem(StoryBlockKind kind, StoryBranch branch)
    {
        Branch = branch;

        // 선택지와 조건은 같은 위젯으로 그린다. 라벨이 표시 텍스트냐 조건식이냐가 유일한 차이다.
        Marker = kind == StoryBlockKind.Option
            ? "→"
            : "?";

        Children = BoxListView.BuildChildren(branch.Children);
    }

    public StoryBranch Branch { get; }

    public string Marker { get; }

    public string Label => Branch.Label;

    public bool HasDestination => !string.IsNullOrEmpty(Branch.Destination);

    /// <summary>목적지가 없으면 갈래가 끝나고 그룹 다음으로 흘러간다. 그때는 아무것도 표시하지 않는다.</summary>
    public string DestinationText => $"→ {Branch.Destination}";

    public bool HasCommands => Branch.Commands.Count > 0;

    public string Commands =>
        string.Join(
            Environment.NewLine,
            Branch.Commands.Select(command => command.Raw));

    public IReadOnlyList<object> Children { get; }
}
