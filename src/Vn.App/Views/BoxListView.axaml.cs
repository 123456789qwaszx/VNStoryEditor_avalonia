using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Vn.App.Services;
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

    /// <summary>대사나 화자를 고치면 그 줄의 새 내용을 알린다. 저장은 듣는 쪽이 한다.</summary>
    public event EventHandler<StoryLineEdit>? LineEdited;

    /// <param name="sourceText">
    /// 원본 파일 전체. 박스가 "이 줄을 안전하게 되살릴 수 있는가"를 판단하는 데 쓴다.
    /// </param>
    public void Show(IReadOnlyList<StoryElement> body, string sourceText)
    {
        Elements.ItemsSource = BuildChildren(body, sourceText);
    }

    public void Clear()
    {
        Elements.ItemsSource = null;
    }

    /// <summary>
    /// 트리 한 층을 화면 항목으로 바꾼다. 갈래 카드가 자기 자식에 대해 다시 부른다.
    /// </summary>
    internal static List<object> BuildChildren(
        IReadOnlyList<StoryElement> body,
        string sourceText)
    {
        var items = new List<object>();

        foreach (StoryElement element in body)
        {
            switch (element)
            {
                case StoryLineElement lineElement:
                    items.Add(new BoxItem(lineElement.Line, sourceText));
                    break;

                case StoryBlockElement blockElement:
                    items.Add(new BlockItem(blockElement.Block, sourceText));
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

    private void OnBoxEdited(object? sender, TextChangedEventArgs e)
    {
        if (sender is Control { DataContext: BoxItem item } && !item.IsLocked)
        {
            LineEdited?.Invoke(this, item.ToEdit());
        }
    }
}

/// <summary>
/// 화면이 필요로 하는 값을 미리 계산해 둔다.
/// 컨버터로 만들면 XAML에만 존재하는 로직이 생긴다.
/// </summary>
public sealed class BoxItem
{
    public BoxItem(StoryLine line, string sourceText)
    {
        Line = line;
        Speaker = line.Speaker ?? string.Empty;
        Text = line.Text;

        string? original = StoryLineReplacer.ReadLine(sourceText, line.Line);

        // 고친 줄은 원본 자리에 그대로 갈아 끼운다. 그러려면 "손대지 않았을 때 원본과 같은가"를
        // 먼저 확인해야 한다. 다르면 저장하는 순간 태그나 주석 같은 것이 사라진다.
        // charter 2-2절이 말하는 손실이 바로 여기서 터진다.
        if (original is null)
        {
            IsLocked = true;
            LockReason = "원본에서 이 줄을 찾지 못했습니다.";
            return;
        }

        string rebuilt = StoryLineReplacer.Compose(
            StoryLineReplacer.ReadIndent(original),
            line.Speaker,
            line.Text);

        if (!string.Equals(rebuilt, original, StringComparison.Ordinal))
        {
            IsLocked = true;
            LockReason =
                "이 줄은 해시태그·주석처럼 모델이 담지 않는 것을 갖고 있어 여기서 고칠 수 없습니다. " +
                "텍스트 탭에서 고치세요.";
        }
    }

    public StoryLine Line { get; }

    public string Speaker { get; set; }

    public string Text { get; set; }

    /// <summary>고쳐도 원본을 그대로 되살릴 수 없는 줄. 잠가서 손실을 막는다.</summary>
    public bool IsLocked { get; }

    public string LockReason { get; } = string.Empty;

    public StoryLineEdit ToEdit()
    {
        return new StoryLineEdit(
            Line.Line,
            string.IsNullOrWhiteSpace(Speaker) ? null : Speaker,
            Text ?? string.Empty);
    }

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

    public BlockItem(StoryBlock block, string sourceText)
    {
        Block = block;

        Branches = block.Branches
            .Select(branch => new BranchItem(block.Kind, branch, sourceText))
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
    public BranchItem(StoryBlockKind kind, StoryBranch branch, string sourceText)
    {
        Branch = branch;

        // 선택지와 조건은 같은 위젯으로 그린다. 라벨이 표시 텍스트냐 조건식이냐가 유일한 차이다.
        Marker = kind == StoryBlockKind.Option
            ? "→"
            : "?";

        Children = BoxListView.BuildChildren(branch.Children, sourceText);
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
