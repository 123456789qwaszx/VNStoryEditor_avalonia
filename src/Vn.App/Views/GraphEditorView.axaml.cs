using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Vn.App.Services;
using Vn.Authoring.Graph;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.App.Views;

/// <summary>
/// GraphProjection을 그리는 저작 화면.
///
/// StoryProject를 직접 순회하지 않는다. 공식 원본과 workspace의 파일 펼침 상태를
/// <see cref="GraphProjectionBuilder"/>에 전달하고, 펼쳐진 노드는 NodeCard로, 접힌 파일은
/// FileProxy로 받은 결과만 그린다. 파일을 접어도 실제 연결 target은 NodeId로 유지되고
/// endpoint만 프록시의 해당 노드 행으로 바뀐다.
/// </summary>
public partial class GraphEditorView : UserControl
{
    private const double CardWidth = 210;
    private const double HeaderHeight = 46;
    private const double PortRowHeight = 24;
    private const double CardPadding = 8;
    private const double PortRadius = 6;

    private const double ProxyWidth = 250;
    private const double ProxyHeaderHeight = 50;
    private const double ProxyRowHeight = 28;
    private const double ProxyPortRadius = 5;

    private readonly List<NodeCard> _cards = new();
    private readonly List<FileProxyVisual> _proxies = new();
    private readonly List<EdgeVisual> _edges = new();

    private AuthoringSession? _session;
    private GraphProjection? _projection;

    private NodeCard? _draggingCard;
    private Point _dragOffset;

    private GraphOutputPortProjection? _connectingFrom;
    private Line? _connectingLine;

    private EdgeVisual? _selectedEdge;

    public GraphEditorView()
    {
        InitializeComponent();

        AddDialogueButton.Click += (_, _) => AddNode(GraphNodeKind.Dialogue);
        AddSetButton.Click += (_, _) => AddNode(GraphNodeKind.Set);
        AddPresentationButton.Click += (_, _) => AddNode(GraphNodeKind.Presentation);
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

        _projection = GraphProjectionBuilder.Build(
            _session.Project,
            _session.ExpandedFileIds,
            _session.Definition);

        GraphCanvas.Children.Clear();
        _cards.Clear();
        _proxies.Clear();
        _edges.Clear();
        _selectedEdge = null;
        DeleteEdgeButton.IsEnabled = false;

        foreach (GraphItemProjection item in _projection.Items)
        {
            switch (item)
            {
                case ExpandedNodeProjection node:
                    _cards.Add(BuildCard(node));
                    break;

                case CollapsedFileProjection file:
                    _proxies.Add(BuildFileProxy(file));
                    break;
            }
        }

        AddDialogueButton.IsEnabled = _session.ActiveFileId is not null;
        AddSetButton.IsEnabled = _session.ActiveFileId is not null;
        AddPresentationButton.IsEnabled = _session.ActiveFileId is not null;

        DrawEdges();
        HighlightSelection();
    }

