using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Vn.Core.Story;

namespace Vn.App.Views;

/// <summary>
/// 분석 결과의 노드를 그래프로 그린다.
///
/// <c>&lt;&lt;jump&gt;&gt;</c>만 간선으로 그린다. <c>&lt;&lt;beat&gt;&gt;</c> 같은 것도 노드 이름을 받지만
/// 이야기의 흐름이 아니므로 그리지 않는다.
///
/// 노드 위치는 그릴 때마다 자동으로 배치하고 저장하지 않는다.
/// 위치는 편집기 전용 데이터라 <c>.yarn</c>에 넣을 자리가 없다.
/// 드래그는 지금 보기 좋으라고 있는 것이며 다음 실행에는 남지 않는다.
/// </summary>
public partial class GraphView : UserControl
{
    private const double CanvasWidth = 2000;
    private const double CanvasHeight = 1200;
    private const double GridSize = 40;
    private const double NodeWidth = 140;
    private const double NodeHeight = 60;

    private const double ColumnStep = 240;
    private const double RowStep = 120;
    private const double OriginX = 80;
    private const double OriginY = 80;
    private const int NodesPerColumn = 8;

    private readonly List<GraphNode> _nodes = new();

    // 선과 그 양 끝 노드를 나란히 들고 있는다. 드래그할 때 선이 따라오려면 끝이 누구인지 알아야 한다.
    private readonly List<Line> _edges = new();
    private readonly List<EdgeEnds> _edgeEnds = new();

    private GraphNode? _dragging;
    private Point _dragOffset;

    public GraphView()
    {
        InitializeComponent();

        DrawGrid();
    }

    /// <summary>노드를 고르면 그 제목을 알린다. 무엇을 할지는 듣는 쪽이 정한다.</summary>
    public event EventHandler<string>? NodeSelected;

    /// <summary>
    /// 분석 결과로 그래프를 다시 그린다. 이전 그림과 드래그해 둔 위치는 사라진다.
    /// </summary>
    public void Show(IReadOnlyList<StoryNode> nodes)
    {
        ClearGraph();

        var byTitle = new Dictionary<string, GraphNode>(StringComparer.Ordinal);

        for (int index = 0; index < nodes.Count; index++)
        {
            StoryNode node = nodes[index];

            // 격자 배치. 겹치지만 않으면 되고, 보기 좋은 배치는 나중 일이다.
            double x = OriginX + index / NodesPerColumn * ColumnStep;
            double y = OriginY + index % NodesPerColumn * RowStep;

            GraphNode created = AddNode(node, x, y);
            byTitle[node.Title] = created;
        }

        DrawEdges(nodes, byTitle);

        SelectedTitleText.Text = nodes.Count == 0
            ? "(분석 결과 없음)"
            : "(없음)";

        SelectedBodyText.Text = string.Empty;
    }

    /// <summary>바깥에서 고른 노드를 그래프에서도 고른 것으로 표시한다.</summary>
    public void SelectByTitle(string title)
    {
        GraphNode? node = _nodes.FirstOrDefault(
            item => string.Equals(item.Title, title, StringComparison.Ordinal));

        if (node is not null)
        {
            Highlight(node);
        }
    }

    private void ClearGraph()
    {
        foreach (GraphNode node in _nodes)
        {
            GraphCanvas.Children.Remove(node.Visual);
        }

        foreach (Line edge in _edges)
        {
            GraphCanvas.Children.Remove(edge);
        }

        _nodes.Clear();
        _edges.Clear();
        _edgeEnds.Clear();
        _dragging = null;
    }

