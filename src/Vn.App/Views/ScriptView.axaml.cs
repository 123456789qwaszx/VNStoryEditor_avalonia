using Avalonia.Controls;
using Vn.App.Services;
using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;

namespace Vn.App.Views;

/// <summary>
/// <b>[대본] — 작가의 자리</b> (R-E · 2026-09-16,
/// <c>docs/work-orders/tool-owns-workbooks-orders.md</c> §6).
///
/// <b>왜 제 탭인가</b> — 이 도구의 탭은 곧 역할이다(챕터 그래프 = 기획자, 연출 그래프 =
/// 연출자). 작가만 제 탭이 없어서, 글을 고치려면 노드·포트·배선이 가득한 판을 열어야 했다.
///
/// ⛔ <b>여기에는 노드도 포트도 조건도 없다.</b> 조건이 없는 것은 숨겨서가 아니라
/// <b>대본 층에 조건이 더는 없기</b> 때문이다(R-C · 규격 v15). 보여 줄 것이 없다.
///
/// <b>새 경로가 아니다</b> — 글은 <c>ScenarioOnly</c> 프리셋으로 그리고
/// <see cref="ProjectEditor.ApplyScenarioText"/>로 반영한다. 연출 그래프의 대사 편집기가
/// 쓰던 그 경로이고, 여기서 달라진 것은 <b>그 둘레</b>뿐이다: 노드가 아니라 에피소드를
/// 고르고, 화면에 글만 남긴다.
/// </summary>
public partial class ScriptView : UserControl
{
    private AuthoringSession? _session;

    /// <summary>삭제 확인 대기 중인 글 — 같은 글로 한 번 더 누르면 적용한다.</summary>
    private string? _pendingDeleteText;

    public ScriptView()
    {
        InitializeComponent();

        ChapterList.SelectionChanged += (_, _) => UiGuard.Run(_session, "챕터 고르기", RebuildEpisodes);
        EpisodeList.SelectionChanged += (_, _) => UiGuard.Run(_session, "에피소드 고르기", ShowSelected);
        ApplyButton.Click += (_, _) => UiGuard.Run(_session, "글 반영", Apply);
    }


    internal void Attach(AuthoringSession session)
    {
        _session = session;

        // 판이 늘거나 줄면 고를 것이 달라진다. ⚠ 글을 쓰는 중에는 다시 그리지 않는다 —
        // 저장 한 번이 알림 여러 개를 내므로, 그때마다 덮으면 타이핑이 씹힌다.
        session.Changed += (_, _) => UiGuard.Run(_session, "대본 탭 갱신", Rebuild);

        Rebuild();
    }

