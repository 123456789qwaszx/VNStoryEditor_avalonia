using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
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

    /// <summary>[화자 ▾]가 여는 목록의 몸통. 열 때마다 다시 채운다 — 등록부는 변한다.</summary>
    private readonly StackPanel _speakerMenu = new();

    /// <summary>
    /// 지금 화자 목록에 선 이름들 — <b>테스트의 손잡이</b>다(챕터 그래프의
    /// <c>ChapterAddCenterButton</c>과 같은 뜻). 플라이아웃의 팝업은 창 밖에 살아
    /// 나무를 타고 내려가 찾을 수 없다.
    /// </summary>
    internal IReadOnlyList<Button> SpeakerMenuItems =>
        _speakerMenu.Children.OfType<Button>().ToList();

    public ScriptView()
    {
        InitializeComponent();

        ChapterList.SelectionChanged += (_, _) => UiGuard.Run(_session, "챕터 고르기", RebuildEpisodes);
        EpisodeList.SelectionChanged += (_, _) => UiGuard.Run(_session, "에피소드 고르기", ShowSelected);
        ApplyButton.Click += (_, _) => UiGuard.Run(_session, "글 반영", Apply);
        EmptyAddScriptButton.Click += (_, _) => UiGuard.Run(_session, "대본 세우기", AddScript);
        SpeakerButton.Click += (_, _) => UiGuard.Run(_session, "화자 고르기", PickSpeaker);
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

    /// <summary>
    /// 지금 고른 것에 맞춰 화면을 세운다 — 그리고 <b>고를 것이 없으면 만들 자리를 세운다</b>.
    ///
    /// 빈 자리의 사다리는 챕터 그래프가 2026-08-26에 세운 규율을 잇는다
    /// (새 프로젝트 → ＋챕터 → ＋에피소드 → <b>＋대본</b>). 규율은 둘이다:
    /// ① <b>한 번에 한 칸만</b> 선다 — 앞 칸을 안 밟았는데 뒤 칸이 보이면 오히려 헷갈린다.
    /// ② 앞의 두 칸은 여기서 <b>누르게 하지 않는다</b> — 챕터·에피소드의 주인은 기획자의
    ///    엑셀이고 그 단추는 [챕터 그래프]에 이미 있다. 두 자리에 같은 단추가 서면 어느
    ///    쪽이 진짜인지 묻게 된다. 여기서는 어디로 가야 하는지만 가리킨다.
    /// </summary>
    private void ShowSelected()
    {
        _pendingDeleteText = null;
        ProblemsText.IsVisible = false;
        UnknownSpeakerText.IsVisible = false;
        EmptyPanel.IsVisible = false;
        EmptyAddScriptButton.IsVisible = false;

        if (_session is null || EpisodeList.SelectedItem is not string episodeId)
        {
            bool noChapter = ChapterList.ItemCount == 0;

            HeaderText.Text = noChapter ? "아직 챕터가 없습니다." : "에피소드를 고르세요.";

            EmptyPanel.IsVisible = true;
            EmptyText.Text = noChapter
                ? "아직 챕터가 없습니다.\n[챕터 그래프]에서 챕터를 세우면 여기에 글 쓸 자리가 생깁니다."
                : ChapterList.SelectedItem is null
                    ? "왼쪽에서 챕터를 고르세요."
                    : "이 챕터에는 아직 에피소드가 없습니다.\n" +
                      "[챕터 그래프]에서 첫 에피소드를 세우면 여기에 섭니다.";

            ScriptBox.Text = string.Empty;
            ScriptBox.IsEnabled = false;
            ApplyButton.IsEnabled = false;
            SpeakerButton.IsEnabled = false;
            HintText.Text = string.Empty;
            return;
        }

        HeaderText.Text = episodeId;
        ScriptBox.IsEnabled = true;
        ApplyButton.IsEnabled = true;
        SpeakerButton.IsEnabled = true;

        // 사다리의 마지막 칸 — 에피소드는 있는데 아직 아무도 안 썼다.
        if (FindNode(episodeId) is not { } node)
        {
            ScriptBox.Text = string.Empty;
            ApplyButton.IsEnabled = false;
            SpeakerButton.IsEnabled = false;

            EmptyPanel.IsVisible = true;
            EmptyAddScriptButton.IsVisible = true;
            EmptyText.Text = $"'{episodeId}'은 아직 빈 대본입니다.";

            HintText.Text = string.Empty;
            return;
        }

        // ⚠ `ScenarioOnly`는 `includeLineId: false`다 (§6.3) — 화면에 `#line:` 태그가
        //    보이면 지저분하고 작가가 지운다. 신원은 diff가 붙들므로 안 보여도 안전하다.
        ScriptBox.Text = DocumentPreviewFormatter.Format(WorkingDialoguePreview.ComposePreset(
            _session.Project, node.Id, OutputPresetCatalog.ScenarioOnly, _session.Definition));

        HintText.Text = "고친 뒤 [글 반영] — 줄의 신원은 보존됩니다.";

        ShowUnknownSpeakers(Speakers(node));
    }

    /// <summary>그 노드가 실제로 쓰고 있는 화자명들 — 프로젝트가 원본이라 글이 아니라 줄에서 센다.</summary>
    private IEnumerable<string> Speakers(DialogueNode node)
    {
        if (_session is null ||
            node.ScriptId is not { } scriptId ||
            _session.Project.FindScript(scriptId) is not { } script)
        {
            return [];
        }

        ScriptLocale primary = script.Locales.Single(
            locale => string.Equals(locale.Locale, script.PrimaryLocale, StringComparison.Ordinal));

        return script.ActiveLines.Select(line => primary.Find(line.Id).Speaker ?? string.Empty);
    }

    /// <summary>
    /// 등록부에 없는 화자를 <b>짚어만 준다</b> (§6.2: "미등록 이름은 오류가 아니라 표시 대상").
    ///
    /// ⚠ 막지 않는 이유 — 작가가 등록부보다 앞서 쓰는 것은 정상이고, 등록은 기획자의 일이다.
    /// 그래도 조용히 넘어가지 않는 이유 — 미등록은 <b>초상화가 안 붙는다</b>는 뜻이고,
    /// 그것을 알아채는 자리가 여기가 아니면 프리뷰에서 얼굴이 빈 것을 보고서야 안다.
    ///
    /// ⚠ 비어 있는 화자는 <b>지문</b>이라 세지 않는다.
    /// </summary>
    private void ShowUnknownSpeakers(IEnumerable<string> speakers)
    {
        if (_session is null)
        {
            return;
        }

        List<string> unknown = speakers
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Where(name => _session.Definition.FindSpeakerCharacterId(name) is null)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        UnknownSpeakerText.IsVisible = unknown.Count > 0;
        UnknownSpeakerText.Text = unknown.Count == 0
            ? string.Empty
            : $"등록부에 없는 화자: {string.Join(", ", unknown)} — 오류는 아닙니다. " +
              "[챕터 그래프]의 [화자]에서 등록하면 초상화가 붙습니다.";
    }

    /// <summary>
    /// <b>화자 드롭다운</b> (§6.2). 지금 커서가 선 줄의 앞에 등록된 이름을 붙인다.
    ///
    /// ⚠ 칸이 아니라 <b>글</b>을 고치는 화면이라 콤보박스가 설 자리가 없다 — 대신
    /// 연출 그래프의 화자 고르기와 같은 <b>플라이아웃</b>이고, 원천도 같은 등록부다.
    /// </summary>
    private void PickSpeaker()
    {
        if (_session is null)
        {
            return;
        }

        List<string> candidates = _session.Definition.Speakers
            .Select(speaker => speaker.Name)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (candidates.Count == 0)
        {
            _session.SetStatus(
                "등록된 화자가 없습니다 — [챕터 그래프]의 [화자]에서 더하면 여기 목록에 옵니다. " +
                "그때까지는 '이름: 대사'로 직접 쓰면 됩니다.");
            return;
        }

        _speakerMenu.Children.Clear();

        var flyout = new Flyout
        {
            Content = new ScrollViewer { MaxHeight = 260, Content = _speakerMenu },
            Placement = PlacementMode.Top
        };

        foreach (string name in candidates)
        {
            var item = new Button
            {
                Content = name,
                FontSize = 11,
                Padding = new Thickness(10, 4),
                MinWidth = 140,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent
            };

            item.Click += (_, _) =>
            {
                flyout.Hide();
                UiGuard.Run(_session, "화자 붙이기", () => SetSpeakerOnCaretLine(name));
            };

            _speakerMenu.Children.Add(item);
        }

        flyout.ShowAt(SpeakerButton);
    }

    /// <summary>
    /// 커서가 선 줄의 화자를 <paramref name="name"/>으로 만든다.
    ///
    /// ⚠ 이미 화자가 적힌 줄이면 <b>갈아 끼운다</b> — 앞에 붙이기만 하면 "라루: 윌로: …"가
    /// 되고, 그것은 파서에게 화자 '라루'에 내용 "윌로: …"인 한 줄이다(조용히 망가진다).
    /// 무엇이 화자인지는 파서의 규칙과 같아야 한다: <b>첫 콜론 앞, 공백 없음</b>
    /// (공백 있는 등록명은 예외 — <c>ScenarioTextParser.SplitSpeaker</c>).
    /// </summary>
    private void SetSpeakerOnCaretLine(string name)
    {
        string text = ScriptBox.Text ?? string.Empty;
        int caret = Math.Clamp(ScriptBox.CaretIndex, 0, text.Length);

        int start = text.LastIndexOf('\n', Math.Max(caret - 1, 0)) + 1;

        if (caret == 0)
        {
            start = 0;
        }

        int end = text.IndexOf('\n', start);
        end = end < 0 ? text.Length : end;

        string line = text[start..end];
        string body = SplitBody(line);

        string replacement = $"{name}: {body}";

        ScriptBox.Text = text[..start] + replacement + text[end..];
        ScriptBox.CaretIndex = start + replacement.Length;
        ScriptBox.Focus();
    }

    /// <summary>화자 접두를 뗀 나머지. 접두가 없으면 줄 그대로다.</summary>
    private string SplitBody(string line)
    {
        int colon = line.IndexOf(':');

        if (colon <= 0)
        {
            return line;
        }

        string prefix = line[..colon];

        // 파서와 같은 규칙 — 접두에 공백이 있으면 산문이라 화자가 아니다. 등록된 이름과
        // 정확히 같을 때만 공백 있는 접두도 이름으로 본다.
        if (prefix.Any(char.IsWhiteSpace) &&
            _session?.Definition.Speakers.Any(speaker =>
                string.Equals(speaker.Name, prefix, StringComparison.Ordinal)) != true)
        {
            return line;
        }

        return line[(colon + 1)..].TrimStart();
    }

    /// <summary>
    /// 사다리의 마지막 칸 — <b>[＋ 대본]</b>. 이 에피소드의 대사노드를 세우고 커서를 넣는다.
    ///
    /// ⚠ 빈 노드가 생기는 것은 여기뿐이고, 그래도 되는 이유는 <b>사람이 눌렀기</b> 때문이다.
    /// 훑어보기만으로 빈 노드가 쌓이면 안 된다는 규율(<see cref="Apply"/>)은 그대로다.
    /// </summary>
    private void AddScript()
    {
        if (_session is null || EpisodeList.SelectedItem is not string episodeId)
        {
            return;
        }

        if (FindNode(episodeId) is null)
        {
            CreateNodeFor(episodeId);
        }

        ShowSelected();

        // 다음 할 일은 쓰는 것이다 — 커서를 옮겨 주지 않으면 한 번 더 눌러야 한다.
        ScriptBox.Focus();

        _session.SetStatus($"'{episodeId}' 대본을 세웠습니다 — 쓰고 [글 반영]을 누르세요.");
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

        // ⛔ <b>작가가 글을 쓰면 노드가 생긴다</b> (§6.2). 보통은 [＋ 대본]이 먼저 세우지만
        //    여기에도 길이 있어야 한다: 글을 쓰는 동안에는 화면을 다시 그리지 않으므로
        //    (<see cref="Rebuild"/>), 타이핑 중에 노드가 다른 탭에서 사라지면 사다리가
        //    올라오지 못한다. 그때 쓰던 글을 잃지 않는 자리가 이 갈래다.
        //    ⚠ 빈 글로는 만들지 않는다 — 잘못 누른 것까지 판에 남기지 않는다.
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

        // 방금 쓴 이름이야말로 미등록이기 쉽다 — 파서가 이미 가려 놓은 것을 그대로 쓴다.
        ShowUnknownSpeakers(outcome.Parsed.Lines
            .Where(line => line.SpeakerUnregistered)
            .Select(line => line.Speaker));

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
