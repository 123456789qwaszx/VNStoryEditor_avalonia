using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Vn.App.Services;
using Vn.Core.Story;

namespace Vn.App.Views;

/// <summary>
/// 분기 트리를 작가가 읽기 쉬운 카드로 보여준다.
/// 저장은 하지 않고, 안전한 한 줄 편집 요청만 외부 세션에 전달한다.
/// </summary>
public partial class BoxListView : UserControl
{
    public BoxListView()
    {
        InitializeComponent();
    }

    public event EventHandler<StoryLine>? LineSelected;

    public event EventHandler<StoryLineEdit>? LineEdited;

    public void Show(
        IReadOnlyList<StoryElement> body,
        string sourceText,
        bool allowEditing = true,
        string? baselineText = null)
    {
        Elements.ItemsSource = BuildChildren(body, sourceText, allowEditing, baselineText);
    }

    public void Clear()
    {
        Elements.ItemsSource = null;
    }

    internal static List<object> BuildChildren(
        IReadOnlyList<StoryElement> body,
        string sourceText,
        bool allowEditing = true,
        string? baselineText = null)
    {
        var items = new List<object>();

        foreach (StoryElement element in body)
        {
            switch (element)
            {
                case StoryLineElement lineElement:
                    items.Add(new BoxItem(lineElement.Line, sourceText, allowEditing, baselineText));
                    break;

                case StoryBlockElement blockElement:
                    items.Add(new BlockItem(blockElement.Block, sourceText, allowEditing, baselineText));
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

    /// <summary>
    /// TextBox는 사용자가 친 글자뿐 아니라 바인딩이 초기값을 넣을 때도 TextChanged를 낸다.
    /// 그 이벤트를 편집으로 취급하면 카드를 그리기만 해도 문서가 고쳐진 것이 되고,
    /// 그 결과 카드 목록을 다시 만들면 새 TextBox가 또 TextChanged를 내는 무한 되풀이가 된다.
    /// 그래서 실제로 원문 줄이 달라지는 변경만 밖으로 알린다. 되돌린 편집도 편집이 아니다.
    /// </summary>
    private void OnBoxEdited(object? sender, TextChangedEventArgs e)
    {
        if (sender is Control { DataContext: BoxItem item } &&
            !item.IsLocked &&
            item.HasPendingChange)
        {
            LineEdited?.Invoke(this, item.ToEdit());
        }
    }
}

public sealed class BoxItem
{
    private readonly string? _originalLine;
    private readonly string _indent = string.Empty;

    public BoxItem(
        StoryLine line,
        string sourceText,
        bool allowEditing = true,
        string? baselineText = null)
    {
        Line = line;

        string baselineSource = baselineText ?? sourceText;
        string? baselineLine = StoryLineReplacer.ReadLine(baselineSource, line.Line);
        string? currentLine = StoryLineReplacer.ReadLine(sourceText, line.Line);

        if (!allowEditing)
        {
            Speaker = line.Speaker ?? string.Empty;
            Text = line.Text;
            IsLocked = true;
            LockReason = "원문에서 줄이 추가되거나 삭제되었습니다. 저장 후 다시 검사해 주세요.";
            return;
        }

        if (baselineLine is null || currentLine is null)
        {
            Speaker = line.Speaker ?? string.Empty;
            Text = line.Text;
            IsLocked = true;
            LockReason = "원문에서 이 줄을 찾지 못했습니다. Yarn 원문에서 수정해 주세요.";
            return;
        }

        string indent = StoryLineReplacer.ReadIndent(baselineLine);
        string rebuiltBaseline = StoryLineReplacer.Compose(indent, line.Speaker, line.Text);

        // 분석 당시 줄이 단순한 화자+대사 형태가 아니었다면 부분 교체로는 보존할 수 없다.
        if (!string.Equals(rebuiltBaseline, baselineLine, StringComparison.Ordinal))
        {
            Speaker = line.Speaker ?? string.Empty;
            Text = line.Text;
            IsLocked = true;
            LockReason =
                "이 줄에는 태그·주석·조건처럼 구조 화면이 안전하게 되살릴 수 없는 내용이 있습니다. " +
                "Yarn 원문에서 수정해 주세요.";
            return;
        }

        if (!TryParseEditableLine(currentLine, indent, out string? speaker, out string text))
        {
            Speaker = line.Speaker ?? string.Empty;
            Text = line.Text;
            IsLocked = true;
            LockReason =
                "현재 원문 줄에 태그·주석 또는 예상하지 못한 들여쓰기가 있습니다. Yarn 원문에서 수정해 주세요.";
            return;
        }

        _originalLine = currentLine;
        _indent = indent;
        Speaker = speaker ?? string.Empty;
        Text = text;
    }

    /// <summary>
    /// 지금 상자의 내용이 원문 줄과 실제로 다른지. 같으면 저장할 것이 없다.
    /// 카드를 그리는 순간의 TextChanged와 사용자가 되돌린 편집을 함께 걸러 낸다.
    /// </summary>
    public bool HasPendingChange =>
        _originalLine is not null &&
        !string.Equals(ComposeCurrentLine(), _originalLine, StringComparison.Ordinal);

    public StoryLine Line { get; }

    public string Speaker { get; set; }

    public string Text { get; set; }

    public bool IsLocked { get; }

    public string LockReason { get; } = string.Empty;

    public StoryLineEdit ToEdit()
    {
        return new StoryLineEdit(
            Line.Line,
            NormalizedSpeaker,
            Text ?? string.Empty,
            _originalLine);
    }

    private string? NormalizedSpeaker =>
        string.IsNullOrWhiteSpace(Speaker) ? null : Speaker;

    private string ComposeCurrentLine() =>
        StoryLineReplacer.Compose(_indent, NormalizedSpeaker, Text ?? string.Empty);

    public bool HasCommands => Line.CommandsSincePreviousLine.Count > 0;

    public string Commands => string.Join(
        Environment.NewLine,
        Line.CommandsSincePreviousLine.Select(command => command.Raw));

    public bool HasHashtags => Line.Hashtags.Count > 0;

    public string Hashtags =>
        string.Join(" ", Line.Hashtags.Select(tag => $"#{tag}"));

    private static bool TryParseEditableLine(
        string line,
        string expectedIndent,
        out string? speaker,
        out string text)
    {
        speaker = null;
        text = string.Empty;

        if (!line.StartsWith(expectedIndent, StringComparison.Ordinal))
        {
            return false;
        }

        string content = line[expectedIndent.Length..];

        // 이 문자를 새로 넣은 줄을 박스에서 다시 쓰면 Yarn 태그나 주석을 잃을 수 있다.
        if (content.Contains("//", StringComparison.Ordinal) ||
            content.Contains(" #", StringComparison.Ordinal) ||
            content.StartsWith("#", StringComparison.Ordinal))
        {
            return false;
        }

        int colon = content.IndexOf(':');

        if (colon > 0)
        {
            string candidate = content[..colon];
            bool validSpeaker = !candidate.Any(character =>
                char.IsWhiteSpace(character) || character is '{' or '<');

            if (validSpeaker)
            {
                speaker = candidate;
                text = content[(colon + 1)..].TrimStart();
                return true;
            }
        }

        text = content;
        return true;
    }
}

public sealed class BlockItem
{
    private const int IndentPerDepth = 16;

    public BlockItem(
        StoryBlock block,
        string sourceText,
        bool allowEditing = true,
        string? baselineText = null)
    {
        Block = block;
        Branches = block.Branches
            .Select(branch => new BranchItem(block.Kind, branch, sourceText, allowEditing, baselineText))
            .ToList();
    }

    public StoryBlock Block { get; }

    public IReadOnlyList<BranchItem> Branches { get; }

    public string Title => Block.Kind == StoryBlockKind.Option
        ? "선택지"
        : "조건";

    public Thickness Indent => new(Block.Depth * IndentPerDepth, 0, 0, 0);
}

public sealed class BranchItem
{
    public BranchItem(
        StoryBlockKind kind,
        StoryBranch branch,
        string sourceText,
        bool allowEditing = true,
        string? baselineText = null)
    {
        Branch = branch;
        Marker = kind == StoryBlockKind.Option ? "→" : "?";
        Children = BoxListView.BuildChildren(branch.Children, sourceText, allowEditing, baselineText);
    }

    public StoryBranch Branch { get; }

    public string Marker { get; }

    public string Label => Branch.Label;

    public bool HasDestination => !string.IsNullOrEmpty(Branch.Destination);

    public string DestinationText => $"→ {Branch.Destination}";

    public bool HasCommands => Branch.Commands.Count > 0;

    public string Commands => string.Join(
        Environment.NewLine,
        Branch.Commands.Select(command => command.Raw));

    public IReadOnlyList<object> Children { get; }
}
