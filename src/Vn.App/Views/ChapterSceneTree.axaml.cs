using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Vn.App.Services;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;

namespace Vn.App.Views;

/// <summary>트리 한 줄의 종류 — 고를 수 있는 것은 <see cref="Episode"/>뿐이다.</summary>
internal enum SceneTreeRowKind
{
    Chapter,
    Scene,
    Episode
}

/// <summary>
/// 트리에 보이는 줄 하나. <b>검증의 손잡이</b>이기도 하다 — 화면 없이 구조를 재려면
/// 그려진 컨트롤이 아니라 이 목록을 봐야 한다.
/// </summary>
/// <param name="Key">
/// 그 줄의 <b>신원</b> — 접힘을 기억하고(챕터·장면) 키보드 커서가 다시 그린 뒤에도 같은 줄을
/// 찾는 자리다. 줄 번호가 아니라 열쇠인 이유: 편집 한 번이 판을 다시 그리고 그때 줄이
/// 늘거나 준다.
/// </param>
internal sealed record SceneTreeRow(
    SceneTreeRowKind Kind,
    string Key,
    string ChapterId,
    string? SceneId,
    string? EpisodeId,
    string Text,
    int Depth,
    bool IsSceneRoot,
    bool HasSplitEntry,
    bool IsEmptyScript);

/// <summary>고른 에피소드. <b>챕터는 상태가 아니라 파생</b>이다 — 그 에피소드가 속한 챕터다.</summary>
internal sealed record ChapterEpisodePick(string ChapterId, string EpisodeId);

