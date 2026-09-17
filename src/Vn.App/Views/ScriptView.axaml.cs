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

/// <summary>글을 대본으로 넣으려 한 결과.</summary>
internal enum ScriptSaveOutcome
{
    /// <summary>넣을 것이 없었다 — 고친 데가 없거나 쓸 자리가 아니다.</summary>
    Nothing,

    /// <summary>들어갔다.</summary>
    Saved,

    /// <summary>줄이 지워질 참이라 <b>한 번 더</b>를 기다린다.</summary>
    NeedsConfirmation,

    /// <summary>넣지 못했다 — 사유는 상태줄에 있다.</summary>
    Refused
}

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
///
/// ⛔ <b>저장은 Ctrl+S 하나다</b> (2026-09-17 소유자). 입력칸은 <b>자유롭게</b> 고친다.
///
/// ⛔ <b>아래 단추 둘도 걷었다</b> (2026-09-18 소유자: *"솔직히 쓸모 없어"*) — [화자 ▾]와
/// [저장]. 화자는 이름을 치는 편이 고르는 것보다 빠르고, 저장은 Ctrl+S 하나다. 남은 것은
/// <b>글과 글에 대한 말</b>뿐이다.
/// </summary>
public partial class ScriptView : UserControl
{
    private AuthoringSession? _session;

    /// <summary>삭제 확인 대기 중인 글 — 같은 글로 한 번 더 누르면 적용한다.</summary>
    private string? _pendingDeleteText;

    /// <summary>
    /// <b>지금 프로젝트에 들어 있는 글</b> — 입력칸을 채울 때와 넣기가 성공할 때 갱신한다.
    ///
    /// ⚠ 이것이 <b>초고인지 아닌지를 아는 유일한 근거</b>다. 입력칸의 글과 이 값이 다르면
    /// 아직 안 들어간 글이 있다는 뜻이고, 그 사실을 알아야 ① 저장이 그것을 먼저 넣고
    /// ② 다시 그리기가 그것을 덮지 않고 ③ 제목이 <c>*</c>를 달 수 있다.
    /// </summary>
    private string _inProject = string.Empty;

    /// <summary>
    /// 입력칸에 <b>아직 안 들어간 글</b>이 있는가.
    ///
    /// ⚠ 껍데기(<see cref="MainWindow"/>)가 저장 표시와 저장 순서에 쓴다 — 프로젝트의
    /// <c>IsDirty</c>는 이것을 모른다. 글은 <see cref="ProjectEditor"/>를 지나야 프로젝트에
    /// 들어가므로, 타이핑만 한 상태는 프로젝트 쪽에서 보면 <b>아무 일도 없는 것</b>이다.
    /// </summary>
    internal bool HasUnsavedText =>
        ScriptBox.IsEnabled &&
        !string.Equals(ScriptBox.Text ?? string.Empty, _inProject, StringComparison.Ordinal);

    /// <summary>글 입력칸 — 테스트의 손잡이다.</summary>
    internal TextBox TextArea => ScriptBox;

    /// <summary>
    /// <see cref="HasUnsavedText"/>가 <b>뒤집혔다</b> — 껍데기가 제목과 표시를 고칠 때다.
    ///
    /// ⚠ <b>글자마다 알리지 않는다.</b> 껍데기의 <c>RefreshShell</c>은 <c>IsDirty</c>를 묻고
    /// 그것은 <b>프로젝트 전체를 문자열로 인코딩한다</b> — 키 입력마다 부르면 큰 프로젝트에서
    /// 타이핑이 끈다. 값이 바뀌는 순간은 글 뭉치 하나에 두 번뿐이다.
    /// </summary>
    internal event Action? DraftChanged;

    /// <summary>마지막으로 알린 <see cref="HasUnsavedText"/> — 뒤집힘만 세려고 든다.</summary>
    private bool _announcedUnsaved;

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

        DeleteRowButton.Click += (_, _) => UiGuard.Run(_session, "지우기", DeleteCursorRow);

        EmptyAddScriptButton.Click += (_, _) => UiGuard.Run(_session, "대본 세우기", AddScript);