    /// <summary>
    /// 고를 수 있는 챕터 = <b>프로젝트의 판</b>이다.
    ///
    /// ⚠ 워크북을 읽지 않는다. 뒤집기 뒤로 원본은 프로젝트이고(R-D), 여기서 엑셀을 다시
    /// 열면 "임포트 뒤 다시 읽지 않는다"(지시서 §5.2)가 그 자리에서 깨진다. 아직 안
    /// 들여온 챕터가 비어 보이는 것은 <b>옳다</b> — [챕터 그래프]의 [대본 가져오기]가
    /// 그것을 채우는 자리다.
    /// </summary>
    private void Rebuild()
    {
        if (_session is null || ScriptBox.IsFocused)
        {
            return;
        }

        object? keep = ChapterList.SelectedItem;

        ChapterList.ItemsSource = _session.Project.Files
            .Select(file => file.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        ChapterList.SelectedItem = keep;

        if (ChapterList.SelectedItem is null && ChapterList.ItemCount > 0)
        {
            ChapterList.SelectedIndex = 0;
        }

        RebuildEpisodes();
    }

    private void RebuildEpisodes()
    {
        if (_session is null || ScriptBox.IsFocused)
        {
            return;
        }

        object? keep = EpisodeList.SelectedItem;

        EpisodeList.ItemsSource = DialogueNodes()
            .Select(node => node.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        EpisodeList.SelectedItem = keep;

        if (EpisodeList.SelectedItem is null && EpisodeList.ItemCount > 0)
        {
            EpisodeList.SelectedIndex = 0;
        }

        ShowSelected();
    }

    /// <summary>
    /// 고른 판의 대사노드들. ⛔ <b>조건 공급 노드는 빼지 않아도 된다</b> — 그것은
    /// <c>SetNode</c>라 여기 애초에 안 들어온다. 작가에게 보일 것은 대사뿐이다.
    /// </summary>
    private IEnumerable<DialogueNode> DialogueNodes()
    {
        if (_session is null || ChapterList.SelectedItem is not string chapterId)
        {
            return [];
        }

        return _session.Project.Files
            .FirstOrDefault(file => string.Equals(file.Name, chapterId, StringComparison.Ordinal))
            ?.Nodes.OfType<DialogueNode>()
            ?? [];
    }

    private DialogueNode? SelectedNode() =>
        EpisodeList.SelectedItem is string name
            ? DialogueNodes().FirstOrDefault(node =>
                string.Equals(node.Name, name, StringComparison.Ordinal))
            : null;

    private void ShowSelected()
    {
        _pendingDeleteText = null;
        ProblemsText.IsVisible = false;

        if (_session is null || SelectedNode() is not { } node)
        {
            HeaderText.Text = ChapterList.ItemCount == 0
                ? "아직 판이 없습니다 — [챕터 그래프]에서 챕터를 고르면 그 판이 섭니다."
                : "에피소드를 고르세요.";

            ScriptBox.Text = string.Empty;
            ScriptBox.IsEnabled = false;
            ApplyButton.IsEnabled = false;
            HintText.Text = string.Empty;
            return;
        }

        HeaderText.Text = node.Name;
        ScriptBox.IsEnabled = true;
        ApplyButton.IsEnabled = true;

        // ⚠ `ScenarioOnly`는 `includeLineId: false`다 (§6.3) — 화면에 `#line:` 태그가
        //    보이면 지저분하고 작가가 지운다. 신원은 diff가 붙들므로 안 보여도 안전하다.
        ScriptBox.Text = DocumentPreviewFormatter.Format(WorkingDialoguePreview.ComposePreset(
            _session.Project, node.Id, OutputPresetCatalog.ScenarioOnly, _session.Definition));

        HintText.Text = "고친 뒤 [글 반영] — 줄의 신원은 보존됩니다.";
    }

    /// <summary>
    /// 쓴 글을 대사로 반영한다.
    ///
    /// ⚠ <b>지우기는 두 번 눌러야 한다.</b> 붙여넣기 한 번이 줄을 통째로 날릴 수 있고,
    /// 그 줄에는 연출이 매달려 있다 — 같은 글로 한 번 더 누르면 그때 지운다
    /// (연출 그래프의 [텍스트 반영]과 같은 규율이다).
    /// </summary>
    private void Apply()
    {
        if (_session is null || SelectedNode() is not { } node)
        {
            return;
        }

        string text = ScriptBox.Text ?? string.Empty;
        bool confirmDeletes = string.Equals(_pendingDeleteText, text, StringComparison.Ordinal);

        ScenarioPasteOutcome outcome = _session.Editor.ApplyScenarioText(
            node.Id, text, _session.Definition, confirmDeletes);

        ProblemsText.IsVisible = outcome.Problems.Count > 0;
        ProblemsText.Text = string.Join(
            Environment.NewLine, outcome.Problems.Select(problem => $"• {problem}"));

        if (outcome.NeedsDeleteConfirmation && !confirmDeletes)
        {
            _pendingDeleteText = text;
            _session.SetStatus(
                $"{outcome.Summary()} — 같은 글로 [글 반영]을 한 번 더 누르면 지웁니다.");
            return;
        }

        _pendingDeleteText = null;
        _session.SetStatus(outcome.Applied ? $"글을 반영했습니다. {outcome.Summary()}" : outcome.Summary());
    }
}