/// <summary>
/// <b>Chapter → Scene → Episode 탐색기</b> (R6 S-2 · 2026-09-16,
/// <c>docs/plans/R6-explorer.md</c>).
///
/// ⛔ <b>장면은 여기서도 엔티티가 아니다.</b> 마디는 <see cref="ChapterSceneGrouping"/>이
/// 그릴 때마다 만드는 투영이고, 이 컨트롤이 들고 있는 상태는 <b>접힘과 선택 둘뿐</b>이다.
/// 마디에 값을 붙이고 싶어지면 그것은 `Episode.SceneId`에 적을 값이다.
///
/// ⚠ <b>고를 수 있는 것은 에피소드뿐이다</b>(규격 §1). 챕터·장면 줄은 담는 자리라
/// 누르면 접거나 편다 — 그래서 <c>TreeView</c>를 안 쓴다(§axaml 주석).
///
/// ⚠ <b>다시 그려도 접힘이 안 풀린다</b>(규격 §4). 편집 한 번이 판을 다시 그리는데 그때마다
/// 펴지면 접는 행위 자체가 뜻을 잃는다.
/// </summary>
public partial class ChapterSceneTree : UserControl
{
    /// <summary>사람이 <b>편</b> 마디들.</summary>
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);

    /// <summary>
    /// 사람이 <b>접은</b> 마디들.
    ///
    /// ⛔ <b>편 것과 접은 것을 따로 든다.</b> 펼침만 기억하면 "고른 에피소드가 든 마디는
    /// 저절로 펴진다"(규격 §4)가 <b>사람의 접기를 이겨</b> 접을 수가 없다 — 눌러도 그 자리에서
    /// 다시 펴진다. 저절로 펴지는 것은 <b>기본값</b>이지 강제가 아니다.
    /// (2026-09-16에 테스트가 잡았다: `접은_것은_다시_그려도_안_펴진다`.)
    /// </summary>
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);

    private readonly List<SceneTreeRow> _rows = [];

    private StoryProject? _project;
    private Func<string, string, bool>? _hasScript;

    /// <summary>접힘을 기억해 둘 프로젝트. 없으면 <b>세션 안에서만</b> 산다.</summary>
    private string? _projectPath;

    public ChapterSceneTree() => InitializeComponent();

    /// <summary>에피소드를 골랐다. 챕터는 파생이므로 함께 실어 보낸다.</summary>
    internal event Action<ChapterEpisodePick>? EpisodeSelected;

    internal ChapterEpisodePick? Selection { get; private set; }

    /// <summary>지금 보이는 줄들 — 검증이 구조를 재는 자리.</summary>
    internal IReadOnlyList<SceneTreeRow> Rows => _rows;

    /// <summary>키보드가 짚고 있는 줄. 아직 아무도 키를 안 눌렀으면 없다.</summary>
    internal SceneTreeRow? CursorRow => _cursorKey is null
        ? null
        : _rows.FirstOrDefault(row => string.Equals(row.Key, _cursorKey, StringComparison.Ordinal));

    /// <summary>
    /// 프로젝트를 읽어 트리를 세운다.
    /// </summary>
    /// <param name="hasScript">
    /// (챕터, 에피소드) → 대본이 있는가. 없는 에피소드는 흐리게 선다 — 아직 아무도 안 쓴
    /// 자리라는 것이 <b>목록에서</b> 보여야 작가가 어디부터 쓸지 정한다.
    /// </param>
    internal void Rebuild(StoryProject? project, Func<string, string, bool> hasScript)
    {
        _project = project;
        _hasScript = hasScript;

        // 고른 에피소드가 사라졌으면 선택도 놓는다 — 없는 것을 고른 채로 두면 오른쪽 화면이
        // 빈 것인지 고장인지 사람이 못 가린다.
        if (Selection is { } pick && Find(pick) is null)
        {
            Selection = null;
        }

        Draw();
    }

    /// <summary>
    /// 이 프로젝트의 접힘을 <b>이어 쓴다</b> (R6 S-4 · 규격 §4).
    ///
    /// 프로젝트가 바뀌면 들고 있던 접힘을 놓고 새로 읽는다 — 앞 프로젝트의 챕터 이름으로 만든
    /// 열쇠가 남아 있으면 이름이 같은 다른 챕터가 엉뚱하게 접힌다.
    ///
    /// ⚠ 같은 프로젝트면 <b>아무것도 안 한다</b>. 이 자리는 다시 그릴 때마다 지나가므로
    /// (편집 한 번이 판을 다시 그린다) 매번 파일을 읽으면 안 된다.
    /// </summary>
    internal void Remember(string? projectPath)
    {
        if (string.Equals(_projectPath, projectPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _projectPath = projectPath;
        _expanded.Clear();
        _collapsed.Clear();

        if (projectPath is null)
        {
            return;
        }

        (IReadOnlyList<string> expanded, IReadOnlyList<string> collapsed) =
            AppSettingsService.LoadExplorerState(projectPath);

        _expanded.UnionWith(expanded);
        _collapsed.UnionWith(collapsed);
    }

    /// <summary>밖에서 고르게 한다(복원·이어 고르기). 가는 길의 마디를 함께 편다.</summary>
    internal void Select(string chapterId, string episodeId)
    {
        Selection = new ChapterEpisodePick(chapterId, episodeId);
        Draw();
    }

    // ── 줄 세우기 ───────────────────────────────────────────────────────────

    private void Draw()
    {
        _rows.Clear();
        RowHost.Children.Clear();

        if (_project is null)
        {
            return;
        }

        foreach (ChapterDocument chapter in _project.Chapters
                     .OrderBy(item => item.ChapterId, StringComparer.OrdinalIgnoreCase))
        {
            DrawChapter(chapter);
        }

        // 커서가 접힌 자리로 사라졌으면 끌어온다 — 그래야 다음 ↑↓가 보이는 자리에서 뜬다.
        // ⚠ <b>줄을 세우기 전에</b> 한다: 그려 놓고 옮기면 그 한 번은 테두리가 딴 줄에 선다.
        // ⚠ 아직 아무도 키를 안 눌렀으면(_cursorKey가 null) 그대로 둔다 — 안 쓰는 테두리가
        //   첫 줄에 늘 떠 있으면 그것대로 노이즈다.
        if (_cursorKey is not null && _rows.Count > 0)
        {
            _cursorKey = _rows[CursorIndex()].Key;
        }

        foreach (SceneTreeRow row in _rows)
        {
            RowHost.Children.Add(Build(row));
        }

        if (_cursorKey is not null && _rows.Count > 0)
        {
            RowHost.Children[CursorIndex()].BringIntoView();
        }
    }

    private void DrawChapter(ChapterDocument chapter)
    {
        IReadOnlyList<ChapterScene> scenes = ChapterSceneGrouping.Of(
            chapter.ToGraphModel(chapter.ChapterId + ".xlsx"));

        // ⛔ <b>장면ID를 하나도 안 적은 챕터는 장면 단을 생략한다</b> (규격 §2). 안 그러면
        //    구판 프로젝트에서 에피소드 수만큼 장면 마디가 생겨 트리가 통째로 노이즈가 된다.
        //    하나라도 적혀 있으면 전부 장면 단으로 본다 — 섞으면 한 화면에 두 규칙이 선다.
        bool flat = scenes.All(scene => scene.IsDefault);

        string key = "ch:" + chapter.ChapterId;

        _rows.Add(new SceneTreeRow(
            SceneTreeRowKind.Chapter,
            key,
            chapter.ChapterId,
            SceneId: null,
            EpisodeId: null,
            flat && scenes.Count > 0
                ? chapter.ChapterId + "   장면 미지정 — 에피소드마다 제 장면입니다"
                : chapter.ChapterId,
            Depth: 0,
            IsSceneRoot: false,
            HasSplitEntry: false,
            IsEmptyScript: false));

        if (!IsExpanded(key, chapter.ChapterId, episodeId: null))
        {
            return;
        }

        foreach (ChapterScene scene in scenes)
        {
            if (flat)
            {
                foreach (ChapterEpisode episode in scene.Episodes)
                {
                    AddEpisode(chapter, scene, episode, depth: 1);
                }

                continue;
            }

            string sceneKey = "sc:" + chapter.ChapterId + "/" + scene.SceneId;

            _rows.Add(new SceneTreeRow(
                SceneTreeRowKind.Scene,
                sceneKey,
                chapter.ChapterId,
                scene.SceneId,
                EpisodeId: null,
                scene.DisplayName,
                Depth: 1,
                IsSceneRoot: false,
                scene.HasSplitEntry,
                IsEmptyScript: false));

            if (!IsExpanded(sceneKey, chapter.ChapterId, SelectedEpisodeIn(scene)))
            {
                continue;
            }

            foreach (ChapterEpisode episode in scene.Episodes)
            {
                AddEpisode(chapter, scene, episode, depth: 2);
            }
        }
    }

    private void AddEpisode(
        ChapterDocument chapter, ChapterScene scene, ChapterEpisode episode, int depth) =>
        _rows.Add(new SceneTreeRow(
            SceneTreeRowKind.Episode,
            "ep:" + chapter.ChapterId + "/" + episode.EpisodeId,
            chapter.ChapterId,
            scene.SceneId,
            episode.EpisodeId,
            episode.EpisodeId,
            depth,
            string.Equals(scene.RootEpisodeId, episode.EpisodeId, StringComparison.Ordinal),
            HasSplitEntry: false,
            _hasScript?.Invoke(chapter.ChapterId, episode.EpisodeId) != true));

    /// <summary>
    /// 이 마디가 펴져 있는가 — 사람이 편 것이거나, <b>고른 에피소드가 그 안에 있을 때</b>다.
    ///
    /// ⚠ 후자가 규격 §4의 "처음 열면 고른 에피소드가 있는 챕터·장면만 펼침"이다. 챕터 수십
    /// 개가 전부 펼쳐져 있으면 탐색기가 아니라 목록이다.
    /// </summary>
    private bool IsExpanded(string key, string chapterId, string? episodeId) =>
        !_collapsed.Contains(key) &&
        (_expanded.Contains(key) ||
         (Selection is { } pick &&
          string.Equals(pick.ChapterId, chapterId, StringComparison.Ordinal) &&
          (episodeId is null || string.Equals(pick.EpisodeId, episodeId, StringComparison.Ordinal))));

    /// <summary>고른 에피소드가 이 장면 안에 있으면 그 Id — 장면 마디를 펼 근거다.</summary>
    private string? SelectedEpisodeIn(ChapterScene scene) =>
        Selection is { } pick &&
        scene.Episodes.Any(episode =>
            string.Equals(episode.EpisodeId, pick.EpisodeId, StringComparison.Ordinal))
            ? pick.EpisodeId
            : null;

    private ChapterEpisode? Find(ChapterEpisodePick pick) =>
        _project?.Chapters
            .FirstOrDefault(chapter =>
                string.Equals(chapter.ChapterId, pick.ChapterId, StringComparison.Ordinal))
            ?.Episodes.FirstOrDefault(episode =>
                string.Equals(episode.EpisodeId, pick.EpisodeId, StringComparison.Ordinal));

    // ── 그리기 ──────────────────────────────────────────────────────────────

    private Control Build(SceneTreeRow row)
    {
        bool selected = row.Kind == SceneTreeRowKind.Episode &&
                        Selection is { } pick &&
                        string.Equals(pick.EpisodeId, row.EpisodeId, StringComparison.Ordinal) &&
                        string.Equals(pick.ChapterId, row.ChapterId, StringComparison.Ordinal);

        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            Margin = new Thickness(row.Depth * 14, 0, 0, 0)
        };

        // 접기 표식 — 에피소드는 담는 것이 없어 자리만 비운다(줄이 들쭉날쭉하지 않게).
        line.Children.Add(new TextBlock
        {
            Text = row.Kind == SceneTreeRowKind.Episode
                ? " "
                : IsExpanded(row.Key, row.ChapterId, null) ? "▾" : "▸",
            FontSize = 9,
            Width = 10,
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (row.IsSceneRoot)
        {
            // ⌂ = 장면 루트. 롤백이 되돌아갈 곳이고 이어하기가 재개할 곳이라 장면에 하나다.
            line.Children.Add(new TextBlock
            {
                Text = "⌂",
                FontSize = 9,
                Opacity = 0.75,
                VerticalAlignment = VerticalAlignment.Center,
                [ToolTip.TipProperty] = "장면 루트 — 롤백이 되돌아가고 이어하기가 재개하는 자리입니다."
            });
        }

        line.Children.Add(new TextBlock
        {
            Text = row.Text,
            FontSize = 11,
            FontWeight = row.Kind == SceneTreeRowKind.Chapter ? FontWeight.SemiBold : FontWeight.Normal,
            // 대본이 없는 에피소드는 흐리게 — 아직 아무도 안 쓴 자리다.
            Opacity = row.IsEmptyScript ? 0.45 : 1,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (row.HasSplitEntry)
        {
            line.Children.Add(new TextBlock
            {
                Text = "⚠",
                FontSize = 10,
                Foreground = Brushes.DarkOrange,
                VerticalAlignment = VerticalAlignment.Center,
                [ToolTip.TipProperty] =
                    "밖에서 들어오는 자리가 둘입니다 — 장면은 한 자리에서만 시작해야 합니다. " +
                    "롤백이 되돌아갈 곳과 이어하기가 재개할 곳이 그 자리입니다."
            });
        }

        // 커서는 선택과 <b>다르게</b> 보여야 한다 — 훑는 중인 자리와 열어 둔 글은 다른 것이다.
        bool cursor = string.Equals(row.Key, _cursorKey, StringComparison.Ordinal);

        var button = new Button
        {
            Content = line,
            Background = selected ? new SolidColorBrush(Color.FromArgb(40, 61, 123, 217)) : Brushes.Transparent,
            BorderThickness = new Thickness(cursor ? 1 : 0),
            BorderBrush = cursor ? new SolidColorBrush(Color.FromArgb(150, 61, 123, 217)) : Brushes.Transparent,
            Padding = new Thickness(4, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,

            // 키는 트리가 받는다 — 줄마다 포커스를 두면 Avalonia의 기본 방향 이동이 먼저
            // 먹어 규격 §7의 ←/→(접기·펼치기)가 설 자리가 없다.
            Focusable = false
        };

        button.Click += (_, _) => UiGuard.Run(null, "탐색기", () =>
        {
            Focus();
            Press(row);
        });

        return button;
    }

    /// <summary>줄을 누르면 — 에피소드는 고르고, 담는 줄은 접거나 편다 (규격 §1).</summary>
    private void Press(SceneTreeRow row)
    {
        _cursorKey = row.Key;

        if (row.Kind == SceneTreeRowKind.Episode)
        {
            Choose(row);
            return;
        }

        // 지금 보이는 대로 뒤집는다 — 저절로 펴진 마디를 누르면 접히고, 그 접힘이 남는다.
        Fold(row, collapse: IsExpanded(row.Key, row.ChapterId, null));
    }

    private void Choose(SceneTreeRow row)
    {
        Selection = new ChapterEpisodePick(row.ChapterId, row.EpisodeId!);
        Draw();
        EpisodeSelected?.Invoke(Selection);
    }

    /// <summary>담는 줄 하나를 접거나 편다. <b>기억까지가 한 동작</b>이다(규격 §4).</summary>
    private void Fold(SceneTreeRow row, bool collapse)
    {
        if (row.Kind == SceneTreeRowKind.Episode)
        {
            return;
        }

        if (collapse)
        {
            _expanded.Remove(row.Key);
            _collapsed.Add(row.Key);
        }
        else
        {
            _collapsed.Remove(row.Key);
            _expanded.Add(row.Key);
        }

        if (_projectPath is not null)
        {
            AppSettingsService.SaveExplorerState(_projectPath, _expanded, _collapsed);
        }

        Draw();
    }

    // ── 키보드 (규격 §7) ────────────────────────────────────────────────────

    /// <summary>
    /// <b>커서</b> — 키보드가 짚고 있는 줄. <b>선택과 다른 것</b>이다: ↑↓로 지나가는 것만으로
    /// 오른쪽 글이 바뀌면 훑어볼 수가 없다. 고르는 것은 Enter다(규격 §7).
    /// </summary>
    private string? _cursorKey;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || _rows.Count == 0)
        {
            return;
        }

        int at = CursorIndex();

        switch (e.Key)
        {
            case Key.Up:
                MoveTo(at - 1);
                break;

            case Key.Down:
                MoveTo(at + 1);
                break;

            case Key.Left:
                Leftward(at);
                break;

            case Key.Right:
                Rightward(at);
                break;

            case Key.Enter or Key.Space:
                if (_rows[at].Kind == SceneTreeRowKind.Episode)
                {
                    Choose(_rows[at]);
                }
                else
                {
                    Press(_rows[at]);
                }

                break;

            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>← 접기 · 이미 접혔으면 부모로 (규격 §7).</summary>
    private void Leftward(int at)
    {
        SceneTreeRow row = _rows[at];

        if (row.Kind != SceneTreeRowKind.Episode && IsExpanded(row.Key, row.ChapterId, null))
        {
            _cursorKey = row.Key;
            Fold(row, collapse: true);
            return;
        }

        // 부모 = 뒤로 올라가며 처음 만나는 더 얕은 줄.
        for (int index = at - 1; index >= 0; index--)
        {
            if (_rows[index].Depth < row.Depth)
            {
                MoveTo(index);
                return;
            }
        }
    }

    /// <summary>→ 펼치기 · 이미 펼쳤으면 첫 자식으로 (규격 §7).</summary>
    private void Rightward(int at)
    {
        SceneTreeRow row = _rows[at];

        if (row.Kind != SceneTreeRowKind.Episode && !IsExpanded(row.Key, row.ChapterId, null))
        {
            _cursorKey = row.Key;
            Fold(row, collapse: false);
            return;
        }

        if (at + 1 < _rows.Count && _rows[at + 1].Depth > row.Depth)
        {
            MoveTo(at + 1);
        }
    }

    private void MoveTo(int index)
    {
        if (index < 0 || index >= _rows.Count)
        {
            return;
        }

        _cursorKey = _rows[index].Key;
        Draw();
    }

    /// <summary>
    /// 커서가 짚은 줄의 번호. 그 줄이 사라졌으면(접혀서·지워져서) <b>고른 에피소드</b>로,
    /// 그것도 없으면 첫 줄로 돌아간다 — 커서가 허공에 있으면 ↑↓가 아무 데서나 시작한다.
    /// </summary>
    private int CursorIndex()
    {
        int at = _cursorKey is null
            ? -1
            : _rows.FindIndex(row => string.Equals(row.Key, _cursorKey, StringComparison.Ordinal));

        if (at >= 0)
        {
            return at;
        }

        if (Selection is { } pick)
        {
            at = _rows.FindIndex(row =>
                row.Kind == SceneTreeRowKind.Episode &&
                string.Equals(row.ChapterId, pick.ChapterId, StringComparison.Ordinal) &&
                string.Equals(row.EpisodeId, pick.EpisodeId, StringComparison.Ordinal));
        }

        return at >= 0 ? at : 0;
    }
}
