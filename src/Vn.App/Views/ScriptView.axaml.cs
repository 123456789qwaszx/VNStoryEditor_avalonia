using Avalonia.Controls;
using Vn.App.Services;
using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Script;

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
    /// 고를 수 있는 챕터 = <c>chapters/</c>의 워크북들.
    ///
    /// ⚠ <b>여기서 챕터 워크북을 읽는 것은 §5.2와 어긋나지 않는다.</b> 그 규율이 막는 것은
    /// <b>대본</b>을 다시 읽는 일이고, 챕터 구조(어떤 에피소드가 있는가)의 주인은 아직
    /// 기획자의 엑셀이다 — 그것을 툴로 옮기는 것은 R-F다. 작가에게 "이 챕터에 어떤
    /// 에피소드가 있는가"를 보여 주려면 지금은 그쪽을 봐야 한다.
    ///
    /// ⚠ 목록은 <b>파일 이름만</b> 본다(파싱하지 않는다). 챕터를 고른 그 하나만 연다 —
    /// 전부 파고들면 노드 60개에서 첫 화면이 58초이던 그 값을 다시 치른다(2026-08-18).
    /// </summary>
    private void Rebuild()
    {
        if (_session is null || ScriptBox.IsFocused)
        {
            return;
        }

        object? keep = ChapterList.SelectedItem;

        ChapterList.ItemsSource = ChapterIds();
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

        EpisodeList.ItemsSource = EpisodeIds();
        EpisodeList.SelectedItem = keep;

        if (EpisodeList.SelectedItem is null && EpisodeList.ItemCount > 0)
        {
            EpisodeList.SelectedIndex = 0;
        }

        ShowSelected();
    }

    /// <summary>`chapters/`의 워크북 이름들 — 파싱하지 않는다.</summary>
    private IReadOnlyList<string> ChapterIds()
    {
        if (ChapterLibrary.FolderFor(_session?.ProjectPath) is not { } folder ||
            !Directory.Exists(folder))
        {
            return [];
        }

        return Directory.EnumerateFiles(folder, "*.xlsx")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(name => !name.StartsWith("~$", StringComparison.Ordinal))   // 엑셀 잠금 파일
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 고른 챕터의 에피소드들 — <b>노드가 아직 없는 것도 포함</b>한다.
    ///
    /// ⛔ 그것이 요점이다. 노드가 있는 것만 보이면 <b>아직 아무도 안 쓴 에피소드에는 작가가
    /// 글을 쓸 자리가 없다</b> — 노드를 만드는 것이 곧 글을 쓰는 일인데, 글을 쓰려면 노드가
    /// 있어야 하는 매듭이 된다. 노드는 <see cref="Apply"/>가 만든다(§6.2).
    /// </summary>
    private IReadOnlyList<string> EpisodeIds()
    {
        if (_session is null ||
            ChapterList.SelectedItem is not string chapterId ||
            ChapterLibrary.FolderFor(_session.ProjectPath) is not { } folder)
        {
            return [];
        }

        ChapterEntry entry = ChapterLibrary.Read(
            Path.Combine(folder, chapterId + ".xlsx"), _session.Definition);

        return entry.Model?.Episodes
            .Select(episode => episode.EpisodeId)
            .ToList() ?? [];
    }

    /// <summary>
    /// 고른 에피소드의 대사노드. <b>없을 수 있다</b> — 아직 아무도 안 쓴 에피소드다.
    ///
    /// 이름의 원천은 챕터 `에피소드` 시트의 `대사엔트리`이고, 비어 있으면 EpisodeId다
    /// (임포터와 [＋ 에피소드]가 쓰는 규칙과 같아야 한다 — 아니면 노드가 둘이 된다).
    /// </summary>
    private DialogueNode? FindNode(string episodeId)
    {
        if (_session is null || ChapterList.SelectedItem is not string chapterId)
        {
            return null;
        }

        return _session.Project.Files
            .FirstOrDefault(file => string.Equals(file.Name, chapterId, StringComparison.Ordinal))
            ?.Nodes.OfType<DialogueNode>()
            .FirstOrDefault(node =>
                string.Equals(node.ExcelEpisodeId, episodeId, StringComparison.Ordinal) ||
                string.Equals(node.Name, episodeId, StringComparison.Ordinal));
    }

    private DialogueNode? SelectedNode() =>
        EpisodeList.SelectedItem is string episodeId ? FindNode(episodeId) : null;

    private void ShowSelected()
    {
        _pendingDeleteText = null;
        ProblemsText.IsVisible = false;

        if (_session is null || EpisodeList.SelectedItem is not string episodeId)
        {
            HeaderText.Text = ChapterList.ItemCount == 0
                ? "아직 챕터가 없습니다 — [챕터 그래프]에서 만들 수 있습니다."
                : "에피소드를 고르세요.";

            ScriptBox.Text = string.Empty;
            ScriptBox.IsEnabled = false;
            ApplyButton.IsEnabled = false;
            HintText.Text = string.Empty;
            return;
        }

        HeaderText.Text = episodeId;
        ScriptBox.IsEnabled = true;
        ApplyButton.IsEnabled = true;

        // 아직 아무도 안 쓴 에피소드 — 빈 화면에서 시작한다. 노드는 [글 반영]이 만든다.
        if (FindNode(episodeId) is not { } node)
        {
            ScriptBox.Text = string.Empty;
            HintText.Text = "아직 빈 대본입니다 — 쓰고 [글 반영]을 누르면 이 에피소드가 섭니다.";
            return;
        }

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
        if (_session is null || EpisodeList.SelectedItem is not string episodeId)
        {
            return;
        }

        string text = ScriptBox.Text ?? string.Empty;

        // ⛔ <b>작가가 글을 쓰면 노드가 생긴다</b> (§6.2). 빈 에피소드에 처음 쓰는 순간이
        //    그 노드가 태어나는 순간이다 — 그 전까지 만들지 않는 것은, 훑어보기만 해도
        //    판에 빈 노드가 쌓이면 안 되기 때문이다.
        if (FindNode(episodeId) is not { } node)
        {
            if (text.Trim().Length == 0)
            {
                _session.SetStatus("빈 글은 반영하지 않습니다 — 한 줄이라도 쓰고 눌러 주세요.");
                return;
            }

            node = CreateNodeFor(episodeId);
        }
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

        if (!outcome.Applied)
        {
            _session.SetStatus(outcome.Summary());
            return;
        }

        _session.SetStatus($"글을 반영했습니다. {outcome.Summary()}{EmitWorkbook(node)}");
    }

    /// <summary>
    /// 이 에피소드의 대사노드를 세운다 — 작가가 처음 글을 쓴 그 순간에.
    ///
    /// ⚠ 이름 규칙은 임포터·[＋ 에피소드]와 <b>같아야 한다</b>(대사엔트리, 없으면 EpisodeId).
    /// 어긋나면 같은 에피소드에 노드가 둘이 되고, 내보내기가 "번들 이름이 겹칩니다"로 막는다.
    /// </summary>
    private DialogueNode CreateNodeFor(string episodeId)
    {
        string fileId = _session!.Editor.EnsureChapterBoard((string)ChapterList.SelectedItem!);
        DialogueNode created = _session.Editor.AddDialogueNode(fileId, name: episodeId);

        created.ExcelEpisodeId = episodeId;

        // 노드 생성이 딸려 주는 첫 빈 줄을 은퇴시킨다 — 작가의 글이 그 자리를 채운다.
        // 남겨 두면 신원 없는 고아가 되어 diff가 "지운 것인지 고친 것인지"를 못 가린다.
        foreach (ScriptLine line in _session.Project.FindScript(created.ScriptId)!.ActiveLines.ToList())
        {
            _session.Editor.RetireScriptLine(created.ScriptId!, line.Id);
        }

        return created;
    }

    /// <summary>
    /// 반영한 글을 <b>워크북으로 다시 낸다</b> (§6.2: "저장 = 프로젝트에 저장 + 워크북 재출력").
    ///
    /// ⛔ <b>여기가 뒤집기의 도착점이다</b> — 툴에 쓴 것이 엑셀을 채운다.
    ///
    /// ⚠ 못 냈다고 편집을 되돌리지 않는다 (§5.3). 원본은 프로젝트라 글은 이미 안전하고,
    /// 못 한 것은 <b>그 파일을 지금 갱신하는 일</b>뿐이다. 잠금의 뜻이 "막는다"에서
    /// "미뤘다"로 바뀐 것이 이 자리다.
    /// </summary>
    /// <returns>상태줄에 덧붙일 말. 잘 났으면 빈 문자열이다 — 잘된 일은 조용하다.</returns>
    private string EmitWorkbook(DialogueNode node)
    {
        if (_session is null ||
            ChapterList.SelectedItem is not string chapterId ||
            EpisodeLibrary.FolderFor(_session.ProjectPath, chapterId) is not { } folder)
        {
            return string.Empty;
        }

        // 노드가 어느 대본 파일의 것인지 — 표식이 없으면 이름이 곧 에피소드 Id다.
        string episodeId = node.ExcelEpisodeId is { Length: > 0 } marked ? marked : node.Name;

        ChapterWriteResult written = EpisodeScriptOutput.Write(
            _session.Project, node, EpisodeLibrary.PathFor(folder, episodeId));

        return written.Written ? string.Empty : $" ⚠ {written.Failure}";
    }
}