    private void DrawGrid()
    {
        IBrush brush = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128));

        for (double x = 0; x <= CanvasWidth; x += GridSize)
        {
            GraphCanvas.Children.Add(new Line
            {
                StartPoint = new Point(x, 0),
                EndPoint = new Point(x, CanvasHeight),
                Stroke = brush,
                StrokeThickness = 1
            });
        }

        for (double y = 0; y <= CanvasHeight; y += GridSize)
        {
            GraphCanvas.Children.Add(new Line
            {
                StartPoint = new Point(0, y),
                EndPoint = new Point(CanvasWidth, y),
                Stroke = brush,
                StrokeThickness = 1
            });
        }
    }

    private void DrawEdges(
        IReadOnlyList<StoryNode> nodes,
        IReadOnlyDictionary<string, GraphNode> byTitle)
    {
        IBrush brush = new SolidColorBrush(Color.FromArgb(160, 128, 128, 128));

        foreach (StoryJump jump in nodes.SelectMany(node => node.Jumps))
        {
            if (!byTitle.TryGetValue(jump.SourceNodeTitle, out GraphNode? from) ||
                !byTitle.TryGetValue(jump.DestinationNodeTitle, out GraphNode? to))
            {
                // 없는 노드로 가는 점프. 진단이 이미 알리므로 여기서는 선만 안 그린다.
                continue;
            }

            var edge = new Line
            {
                StartPoint = CenterOf(from),
                EndPoint = CenterOf(to),
                Stroke = brush,
                StrokeThickness = 2
            };

            // 간선은 노드 뒤에 깔린다.
            GraphCanvas.Children.Insert(0, edge);
            _edges.Add(edge);
            _edgeEnds.Add(new EdgeEnds(from, to));
        }
    }

    private static Point CenterOf(GraphNode node)
    {
        return new Point(
            Canvas.GetLeft(node.Visual) + (NodeWidth / 2),
            Canvas.GetTop(node.Visual) + (NodeHeight / 2));
    }

    private GraphNode AddNode(StoryNode node, double x, double y)
    {
        var border = new Border
        {
            Width = NodeWidth,
            Height = NodeHeight,
            Background = Brushes.SteelBlue,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Child = new TextBlock
            {
                Text = node.Title,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            }
        };

        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, y);

        var created = new GraphNode(node, border);
        border.Tag = created;

        border.PointerPressed += OnNodePointerPressed;
        border.PointerMoved += OnNodePointerMoved;
        border.PointerReleased += OnNodePointerReleased;

        GraphCanvas.Children.Add(border);
        _nodes.Add(created);

        return created;
    }

    private void OnNodePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not GraphNode node)
        {
            return;
        }

        Highlight(node);
        NodeSelected?.Invoke(this, node.Title);

        Point pointer = e.GetPosition(GraphCanvas);

        _dragging = node;
        _dragOffset = new Point(
            pointer.X - Canvas.GetLeft(border),
            pointer.Y - Canvas.GetTop(border));

        e.Pointer.Capture(border);
        e.Handled = true;
    }

    private void OnNodePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging is null)
        {
            return;
        }

        Point pointer = e.GetPosition(GraphCanvas);

        double x = Snap(pointer.X - _dragOffset.X);
        double y = Snap(pointer.Y - _dragOffset.Y);

        Canvas.SetLeft(
            _dragging.Visual,
            Math.Clamp(x, 0, CanvasWidth - NodeWidth));

        Canvas.SetTop(
            _dragging.Visual,
            Math.Clamp(y, 0, CanvasHeight - NodeHeight));

        MoveEdges();
    }

    private void OnNodePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragging = null;
        e.Pointer.Capture(null);
    }

    /// <summary>드래그한 노드에 붙은 선이 따라오게 한다.</summary>
    private void MoveEdges()
    {
        for (int index = 0; index < _edges.Count; index++)
        {
            EdgeEnds ends = _edgeEnds[index];

            _edges[index].StartPoint = CenterOf(ends.From);
            _edges[index].EndPoint = CenterOf(ends.To);
        }
    }

    private void Highlight(GraphNode node)
    {
        foreach (GraphNode other in _nodes)
        {
            other.Visual.BorderBrush = Brushes.Gray;
            other.Visual.BorderThickness = new Thickness(1);
        }

        node.Visual.BorderBrush = Brushes.OrangeRed;
        node.Visual.BorderThickness = new Thickness(3);

        SelectedTitleText.Text = node.Title;

        SelectedBodyText.Text =
            $"{System.IO.Path.GetFileName(node.Node.FilePath)}:{node.Node.HeaderLine}" +
            $"{Environment.NewLine}라인 {node.Node.Lines.Count}개, 점프 {node.Node.Jumps.Count}개";
    }

    private static double Snap(double value)
    {
        return Math.Round(value / GridSize) * GridSize;
    }

    private sealed record EdgeEnds(GraphNode From, GraphNode To);

    private sealed class GraphNode
    {
        public GraphNode(StoryNode node, Border visual)
        {
            Node = node;
            Visual = visual;
        }

        public StoryNode Node { get; }

        public string Title => Node.Title;

        public Border Visual { get; }
    }
}
