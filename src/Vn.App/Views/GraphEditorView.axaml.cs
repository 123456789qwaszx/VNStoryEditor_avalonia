using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Vn.App.Services;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;

namespace Vn.App.Views;

/// <summary>
/// 노드를 만들고 이어 붙이는 저작 화면.
///
/// 여기서 보이는 포트와 간선은 저장된 것이 아니다. 노드의 조건 전환에서 계산된다
/// (<see cref="NodeConnections"/>). 그래서 대사 화면에서 elseif를 하나 추가하면
/// 이 화면에도 포트가 하나 늘고, 여기서 간선을 끌면 대사 화면의 출구 표시가 바뀐다.
/// 두 화면 사이에 동기화 코드는 없다. 같은 것을 계산해서 볼 뿐이다.
///
/// 카드의 좌표는 계산하지 않고 <see cref="NodeLayout"/>에 저장한다. 배치는 작가의 의도이지
/// 데이터에서 유도되는 값이 아니기 때문이다. 반대로 파일에서의 노드 순서는 배치와 무관하다.
/// </summary>
public partial class GraphEditorView : UserControl
{
    private const double CardWidth = 210;
    private const double HeaderHeight = 46;
    private const double PortRowHeight = 24;
    private const double CardPadding = 8;
    private const double PortRadius = 6;

    private readonly List<NodeCard> _cards = new();
    private readonly List<EdgeVisual> _edges = new();

    private AuthoringSession? _session;

    private NodeCard? _draggingCard;
    private Point _dragOffset;

    private ExitPort? _connectingFrom;
    private Line? _connectingLine;

    private EdgeVisual? _selectedEdge;

    public GraphEditorView()
    {
        InitializeComponent();

        AddDialogueButton.Click += (_, _) => AddNode(dialogue: true);
        AddSetButton.Click += (_, _) => AddNode(dialogue: false);
        DeleteNodeButton.Click += (_, _) => DeleteSelectedNode();
        DeleteEdgeButton.Click += (_, _) => DeleteSelectedEdge();

        GraphCanvas.PointerMoved += OnCanvasPointerMoved;
        GraphCanvas.PointerReleased += OnCanvasPointerReleased;
        GraphCanvas.PointerPressed += OnCanvasPointerPressed;
    }

    internal void Attach(AuthoringSession session)
    {
        _session = session;
    }

    // ── 그리기 ──────────────────────────────────────────────────────────────

    internal void Rebuild()
    {
        if (_session is null)
        {
            return;
        }

        GraphCanvas.Children.Clear();
        _cards.Clear();
        _edges.Clear();
        _selectedEdge = null;
        DeleteEdgeButton.IsEnabled = false;

        foreach (StoryNode node in _session.Project.EnumerateNodes())
        {
            _cards.Add(BuildCard(node));
        }

        DrawEdges();
        HighlightSelection();
    }

    /// <summary>좌표만 바뀌었을 때. 카드를 다시 만들지 않고 위치와 선만 옮긴다.</summary>
    internal void RefreshPositions()
    {
        foreach (NodeCard card in _cards)
        {
            StoryNode? node = _session?.Project.FindNode(card.NodeId);

            if (node is not null)
            {
                Canvas.SetLeft(card.Visual, node.Layout.X);
                Canvas.SetTop(card.Visual, node.Layout.Y);
            }
        }

        foreach (EdgeVisual edge in _edges)
        {
            PositionEdge(edge);
        }
    }

    internal void HighlightSelection()
    {
        foreach (NodeCard card in _cards)
        {
            bool selected = string.Equals(card.NodeId, _session?.SelectedNodeId, StringComparison.Ordinal);

            card.Visual.BorderBrush = selected
                ? new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB))
                : new SolidColorBrush(Color.FromArgb(90, 128, 128, 128));

