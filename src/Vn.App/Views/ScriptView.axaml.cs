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

        EpisodeTree.EpisodeSelected += _ => UiGuard.Run(_session, "에피소드 고르기", () =>
        {
            // 고른 것을 세션에 올린다 — 여기가 두 화면이 같은 것을 가리키는 자리다 (R6 S-4).
            if (SelectedNode() is { } node)
            {
                _session?.Select(node.Id);
            }

            ShowSelected();
        });
        EpisodeTree.CommandRequested += (command, row) =>
            UiGuard.Run(_session, "탐색기 차림표", () => RunTreeCommand(command, row));

        EpisodeTree.RenameRequested += (row, wanted) =>
            UiGuard.Run(_session, "이름 고치기", () => Rename(row, wanted));

        EpisodeTree.Dropped += drop =>
            UiGuard.Run(_session, "옮기기", () => Move(drop));

        ChapterAddButton.Click += (_, _) => UiGuard.Run(_session, "새 챕터", () =>
        {
            if (_session is not null)
            {
                ChapterAddFlyout.ShowAt(ChapterAddButton, _session);
            }
        });

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
        session.SelectionChanged += (_, _) => UiGuard.Run(_session, "선택 따라가기", Follow);

        Rebuild();
    }

    /// <summary>
    /// [연출 그래프]에서 고른 것을 따라간다 (R6 S-4 · 규격 §5).
    ///
    /// ⛔ <b>정본은 세션의 <c>SelectedNodeId</c> 하나다.</b> 두 화면이 제 선택을 따로 들면
    /// 같은 에피소드를 두 자리에서 고르게 되고, 어느 쪽이 지금 열린 글인지 사람이 못 가린다.
    ///
    /// ⚠ <b>챕터 판의 에피소드 노드일 때만</b> 따라간다. 작가의 자유 판에서 고른 노드는 대본
    /// 탭에 설 자리가 없으니 <b>가만히 둔다</b> — 엉뚱한 글로 튀는 것보다 낫다.
    /// </summary>
    private void Follow()
    {
        if (_session?.SelectedNode is not DialogueNode node ||
            PickOf(node) is not { } pick ||
            pick == EpisodeTree.Selection)
        {
            return;
        }

        EpisodeTree.Select(pick.ChapterId, pick.EpisodeId);
        ShowSelected();
    }

    // ── 탐색기 차림표 (2026-09-16 소유자) ──────────────────────────────────

    /// <summary>
    /// 트리에서 시킨 일을 한다. ⛔ <b>트리는 하지 않는다</b> — 그쪽은 투영이고 편집기를
    /// 모른다(두 화면이 그 컨트롤 하나를 쓴다).
    /// </summary>
    private void RunTreeCommand(SceneTreeCommand command, SceneTreeRow row)
    {
        if (_session is null)
        {
            return;
        }

        switch (command)
        {
            case SceneTreeCommand.AddScene:
                string opened = EpisodeTree.AddDraftScene(row.ChapterId);
                _session.SetStatus(
                    $"'{row.ChapterId}'에 장면 '{opened}' 자리를 열었습니다 — " +
                    "우클릭해 에피소드를 넣으면 그때 저장됩니다.");
                break;

            case SceneTreeCommand.AddEpisode:
                AddEpisodeToScene(row);
                break;

            case SceneTreeCommand.DeleteScene when row.IsDraft:
                EpisodeTree.DropDraftScene(row.ChapterId, row.SceneId!);
                _session.SetStatus($"빈 장면 '{row.SceneId}' 자리를 닫았습니다.");
                break;

            case SceneTreeCommand.DeleteScene:
                ConfirmSceneDelete(row);
                break;

            case SceneTreeCommand.DeleteChapter:
                ConfirmChapterDelete(row);
                break;
        }
    }

    // ── 이름 고치기 (줄을 더블클릭) ────────────────────────────────────────

    /// <summary>
    /// 트리에서 고친 이름을 <b>그 이름을 지고 있는 것들</b>과 함께 옮긴다.
    ///
    /// ⛔ 규율은 셋 다 밖에 있다 — <see cref="ChapterRenamer"/>(워크북·대본 폴더·판) ·
    /// <see cref="EpisodeRenamer"/>(간선·픽스처·대본 파일·대사 노드) ·
    /// <see cref="ProjectEditor.UpdateEpisodeScenes"/>(장면은 이름표라 붙은 것들을 한 번에).
    /// 여기서 하는 일은 <b>어느 규율을 부를지 고르는 것</b>뿐이다.
    /// </summary>
    private void Rename(SceneTreeRow row, string wanted)
    {
        if (_session is null)
        {
            return;
        }

        switch (row.Kind)
        {
            case SceneTreeRowKind.Chapter:
                ChapterRenamer.Result chapter =
                    ChapterRenamer.Rename(_session.Editor, _session.ProjectPath, row.ChapterId, wanted);

                _session.SetStatus(chapter.Renamed
                    ? $"챕터 '{row.ChapterId}' → '{wanted}'. 워크북·대본 폴더·판이 함께 갔습니다."
                    : chapter.Failure!);
                break;

            case SceneTreeRowKind.Episode:
                EpisodeRenamer.Result episode = EpisodeRenamer.Rename(
                    _session.Editor, _session.ProjectPath, row.ChapterId, row.EpisodeId!, wanted);

                if (episode.Renamed)
                {
                    EpisodeTree.Select(row.ChapterId, wanted);
                    ShowSelected();
                }

                _session.SetStatus(episode.Renamed
                    ? $"에피소드 '{row.EpisodeId}' → '{wanted}'. 간선·픽스처·대본·대사 노드가 함께 갔습니다."
                    : episode.Failure!);
                break;

            case SceneTreeRowKind.Scene:
                RenameScene(row, wanted);
                break;
        }
    }

    /// <summary>
    /// 장면 이름 고치기 = 그 장면을 지고 있던 <b>에피소드들의 <c>SceneId</c>를 한 번에</b> 바꾸기.
    /// 장면에는 고칠 제 칸이 없다 — 이름표이기 때문이다.
    ///
    /// ⚠ 빈 장면은 프로젝트에 없으므로 자리표시만 갈아 끼운다.
    /// </summary>
    private void RenameScene(SceneTreeRow row, string wanted)
    {
        if (row.IsDraft)
        {
            EpisodeTree.DropDraftScene(row.ChapterId, row.SceneId!);
            EpisodeTree.AddDraftScene(row.ChapterId, wanted);
            _session!.SetStatus($"빈 장면 자리를 '{wanted}'로 바꿨습니다.");
            return;
        }

        List<string> episodes = _session!.Editor.FindChapter(row.ChapterId)?.Episodes
            .Where(episode =>
                string.Equals(episode.EffectiveSceneId, row.SceneId, StringComparison.Ordinal))
            .Select(episode => episode.EpisodeId)
            .ToList() ?? [];

        _session.Editor.UpdateEpisodeScenes(row.ChapterId, episodes, wanted);

        _session.SetStatus(
            $"장면 '{row.SceneId}' → '{wanted}'. 에피소드 {episodes.Count}개가 함께 갔습니다.");
    }

    // ── 끌어다 놓기 ────────────────────────────────────────────────────────

    /// <summary>
    /// 장면을 다른 챕터로, 에피소드를 다른 장면으로 (2026-09-16 소유자).
    ///
    /// ⚠ <b>가로지르게 된 간선은 걷힌다</b> — 간선은 챕터 안의 길이라 두 챕터를 이을 수 없다.
    /// 몇 개가 걷혔는지 <b>말해 준다</b>: 조용히 지우면 다음에 판을 열었을 때 길이 없어진
    /// 이유를 알 수가 없다.
    /// </summary>
    private void Move(SceneTreeDrop drop)
    {
        if (_session is null)
        {
            return;
        }

        (SceneTreeRow source, SceneTreeRow target) = (drop.Source, drop.Target);

        if (source.Kind == SceneTreeRowKind.Scene)
        {
            MoveScene(source, target.ChapterId);
            return;
        }

        int cut = _session.Editor.MoveEpisodesToChapter(
            source.ChapterId, [source.EpisodeId!], target.ChapterId, target.SceneId);

        EpisodeTree.Select(target.ChapterId, source.EpisodeId!);
        ShowSelected();

        _session.SetStatus(
            $"에피소드 '{source.EpisodeId}'를 장면 '{target.SceneId}'으로 옮겼습니다." + Cut(cut));
    }

    private void MoveScene(SceneTreeRow scene, string toChapterId)
    {
        // 빈 장면은 프로젝트에 없다 — 자리표시를 저쪽 챕터로 옮기는 것이 전부다.
        if (scene.IsDraft)
        {
            EpisodeTree.DropDraftScene(scene.ChapterId, scene.SceneId!);
            EpisodeTree.AddDraftScene(toChapterId);
            _session!.SetStatus($"빈 장면 자리를 '{toChapterId}'로 옮겼습니다.");
            return;
        }

        List<string> episodes = _session!.Editor.FindChapter(scene.ChapterId)?.Episodes
            .Where(episode =>
                string.Equals(episode.EffectiveSceneId, scene.SceneId, StringComparison.Ordinal))
            .Select(episode => episode.EpisodeId)
            .ToList() ?? [];

        int cut = _session.Editor.MoveEpisodesToChapter(scene.ChapterId, episodes, toChapterId);

        _session.SetStatus(
            $"장면 '{scene.SceneId}'을 '{toChapterId}'로 옮겼습니다 " +
            $"(에피소드 {episodes.Count}개)." + Cut(cut));
    }

    private static string Cut(int edges) => edges == 0
        ? string.Empty
        : $" ⚠ 챕터를 가로지르게 된 길 {edges}개는 걷었습니다 — 간선은 챕터 안에서만 잇습니다.";

    /// <summary>
    /// 그 장면에 에피소드 하나. <b>간선이 함께 선다</b> — 챕터 그래프의 [＋ 에피소드]가 세운
    /// 규율 그대로다(v12): 떨어진 섬을 만들지 않는다.
    ///
    /// 이을 곳은 <b>같은 장면의 마지막 에피소드</b>이고, 장면이 아직 비어 있으면 <b>챕터의
    /// 마지막 에피소드</b>다 — 그것이 이 장면의 <b>들어오는 자리 하나</b>가 된다
    /// (<c>ChapterSceneEntryCheck</c>가 재는 그 자리다). 챕터가 통째로 비어 있을 때만
    /// 간선 없이 첫 카드가 선다.
    ///
    /// ⚠ Id는 자리표시다 — 자동으로 <b>발명</b>하지 않고 겹치지 않는 이름만 주며, 사람이
    /// [챕터 그래프]의 이름 칸에서 정한다.
    /// </summary>
    private void AddEpisodeToScene(SceneTreeRow row)
    {
        if (_session!.Editor.FindChapter(row.ChapterId) is not { } chapter)
        {
            return;
        }

        int number = 1;

        while (chapter.Episodes.Any(episode =>
                   string.Equals(episode.EpisodeId, $"new{number:D2}", StringComparison.Ordinal)))
        {
            number++;
        }

        string episodeId = $"new{number:D2}";
        string sceneId = row.SceneId!;

        ChapterEpisode? parent =
            chapter.Episodes.LastOrDefault(episode =>
                string.Equals(episode.EffectiveSceneId, sceneId, StringComparison.Ordinal))
            ?? chapter.Episodes.LastOrDefault();

        if (parent is null)
        {
            _session.Editor.AddEpisode(row.ChapterId, episodeId, title: string.Empty, 0, 0, sceneId);
        }
        else
        {
            _session.Editor.AddNextEpisode(
                row.ChapterId, parent.EpisodeId, episodeId, title: string.Empty,
                parent.X + 220, parent.Y, optionLabel: "다음", sceneId);
        }

        EpisodeTree.Select(row.ChapterId, episodeId);
        ShowSelected();

        _session.SetStatus(
            $"장면 '{sceneId}'에 에피소드 '{episodeId}'를 넣었습니다 — " +
            "이름은 [챕터 그래프]에서 정합니다.");
    }

    /// <summary>
    /// [장면 삭제] — <b>장면만 걷고 에피소드는 남긴다</b>.
    ///
    /// ⛔ 장면은 담는 그릇이 아니라 에피소드에 붙은 <b>이름표</b>다(R6 §3-1). 이름표를 떼면
    /// 그 에피소드들은 제각기 미지정 장면이 된다 — 작가가 쓴 글이 사라질 이유가 없다.
    /// 글을 지우려면 에피소드를 지우는 것이고, 그 자리는 [챕터 그래프]다.
    /// </summary>
    private void ConfirmSceneDelete(SceneTreeRow row)
    {
        string sceneId = row.SceneId!;

        List<string> episodes = _session!.Editor.FindChapter(row.ChapterId)?.Episodes
            .Where(episode => string.Equals(episode.EffectiveSceneId, sceneId, StringComparison.Ordinal))
            .Select(episode => episode.EpisodeId)
            .ToList() ?? [];

        Confirm(
            $"장면 '{sceneId}'을 걷습니다. 에피소드 {episodes.Count}개는 남고 " +
            "장면 미지정이 됩니다 — 글은 그대로입니다.",
            "장면 걷기",
            () =>
            {
                // 빈 값이 곧 미지정이다 — `__scene_{EpisodeId}`로 퇴화한다.
                _session.Editor.UpdateEpisodeScenes(row.ChapterId, episodes, sceneId: string.Empty);
                _session.SetStatus($"장면 '{sceneId}'을 걷었습니다. 에피소드 {episodes.Count}개는 남았습니다.");
            });
    }

    /// <summary>
    /// [챕터 삭제] — 되돌릴 자리를 <b>이름으로</b> 말한다. 규칙은
    /// <see cref="ChapterDeleter"/>가 갖는다([챕터 그래프]의 [챕터 제거]와 같은 길).
    /// </summary>
    private void ConfirmChapterDelete(SceneTreeRow row)
    {
        Confirm(
            $"'{row.ChapterId}'의 대본과 연출 그래프의 노드가 함께 사라집니다. " +
            "원고는 .bak으로 남고, 판은 되돌리기로 돌아옵니다.",
            "정말 제거",
            () =>
            {
                ChapterDeleter.Result result =
                    ChapterDeleter.Delete(_session!.Editor, _session.ProjectPath, row.ChapterId);

                if (!result.Deleted)
                {
                    _session.SetStatus(result.Failure!);
                    return;
                }

                string kept = string.Join(" · ",
                    new[] { result.WorkbookBackup, result.EpisodesBackup }.Where(item => item is not null));

                _session.SetStatus(
                    $"챕터 '{row.ChapterId}'를 지웠습니다(연출 노드 {result.NodesRemoved}개도 함께)." +
                    (kept.Length > 0 ? $" 원고는 남겨 뒀습니다: {kept}" : string.Empty));
            });
    }

    /// <summary>
    /// 지우는 일 앞의 한 걸음. 차림표를 누른 것이 첫 걸음이고(이름 끝의 `…`가 그 뜻이다),
    /// 여기가 <b>무엇이 사라지는지 읽고 누르는</b> 두 번째다.
    ///
    /// ⚠ 확인 창을 띄우지 않는다 — 창은 방금 읽던 트리를 덮는다. 트리 옆에 붙어 뜨는
    /// 쪽이 어느 줄을 지우는 것인지 더 분명하다([챕터 그래프]가 세운 그 규율).
    /// </summary>
    private void Confirm(string caution, string confirmText, Action act)
    {
        var panel = new StackPanel { Spacing = 6, MaxWidth = 260 };

        panel.Children.Add(new TextBlock
        {
            Text = caution,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85
        });

        var flyout = new Flyout { Content = panel };

        var confirm = new Button
        {
            Content = confirmText,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Foreground = new SolidColorBrush(Color.FromRgb(190, 60, 60))
        };

        confirm.Click += (_, _) => UiGuard.Run(_session, confirmText, () =>
        {
            flyout.Hide();
            ConfirmButton = null;
            act();
        });

        panel.Children.Add(confirm);
        flyout.ShowAt(EpisodeTree);

        ConfirmButton = confirm;
    }

    /// <summary>
    /// 지금 떠 있는 확인 단추 — <b>테스트의 손잡이</b>다([화자 ▾]의 <see cref="SpeakerMenuItems"/>와
    /// 같은 뜻). 플라이아웃의 팝업은 창 밖에 살아 나무를 타고 내려가 찾을 수 없다.
    /// </summary>
    internal Button? ConfirmButton { get; private set; }

    /// <summary>
    /// 그 노드가 선 자리 — 판 이름이 챕터고, 표식이 먼저고 없으면 이름이 에피소드다
    /// (<see cref="NodeIn"/>의 뒤집힌 짝이라 규칙이 같아야 한다).
    /// </summary>
    private ChapterEpisodePick? PickOf(DialogueNode node)
    {
        if (_session?.Project.FindFileContainingNode(node.Id) is not { } file)
        {
            return null;
        }

        string episodeId = node.ExcelEpisodeId is { Length: > 0 } marked ? marked : node.Name;

        return _session.Project.Chapters.Any(chapter =>
            string.Equals(chapter.ChapterId, file.Name, StringComparison.Ordinal) &&
            chapter.Episodes.Any(episode =>
                string.Equals(episode.EpisodeId, episodeId, StringComparison.Ordinal)))
            ? new ChapterEpisodePick(file.Name, episodeId)
            : null;
    }

    /// <summary>
    /// 탐색기를 다시 세운다 — 챕터가 늘거나 에피소드가 생기면 고를 것이 달라진다.
    ///
    /// ⛔ <b>2026-09-16에 목록 둘이 트리 하나가 됐다</b> (R6 S-2). 예전에는 챕터 목록과
    /// 에피소드 목록이 따로 있었고, 그 구조에는 <b>장면이 설 자리가 없었다</b> — 챕터와
    /// 에피소드 사이가 비어 있었기 때문이다.
    ///
    /// ⚠ <b>"고른 챕터"라는 상태가 사라졌다.</b> 이제 챕터는 고른 에피소드에서 나오는
    /// 파생이다(<c>docs/plans/R6-explorer.md</c> §1) — 따로 들면 트리의 선택과 어긋날
    /// 자리가 생긴다.
    /// </summary>
    private void Rebuild()
    {
        if (_session is null || ScriptBox.IsFocused)
        {
            return;
        }

        // 접힘은 사람의 것이라 프로젝트를 따라다닌다 (R6 S-4) — 같은 프로젝트면 지나간다.
        EpisodeTree.Remember(_session.ProjectPath);
        EpisodeTree.Rebuild(_session.Project, HasScript);

        // 아직 아무것도 안 골랐으면 첫 에피소드를 고른다 — 빈 오른쪽 화면으로 시작하면
        // 작가는 무엇부터 눌러야 하는지 모른다.
        if (EpisodeTree.Selection is null && FirstEpisode() is { } first)
        {
            EpisodeTree.Select(first.ChapterId, first.EpisodeId);
        }

        ShowSelected();
    }

    /// <summary>
    /// 그 에피소드에 대본이 있는가 — 트리가 <b>아직 아무도 안 쓴 자리</b>를 흐리게 그린다.
    /// </summary>
    private bool HasScript(string chapterId, string episodeId) =>
        NodeIn(chapterId, episodeId) is not null;

    /// <summary>첫 챕터의 첫 에피소드. 아무 데도 없으면 null이다.</summary>
    private ChapterEpisodePick? FirstEpisode() =>
        _session?.Project.Chapters
            .OrderBy(chapter => chapter.ChapterId, StringComparer.OrdinalIgnoreCase)
            .Where(chapter => chapter.Episodes.Count > 0)
            .Select(chapter => new ChapterEpisodePick(chapter.ChapterId, chapter.Episodes[0].EpisodeId))
            .FirstOrDefault();

    // ⛔ `ChapterIds()`·`EpisodeIds()`는 2026-09-16에 걷혔다 (R6 S-2). 목록 둘을 채우던
    //    함수들이고, 그 둘 사이에는 <b>장면이 설 자리가 없었다</b>. 이제 트리가 프로젝트를
    //    직접 읽고 `ChapterSceneGrouping`으로 묶는다.
    //
    //    ⚠ 그때 지킨 규율은 트리가 이어받았다: <b>노드가 아직 없는 에피소드도 선다</b>.
    //    노드 있는 것만 보이면 아직 아무도 안 쓴 에피소드에 글 쓸 자리가 없고, 노드를
    //    만드는 것이 곧 글을 쓰는 일이라 매듭이 된다 — 트리는 그런 줄을 흐리게 그린다.

    /// <summary>
    /// 고른 에피소드의 대사노드. <b>없을 수 있다</b> — 아직 아무도 안 쓴 에피소드다.
    ///
    /// 이름의 원천은 챕터 `에피소드` 시트의 `대사엔트리`이고, 비어 있으면 EpisodeId다
    /// (임포터와 [＋ 에피소드]가 쓰는 규칙과 같아야 한다 — 아니면 노드가 둘이 된다).
    /// </summary>
    private DialogueNode? FindNode(string episodeId) =>
        EpisodeTree.Selection is { } pick ? NodeIn(pick.ChapterId, episodeId) : null;

    /// <summary>그 챕터의 판에서 그 에피소드의 대사노드를 찾는다.</summary>
    private DialogueNode? NodeIn(string chapterId, string episodeId)
    {
        if (_session is null)
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
        EpisodeTree.Selection is { } pick ? NodeIn(pick.ChapterId, pick.EpisodeId) : null;

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

        if (_session is null || EpisodeTree.Selection is not { } picked)
        {
            bool noChapter = _session?.Project.Chapters.Count is null or 0;

            HeaderText.Text = noChapter ? "아직 챕터가 없습니다." : "에피소드를 고르세요.";

            EmptyPanel.IsVisible = true;
            EmptyText.Text = noChapter
                ? "아직 챕터가 없습니다.\n[챕터 그래프]에서 챕터를 세우면 여기에 글 쓸 자리가 생깁니다."
                : "이 챕터에는 아직 에피소드가 없습니다.\n" +
                  "[챕터 그래프]에서 첫 에피소드를 세우면 여기에 섭니다.";

            ScriptBox.Text = string.Empty;
            ScriptBox.IsEnabled = false;
            ApplyButton.IsEnabled = false;
            SpeakerButton.IsEnabled = false;
            HintText.Text = string.Empty;
            return;
        }

        string episodeId = picked.EpisodeId;

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
        if (_session is null || EpisodeTree.Selection is not { } pick)
        {
            return;
        }

        string episodeId = pick.EpisodeId;

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
        if (_session is null || EpisodeTree.Selection is not { } pick)
        {
            return;
        }

        string episodeId = pick.EpisodeId;

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
        string fileId = _session!.Editor.EnsureChapterBoard(EpisodeTree.Selection!.ChapterId);
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
            EpisodeTree.Selection is not { ChapterId: { } chapterId } ||
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
