using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
/// <param name="IsDraft">
/// <b>아직 아무 에피소드도 안 든 장면</b>인가 (2026-09-16).
///
/// ⛔ <b>장면은 엔티티가 아니다</b> — 프로젝트에 장면 표가 없고, 장면은 에피소드가
/// <c>SceneId</c>를 들고 있어야 <b>비로소 있다</b>. 그래서 빈 장면은 저장할 자리가 없고,
/// 이 줄은 <b>작업 중인 자리표시</b>다: 에피소드가 하나 들어오면 진짜가 되고, 그 전에
/// 프로젝트를 다시 열면 없다. 그 사실을 줄이 직접 말한다(아래 <c>Text</c>).
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
    bool IsEmptyScript,
    bool IsDraft = false);

/// <summary>
/// 무엇을 어디로 끌어다 놓았다 (2026-09-16 소유자).
/// </summary>
/// <param name="Source">끌어 온 줄 — 장면이거나 에피소드다.</param>
/// <param name="Target">놓은 줄 — 장면을 놓았으면 챕터, 에피소드를 놓았으면 장면이다.</param>
internal sealed record SceneTreeDrop(SceneTreeRow Source, SceneTreeRow Target);

/// <summary>줄을 우클릭했을 때 할 수 있는 일 — <b>하는 것은 이 컨트롤이 아니다</b>.</summary>
internal enum SceneTreeCommand
{
    /// <summary>챕터 줄에서 — 빈 장면 자리를 하나 연다.</summary>
    AddScene,

    /// <summary>챕터 줄에서.</summary>
    DeleteChapter,

    /// <summary>장면 줄에서 — 장면을 걷는다(에피소드는 남는다).</summary>
    DeleteScene,

    /// <summary>
    /// 에피소드를 하나 더한다 — <b>누른 줄이 어디에 붙일지를 정한다</b>: 장면 줄에서는 그
    /// 장면의 끝에, 에피소드 줄에서는 <b>그 뒤에</b>(간선까지 함께).
    /// </summary>
    AddEpisode,

    /// <summary>에피소드 줄에서 — 그 에피소드를 걷는다(2026-09-18 소유자).</summary>
    DeleteEpisode,