        ScriptBox.TextChanged += (_, _) => AnnounceDraft();
    }

    /// <summary>안 들어간 글이 있느냐가 <b>뒤집혔을 때만</b> 껍데기를 깨운다.</summary>
    private void AnnounceDraft()
    {
        bool unsaved = HasUnsavedText;

        if (unsaved == _announcedUnsaved)
        {
            return;
        }

        _announcedUnsaved = unsaved;
        DraftChanged?.Invoke();
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
    /// 🗑 — <b>지금 짚고 있는 줄</b>을 지운다 (2026-09-18 소유자).
    ///
    /// ⛔ <b>지우는 자리를 하나로 모은 것</b>이 요점이다. 전에는 줄마다 우클릭 차림표에
    /// 삭제가 있어서, 다른 것을 누르려다 눌리기 쉬웠다 — 이제 차림표에는 만드는 일만 있고
    /// 지우는 일은 이 단추 하나다.
    ///
    /// ⚠ 종류마다 지우는 뜻이 다르다: 챕터는 통째로, 장면은 <b>이름표만</b>(에피소드는
    /// 남는다), 에피소드는 그 한 칸. 세 갈래를 여기서 고르고 <b>규율은 각자의 자리</b>가 갖는다.
    ///
    /// ⚠ 셋 다 <b>되돌리기(Ctrl+Z)로 돌아온다</b> — 프로젝트가 원본이라서다. 함께 밀리는
    /// 워크북 <c>.bak</c>은 산출물이라 다음 출력에서 다시 난다.
    /// </summary>
    private void DeleteCursorRow()
    {
        if (_session is null)
        {
            return;
        }

        if (EpisodeTree.CursorRow is not { } row)
        {
            _session.SetStatus("지울 것을 트리에서 먼저 눌러 주세요 — 챕터·장면·에피소드.");
            return;
        }

        switch (row.Kind)
        {
            case SceneTreeRowKind.Chapter:
                ConfirmChapterDelete(row);
                break;

            case SceneTreeRowKind.Scene when row.IsDraft:
                EpisodeTree.DropDraftScene(row.ChapterId, row.SceneId!);
                _session.SetStatus($"빈 장면 '{row.SceneId}' 자리를 닫았습니다.");
                break;

            case SceneTreeRowKind.Scene:
                ConfirmSceneDelete(row);
                break;

            case SceneTreeRowKind.Episode:
                ConfirmEpisodeDelete(row);
                break;
        }
    }

    /// <summary>
    /// 누른 줄이 <b>어느 챕터</b>인가. 빈 자리에서 우클릭했으면 지금 짚고 있는 줄의 챕터,
    /// 그것도 없으면 첫 챕터다 — 챕터가 하나도 없으면 <c>null</c>.
    /// </summary>
    private string? TargetChapter(SceneTreeRow row)
    {
        if (row.ChapterId is { Length: > 0 } named)
        {
            return named;
        }

        if (EpisodeTree.CursorRow?.ChapterId is { Length: > 0 } cursor)
        {
            return cursor;
        }

        return _session?.Project.Chapters.FirstOrDefault()?.ChapterId;
    }

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
            // ⚠ 셋이 <b>어느 줄에서든</b> 뜨므로, 대상이 없을 수 있다 (2026-09-18).
            //    그때는 조용히 지나가지 않고 <b>무엇이 먼저 필요한지</b> 말한다.
            case SceneTreeCommand.AddScene when TargetChapter(row) is { } sceneChapter:
                string opened = EpisodeTree.AddDraftScene(sceneChapter);
                _session.SetStatus(
                    $"'{sceneChapter}'에 장면 '{opened}' 자리를 열었습니다 — " +
                    "우클릭해 에피소드를 넣으면 그때 저장됩니다.");
                break;

            case SceneTreeCommand.AddScene:
                _session.SetStatus("장면을 넣을 챕터를 먼저 만들거나 골라 주세요.");
                break;

            case SceneTreeCommand.AddChapter:
                ChapterAddFlyout.ShowAt(DeleteRowButton, _session);
                break;

            case SceneTreeCommand.AddEpisode when TargetChapter(row) is not null:
                AddEpisodeToScene(row);
                break;

            case SceneTreeCommand.AddEpisode:
                _session.SetStatus("에피소드를 넣을 챕터를 먼저 만들거나 골라 주세요.");
                break;

            case SceneTreeCommand.DeleteEpisode:
                ConfirmEpisodeDelete(row);
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

        EpisodeMover.Result moved = EpisodeMover.Move(
            _session.Editor, _session.ProjectPath,
            source.ChapterId, [source.EpisodeId!], target.ChapterId, target.SceneId);

        if (!moved.Moved)
        {
            _session.SetStatus(moved.Failure!);
            return;
        }

        EpisodeTree.Select(target.ChapterId, source.EpisodeId!);
        ShowSelected();

        _session.SetStatus(
            $"에피소드 '{source.EpisodeId}'를 장면 '{target.SceneId}'으로 옮겼습니다." + Notice(moved));
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

        EpisodeMover.Result moved = EpisodeMover.Move(
            _session.Editor, _session.ProjectPath, scene.ChapterId, episodes, toChapterId);

        if (!moved.Moved)
        {
            _session.SetStatus(moved.Failure!);
            return;
        }

        _session.SetStatus(
            $"장면 '{scene.SceneId}'을 '{toChapterId}'로 옮겼습니다 " +
            $"(에피소드 {episodes.Count}개)." + Notice(moved));
    }

    /// <summary>
    /// 옮기면서 <b>사람이 알아야 할 것</b> — 걷힌 길과 밀어 둔 원고.
    ///
    /// 둘 다 조용히 하면 나중에 "선택지가 왜 없지" · "원고가 어디 갔지"가 되고, 그때는
    /// 폴더를 열어 봐야만 풀린다.
    /// </summary>
    private static string Notice(EpisodeMover.Result moved) =>
        (moved.EdgesCut == 0
            ? string.Empty
            : $" ⚠ 챕터를 가로지르게 된 길 {moved.EdgesCut}개는 걷었습니다 — 간선은 챕터 안에서만 잇습니다.") +
        (moved.Backups.Count == 0
            ? string.Empty
            : $" 옛 자리의 대본은 밀어 뒀습니다: {string.Join(" · ", moved.Backups)}");

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
        // ⚠ <b>셋이 어느 줄에서든 뜬다</b>(2026-09-18) — 챕터 줄이나 빈 자리에서 불렀으면
        //    장면이 안 정해져 있다. 그때는 그 챕터의 <b>마지막 에피소드가 선 장면</b>을 따른다:
        //    "지금 쓰던 자리 뒤에 한 칸"이 사람이 기대하는 것이다.
        if (TargetChapter(row) is not { } chapterId ||
            _session!.Editor.FindChapter(chapterId) is not { } chapter)
        {
            return;
        }

        // ⚠ Id 짓는 규칙은 편집기 하나다 (2026-09-18) — 화면마다 세던 것을 걷었다.
        string episodeId = _session.Editor.NextEpisodeId(chapterId);
        string? sceneId = row.SceneId ?? chapter.Episodes.LastOrDefault()?.EffectiveSceneId;

        // ⚠ <b>누른 줄이 어디에 붙일지를 정한다</b> (2026-09-18). 에피소드 줄에서 불렀으면
        //    <b>그 뒤에</b> 붙인다 — 이야기를 쓰다가 "여기 한 칸 더"가 그 자리다. 장면 줄에서
        //    불렀으면 그 장면의 끝이다.
        ChapterEpisode? parent = row.Kind == SceneTreeRowKind.Episode
            ? chapter.Episodes.FirstOrDefault(episode =>
                string.Equals(episode.EpisodeId, row.EpisodeId, StringComparison.Ordinal))
            : chapter.Episodes.LastOrDefault(episode =>
                string.Equals(episode.EffectiveSceneId, sceneId, StringComparison.Ordinal))
              ?? chapter.Episodes.LastOrDefault();

        if (parent is null)
        {
            _session.Editor.AddEpisode(chapterId, episodeId, title: string.Empty, 0, 0, sceneId);
        }
        else
        {
            _session.Editor.AddNextEpisode(
                chapterId, parent.EpisodeId, episodeId, title: string.Empty,
                parent.X + 220, parent.Y, optionLabel: "다음", sceneId);
        }

        EpisodeTree.Select(chapterId, episodeId);
        ShowSelected();

        // ⭐ 2026-09-18부터 카드와 대본이 함께 서므로 <b>바로 쓸 수 있다</b> — 전에는
        //    여기서 만든 에피소드만 쓸 자리가 없어 [＋ 대본]을 한 번 더 눌러야 했다.
        _session.SetStatus(
            $"장면 '{sceneId}'에 에피소드 '{episodeId}'를 넣었습니다 — 바로 쓰고 Ctrl+S. " +
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
    /// [에피소드 삭제] (2026-09-18 소유자) — 규칙은 <see cref="EpisodeDeleter"/>가 갖는다.
    /// [챕터 그래프]의 [에피소드 삭제]와 <b>같은 길</b>이고, 여기서 하는 일은 무엇을
    /// 골랐는지 말하고 결과를 상태줄에 옮기는 것뿐이다.
    ///
    /// ⚠ <b>지우는 것이 글이라는 것을 먼저 말한다.</b> 작가의 탭에서 누르는 삭제이므로
    /// 사라지는 것이 줄거리 한 칸이 아니라 <b>원고</b>다 — 원고는 <c>.bak</c>으로 남는다.
    /// </summary>
    private void ConfirmEpisodeDelete(SceneTreeRow row)
    {
        string episodeId = row.EpisodeId!;

        Confirm(
            $"'{episodeId}'과 그 간선·픽스처 참조를 지웁니다. " +
            "원고는 .bak으로 남고, 글이 든 카드는 지우지 않고 떼어만 냅니다.",
            "에피소드 지우기",
            () =>
            {
                EpisodeDeleter.Result result = EpisodeDeleter.Delete(
                    _session!.Editor, _session.ProjectPath, row.ChapterId, episodeId);

                if (!result.Ok)
                {
                    _session.SetStatus(result.Failure!);
                    return;
                }

                // 지운 것을 계속 고르고 있으면 오른쪽에 없는 글이 선다.
                if (EpisodeTree.Selection?.EpisodeId == episodeId)
                {
                    EpisodeTree.ClearSelection();
                }

                ShowSelected();
                _session.SetStatus(result.Describe(episodeId));
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
    private void Confirm(string caution, string confirmText, Action act) =>
        ConfirmButton = ConfirmFlyout.Show(
            EpisodeTree, _session, caution, confirmText, act, closed: () => ConfirmButton = null);

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

        string episodeId = EpisodeNaming.EpisodeIdOf(node);

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
        // ⛔ <b>안 들어간 글은 덮지 않는다</b> (2026-09-17). 포커스만 보던 시절에는 입력칸을
        //    떠난 초고가 <b>다른 탭의 변경 알림 한 번에 사라졌다</b> — 저장이 엔터·[글 반영]에
        //    묶여 있어서 "떠났으면 넣었겠지"가 대개 맞았기 때문이다. 자유롭게 고치는
        //    구조에서는 그 가정이 깨진다: 쓰다 말고 마우스를 옮기는 것이 정상이다.
        if (_session is null || ScriptBox.IsFocused || HasUnsavedText)
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

        return EpisodeNaming.CardFor(_session.Project, chapterId, episodeId);
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

            _inProject = string.Empty;
            ScriptBox.Text = _inProject;
            ScriptBox.IsEnabled = false;
            return;
        }

        string episodeId = picked.EpisodeId;

        HeaderText.Text = episodeId;
        ScriptBox.IsEnabled = true;

        // 사다리의 마지막 칸 — 에피소드는 있는데 아직 아무도 안 썼다.
        if (FindNode(episodeId) is not { } node)
        {
            _inProject = string.Empty;
            ScriptBox.Text = _inProject;

            EmptyPanel.IsVisible = true;
            EmptyAddScriptButton.IsVisible = true;
            EmptyText.Text = $"'{episodeId}'은 아직 빈 대본입니다.";

            return;
        }

        _inProject = ProjectText(node) ?? string.Empty;
        ScriptBox.Text = _inProject;

        ShowUnknownSpeakers(Speakers(node));
    }

    /// <summary>
    /// 프로젝트에 들어 있는 글을 <b>입력칸의 모양으로</b> 그린다 — <b>이 계산은 한 자리다</b>.
    ///
    /// ⚠ 채울 때와 <see cref="HasUnsavedText"/>가 견줄 때가 같은 글을 봐야 한다. 둘이
    /// 갈리면 아무것도 안 고쳤는데 "안 들어간 글이 있다"가 되고, 그러면 다시 그리기가
    /// 영영 막히고 제목의 <c>*</c>가 안 꺼진다.
    ///
    /// ⚠ `ScenarioOnly`는 `includeLineId: false`다 (§6.3) — 화면에 `#line:` 태그가 보이면
    /// 지저분하고 작가가 지운다. 신원은 diff가 붙들므로 안 보여도 안전하다.
    /// </summary>
    private string? ProjectText(DialogueNode node) =>
        _session is null
            ? null
            : DocumentPreviewFormatter.Format(WorkingDialoguePreview.ComposePreset(
                _session.Project, node.Id, OutputPresetCatalog.ScenarioOnly, _session.Definition));

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

        _session.SetStatus($"'{episodeId}' 대본을 세웠습니다 — 쓰고 Ctrl+S로 저장하세요.");
    }

    /// <summary>
    /// 쓴 글을 대사로 넣는다 — <b>저장의 첫 절반</b>이다.
    ///
    /// ⛔ <b>Ctrl+S가 이것을 먼저 부른다</b> (2026-09-17 소유자). 전에는 그러지 않았고,
    /// 그것이 조용한 사고였다: 입력칸에 글을 쓰고 Ctrl+S를 누르면 <b>상태줄이 저장했다고
    /// 말하는데 쓴 글은 프로젝트에 없었다</b>. 글은 <see cref="ProjectEditor.ApplyScenarioText"/>를
    /// 지나야 프로젝트에 들어가는데 저장은 프로젝트만 쓰기 때문이다.
    ///
    /// ⚠ <b>지우기는 두 번 눌러야 한다.</b> 붙여넣기 한 번이 줄을 통째로 날릴 수 있고,
    /// 그 줄에는 연출이 매달려 있다 — 같은 글로 한 번 더 누르면 그때 지운다
    /// (연출 그래프의 [텍스트 반영]과 같은 규율이다).
    /// </summary>
    internal ScriptSaveOutcome SaveText()
    {
        if (_session is null || EpisodeTree.Selection is not { } pick)
        {
            return ScriptSaveOutcome.Nothing;
        }

        // 고친 데가 없으면 지나간다 — 저장마다 같은 글을 되넣으면 되돌리기가 그것으로 찬다.
        if (!HasUnsavedText)
        {
            return ScriptSaveOutcome.Nothing;
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
                _session.SetStatus("빈 글은 넣지 않습니다 — 한 줄이라도 쓰고 저장해 주세요.");
                return ScriptSaveOutcome.Refused;
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
                $"{outcome.Summary()} — 같은 글로 한 번 더 저장하면 지웁니다(Ctrl+S 또는 [저장]).");
            return ScriptSaveOutcome.NeedsConfirmation;
        }

        _pendingDeleteText = null;

        // 방금 쓴 이름이야말로 미등록이기 쉽다 — 파서가 이미 가려 놓은 것을 그대로 쓴다.
        ShowUnknownSpeakers(outcome.Parsed.Lines
            .Where(line => line.SpeakerUnregistered)
            .Select(line => line.Speaker));

        if (!outcome.Applied)
        {
            _session.SetStatus(outcome.Summary());
            return ScriptSaveOutcome.Refused;
        }

        // ⚠ <b>들어간 글을 여기서 기억한다.</b> 입력칸의 글자 그대로가 아니라 <b>프로젝트를
        //    다시 그린 글</b>이어야 한다 — 파서가 다듬는 자리가 있어서(빈 줄·공백·화자 표기)
        //    입력칸 글을 그대로 기준으로 잡으면 넣은 직후에도 "안 들어간 글이 있다"가 된다.
        _inProject = ProjectText(node) ?? text;
        ScriptBox.Text = _inProject;

        _session.SetStatus($"글을 저장했습니다. {outcome.Summary()}{EmitWorkbook(node)}");

        return ScriptSaveOutcome.Saved;
    }

    /// <summary>
    /// 이 에피소드의 대사노드를 세운다 — 작가가 처음 글을 쓴 그 순간에.
    ///
    /// ⚠ 이름 규칙은 임포터·[＋ 에피소드]와 <b>같아야 한다</b>(대사엔트리, 없으면 EpisodeId).
    /// 어긋나면 같은 에피소드에 노드가 둘이 되고, 내보내기가 "번들 이름이 겹칩니다"로 막는다.
    /// </summary>
    private DialogueNode CreateNodeFor(string episodeId)
    {
        string chapterId = EpisodeTree.Selection!.ChapterId;
        string fileId = _session!.Editor.EnsureChapterBoard(chapterId);

        // 새 카드는 <b>제 장면의 줄</b>에 선다 (R7 P-4) — 원점에 쌓이면 챕터 프레임도
        // 장면 영역도 뜻을 잃는다. 규칙은 세 창구가 함께 쓴다.
        (double x, double y) = Vn.Authoring.Graph.NodePlacement.For(_session.Project, chapterId, episodeId);
        // ⛔ 여기서 표식(`MarkedEpisodeId`)을 직접 붙이던 줄은 2026-09-18에 걷혔다 —
        //    이름이 곧 EpisodeId이므로 편집기의 `NewEpisodeFor`가 이미 같은 값을 넣는다.
        //    표식의 규칙은 한 자리에만 있어야 한다.
        DialogueNode created = _session.Editor.AddDialogueNode(fileId, x, y, episodeId);

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
        string episodeId = EpisodeNaming.EpisodeIdOf(node);

        ChapterWriteResult written = EpisodeScriptOutput.Write(
            _session.Project, node, EpisodeLibrary.PathFor(folder, episodeId));

        return written.Written ? string.Empty : $" ⚠ {written.Failure}";
    }
}