            card.Visual.BorderThickness = new Thickness(selected ? 2 : 1);
        }
    }

    private NodeCard BuildCard(StoryNode node)
    {
        IReadOnlyList<ExitPort> ports = NodeConnections.PortsOf(node, _session!.Project);

        var body = new StackPanel { Spacing = 0 };

        body.Children.Add(new TextBlock
        {
            Text = node.Name,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        body.Children.Add(new TextBlock
        {
            Text = node is SetNode ? "설정" : "대사",
            FontSize = 10,
            Opacity = 0.6,
            Margin = new Thickness(0, 1, 0, 6)
        });

        var card = new Border
        {
            Width = CardWidth,
            Padding = new Thickness(CardPadding),
            CornerRadius = new CornerRadius(8),
            Background = node is SetNode
                ? new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF))
                : new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 128, 128, 128)),
            Child = body,
            Tag = node.Id
        };

        var visual = new NodeCard(node.Id, card, ports);

        for (int index = 0; index < ports.Count; index++)
        {
            body.Children.Add(BuildPortRow(ports[index], visual, index));
        }

        Canvas.SetLeft(card, node.Layout.X);
        Canvas.SetTop(card, node.Layout.Y);
        GraphCanvas.Children.Add(card);

        card.PointerPressed += (_, args) => OnCardPressed(visual, args);
        return visual;
    }

    private Control BuildPortRow(ExitPort port, NodeCard card, int index)
    {
        var label = new TextBlock
        {
            Text = port.Label,
            FontSize = 10,
            Opacity = port.Kind == ExitPortKind.Default ? 0.6 : 1,
            Foreground = port.Kind == ExitPortKind.Branch
                ? BranchPalette.Accent(port.PaletteIndex)
                : null,
            FontWeight = port.Kind == ExitPortKind.Branch ? FontWeight.SemiBold : FontWeight.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var knob = new Ellipse
        {
            Width = PortRadius * 2,
            Height = PortRadius * 2,
            Margin = new Thickness(6, 0, -CardPadding - PortRadius, 0),
            Fill = port.IsConnected
                ? (port.Kind == ExitPortKind.Branch ? BranchPalette.Accent(port.PaletteIndex) : Brushes.DimGray)
                : Brushes.Transparent,
            Stroke = port.Kind == ExitPortKind.Branch
                ? BranchPalette.Accent(port.PaletteIndex)
                : Brushes.DimGray,
            StrokeThickness = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        knob.PointerPressed += (_, args) => OnPortPressed(port, card, index, args);

        var row = new Grid
        {
            Height = PortRowHeight,
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };

        Grid.SetColumn(label, 0);
        Grid.SetColumn(knob, 1);
        row.Children.Add(label);
        row.Children.Add(knob);

        return row;
    }

    private void DrawEdges()
    {
        foreach (NodeCard card in _cards)
        {
            for (int index = 0; index < card.Ports.Count; index++)
            {
                ExitPort port = card.Ports[index];

                if (!port.IsConnected)
                {
                    continue;
                }

                NodeCard? target = _cards.FirstOrDefault(
                    item => string.Equals(item.NodeId, port.TargetNodeId, StringComparison.Ordinal));

                if (target is null)
                {
                    continue;
                }

                IBrush stroke = port.Kind == ExitPortKind.Branch
                    ? BranchPalette.Accent(port.PaletteIndex)
                    : new SolidColorBrush(Color.FromArgb(150, 100, 100, 100));

                var line = new Line { Stroke = stroke, StrokeThickness = 2 };

                // 조건 간선에는 어떤 조건에서 선택되는지 반드시 보인다.
                var label = new Border
                {
                    Padding = new Thickness(5, 1),
                    CornerRadius = new CornerRadius(3),
                    Background = port.Kind == ExitPortKind.Branch ? stroke : Brushes.Transparent,
                    IsVisible = port.Kind == ExitPortKind.Branch,
                    Child = new TextBlock
                    {
                        Text = port.Label,
                        FontSize = 9,
                        FontWeight = FontWeight.Bold,
                        Foreground = Brushes.White
                    }
                };

                var edge = new EdgeVisual(port, card, index, target, line, label);

                line.PointerPressed += (_, _) => SelectEdge(edge);
                label.PointerPressed += (_, _) => SelectEdge(edge);

                // 간선은 노드 뒤에 깔린다.
                GraphCanvas.Children.Insert(0, line);
                GraphCanvas.Children.Add(label);
                _edges.Add(edge);

                PositionEdge(edge);
            }
        }
    }

    private void PositionEdge(EdgeVisual edge)
    {
        Point from = PortAnchor(edge.Source, edge.PortIndex);
        Point to = InputAnchor(edge.Target);

        edge.Line.StartPoint = from;
        edge.Line.EndPoint = to;

        edge.Label.Measure(Size.Infinity);
        Canvas.SetLeft(edge.Label, ((from.X + to.X) / 2) - (edge.Label.DesiredSize.Width / 2));
        Canvas.SetTop(edge.Label, ((from.Y + to.Y) / 2) - (edge.Label.DesiredSize.Height / 2));
    }

    /// <summary>
    /// 포트의 화면 좌표. 카드 안의 배치가 고정 높이라서 계산으로 얻을 수 있다.
    /// 실제 렌더링을 기다렸다가 읽으면 첫 그리기에서 선이 엉뚱한 곳에 놓인다.
    /// </summary>
    private static Point PortAnchor(NodeCard card, int portIndex)
    {
        double x = Canvas.GetLeft(card.Visual) + CardWidth;
        double y = Canvas.GetTop(card.Visual)
            + CardPadding + HeaderHeight
            + (portIndex * PortRowHeight) + (PortRowHeight / 2);

        return new Point(x, y);
    }

    private static Point InputAnchor(NodeCard card)
    {
        return new Point(
            Canvas.GetLeft(card.Visual),
            Canvas.GetTop(card.Visual) + CardPadding + (HeaderHeight / 2));
    }

    // ── 조작 ────────────────────────────────────────────────────────────────

    private void OnCardPressed(NodeCard card, PointerPressedEventArgs args)
    {
        _session?.Select(card.NodeId);
        HighlightSelection();

        _draggingCard = card;
        Point position = args.GetPosition(GraphCanvas);
        _dragOffset = new Point(
            position.X - Canvas.GetLeft(card.Visual),
            position.Y - Canvas.GetTop(card.Visual));

        args.Handled = true;
    }

    private void OnPortPressed(ExitPort port, NodeCard card, int index, PointerPressedEventArgs args)
    {
        _connectingFrom = port;

        Point start = PortAnchor(card, index);
        _connectingLine = new Line
        {
            StartPoint = start,
            EndPoint = start,
            Stroke = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
            StrokeThickness = 2,
            StrokeDashArray = new AvaloniaList<double> { 4, 3 }
        };

        GraphCanvas.Children.Add(_connectingLine);
        HintText.Text = "연결할 노드 위에서 놓으세요. 빈 곳에 놓으면 연결이 끊어집니다.";

        // 카드 드래그로 넘어가지 않게 막는다.
        args.Handled = true;
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        // 빈 곳을 눌렀다면 간선 선택을 푼다.
        if (args.Source is Canvas)
        {
            SelectEdge(null);
        }
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs args)
    {
        Point position = args.GetPosition(GraphCanvas);

        if (_connectingLine is not null)
        {
            _connectingLine.EndPoint = position;
            return;
        }

        if (_draggingCard is null || _session is null)
        {
            return;
        }

        double x = Math.Max(0, position.X - _dragOffset.X);
        double y = Math.Max(0, position.Y - _dragOffset.Y);

        _session.Editor.MoveNode(_draggingCard.NodeId, x, y);
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        _draggingCard = null;

        if (_connectingFrom is null)
        {
            return;
        }

        ExitPort port = _connectingFrom;
        _connectingFrom = null;

        if (_connectingLine is not null)
        {
            GraphCanvas.Children.Remove(_connectingLine);
            _connectingLine = null;
        }

        HintText.Text = "포트(●)를 끌어 다른 노드에 놓으면 연결됩니다. 노드를 끌면 배치가 바뀝니다.";

        NodeCard? dropped = CardAt(args.GetPosition(GraphCanvas));

        // 자기 자신으로는 잇지 않는다. 빈 곳에 놓으면 연결을 끊는 것으로 본다.
        string? target = dropped is null || string.Equals(dropped.NodeId, port.NodeId, StringComparison.Ordinal)
            ? null
            : dropped.NodeId;

        _session?.Editor.SetExitTarget(port, target);
    }

    private NodeCard? CardAt(Point point)
    {
        foreach (NodeCard card in _cards)
        {
            double left = Canvas.GetLeft(card.Visual);
            double top = Canvas.GetTop(card.Visual);
            double height = card.Visual.Bounds.Height > 0
                ? card.Visual.Bounds.Height
                : HeaderHeight + (card.Ports.Count * PortRowHeight) + (CardPadding * 2);

            if (point.X >= left && point.X <= left + CardWidth &&
                point.Y >= top && point.Y <= top + height)
            {
                return card;
            }
        }

        return null;
    }

    private void SelectEdge(EdgeVisual? edge)
    {
        foreach (EdgeVisual item in _edges)
        {
            item.Line.StrokeThickness = 2;
        }

        _selectedEdge = edge;
        DeleteEdgeButton.IsEnabled = edge is not null;

        if (edge is null)
        {
            return;
        }

        edge.Line.StrokeThickness = 4;

        StoryNode? source = _session?.Project.FindNode(edge.Port.NodeId);
        StoryNode? target = _session?.Project.FindNode(edge.Port.TargetNodeId);

        string kind = edge.Port.Kind == ExitPortKind.Branch ? $"조건 '{edge.Port.Label}'" : "기본 출구";
        HintText.Text = $"{source?.Name} — {kind} → {target?.Name}";
    }

    private void DeleteSelectedEdge()
    {
        if (_selectedEdge is not null)
        {
            _session?.Editor.SetExitTarget(_selectedEdge.Port, null);
        }
    }

    private void AddNode(bool dialogue)
    {
        if (_session is null)
        {
            return;
        }

        // 겹치지 않게 대충 아래로 쌓는다. 배치는 작가가 다시 옮기면 된다.
        double x = 60 + (_session.Project.EnumerateNodes().Count() % 4) * 260;
        double y = 60 + (_session.Project.EnumerateNodes().Count() / 4) * 200;

        if (_session.ActiveFileId is not { } fileId)
        {
            _session.SetStatus("새 노드를 추가할 StoryFile이 없습니다.");
            return;
        }

        StoryNode node = dialogue
            ? _session.Editor.AddDialogueNode(fileId, x, y)
            : _session.Editor.AddSetNode(fileId, x, y);

        _session.Select(node.Id);
    }

    private void DeleteSelectedNode()
    {
        if (_session?.SelectedNodeId is { } nodeId)
        {
            _session.Editor.RemoveNode(nodeId);
        }
    }

    private sealed class NodeCard
    {
        public NodeCard(string nodeId, Border visual, IReadOnlyList<ExitPort> ports)
        {
            NodeId = nodeId;
            Visual = visual;
            Ports = ports;
        }

        public string NodeId { get; }
        public Border Visual { get; }
        public IReadOnlyList<ExitPort> Ports { get; }
    }

    private sealed class EdgeVisual
    {
        public EdgeVisual(
            ExitPort port,
            NodeCard source,
            int portIndex,
            NodeCard target,
            Line line,
            Border label)
        {
            Port = port;
            Source = source;
            PortIndex = portIndex;
            Target = target;
            Line = line;
            Label = label;
        }

        public ExitPort Port { get; }
        public NodeCard Source { get; }
        public int PortIndex { get; }
        public NodeCard Target { get; }
        public Line Line { get; }
        public Border Label { get; }
    }
}