    /// <summary>좌표만 바뀌었을 때. projection을 다시 계산하되 컨트롤은 유지한다.</summary>
    internal void RefreshPositions()
    {
        if (_session is null)
        {
            return;
        }

        _projection = GraphProjectionBuilder.Build(
            _session.Project,
            _session.ExpandedFileIds,
            _session.Definition);

        foreach (ExpandedNodeProjection node in _projection.Items.OfType<ExpandedNodeProjection>())
        {
            NodeCard? card = FindCard(node.NodeId);

            if (card is not null)
            {
                Canvas.SetLeft(card.Visual, node.Position.X);
                Canvas.SetTop(card.Visual, node.Position.Y);
            }
        }

        foreach (CollapsedFileProjection file in _projection.Items.OfType<CollapsedFileProjection>())
        {
            FileProxyVisual? proxy = FindProxy(file.FileId);

            if (proxy is not null)
            {
                Canvas.SetLeft(proxy.Visual, file.Position.X);
                Canvas.SetTop(proxy.Visual, file.Position.Y);
            }
        }

        foreach (EdgeVisual edge in _edges)
        {
            GraphConnectionProjection? current = _projection.Connections.FirstOrDefault(
                item => string.Equals(item.Key, edge.Connection.Key, StringComparison.Ordinal));

            if (current is not null)
            {
                edge.Connection = current;
                PositionEdge(edge);
            }
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

        foreach (FileProxyVisual proxy in _proxies)
        {
            foreach (ProxyNodeRow row in proxy.Rows)
            {
                bool selected = string.Equals(row.NodeId, _session?.SelectedNodeId, StringComparison.Ordinal);
                row.Visual.Background = selected
                    ? new SolidColorBrush(Color.FromArgb(42, 37, 99, 235))
                    : Brushes.Transparent;
            }
        }
    }

    private NodeCard BuildCard(ExpandedNodeProjection node)
    {
        var body = new StackPanel { Spacing = 0 };

        body.Children.Add(new TextBlock
        {
            Text = node.NodeName,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        body.Children.Add(new TextBlock
        {
            Text = node.Badge is null
                ? NodeKindLabel(node.NodeKind)
                : $"{NodeKindLabel(node.NodeKind)} · {node.Badge}",
            FontSize = 10,
            Opacity = 0.6,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 1, 0, 6)
        });

        var card = new Border
        {
            Width = CardWidth,
            Padding = new Thickness(CardPadding),
            CornerRadius = new CornerRadius(8),
            Background = node.NodeKind switch
            {
                GraphNodeKind.Set => new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF)),
                GraphNodeKind.Presentation => new SolidColorBrush(Color.FromRgb(0xFA, 0xF5, 0xFF)),
                _ => new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))
            },
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 128, 128, 128)),
            Child = body,
            Tag = node.NodeId
        };

        var visual = new NodeCard(node.NodeId, node.NodeKind, card, node.OutputPorts);

        for (int index = 0; index < node.OutputPorts.Count; index++)
        {
            body.Children.Add(BuildPortRow(node.OutputPorts[index], visual, index));
        }

        Canvas.SetLeft(card, node.Position.X);
        Canvas.SetTop(card, node.Position.Y);
        GraphCanvas.Children.Add(card);

        card.PointerPressed += (_, args) => OnCardPressed(visual, args);
        return visual;
    }

    private FileProxyVisual BuildFileProxy(CollapsedFileProjection file)
    {
        var content = new StackPanel { Spacing = 0 };

        var headerDot = new Ellipse
        {
            Width = 11,
            Height = 11,
            Fill = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var headerText = new StackPanel
        {
            Spacing = 1,
            Children =
            {
                new TextBlock
                {
                    Text = file.FileName,
                    FontWeight = FontWeight.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                new TextBlock
                {
                    Text = $"접힘 · {file.Nodes.Count}개 노드 · {file.RelativePath}",
                    FontSize = 9,
                    Opacity = 0.6,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            }
        };

        var header = new Grid
        {
            Height = ProxyHeaderHeight,
            Margin = new Thickness(10, 0),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(headerDot, 0);
        Grid.SetColumn(headerText, 1);
        header.Children.Add(headerDot);
        header.Children.Add(headerText);
        content.Children.Add(header);

        var rows = new List<ProxyNodeRow>();

        if (file.Nodes.Count == 0)
        {
            content.Children.Add(new Border
            {
                Height = ProxyRowHeight,
                Child = new TextBlock
                {
                    Text = "빈 파일",
                    Margin = new Thickness(22, 0),
                    FontSize = 10,
                    Opacity = 0.5,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
        }
        else
        {
            for (int index = 0; index < file.Nodes.Count; index++)
            {
                CollapsedNodeEntry entry = file.Nodes[index];
                ProxyNodeRow row = BuildProxyRow(entry, index);
                rows.Add(row);
                content.Children.Add(row.Visual);
            }
        }

        var visual = new Border
        {
            Width = ProxyWidth,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
            BorderThickness = new Thickness(1),
            ClipToBounds = false,
            Child = content,
            Tag = file.FileId
        };

        Canvas.SetLeft(visual, file.Position.X);
        Canvas.SetTop(visual, file.Position.Y);
        GraphCanvas.Children.Add(visual);

        return new FileProxyVisual(file.FileId, visual, rows);
    }

    private ProxyNodeRow BuildProxyRow(CollapsedNodeEntry entry, int index)
    {
        IBrush dotBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));

        var input = new Ellipse
        {
            Width = ProxyPortRadius * 2,
            Height = ProxyPortRadius * 2,
            Margin = new Thickness(-ProxyPortRadius, 0, 7, 0),
            Fill = entry.IncomingCount > 0 ? dotBrush : Brushes.Transparent,
            Stroke = dotBrush,
            StrokeThickness = 1.5,
            VerticalAlignment = VerticalAlignment.Center
        };

        var name = new TextBlock
        {
            Text = entry.NodeName,
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        var kind = new TextBlock
        {
            Text = NodeKindLabel(entry.NodeKind),
            FontSize = 9,
            Opacity = 0.5,
            Margin = new Thickness(6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var output = new Ellipse
        {
            Width = ProxyPortRadius * 2,
            Height = ProxyPortRadius * 2,
            Margin = new Thickness(7, 0, -ProxyPortRadius, 0),
            Fill = entry.OutgoingCount > 0 ? dotBrush : Brushes.Transparent,
            Stroke = dotBrush,
            StrokeThickness = 1.5,
            VerticalAlignment = VerticalAlignment.Center
        };

        var grid = new Grid
        {
            Height = ProxyRowHeight,
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto")
        };
        Grid.SetColumn(input, 0);
        Grid.SetColumn(name, 1);
        Grid.SetColumn(kind, 2);
        Grid.SetColumn(output, 3);
        grid.Children.Add(input);
        grid.Children.Add(name);
        grid.Children.Add(kind);
        grid.Children.Add(output);

        var row = new Border
        {
            Height = ProxyRowHeight,
            Padding = new Thickness(0, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(35, 107, 114, 128)),
            BorderThickness = new Thickness(0, index == 0 ? 1 : 0, 0, 1),
            Child = grid,
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = entry.NodeId
        };

        row.PointerPressed += (_, args) =>
        {
            _session?.Select(entry.NodeId);
            HighlightSelection();
            args.Handled = true;
        };

        return new ProxyNodeRow(entry.NodeId, entry.NodeKind, index, row);
    }

    private Control BuildPortRow(GraphOutputPortProjection port, NodeCard card, int index)
    {
        bool branch = port.Kind == GraphOutputPortKind.ExecutionBranch;
        bool settings = port.Kind == GraphOutputPortKind.Settings;
        bool presentation = port.Kind == GraphOutputPortKind.PublishedResult;
        IBrush settingsBrush = new SolidColorBrush(Color.FromRgb(0x0F, 0x76, 0x6E));
        IBrush presentationBrush = new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED));

        var label = new TextBlock
        {
            Text = port.Label,
            FontSize = 10,
            Opacity = port.Kind == GraphOutputPortKind.ExecutionDefault ? 0.6 : 1,
            Foreground = branch
                ? BranchPalette.Accent(port.PaletteIndex)
                : settings
                    ? settingsBrush
                    : presentation
                        ? presentationBrush
                        : null,
            FontWeight = branch || settings || presentation ? FontWeight.SemiBold : FontWeight.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        IBrush portBrush = branch
            ? BranchPalette.Accent(port.PaletteIndex)
            : settings
                ? settingsBrush
                : presentation
                    ? presentationBrush
                    : Brushes.DimGray;

        var knob = new Ellipse
        {
            Width = PortRadius * 2,
            Height = PortRadius * 2,
            Margin = new Thickness(6, 0, -CardPadding - PortRadius, 0),
            Fill = port.IsConnected ? portBrush : Brushes.Transparent,
            Stroke = portBrush,
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
        if (_projection is null)
        {
            return;
        }

        foreach (GraphConnectionProjection connection in _projection.Connections)
        {
            AddEdge(connection);
        }
    }

    private void AddEdge(GraphConnectionProjection connection)
    {
        if (!TryEndpointAnchor(connection.Source, out _) ||
            !TryEndpointAnchor(connection.Target, out _))
        {
            return;
        }

        IBrush stroke = ConnectionBrush(connection);
        var path = new ShapePath
        {
            Stroke = stroke,
            StrokeThickness = 2,
            StrokeDashArray = connection.Kind switch
            {
                GraphConnectionKind.Settings => new AvaloniaList<double> { 5, 3 },
                GraphConnectionKind.ResultSnapshot => new AvaloniaList<double> { 2, 3 },
                _ => null
            }
        };

        bool showLabel = connection.Kind is
            GraphConnectionKind.ExecutionBranch or
            GraphConnectionKind.Settings or
            GraphConnectionKind.ResultSnapshot;
        var label = new Border
        {
            Padding = new Thickness(5, 1),
            CornerRadius = new CornerRadius(3),
            Background = showLabel ? stroke : Brushes.Transparent,
            IsVisible = showLabel,
            Child = new TextBlock
            {
                Text = connection.Label,
                FontSize = 9,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White
            }
        };

        var edge = new EdgeVisual(connection, path, label);

        path.PointerPressed += (_, args) =>
        {
            SelectEdge(edge);
            args.Handled = true;
        };
        label.PointerPressed += (_, args) =>
        {
            SelectEdge(edge);
            args.Handled = true;
        };

        GraphCanvas.Children.Insert(0, path);
        GraphCanvas.Children.Add(label);
        _edges.Add(edge);

        PositionEdge(edge);
    }

    private void PositionEdge(EdgeVisual edge)
    {
        if (!TryEndpointAnchor(edge.Connection.Source, out Point from) ||
            !TryEndpointAnchor(edge.Connection.Target, out Point to))
        {
            edge.Path.IsVisible = false;
            edge.Label.IsVisible = false;
            return;
        }

        edge.Path.IsVisible = true;
        bool showLabel = edge.Connection.Kind is
            GraphConnectionKind.ExecutionBranch or
            GraphConnectionKind.Settings or
            GraphConnectionKind.ResultSnapshot;
        edge.Label.IsVisible = showLabel;

        IReadOnlyList<GraphPosition> route = OrthogonalEdgeRouter.Route(
            new GraphPosition(from.X, from.Y),
            new GraphPosition(to.X, to.Y));

        var geometry = new StreamGeometry();

        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(ToPoint(route[0]), isFilled: false);

            for (int index = 1; index < route.Count; index++)
            {
                context.LineTo(ToPoint(route[index]));
            }

            context.EndFigure(isClosed: false);
        }

        edge.Path.Data = geometry;

        GraphPosition labelPoint = new(
            (route[1].X + route[2].X) / 2,
            (route[1].Y + route[2].Y) / 2);

        edge.Label.Measure(Size.Infinity);
        Canvas.SetLeft(edge.Label, labelPoint.X - (edge.Label.DesiredSize.Width / 2));
        Canvas.SetTop(edge.Label, labelPoint.Y - (edge.Label.DesiredSize.Height / 2));
    }

    private bool TryEndpointAnchor(GraphEndpointProjection endpoint, out Point point)
    {
        switch (endpoint.Kind)
        {
            case GraphEndpointKind.ExpandedNodeOutput:
            {
                NodeCard? card = FindCard(endpoint.NodeId);
                int portIndex = card?.PortIndex(endpoint.PortKey) ?? -1;

                if (card is not null && portIndex >= 0)
                {
                    point = PortAnchor(card, portIndex);
                    return true;
                }

                break;
            }

            case GraphEndpointKind.ExpandedNodeInput:
            {
                NodeCard? card = FindCard(endpoint.NodeId);

                if (card is not null)
                {
                    point = InputAnchor(card);
                    return true;
                }

                break;
            }

            case GraphEndpointKind.CollapsedFileNodeOutput:
            case GraphEndpointKind.CollapsedFileNodeInput:
            {
                FileProxyVisual? proxy = FindProxy(endpoint.FileId);

                if (proxy is not null && endpoint.ProxyRowIndex is { } rowIndex)
                {
                    point = ProxyRowAnchor(
                        proxy,
                        rowIndex,
                        output: endpoint.Kind == GraphEndpointKind.CollapsedFileNodeOutput);
                    return true;
                }

                break;
            }
        }

        point = default;
        return false;
    }

    /// <summary>펼쳐진 노드 카드의 출력 포트 좌표.</summary>
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

    private static Point ProxyRowAnchor(FileProxyVisual proxy, int rowIndex, bool output)
    {
        double x = Canvas.GetLeft(proxy.Visual) + (output ? ProxyWidth : 0);
        double y = Canvas.GetTop(proxy.Visual)
            + ProxyHeaderHeight
            + (rowIndex * ProxyRowHeight)
            + (ProxyRowHeight / 2);

        return new Point(x, y);
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

    private void OnPortPressed(
        GraphOutputPortProjection port,
        NodeCard card,
        int index,
        PointerPressedEventArgs args)
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
        HintText.Text = port.Kind switch
        {
            GraphOutputPortKind.Settings =>
                "조건을 공급할 대사 노드 또는 접힌 파일의 대사 행 위에서 놓으세요.",
            GraphOutputPortKind.PublishedResult =>
                "이 대사 노드의 최신 발행 결과를 읽을 연출 노드 위에서 놓으세요.",
            _ =>
                "연결할 실행 노드 또는 접힌 파일의 실행 노드 행 위에서 놓으세요. 빈 곳에 놓으면 실행 연결이 끊어집니다."
        };

        args.Handled = true;
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs args)
    {
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

        GraphOutputPortProjection port = _connectingFrom;
        _connectingFrom = null;

        if (_connectingLine is not null)
        {
            GraphCanvas.Children.Remove(_connectingLine);
            _connectingLine = null;
        }

        HintText.Text = "포트(●)를 끌어 실제 노드나 FileProxy의 노드 행에 놓으면 연결됩니다.";

        GraphNodeHit? dropped = NodeAt(args.GetPosition(GraphCanvas));

        if (port.Kind == GraphOutputPortKind.Settings)
        {
            if (dropped is { NodeKind: GraphNodeKind.Dialogue } &&
                !string.Equals(dropped.NodeId, port.NodeId, StringComparison.Ordinal))
            {
                _session?.Editor.AddSettingsLink(port.NodeId, dropped.NodeId);
            }
            else if (dropped is not null)
            {
                _session?.SetStatus("Settings link는 SetNode에서 DialogueNode로만 연결할 수 있습니다.");
            }

            return;
        }

        if (port.Kind == GraphOutputPortKind.PublishedResult)
        {
            AttachLatestResult(port.NodeId, dropped);
            return;
        }

        if (dropped is { NodeKind: GraphNodeKind.Presentation })
        {
            _session?.SetStatus("PresentationNode는 실행 출구의 대상이 될 수 없습니다.");
            return;
        }

        string? target = dropped is null || string.Equals(dropped.NodeId, port.NodeId, StringComparison.Ordinal)
            ? null
            : dropped.NodeId;

        if (port.ExecutionPort is not null)
        {
            _session?.Editor.SetExitTarget(port.ExecutionPort, target);
        }
    }

    /// <summary>
    /// 대사 노드의 발행 결과 포트를 연출 노드에 끌어다 놓았을 때.
    ///
    /// <b>최신 버전을 명시적으로 고정한다.</b> "이 노드의 최신"으로 저장하면 다음 발행 때
    /// 연출가가 모르는 사이에 발밑의 대사가 바뀐다. 다른 버전으로 옮기는 것은 연출 편집기의
    /// 버전 목록에서 한다.
    /// </summary>
    private void AttachLatestResult(string dialogueNodeId, GraphNodeHit? dropped)
    {
        if (_session is null)
        {
            return;
        }

        if (dropped is null)
        {
            _session.SetStatus("연출 노드 위에 놓아야 입력 결과가 연결됩니다.");
            return;
        }

        if (dropped.NodeKind != GraphNodeKind.Presentation)
        {
            _session.SetStatus("발행 결과는 연출 노드만 읽을 수 있습니다.");
            return;
        }

        DialogueResult? latest = _session.Project.Results
            .DialogueResultsOf(dialogueNodeId)
            .LastOrDefault();

        if (latest is null)
        {
            _session.SetStatus(
                "이 대사 노드는 아직 발행된 결과가 없습니다. 대사 편집기에서 먼저 발행하세요.");
            return;
        }

        try
        {
            _session.Editor.SetPresentationSource(
                dropped.NodeId,
                latest.Identity.ResultId,
                latest.Identity.Version);
            _session.SetStatus($"연출이 '{latest.SourceNodeName} v{latest.Identity.Version}'을 읽습니다.");
        }
        catch (InvalidOperationException exception)
        {
            _session.SetStatus(exception.Message);
        }
    }

    private GraphNodeHit? NodeAt(Point point)
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
                return new GraphNodeHit(card.NodeId, card.NodeKind);
            }
        }

        foreach (FileProxyVisual proxy in _proxies)
        {
            double left = Canvas.GetLeft(proxy.Visual);
            double top = Canvas.GetTop(proxy.Visual);

            if (point.X < left || point.X > left + ProxyWidth || point.Y < top + ProxyHeaderHeight)
            {
                continue;
            }

            int rowIndex = (int)((point.Y - top - ProxyHeaderHeight) / ProxyRowHeight);

            if (rowIndex >= 0 && rowIndex < proxy.Rows.Count)
            {
                ProxyNodeRow row = proxy.Rows[rowIndex];
                return new GraphNodeHit(row.NodeId, row.NodeKind);
            }
        }

        return null;
    }

    private void SelectEdge(EdgeVisual? edge)
    {
        foreach (EdgeVisual item in _edges)
        {
            item.Path.StrokeThickness = 2;
        }

        _selectedEdge = edge;
        DeleteEdgeButton.IsEnabled = edge is not null;

        if (edge is null)
        {
            return;
        }

        edge.Path.StrokeThickness = 4;

        string sourceName = FindNodeName(edge.Connection.SourceNodeId) ?? edge.Connection.SourceNodeId;
        string targetName = FindNodeName(edge.Connection.TargetNodeId) ?? edge.Connection.TargetNodeId;
        string kind = edge.Connection.Kind switch
        {
            GraphConnectionKind.Settings => "조건 공급",
            GraphConnectionKind.ResultSnapshot => "발행 결과 입력",
            GraphConnectionKind.ExecutionBranch => $"조건 '{edge.Connection.Label}'",
            _ => "기본 출구"
        };

        HintText.Text = $"{sourceName} — {kind} → {targetName}";
    }

    private void DeleteSelectedEdge()
    {
        if (_selectedEdge is null || _session is null)
        {
            return;
        }

        if (_selectedEdge.Connection.LinkId is { } linkId)
        {
            _session.Editor.RemoveLink(linkId);
        }
        else if (_selectedEdge.Connection.ExecutionPort is { } exit)
        {
            _session.Editor.SetExitTarget(exit, null);
        }
    }

    private void AddNode(GraphNodeKind kind)
    {
        if (_session is null)
        {
            return;
        }

        if (_session.ActiveFileId is not { } fileId)
        {
            _session.SetStatus("새 노드를 추가할 StoryFile이 없습니다.");
            return;
        }

        StoryFile activeFile = _session.ActiveFile
            ?? throw new InvalidOperationException($"현재 StoryFile '{fileId}'를 찾을 수 없습니다.");

        int fileNodeCount = activeFile.Nodes.Count;
        double x = 60 + (fileNodeCount % 4) * 260;
        double y = 60 + (fileNodeCount / 4) * 200;

        StoryNode node = kind switch
        {
            GraphNodeKind.Dialogue => _session.Editor.AddDialogueNode(fileId, x, y),
            GraphNodeKind.Set => _session.Editor.AddSetNode(fileId, x, y),
            GraphNodeKind.Presentation => _session.Editor.AddPresentationNode(fileId, x, y),
            _ => throw new NotSupportedException($"지원하지 않는 그래프 노드 종류 '{kind}'입니다.")
        };

        _session.Select(node.Id);
    }

    private void DeleteSelectedNode()
    {
        if (_session?.SelectedNodeId is { } nodeId)
        {
            _session.Editor.RemoveNode(nodeId);
        }
    }

    private NodeCard? FindCard(string nodeId)
    {
        return _cards.FirstOrDefault(item => string.Equals(item.NodeId, nodeId, StringComparison.Ordinal));
    }

    private FileProxyVisual? FindProxy(string fileId)
    {
        return _proxies.FirstOrDefault(item => string.Equals(item.FileId, fileId, StringComparison.Ordinal));
    }

    private string? FindNodeName(string nodeId)
    {
        ExpandedNodeProjection? expanded = _projection?.Items
            .OfType<ExpandedNodeProjection>()
            .FirstOrDefault(item => string.Equals(item.NodeId, nodeId, StringComparison.Ordinal));

        if (expanded is not null)
        {
            return expanded.NodeName;
        }

        return _projection?.Items
            .OfType<CollapsedFileProjection>()
            .SelectMany(file => file.Nodes)
            .FirstOrDefault(item => string.Equals(item.NodeId, nodeId, StringComparison.Ordinal))
            ?.NodeName;
    }

    private static IBrush ConnectionBrush(GraphConnectionProjection connection)
    {
        return connection.Kind switch
        {
            GraphConnectionKind.Settings => new SolidColorBrush(Color.FromRgb(0x0F, 0x76, 0x6E)),
            GraphConnectionKind.ResultSnapshot => new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)),
            GraphConnectionKind.ExecutionBranch => BranchPalette.Accent(connection.PaletteIndex),
            _ => new SolidColorBrush(Color.FromArgb(150, 100, 100, 100))
        };
    }

    private static string NodeKindLabel(GraphNodeKind kind)
    {
        return kind switch
        {
            GraphNodeKind.Set => "설정",
            GraphNodeKind.Presentation => "연출",
            _ => "대사"
        };
    }

    private static Point ToPoint(GraphPosition position) => new(position.X, position.Y);

    private sealed class NodeCard
    {
        public NodeCard(
            string nodeId,
            GraphNodeKind nodeKind,
            Border visual,
            IReadOnlyList<GraphOutputPortProjection> ports)
        {
            NodeId = nodeId;
            NodeKind = nodeKind;
            Visual = visual;
            Ports = ports;
        }

        public string NodeId { get; }
        public GraphNodeKind NodeKind { get; }
        public Border Visual { get; }
        public IReadOnlyList<GraphOutputPortProjection> Ports { get; }

        public int PortIndex(string? key)
        {
            return key is null
                ? -1
                : Ports.ToList().FindIndex(port => string.Equals(port.Key, key, StringComparison.Ordinal));
        }
    }

    private sealed class FileProxyVisual
    {
        public FileProxyVisual(string fileId, Border visual, IReadOnlyList<ProxyNodeRow> rows)
        {
            FileId = fileId;
            Visual = visual;
            Rows = rows;
        }

        public string FileId { get; }
        public Border Visual { get; }
        public IReadOnlyList<ProxyNodeRow> Rows { get; }
    }

    private sealed record ProxyNodeRow(
        string NodeId,
        GraphNodeKind NodeKind,
        int Index,
        Border Visual);

    private sealed record GraphNodeHit(string NodeId, GraphNodeKind NodeKind);

    private sealed class EdgeVisual
    {
        public EdgeVisual(GraphConnectionProjection connection, ShapePath path, Border label)
        {
            Connection = connection;
            Path = path;
            Label = label;
        }

        public GraphConnectionProjection Connection { get; set; }
        public ShapePath Path { get; }
        public Border Label { get; }
    }
}