    /// <summary>
    /// <b>빈 자리</b>에서 — 챕터를 하나 세운다 (2026-09-18 소유자).
    ///
    /// ⚠ 머리글의 <c>＋</c>와 같은 일이다. 단추 하나로 충분해 보였지만, 트리가 비었을 때
    /// 사람이 먼저 누르는 것은 <b>비어 있는 그 자리</b>다.
    /// </summary>
    AddChapter
}

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

    public ChapterSceneTree()
    {
        InitializeComponent();

        // ⚠ <b>빈 자리의 차림표</b> (2026-09-18 소유자). 줄이 아니라 <see cref="TreeScroll"/>에
        //    단다 — 줄 위에서 우클릭하면 그 줄의 차림표가 먼저 뜨고, 빈 데서 눌렀을 때만
        //    여기까지 올라온다. 챕터가 하나도 없을 때 사람이 먼저 누르는 곳이 그 자리다.
        TreeScroll.ContextMenu = EmptyMenu();
    }

    /// <summary>빈 자리에서 여는 차림표 — 지금은 [챕터 추가] 하나다.</summary>
    private ContextMenu EmptyMenu()
    {
        var add = new MenuItem { Header = "챕터 추가", FontSize = 12 };

        add.Click += (_, _) => UiGuard.Run(null, "탐색기 차림표", () =>
            CommandRequested?.Invoke(
                SceneTreeCommand.AddChapter,
                new SceneTreeRow(
                    SceneTreeRowKind.Chapter, Key: string.Empty, ChapterId: string.Empty,
                    SceneId: null, EpisodeId: null, Text: string.Empty, Depth: 0,
                    IsSceneRoot: false, HasSplitEntry: false, IsEmptyScript: false)));

        return new ContextMenu { ItemsSource = new[] { add } };
    }

    /// <summary>
    /// <b>빈 장면 자리들</b> — <c>{챕터}/{장면}</c>. 에피소드가 들어오면 진짜 장면이 되고
    /// 이 목록에서 저절로 빠진다(<see cref="DraftsOf"/>).
    ///
    /// ⛔ <b>저장하지 않는다.</b> 프로젝트에 장면 표가 없어서가 아니라, 없는 것이 옳아서다
    /// (R6 §3-1) — 장면은 에피소드가 <c>SceneId</c>를 들어야 있는 것이고, 빈 장면을 저장하면
    /// 그 순간 정본이 둘이 된다.
    /// </summary>
    private readonly HashSet<string> _drafts = new(StringComparer.Ordinal);

    /// <summary>에피소드를 골랐다. 챕터는 파생이므로 함께 실어 보낸다.</summary>
    internal event Action<ChapterEpisodePick>? EpisodeSelected;

    /// <summary>
    /// 줄에서 무엇을 하자고 했다 (우클릭 차림표).
    ///
    /// ⛔ <b>이 컨트롤은 하지 않는다.</b> 트리는 투영이고 편집기를 모른다 — 알게 하면
    /// 같은 명령이 화면마다 조금씩 달라진다(두 화면이 이 컨트롤 하나를 쓴다).
    /// </summary>
    internal event Action<SceneTreeCommand, SceneTreeRow>? CommandRequested;

    /// <summary>
    /// 이름을 고쳐 달라고 했다 (줄을 더블클릭해 새 이름을 적고 Enter).
    ///
    /// ⛔ 여기서도 <b>고치지 않는다</b> — 챕터 개명은 워크북·대본 폴더·판 이름을 함께 끌고
    /// 가는 일이고(<c>ChapterRenamer</c>), 에피소드 개명은 간선·픽스처·대사 노드를 함께 끌고
    /// 간다(<c>EpisodeRenamer</c>). 트리가 그것을 알면 규율이 두 벌이 된다.
    /// </summary>
    internal event Action<SceneTreeRow, string>? RenameRequested;

    /// <summary>끌어다 놓았다 — 장면을 챕터에, 또는 에피소드를 장면에.</summary>
    internal event Action<SceneTreeDrop>? Dropped;

    internal ChapterEpisodePick? Selection { get; private set; }

    /// <summary>
    /// 고른 것을 놓는다 — <b>고른 에피소드가 지워졌을 때</b>.
    ///
    /// ⚠ <see cref="Rebuild"/>도 사라진 선택을 놓지만, 그쪽은 <b>다시 그릴 때</b>의 일이고
    /// 부르는 화면이 사정상 다시 그리기를 미룰 수 있다([대본] 탭은 안 저장한 글이 있으면
    /// 안 그린다). 지운 쪽이 직접 놓는 편이 확실하다.
    /// </summary>
    internal void ClearSelection()
    {
        Selection = null;
        Draw();
    }

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

    // ── 빈 장면 자리 ────────────────────────────────────────────────────────

    /// <summary>
    /// 빈 장면 자리를 하나 연다. 겹치지 않는 Id를 골라 돌려준다 — 사람이 나중에 고친다
    /// ([챕터 그래프]의 `장면ID` 칸이 그 자리다. 여기서 안 고치는 이유는 규격 §5).
    /// </summary>
    /// <param name="wanted">
    /// 정해진 이름. 주면 그대로 쓴다(빈 장면의 이름을 고치거나 다른 챕터로 옮길 때).
    /// </param>
    internal string AddDraftScene(string chapterId, string? wanted = null)
    {
        if (wanted is { Length: > 0 })
        {
            _drafts.Add(Draft(chapterId, wanted));
            _expanded.Add("ch:" + chapterId);
            _collapsed.Remove("ch:" + chapterId);
            Draw();

            return wanted;
        }

        var taken = new HashSet<string>(StringComparer.Ordinal);

        if (_project?.Chapters.FirstOrDefault(item =>
                string.Equals(item.ChapterId, chapterId, StringComparison.Ordinal)) is { } chapter)
        {
            foreach (ChapterEpisode episode in chapter.Episodes)
            {
                taken.Add(episode.EffectiveSceneId);
            }
        }

        int number = 1;

        while (taken.Contains($"scene{number:D2}") || _drafts.Contains(Draft(chapterId, $"scene{number:D2}")))
        {
            number++;
        }

        string sceneId = $"scene{number:D2}";

        _drafts.Add(Draft(chapterId, sceneId));
        _expanded.Add("ch:" + chapterId);
        _collapsed.Remove("ch:" + chapterId);
        Draw();

        return sceneId;
    }

    /// <summary>빈 장면 자리를 닫는다 — 에피소드가 없으니 지울 것도 없다.</summary>
    internal void DropDraftScene(string chapterId, string sceneId)
    {
        if (_drafts.Remove(Draft(chapterId, sceneId)))
        {
            Draw();
        }
    }

    private static string Draft(string chapterId, string sceneId) => chapterId + "/" + sceneId;

    /// <summary>
    /// 이 챕터에 남아 있는 빈 장면들. <b>진짜가 된 것은 빠진다</b> — 에피소드가 들어오면
    /// 그 장면은 이제 투영에서 나오므로, 자리표시를 겹쳐 두면 같은 줄이 둘이 선다.
    /// </summary>
    private List<string> DraftsOf(string chapterId, IReadOnlyList<ChapterScene> scenes)
    {
        var real = new HashSet<string>(scenes.Select(scene => scene.SceneId), StringComparer.Ordinal);

        foreach (string key in _drafts.ToList())
        {
            int slash = key.IndexOf('/', StringComparison.Ordinal);

            if (slash > 0 &&
                string.Equals(key[..slash], chapterId, StringComparison.Ordinal) &&
                real.Contains(key[(slash + 1)..]))
            {
                _drafts.Remove(key);
            }
        }

        return _drafts
            .Where(key => key.StartsWith(chapterId + "/", StringComparison.Ordinal))
            .Select(key => key[(chapterId.Length + 1)..])
            .OrderBy(sceneId => sceneId, StringComparer.Ordinal)
            .ToList();
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

        RenameBox = null;

        foreach (SceneTreeRow row in _rows)
        {
            RowHost.Children.Add(
                string.Equals(_renamingKey, row.Key, StringComparison.Ordinal)
                    ? RenameRow(row)
                    : Build(row));
        }

        RenameBox?.Focus();
        RenameBox?.SelectAll();

        Paint();

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
        List<string> drafts = DraftsOf(chapter.ChapterId, scenes);

        // ⚠ 빈 장면 자리가 하나라도 있으면 <b>장면 단을 편다</b>. 안 그러면 방금 만든 장면이
        //   어디에도 안 보이고, 사람은 [장면 추가]가 아무 일도 안 한 줄로 안다.
        bool flat = scenes.All(scene => scene.IsDefault) && drafts.Count == 0;

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

        // 빈 장면 자리는 진짜 장면들 뒤에 — 방금 만든 것이 맨 아래 있는 편이 눈에 띈다.
        foreach (string sceneId in drafts)
        {
            _rows.Add(new SceneTreeRow(
                SceneTreeRowKind.Scene,
                "sc:" + chapter.ChapterId + "/" + sceneId,
                chapter.ChapterId,
                sceneId,
                EpisodeId: null,
                sceneId + "   빈 장면 — 에피소드를 넣어야 저장됩니다",
                Depth: 1,
                IsSceneRoot: false,
                HasSplitEntry: false,
                IsEmptyScript: false,
                IsDraft: true));
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

    /// <summary>
    /// 줄 하나 = <b>[삼각형][이름 단추]</b>.
    ///
    /// ⛔ <b>접기는 삼각형만 한다</b> (2026-09-16 소유자). 줄 전체가 접기 손잡이였을 때
    /// <b>끌기와 이름 고치기가 물리적으로 막혔다</b>: 누를 때마다 접혔다 펴지고, 그때
    /// <see cref="Draw"/>가 줄을 통째로 다시 세우는 바람에 <b>두 번째 누름이 새 컨트롤에
    /// 떨어져</b> 더블클릭이 성립하지 않았다. 손잡이를 삼각형으로 좁히니 이름 쪽은
    /// 끌기·더블클릭만 받는다.
    /// </summary>
    private Control Build(SceneTreeRow row)
    {
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3
        };

        // ⛔ <b>에피소드는 전부 표를 단다</b> (2026-09-18 소유자: *"장면의 첫번째
        //    에피소드에만 있고 그 아래쪽에는 없는 게 불편"*). 전에는 장면 루트(`⌂`)만
        //    표가 있어서 <b>나머지 줄이 왼쪽으로 반 칸 밀려 보였다</b> — 루트를 눈에
        //    띄게 하려던 표가 다른 줄을 들쭉날쭉하게 만든 셈이다.
        //
        // ⚠ 뜻은 그대로다: `⌂`는 여전히 <b>장면 루트</b>이고, 나머지는 자리만 맞추는
        //    점이다. 같은 열에 서되 무게가 다르다.
        if (row.Kind == SceneTreeRowKind.Episode)
        {
            line.Children.Add(new TextBlock
            {
                Text = row.IsSceneRoot ? "⌂" : "·",
                FontSize = row.IsSceneRoot ? 12 : 13,
                Width = 12,
                TextAlignment = TextAlignment.Center,
                Opacity = row.IsSceneRoot ? 0.8 : 0.35,
                VerticalAlignment = VerticalAlignment.Center,
                [ToolTip.TipProperty] = row.IsSceneRoot
                    ? "장면 루트 — 롤백이 되돌아가고 이어하기가 재개하는 자리입니다."
                    : null
            });
        }

        // ⚠ <b>종류가 한눈에 갈려야 한다</b> (2026-09-18 소유자: *"뭐가 뭔지 구분이 안 돼"*).
        //    들여쓰기만으로는 챕터·장면·에피소드가 같은 글자로 보인다 — 크기와 무게를 함께 준다.
        (double size, FontWeight weight, double dim) = row.Kind switch
        {
            SceneTreeRowKind.Chapter => (15.0, FontWeight.Bold, 1.0),
            SceneTreeRowKind.Scene => (13.0, FontWeight.SemiBold, 0.8),
            _ => (13.0, FontWeight.Normal, 1.0)
        };

        line.Children.Add(new TextBlock
        {
            Text = row.Text,
            FontSize = size,
            FontWeight = weight,
            // 대본이 없는 에피소드와 빈 장면은 흐리게 — 아직 아무것도 안 든 자리다.
            Opacity = row.IsEmptyScript || row.IsDraft ? 0.45 : dim,
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

        var button = new Button
        {
            Content = line,
            Padding = new Thickness(4, 3),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,

            // 키는 트리가 받는다 — 줄마다 포커스를 두면 Avalonia의 기본 방향 이동이 먼저
            // 먹어 규격 §7의 ←/→(접기·펼치기)가 설 자리가 없다.
            Focusable = false,
            Tag = row.Key
        };

        button.Click += (_, _) => UiGuard.Run(null, "탐색기", () =>
        {
            Focus();
            Press(row);
        });

        if (Menu(row) is { } menu)
        {
            button.ContextMenu = menu;
        }

        // 두 번 눌러 이름 고치기.
        //
        // ⚠ <b>Tunnel로 달면 영영 안 온다</b> — DoubleTapped는 올라가는(Bubble) 이벤트라
        //   내려가는 차례가 아예 없다. 2026-09-16에 테스트가 그 자리에서 잡았다.
        button.DoubleTapped += (_, args) => UiGuard.Run(null, "이름 고치기", () =>
        {
            args.Handled = true;
            BeginRename(row);
        });

        button.AddHandler(PointerPressedEvent, (_, _) =>
        {
            _dragKey = Draggable(row) ? row.Key : null;
            _hoverKey = null;
        }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

        // ⛔ <b>놓은 자리는 좌표로 찾는다, 이벤트가 온 컨트롤로 찾지 않는다</b>
        //    (2026-09-16 소유자: "여전히 드래그 기능은 안돼").
        //
        //    Button은 눌리는 순간 <b>포인터를 잡는다</b>(캡처). 그래서 손을 어디서 떼든
        //    놓임 이벤트는 <b>누른 그 단추</b>로 온다 — 출발과 도착이 늘 같아 보여 드롭이
        //    한 번도 성립하지 않았다. 같은 이유로 도착 줄의 PointerEntered도 안 온다.
        button.AddHandler(PointerMovedEvent, (_, args) => UiGuard.Run(null, "옮기기", () =>
        {
            if (_dragKey is null)
            {
                return;
            }

            string? wanted = RowAt(args.GetPosition(RowHost))?.Key;

            if (!string.Equals(_hoverKey, wanted, StringComparison.Ordinal))
            {
                _hoverKey = wanted;
                Paint();
            }
        }), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

        button.AddHandler(PointerReleasedEvent, (_, args) => UiGuard.Run(null, "옮기기", () =>
        {
            _hoverKey = null;
            Release(RowAt(args.GetPosition(RowHost)) ?? row);
        }), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

        // 깊이 한 칸을 넓힌다 — 들여쓰기가 종류를 읽는 두 번째 단서다.
        var host = new DockPanel { Margin = new Thickness(row.Depth * 18, 0, 0, 0) };
        Control arrow = Arrow(row);

        DockPanel.SetDock(arrow, Dock.Left);
        host.Children.Add(arrow);
        host.Children.Add(button);

        return host;
    }

    /// <summary>
    /// 접기 손잡이. 에피소드와 빈 장면은 <b>펼 자식이 없어</b> 자리만 비운다(줄이 들쭉날쭉하지
    /// 않게). ⚠ 단추가 아니라 <see cref="Border"/>다 — 눌림을 여기서 삼켜야 이름 단추의
    /// 더블클릭·끌기와 안 엉킨다.
    /// </summary>
    private Control Arrow(SceneTreeRow row)
    {
        bool leaf = row.Kind == SceneTreeRowKind.Episode || row.IsDraft;

        // ⛔ <b>접힘이 보여야 한다</b> (2026-09-18 소유자: *"접혀있는건지 펴진건지 구분이
        //    안돼"*). 9포인트에 70% 불투명한 <c>▾</c>·<c>▸</c>는 둘 다 <b>작은 얼룩</b>으로
        //    보였다 — 모양이 아니라 <b>크기</b>가 먼저 문제였다. 속을 채운 글자로 키운다.
        var arrow = new Border
        {
            Width = 18,
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = new TextBlock
            {
                Text = leaf ? " " : IsExpanded(row.Key, row.ChapterId, null) ? "▼" : "▶",
                FontSize = 11,
                Opacity = 0.85,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        if (leaf)
        {
            return arrow;
        }

        arrow[ToolTip.TipProperty] = "접었다 폅니다";

        // ⚠ 삼각형은 이름 단추의 <b>형제</b>라 여기서 받은 눌림은 그쪽에 안 간다 —
        //   가로채는 장치가 필요 없다(그래서 Tunnel도 아니다).
        arrow.PointerPressed += (_, args) => UiGuard.Run(null, "접기", () =>
        {
            args.Handled = true;
            _dragKey = null;
            _cursorKey = row.Key;
            Fold(row, collapse: IsExpanded(row.Key, row.ChapterId, null));
        });

        return arrow;
    }

    /// <summary>
    /// 고른 줄·커서만 다시 칠한다 — <b>줄을 다시 세우지 않는다</b>.
    ///
    /// ⛔ 고를 때마다 <see cref="Draw"/>를 부르면 컨트롤이 통째로 갈려서 <b>더블클릭의 두 번째
    /// 누름이 새 컨트롤에 떨어진다</b>(= 이름 고치기가 영영 안 열린다). 구조가 안 바뀌는
    /// 변화는 칠만 한다.
    /// </summary>
    private void Paint()
    {
        for (int index = 0; index < _rows.Count && index < RowHost.Children.Count; index++)
        {
            if (RowHost.Children[index] is not DockPanel host ||
                host.Children.OfType<Button>().FirstOrDefault() is not { } button)
            {
                continue;
            }

            SceneTreeRow row = _rows[index];

            bool selected = row.Kind == SceneTreeRowKind.Episode &&
                            Selection is { } pick &&
                            string.Equals(pick.EpisodeId, row.EpisodeId, StringComparison.Ordinal) &&
                            string.Equals(pick.ChapterId, row.ChapterId, StringComparison.Ordinal);

            // 커서는 선택과 <b>다르게</b> 보여야 한다 — 훑는 중인 자리와 열어 둔 글은 다르다.
            bool cursor = string.Equals(row.Key, _cursorKey, StringComparison.Ordinal);

            button.Background = selected
                ? new SolidColorBrush(Color.FromArgb(40, 61, 123, 217))
                : Brushes.Transparent;

            // 끌고 지나가는 동안 <b>받아 줄 자리</b>에 테두리를 낸다 — 아무 표시가 없으면
            // 사람은 놓아도 되는지 모른 채로 손을 놓는다.
            bool landing = string.Equals(row.Key, _hoverKey, StringComparison.Ordinal) &&
                           DragSource() is { } dragged && Accepts(dragged, row);

            button.BorderThickness = new Thickness(landing || cursor ? 1 : 0);
            button.BorderBrush = landing
                ? new SolidColorBrush(Color.FromArgb(220, 61, 123, 217))
                : cursor
                    ? new SolidColorBrush(Color.FromArgb(150, 61, 123, 217))
                    : Brushes.Transparent;
        }
    }

    /// <summary>
    /// 그 자리에 있는 줄 — 좌표는 <see cref="RowHost"/> 기준이다. 없으면 null.
    /// </summary>
    private SceneTreeRow? RowAt(Point point)
    {
        for (int index = 0; index < RowHost.Children.Count && index < _rows.Count; index++)
        {
            if (RowHost.Children[index].Bounds.Contains(point))
            {
                return _rows[index];
            }
        }

        return null;
    }

    // ── 이름 고치기 ─────────────────────────────────────────────────────────

    /// <summary>지금 이름을 고치고 있는 줄. 없으면 null.</summary>
    private string? _renamingKey;

    /// <summary>그 줄이 지고 있는 이름 — 화면 글월이 아니라 <b>Id</b>다(꼬리말이 붙어 있다).</summary>
    internal static string NameOf(SceneTreeRow row) => row.Kind switch
    {
        SceneTreeRowKind.Chapter => row.ChapterId,
        SceneTreeRowKind.Scene => row.SceneId ?? string.Empty,
        _ => row.EpisodeId ?? string.Empty
    };

    private void BeginRename(SceneTreeRow row)
    {
        _renamingKey = row.Key;
        Draw();
    }

    /// <summary>고치는 중인 칸 — <b>테스트의 손잡이</b>이자 실제 입력칸이다.</summary>
    internal TextBox? RenameBox { get; private set; }

    private Control RenameRow(SceneTreeRow row)
    {
        var box = new TextBox
        {
            Text = NameOf(row),
            FontSize = 11,
            Padding = new Thickness(4, 1),
            Margin = new Thickness(row.Depth * 18 + 17, 0, 0, 0)
        };

        void Commit(bool keep)
        {
            _renamingKey = null;
            RenameBox = null;

            string wanted = box.Text?.Trim() ?? string.Empty;

            if (keep && wanted.Length > 0 &&
                !string.Equals(wanted, NameOf(row), StringComparison.Ordinal))
            {
                RenameRequested?.Invoke(row, wanted);
            }

            Draw();
        }

        box.KeyDown += (_, args) => UiGuard.Run(null, "이름 고치기", () =>
        {
            if (args.Key is Key.Enter or Key.Escape)
            {
                args.Handled = true;
                Commit(args.Key == Key.Enter);
            }
        });

        // 딴 데를 누르면 없던 일로 — 고치다 만 이름이 남아 있으면 무엇이 진짜인지 흐려진다.
        box.LostFocus += (_, _) => UiGuard.Run(null, "이름 고치기", () =>
        {
            if (string.Equals(_renamingKey, row.Key, StringComparison.Ordinal))
            {
                Commit(keep: false);
            }
        });

        RenameBox = box;
        return box;
    }

    // ── 끌어다 놓기 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 끌고 있는 줄. ⛔ <b>Avalonia의 DragDrop을 안 쓴다</b> — 창 밖으로 나가는 끌기가 아니라
    /// 한 컨트롤 안에서 줄을 옮기는 일이고, 눌림·놓임만으로 충분하다. 그 편이 화면 없이도
    /// 그대로 잴 수 있다(끌기 이벤트는 헤드리스에서 만들 수가 없다).
    /// </summary>
    private string? _dragKey;

    /// <summary>끌고 지나가는 중인 줄 — 받아 줄 자리면 테두리가 뜬다.</summary>
    private string? _hoverKey;

    /// <summary>지금 끌고 있는 줄. 없으면 null.</summary>
    private SceneTreeRow? DragSource() =>
        _dragKey is null
            ? null
            : _rows.FirstOrDefault(row => string.Equals(row.Key, _dragKey, StringComparison.Ordinal));

    /// <summary>끌 수 있는 줄 — 장면과 에피소드. 챕터는 담는 자리라 끌지 않는다.</summary>
    private static bool Draggable(SceneTreeRow row) =>
        row.Kind is SceneTreeRowKind.Scene or SceneTreeRowKind.Episode;

    private void Release(SceneTreeRow target)
    {
        if (_dragKey is not { } key)
        {
            return;
        }

        _dragKey = null;

        if (string.Equals(key, target.Key, StringComparison.Ordinal) ||
            _rows.FirstOrDefault(row => string.Equals(row.Key, key, StringComparison.Ordinal))
                is not { } source ||
            !Accepts(source, target))
        {
            return;
        }

        Dropped?.Invoke(new SceneTreeDrop(source, target));
    }

    /// <summary>
    /// 놓을 수 있는 자리인가 — <b>장면은 챕터에, 에피소드는 장면에</b>.
    ///
    /// ⚠ 에피소드를 챕터에 놓는 것은 안 받는다: 장면을 안 정한 채로 남는데, 그것은
    /// <b>미지정</b>이라는 뜻이 되어 사람이 의도한 것과 다를 수 있다. 갈 곳을 짚게 한다.
    /// </summary>
    internal static bool Accepts(SceneTreeRow source, SceneTreeRow target) => source.Kind switch
    {
        SceneTreeRowKind.Scene => target.Kind == SceneTreeRowKind.Chapter &&
                                  !string.Equals(source.ChapterId, target.ChapterId, StringComparison.Ordinal),

        SceneTreeRowKind.Episode => target.Kind == SceneTreeRowKind.Scene &&
                                    !string.Equals(source.SceneId, target.SceneId, StringComparison.Ordinal),

        _ => false
    };


    /// <summary>화면 없이 끌기를 재는 자리 — 눌림·놓임 두 번을 대신한다.</summary>
    internal void Drag(SceneTreeRow source, SceneTreeRow target)
    {
        _dragKey = Draggable(source) ? source.Key : null;
        Release(target);
    }

    /// <summary>
    /// 줄의 우클릭 차림표 (2026-09-16 소유자).
    ///
    /// ⛔ <b>2026-09-18에 에피소드 줄에도 붙었다</b> (소유자). 옛 규칙은 <i>"담는 줄에만 —
    /// 에피소드는 이 탭에서 만드는 것이 글이지 구조가 아니고, 지우는 자리는 [챕터 그래프]에
    /// 있다"</i>였는데, 그러면 작가가 <b>탭을 건너가야</b> 한 칸을 더하거나 뺀다. 구조를
    /// 못 만지게 막은 것이 아니라 <b>먼 데로 보낸 것</b>이었다.
    ///
    /// ⚠ 규율은 여전히 밖에 있다 — 만들기는 <see cref="Vn.Authoring.Editing.ProjectEditor"/>,
    /// 지우기는 <see cref="Vn.Authoring.Chapters.EpisodeDeleter"/>. 여기서 느는 것은
    /// <b>부르는 자리</b>뿐이다.
    /// </summary>
    private ContextMenu? Menu(SceneTreeRow row)
    {
        MenuItem Item(string header, SceneTreeCommand command)
        {
            var item = new MenuItem { Header = header, FontSize = 11 };
            item.Click += (_, _) => UiGuard.Run(null, "탐색기 차림표",
                () => CommandRequested?.Invoke(command, row));

            return item;
        }

        return row.Kind switch
        {
            SceneTreeRowKind.Chapter => new ContextMenu
            {
                ItemsSource = new[]
                {
                    Item("장면 추가", SceneTreeCommand.AddScene),
                    Item("챕터 삭제…", SceneTreeCommand.DeleteChapter)
                }
            },

            SceneTreeRowKind.Scene => new ContextMenu
            {
                ItemsSource = new[]
                {
                    Item("에피소드 추가", SceneTreeCommand.AddEpisode),
                    Item(row.IsDraft ? "빈 장면 닫기" : "장면 삭제…", SceneTreeCommand.DeleteScene)
                }
            },

            // ⚠ 에피소드 줄에서의 [에피소드 추가]는 <b>그 뒤에</b> 붙인다(간선까지) —
            //    장면 줄에서는 그 장면의 끝에 붙는다. 어느 줄을 눌렀느냐가 곧 어디에
            //    붙일지다.
            SceneTreeRowKind.Episode => new ContextMenu
            {
                ItemsSource = new[]
                {
                    Item("에피소드 추가", SceneTreeCommand.AddEpisode),
                    Item("에피소드 삭제…", SceneTreeCommand.DeleteEpisode)
                }
            },

            _ => null
        };
    }

    /// <summary>
    /// 이름 쪽을 누르면 — 에피소드는 고르고, 담는 줄은 <b>커서만 옮긴다</b>.
    ///
    /// ⛔ <b>여기서 접지 않는다</b> (2026-09-16 소유자). 접기는 삼각형의 일이다 —
    /// 줄 전체가 접기 손잡이면 이름을 두 번 누를 수도, 끌 수도 없다(<see cref="Build"/>).
    /// </summary>
    private void Press(SceneTreeRow row)
    {
        _cursorKey = row.Key;

        if (row.Kind == SceneTreeRowKind.Episode)
        {
            Choose(row);
            return;
        }

        Paint();
    }

    private void Choose(SceneTreeRow row)
    {
        Selection = new ChapterEpisodePick(row.ChapterId, row.EpisodeId!);

        // ⚠ <b>칠만 한다.</b> 여기서 다시 세우면 더블클릭의 두 번째 누름이 새 컨트롤에
        //   떨어져 이름 고치기가 영영 안 열린다. 고른 에피소드는 이미 보이는 줄이므로
        //   구조는 안 바뀐다.
        Paint();
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

        // 커서만 옮기는 것은 구조가 안 바뀌는 변화다 — 칠만 하고 보이는 자리로 끌어온다.
        Paint();
        RowHost.Children[index].BringIntoView();
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
