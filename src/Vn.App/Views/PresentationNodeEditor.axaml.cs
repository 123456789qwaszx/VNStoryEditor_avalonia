using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Vn.App.Services;
using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.App.Views;

/// <summary>
/// 연출 노드 하나의 <b>노드 수준</b> 상태 — 이름, 자동 채널(입력 결과·공급·발행), 입력 결과
/// 요약, 그 결과에 붙지 않는 고아 연출. 라인별 커맨드 편집은 이 툴의 일이 아니다
/// (2026-10-28 소유자: 이 툴은 작가가 글을 쓰는 곳이고, 연출 저작은 별도 툴이 맡는다).
///
/// 입력은 편집 중인 DialogueNode가 아니라 <b>발행된 대사 결과</b>다. 편집 중인 노드를
/// 읽으면 연출가가 작업하는 동안 발밑의 대사가 바뀌고, 완성한 연출표가 어느 대사에 맞는
/// 것인지 아무도 말할 수 없게 된다.
/// </summary>
public partial class PresentationNodeEditor : UserControl
{
    private AuthoringSession? _session;
    private string? _nodeId;
    private bool _building;

    public PresentationNodeEditor()
    {
        InitializeComponent();

        NameBox.LostFocus += (_, _) =>
        {
            if (!_building && _session is not null && _nodeId is not null)
            {
                _session.Editor.RenameNode(_nodeId, NameBox.Text ?? string.Empty);
            }
        };

        // 입력·공급 콤보와 [연출 발행]은 2026-08-21에 사라졌다 — 채널이 자동이다
        // (ProjectEditor.EnsurePresentationChannel). 이 편집기는 상태를 보여 줄 뿐이다.
    }

    internal void Attach(AuthoringSession session) => _session = session;

    internal string? NodeId => _nodeId;

    internal void Show(string? nodeId)
    {
        _nodeId = nodeId;
        Rebuild();
    }

    internal void Rebuild()
    {
        if (_session is null || _session.Project.FindPresentation(_nodeId) is not { } presentation)
        {
            LineHost.Children.Clear();
            return;
        }

        _building = true;

        try
        {
            NameBox.Text = presentation.Name;
            LineHost.Children.Clear();

            BuildChannelStatus(presentation);

            PresentationWorkspace workspace = PresentationBindingResolver.Resolve(
                _session.Project,
                presentation);

            // 카탈로그 범위는 연결된 공급 노드가 정한다. 없으면 전체 카탈로그 폴백이다.
            PresentationCommandCatalog catalog = AvailablePresentationCommandResolver.Resolve(
                _session.Project,
                presentation.Id,
                _session.Definition).Catalog;

            if (workspace.Dialogue is not { } dialogue)
            {
                TargetText.Text = presentation.Source is { } missing
                    ? $"입력으로 지정한 대사 결과 '{missing.Label}'을 찾을 수 없습니다."
                    : "읽을 대사 결과가 없습니다 — 대사 노드의 연출 채널이 서면 자동으로 고정됩니다.";
            }
            else
            {
                TargetText.Text =
                    $"{dialogue.SourceNodeName} · {dialogue.Identity.Label} · {dialogue.Lines.Count}줄 · " +
                    $"{dialogue.Locale}" +
                    (workspace.IsStale ? " · 내용 해시 불일치" : string.Empty);
            }

            IReadOnlyList<ResolvedPresentationBinding> orphaned = workspace.Orphans.ToArray();

            if (orphaned.Count > 0)
            {
                LineHost.Children.Add(new TextBlock
                {
                    Text = "이 결과에 붙지 않는 연출",
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 8, 0, 0)
                });

                foreach (ResolvedPresentationBinding orphan in orphaned)
                {
                    LineHost.Children.Add(BuildOrphanCard(orphan, catalog));
                }
            }
        }
        finally
        {
            _building = false;
        }
    }

    /// <summary>
    /// 자동 채널의 현재 상태 한 줄 — 무엇을 읽고 어디에 공급하며 언제 발행됐는지.
    /// 편집 UI가 아니다: 값의 주인은 EnsurePresentationChannel이다.
    /// </summary>
    private void BuildChannelStatus(PresentationNode presentation)
    {
        NodeLink? supply = _session!.Project.Links.FirstOrDefault(link =>
            link.Kind == NodeLinkKind.PresentationSupply &&
            link.IsEnabled &&
            string.Equals(link.SourceNodeId, presentation.Id, StringComparison.Ordinal));
        string target = supply is null
            ? "(공급 안 함)"
            : _session.Project.FindNode(supply.TargetNodeId)?.Name ?? supply.TargetNodeId;

        PresentationResult? latest = _session.Project.Results
            .PresentationResultsOf(presentation.Id)
            .LastOrDefault();

        ChannelStatusText.Text =
            $"자동 채널 — 입력 {presentation.Source?.Label ?? "없음"} · 공급 → {target}" +
            (latest is null ? string.Empty : $" · 발행 {latest.Identity.Label}");
    }

    private static Control BuildOrphanCard(
        ResolvedPresentationBinding orphan,
        PresentationCommandCatalog catalog)
    {
        string commands = orphan.Binding.Commands.Count == 0
            ? "명령 없음"
            : string.Join(", ", orphan.Binding.Commands.Select(command =>
                catalog.Find(command.DefinitionId)?.DisplayName ?? command.DefinitionId));

        return new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromArgb(20, 220, 38, 38)),
            Child = new TextBlock
            {
                Text = $"{orphan.Binding.LineId}\n{commands}",
                TextWrapping = TextWrapping.Wrap
            }
        };
    }
}
