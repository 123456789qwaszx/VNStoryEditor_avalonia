using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;

namespace Vn.App.Views;

public partial class GraphView : UserControl
{
    private const double CanvasWidth = 2000;
    private const double CanvasHeight = 1200;
    private const double GridSize = 40;
    private const double NodeWidth = 140;
    private const double NodeHeight = 60;

    private readonly List<GraphNode> _nodes = new();

    private GraphNode? _dragging;
    private Point _dragOffset;

    public GraphView()
    {
        InitializeComponent();

        DrawGrid();

        AddNode("Start", "이야기가 시작되는 노드입니다.", 80, 80);
        AddNode("Middle", "중간에서 갈라지는 지점.", 320, 240);
        AddNode("Ending", "여기서 끝납니다.", 560, 80);
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

    private void AddNode(string title, string body, double x, double y)
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
                Text = title,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            }
        };

        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, y);

        var node = new GraphNode(title, body, border);
        border.Tag = node;

        border.PointerPressed += OnNodePointerPressed;
        border.PointerMoved += OnNodePointerMoved;
        border.PointerReleased += OnNodePointerReleased;

        GraphCanvas.Children.Add(border);
        _nodes.Add(node);
    }

    private void OnNodePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not GraphNode node)
        {
            return;
        }

        Select(node);

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
    }

    private void OnNodePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragging = null;
        e.Pointer.Capture(null);
    }

    private void Select(GraphNode node)
    {
        foreach (GraphNode other in _nodes)
        {
            other.Visual.BorderBrush = Brushes.Gray;
            other.Visual.BorderThickness = new Thickness(1);
        }

        node.Visual.BorderBrush = Brushes.OrangeRed;
        node.Visual.BorderThickness = new Thickness(3);

        SelectedTitleText.Text = node.Title;
        SelectedBodyText.Text = node.Body;
    }

    private static double Snap(double value)
    {
        return Math.Round(value / GridSize) * GridSize;
    }

    private sealed class GraphNode
    {
        public GraphNode(string title, string body, Border visual)
        {
            Title = title;
            Body = body;
            Visual = visual;
        }

        public string Title { get; }
        public string Body { get; }
        public Border Visual { get; }
    }
}