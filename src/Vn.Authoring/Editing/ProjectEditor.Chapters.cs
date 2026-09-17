using Vn.Authoring.Model;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Editing;

/// <summary>
/// <b>챕터를 바꾸는 코드는 이 파일 안에만 있다</b> (R-F · 2026-09-16, 지시서 §1.1·§4.1).
///
/// <c>ProjectEditor.Scripts.cs</c>가 대사에 대해 하는 일을 챕터 구조에 대해 한다 — 그래야
/// <i>"한 값을 고쳤을 때 권위 있는 데이터가 하나만 바뀐다"</i>가 코드 구조로 보장된다.
///
/// ⛔ <b>여기는 워크북을 모른다.</b> <see cref="ChapterWorkbookWriter"/>는 셀 하나를 고치는
/// 외과수술이었고 그래서 열 번호·시트 이름·행 삭제가 부르는 쪽까지 스며 있었다. 이제 고치는
/// 것은 <b>모델</b>이고, 파일은 <see cref="ChapterWorkbookEmitter"/>가 통째로 낸다 — 삽입·
/// 삭제·인덱스 충돌이라는 난제가 그 경계에서 사라진다(§4.2).
///
/// ⚠ <b>규칙은 옛 창구에서 그대로 옮겨 왔다.</b> 간선의 신원은 (출발, 도착, 문구)이고(v9),
/// 개명은 간선·픽스처를 함께 끌고 가며, 삭제는 끝점 간선을 함께 걷고, <b>선택지 사전은
/// 건드리지 않는다</b>(어휘집이지 배선이 아니다). 바뀐 것은 <b>어디에 쓰는가</b>뿐이다.
///
/// ⚠ 실패는 <see cref="InvalidOperationException"/>이다 — 옛 창구와 같다. 부르는 쪽이
/// <c>UiGuard</c>로 받아 상태줄에 말한다.
/// </summary>
public sealed partial class ProjectEditor
{
    /// <summary>그 Id의 챕터. 없으면 null — 부르는 쪽이 "아직 안 들여왔다"를 구분한다.</summary>
    public ChapterDocument? FindChapter(string chapterId) =>
        Project.Chapters.FirstOrDefault(chapter =>
            string.Equals(chapter.ChapterId, chapterId, StringComparison.Ordinal));

    private ChapterDocument RequireChapter(string chapterId) =>
        FindChapter(chapterId)
        ?? throw new InvalidOperationException($"챕터 '{chapterId}'가 프로젝트에 없습니다.");

    /// <summary>빈 챕터 하나를 세운다. 같은 Id가 이미 있으면 그것을 돌려준다(멱등).</summary>
    public ChapterDocument EnsureChapter(string chapterId)
    {
        if (FindChapter(chapterId) is { } existing)
        {
            return existing;
        }

        var chapter = new ChapterDocument { ChapterId = chapterId };
        Mutate(() => Project.Chapters.Add(chapter));

        return chapter;
    }

    /// <summary>
    /// 챕터와 <b>그 판</b>을 한 번의 변경으로 걷는다 — 되돌리기 한 번에 둘 다 돌아온다.
    /// </summary>
    /// <returns>판과 함께 걷힌 노드 수. 지울 것이 없었으면 0.</returns>
    /// <remarks>
    /// ⛔ <b>R-F가 남긴 구멍을 메운다</b> (2026-09-16). 뒤집기 전에는 챕터가 곧 워크북이라
    /// 판만 걷으면 됐는데, 이제 챕터는 <c>Project.Chapters</c>에 산다 — 판만 걷으면
    /// <b>목록·트리·검증에 그대로 남고 다음 저장이 워크북을 되살린다</b>.
    /// </remarks>
    public int RemoveChapterWithBoard(string chapterId)
    {
        ChapterDocument? chapter = FindChapter(chapterId);
        StoryFile? board = Project.Files.FirstOrDefault(file =>
            string.Equals(file.Name, chapterId, StringComparison.Ordinal));

        if (chapter is null && board is null)
        {
            return 0;
        }

        if (board is not null && Project.Files.Count <= 1)
        {
            throw new InvalidOperationException(
                "마지막 시나리오 파일은 제거할 수 없습니다 — 새 노드가 갈 곳이 없어집니다.");
        }

        int nodes = board?.Nodes.Count ?? 0;

        Mutate(() =>
        {
            if (chapter is not null)
            {
                Project.Chapters.Remove(chapter);
            }

            if (board is not null)
            {
                RemoveFileCore(board);
            }
        });

        return nodes;
    }

    /// <summary>챕터 하나를 걷는다. ⚠ <b>판은 남는다</b> — 둘 다면 <see cref="RemoveChapterWithBoard"/>.</summary>
    public void RemoveChapter(string chapterId)
    {
        if (FindChapter(chapterId) is not { } chapter)
        {
            return;
        }

        Mutate(() => Project.Chapters.Remove(chapter));
    }

    public void RenameChapter(string oldId, string newId)
    {
        ChapterDocument chapter = RequireChapter(oldId);

        if (FindChapter(newId) is not null)
        {
            throw new InvalidOperationException($"챕터 '{newId}'가 이미 있습니다.");
        }

        Mutate(() => chapter.ChapterId = newId);
    }

    // ── 에피소드 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 에피소드 하나를 세운다.
    ///
    /// ⚠ <b>대사엔트리 = EpisodeId</b>가 기본이다(v3 규약) — 옛 창구가 B열에 같은 값을 적던
    /// 그 규칙이고, 임포터·[대본] 탭의 노드 이름 규칙도 같은 값을 본다. 어긋나면 같은
    /// 에피소드에 대사노드가 둘이 된다.
    /// </summary>
    /// <param name="sceneId">
    /// 어느 장면에 놓을지. ⚠ <b>만들면서 함께 정한다</b> — 만든 뒤에 <see cref="UpdateEpisode"/>로
    /// 옮기면 되돌리기가 두 걸음이 되고, 그 사이 상태(장면 없는 에피소드)가 잠깐 진짜가 된다.
    /// </param>
    public ChapterEpisode AddEpisode(
        string chapterId, string episodeId, string title, double x, double y, string? sceneId = null)
    {
        ChapterDocument chapter = RequireChapter(chapterId);

        if (FindEpisode(chapter, episodeId) is not null)
        {
            throw new InvalidOperationException($"EpisodeId '{episodeId}'가 이미 있습니다.");
        }

        var episode = new ChapterEpisode(
            episodeId, title, Index: string.Empty, DialogueEntry: episodeId,
            Math.Round(x, 2), Math.Round(y, 2), Memo: null, SourceRow: 0)
        {
            SceneId = sceneId
        };

        Mutate(() => chapter.Episodes.Add(episode));
        return episode;
    }

    /// <summary>
    /// 다음 에피소드 — 분기 저작의 핵심 동작. 에피소드와 간선이 <b>한 번의 변경</b>이다
    /// (둘 중 하나만 선 채로 남지 않는다).
    /// </summary>
    public ChapterEpisode AddNextEpisode(
        string chapterId,
        string parentEpisodeId,
        string newEpisodeId,
        string title,
        double x,
        double y,
        string? optionLabel = null,
        string? sceneId = null)
    {
        ChapterDocument chapter = RequireChapter(chapterId);

        if (FindEpisode(chapter, newEpisodeId) is not null)
        {
            throw new InvalidOperationException($"EpisodeId '{newEpisodeId}'가 이미 있습니다.");
        }

        if (FindEpisode(chapter, parentEpisodeId) is null)
        {
            throw new InvalidOperationException($"부모 에피소드 '{parentEpisodeId}'가 없습니다.");
        }

        var episode = new ChapterEpisode(
            newEpisodeId, title, Index: string.Empty, DialogueEntry: newEpisodeId,
            Math.Round(x, 2), Math.Round(y, 2), Memo: null, SourceRow: 0)
        {
            SceneId = sceneId
        };

        ChapterEdge edge = NewEdge(chapter, parentEpisodeId, newEpisodeId, optionLabel);

        Mutate(() =>
        {
            chapter.Episodes.Add(episode);
            chapter.Edges.Add(edge);
            LearnChoiceLabel(chapter, optionLabel);
        });

        return episode;
    }

    /// <summary>속성 패널의 저장 — null이 아닌 것만 바꾼다.</summary>
    public void UpdateEpisode(
        string chapterId,
        string episodeId,
        string? title = null,
        string? dialogueEntry = null,
        string? memo = null,
        bool? allowUnreachable = null,
        string? sceneId = null)
    {
        ChapterDocument chapter = RequireChapter(chapterId);
        int index = RequireEpisodeIndex(chapter, episodeId);

        Mutate(() => chapter.Episodes[index] = chapter.Episodes[index] with
        {
            Title = title ?? chapter.Episodes[index].Title,
            DialogueEntry = dialogueEntry ?? chapter.Episodes[index].DialogueEntry,
            Memo = memo ?? chapter.Episodes[index].Memo,
            AllowUnreachable = allowUnreachable ?? chapter.Episodes[index].AllowUnreachable,
            SceneId = sceneId ?? chapter.Episodes[index].SceneId
        });
    }

    /// <summary>
    /// <b>에피소드 몇을 다른 챕터로 옮긴다</b> — 탐색기에서 장면을 통째로 끌어다 놓는 일이
    /// 여기로 온다 (2026-09-16 소유자).
    ///
    /// 함께 가는 것: <b>장면ID</b>(그대로 두면 장면 이름이 따라간다) · <b>안쪽 간선</b> ·
    /// <b>연출 그래프의 대사 노드</b>(그래야 글이 따라간다 — 노드가 대본을 들고 있다).
    ///
    /// ⛔ <b>가로지르게 된 간선은 걷는다.</b> 간선은 챕터 안의 길이라 두 챕터를 이을 수 없다.
    /// 조용히 지우지 않고 <b>몇 개였는지 돌려준다</b> — 부르는 쪽이 사람에게 말한다.
    ///
    /// ⚠ 도착 챕터에 같은 Id가 있으면 <b>하나도 안 옮긴다</b>. 겹치는 것만 빼고 옮기면
    /// 그 장면이 두 챕터에 갈려 앉는다.
    /// </summary>
    /// <param name="sceneId">
    /// 도착에서의 장면. null이면 <b>지금 장면을 그대로</b> 들고 간다(장면째 옮기기).
    /// </param>
    /// <returns>걷힌 간선 수.</returns>
    public int MoveEpisodesToChapter(
        string fromChapterId,
        IReadOnlyCollection<string> episodeIds,
        string toChapterId,
        string? sceneId = null)
    {
        ArgumentNullException.ThrowIfNull(episodeIds);

        ChapterDocument from = RequireChapter(fromChapterId);
        ChapterDocument to = RequireChapter(toChapterId);

        var wanted = new HashSet<string>(episodeIds, StringComparer.Ordinal);

        List<ChapterEpisode> moving = from.Episodes
            .Where(episode => wanted.Contains(episode.EpisodeId))
            .ToList();

        if (moving.Count == 0)
        {
            return 0;
        }

        if (ReferenceEquals(from, to))
        {
            // 같은 챕터 안이면 옮길 것이 없다 — 장면만 다시 긋는다.
            if (sceneId is not null)
            {
                UpdateEpisodeScenes(fromChapterId, moving.Select(episode => episode.EpisodeId).ToList(), sceneId);
            }

            return 0;
        }

        if (moving.Where(episode => FindEpisode(to, episode.EpisodeId) is not null)
                .Select(episode => episode.EpisodeId).ToList() is { Count: > 0 } clash)
        {
            throw new InvalidOperationException(
                $"'{toChapterId}'에 같은 Id가 이미 있습니다: {string.Join(", ", clash)}. " +
                "이름을 먼저 바꿔 주세요.");
        }

        var ids = new HashSet<string>(
            moving.Select(episode => episode.EpisodeId), StringComparer.Ordinal);

        List<ChapterEdge> inside = from.Edges
            .Where(edge => ids.Contains(edge.FromEpisodeId) && ids.Contains(edge.ToEpisodeId))
            .ToList();

        List<ChapterEdge> cut = from.Edges
            .Where(edge => ids.Contains(edge.FromEpisodeId) ^ ids.Contains(edge.ToEpisodeId))
            .ToList();

        // 도착 판의 오른쪽에 붙인다 — 그대로 두면 남의 카드 위에 겹쳐 앉는다.
        double shift = to.Episodes.Count == 0
            ? 0
            : to.Episodes.Max(episode => episode.X) + 260 - moving.Min(episode => episode.X);

        StoryFile? fromBoard = BoardOf(fromChapterId);

        List<DialogueNode> nodes = fromBoard?.Nodes.OfType<DialogueNode>()
            .Where(node => ids.Contains(
                EpisodeNaming.EpisodeIdOf(node)))
            .ToList() ?? [];

        // ⚠ Mutate 밖에서 보장한다 — 안에서 부르면 변경이 겹쳐 쌓인다.
        StoryFile? toBoard = nodes.Count > 0 ? RequireFile(EnsureChapterBoard(toChapterId)) : null;

        Mutate(() =>
        {
            foreach (ChapterEpisode episode in moving)
            {
                from.Episodes.Remove(episode);
            }

            foreach (ChapterEdge edge in inside.Concat(cut))
            {
                from.Edges.Remove(edge);
            }

            foreach (ChapterEpisode episode in moving)
            {
                to.Episodes.Add(episode with
                {
                    X = Math.Round(episode.X + shift, 2),
                    SceneId = sceneId ?? episode.SceneId
                });
            }

            to.Edges.AddRange(inside);

            foreach (DialogueNode node in nodes)
            {
                fromBoard!.Nodes.Remove(node);
                toBoard!.Nodes.Add(node);
            }
        });

        return cut.Count;
    }

    private StoryFile? BoardOf(string chapterId) =>
        Project.Files.FirstOrDefault(file =>
            string.Equals(file.Name, chapterId, StringComparison.Ordinal));

    /// <summary>여러 에피소드의 장면ID를 한 번에 — 장면 경계는 묶어서 긋는 값이다.</summary>
    public void UpdateEpisodeScenes(
        string chapterId, IReadOnlyCollection<string> episodeIds, string sceneId)
    {
        ChapterDocument chapter = RequireChapter(chapterId);

        List<int> targets = episodeIds.Distinct(StringComparer.Ordinal)
            .Select(episodeId => RequireEpisodeIndex(chapter, episodeId))
            .ToList();

        Mutate(() =>
        {
            foreach (int index in targets)
            {
                chapter.Episodes[index] = chapter.Episodes[index] with { SceneId = sceneId };
            }
        });
    }

    /// <summary>
    /// 에피소드의 자리 — 그래프에서 끌어 옮긴 결과.
    ///
    /// ⚠ 자리는 <b>구조가 아니다</b>. 되돌리기 목록이 드래그 한 번마다 부풀면 사람이 진짜
    /// 되돌리고 싶은 편집까지 밀려 나간다 — 그래서 여기만 <c>NodeMetadata</c>로 알린다.
    /// </summary>
    public void MoveEpisode(string chapterId, string episodeId, double x, double y)
    {
        ChapterDocument chapter = RequireChapter(chapterId);
        int index = RequireEpisodeIndex(chapter, episodeId);

        Mutate(ProjectChangeKind.NodeMetadata, () =>
            chapter.Episodes[index] = chapter.Episodes[index] with
            {
                X = Math.Round(x, 2),
                Y = Math.Round(y, 2)
            });
    }

    /// <summary>
    /// EpisodeId 개명. <b>간선의 출발·도착과 픽스처 고정 선택이 함께 따라간다</b> — 신원이
    /// 바뀌었는데 참조가 남으면 유령 간선이 된다.
    ///
    /// ⛔ 폐지된 <c>cleared:</c>를 참조하는 조건식이 있으면 <b>막는다</b>. 식은 사람 소유라
    /// 툴이 고쳐 주지 않고(자동 추측 금지), 개명으로 참조만 더 낡게 만들 이유가 없다.
    /// </summary>
    public void RenameEpisode(string chapterId, string oldId, string newId)
    {
        ChapterDocument chapter = RequireChapter(chapterId);

        if (chapter.Conditions.FirstOrDefault(condition =>
                condition.Expression.Contains($"cleared:{oldId}", StringComparison.Ordinal))
            is { } stale)
        {
            throw new InvalidOperationException(
                $"조건 '{stale.Label}'의 식이 cleared:{oldId}를 참조합니다. " +
                "cleared: 는 2026-08-25에 폐지됐습니다 — [조건]에서 Bool 스탯으로 " +
                "먼저 바꿔 주세요. 조건식은 사람 소유라 툴이 고치지 않습니다.");
        }

        int index = RequireEpisodeIndex(chapter, oldId);

        if (FindEpisode(chapter, newId) is not null)
        {
            throw new InvalidOperationException($"EpisodeId '{newId}'가 이미 있습니다.");
        }

        Mutate(() =>
        {
            ChapterEpisode episode = chapter.Episodes[index];

            chapter.Episodes[index] = episode with
            {
                EpisodeId = newId,

                // 대사엔트리 = EpisodeId 규약(v3)을 따르던 것만 함께 간다 —
                // 사람이 다르게 적어 둔 값은 건드리지 않는다.
                DialogueEntry = string.Equals(episode.DialogueEntry, oldId, StringComparison.Ordinal)
                    ? newId
                    : episode.DialogueEntry
            };

            Retarget(chapter, oldId, newId);
        });
    }

    /// <summary>
    /// 에피소드 삭제 — 그 에피소드를 끝점으로 하는 간선과 픽스처 고정 선택도 함께 걷는다.
    ///
    /// ⚠ <b>선택지 사전은 건드리지 않는다</b>(v9). 챕터 전체의 어휘라, 에피소드 하나가
    /// 사라졌다고 낱말을 지우면 다른 에피소드의 드롭다운에서도 사라진다.
    /// </summary>
    public void RemoveEpisode(string chapterId, string episodeId)
    {
        ChapterDocument chapter = RequireChapter(chapterId);
        int index = RequireEpisodeIndex(chapter, episodeId);

        Mutate(() =>
        {
            chapter.Episodes.RemoveAt(index);

            chapter.Edges.RemoveAll(edge =>
                string.Equals(edge.FromEpisodeId, episodeId, StringComparison.Ordinal) ||
                string.Equals(edge.ToEpisodeId, episodeId, StringComparison.Ordinal));

            Retarget(chapter, episodeId, null);
        });
    }

    // ── 간선 ────────────────────────────────────────────────────────────────
    // v9 — 길 하나가 곧 선택지 하나이고, 신원은 (출발, 도착, 문구)다.

    public ChapterEdge AddEdge(
        string chapterId,
        string fromEpisodeId,
        string toEpisodeId,
        string? conditionLabel = null,
        string? optionLabel = null,
        string? statChanges = null,
        bool auto = false)
    {
        ChapterDocument chapter = RequireChapter(chapterId);
        ChapterEdge edge = NewEdge(chapter, fromEpisodeId, toEpisodeId, optionLabel, conditionLabel, statChanges, auto);

        Mutate(() =>
        {
            chapter.Edges.Add(edge);
            LearnChoiceLabel(chapter, optionLabel);
        });

        return edge;
    }

    /// <summary>
    /// 선택지 한 줄의 배선을 고친다 — 문구와 도착을 한 변경으로.
    /// 찾을 때는 고치기 <b>전</b>의 신원을 쓴다.
    /// </summary>
    public void SetEdgeRoute(
        string chapterId,
        string fromEpisodeId,
        string currentToEpisodeId,
        string? currentOptionLabel,
        string newToEpisodeId,
        string? newOptionLabel,
        string? statChanges = null,
        bool? auto = null)
    {
        ChapterDocument chapter = RequireChapter(chapterId);
        int index = RequireEdgeIndex(chapter, fromEpisodeId, currentToEpisodeId, currentOptionLabel);

        Mutate(() =>
        {
            ChapterEdge edge = chapter.Edges[index];

            chapter.Edges[index] = edge with
            {
                ToEpisodeId = newToEpisodeId,
                OptionLabel = newOptionLabel,
                StatChanges = statChanges is null ? edge.StatChanges : ParseDeltas(chapter, statChanges),
                Auto = auto ?? edge.Auto
            };

            LearnChoiceLabel(chapter, newOptionLabel);
        });
    }

    /// <summary>간선 한 줄의 속성 편집 — null이 아닌 것만 바꾼다 (관문 둘 포함, v8).</summary>
    public void UpdateEdge(
        string chapterId,
        string fromEpisodeId,
        string toEpisodeId,
        string? conditionLabel = null,
        string? lockedMessage = null,
        string? statChanges = null,
        string? matchOptionLabel = null,
        string? visibleConditionLabel = null,
        string? optionLabel = null,
        bool? auto = null)
    {
        ChapterDocument chapter = RequireChapter(chapterId);
        int index = RequireEdgeIndex(chapter, fromEpisodeId, toEpisodeId, matchOptionLabel);

        Mutate(() =>
        {
            ChapterEdge edge = chapter.Edges[index];

            chapter.Edges[index] = edge with
            {
                ConditionLabel = conditionLabel ?? edge.ConditionLabel,
                VisibleConditionLabel = visibleConditionLabel ?? edge.VisibleConditionLabel,
                LockedMessage = lockedMessage ?? edge.LockedMessage,
                StatChanges = statChanges is null ? edge.StatChanges : ParseDeltas(chapter, statChanges),
                OptionLabel = optionLabel ?? edge.OptionLabel,
                Auto = auto ?? edge.Auto
            };

            LearnChoiceLabel(chapter, optionLabel);
        });
    }

    /// <summary>간선 삭제 — 사전의 문구는 남는다(다른 길에서 또 쓴다).</summary>
    public void RemoveEdge(
        string chapterId, string fromEpisodeId, string toEpisodeId, string? optionLabel = null)
    {
        ChapterDocument chapter = RequireChapter(chapterId);
        int index = RequireEdgeIndex(chapter, fromEpisodeId, toEpisodeId, optionLabel);

        Mutate(() => chapter.Edges.RemoveAt(index));
    }

    // ── 선택지 사전 · 조건 ──────────────────────────────────────────────────

    /// <summary>선택지 사전에 문구 한 줄 — 어느 에피소드의 것도 아니다 (v9).</summary>
    public void AddChoiceLabel(string chapterId, string text)
    {
        ChapterDocument chapter = RequireChapter(chapterId);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("선택지 문구가 비어 있습니다.");
        }

        if (FindChoice(chapter, text) is not null)
        {
            throw new InvalidOperationException($"'{text}'는 이미 선택지 사전에 있습니다.");
        }

        Mutate(() => LearnChoiceLabel(chapter, text));
    }

    /// <summary>
    /// 선택지 사전에서 문구 한 줄 지우기. <b>간선은 건드리지 않는다</b> — 사전은 어휘집이지
    /// 배선이 아니라서, 이미 그 문구를 쓰고 있는 길은 그대로 산다(드롭다운에서만 사라진다).
    /// </summary>
    public void RemoveChoiceLabel(string chapterId, string text)
    {
        ChapterDocument chapter = RequireChapter(chapterId);

        ChapterChoiceOption option = FindChoice(chapter, text)
            ?? throw new InvalidOperationException($"선택지 사전에 '{text}'가 없습니다.");

        Mutate(() => chapter.ChoiceOptions.Remove(option));
    }

    /// <summary>
    /// 챕터 조건 하나 — 간선의 표시조건·해금조건이 라벨로 가리킨다.
    ///
    /// ⚠ <b>이름에 "Chapter"가 붙은 이유</b>: <c>AddCondition</c>은 이미 <b>설정노드</b>의
    /// 창구다. 둘은 다른 것이다 — 저쪽은 대사 안의 [3] 조건이 태어나는 자리이고, 이쪽은
    /// 챕터 간선이 보는 [2] 진행 스탯이다(지시서 §3.2). 같은 이름으로 두면 인자 개수만
    /// 다른 호출이 <b>조용히 저쪽으로 간다</b> — 실제로 이 테스트를 쓰다 한 번 물렸다.
    /// </summary>
    public void AddChapterCondition(string chapterId, string label, string expression, string? description = null)
    {
        ChapterDocument chapter = RequireChapter(chapterId);

        if (FindCondition(chapter, label) is not null)
        {
            throw new InvalidOperationException($"조건 라벨 '{label}'이 이미 있습니다.");
        }

        Mutate(() => chapter.Conditions.Add(
            new ChapterCondition(label, expression, description, Parsed: [], IsValid: false, SourceRow: 0)));
    }

    /// <summary>챕터 조건의 식·설명을 고친다 — 설정노드의 <c>UpdateCondition</c>과 다른 것이다.</summary>
    public void UpdateChapterCondition(string chapterId, string label, string expression, string? description = null)
    {
        ChapterDocument chapter = RequireChapter(chapterId);

        ChapterCondition condition = FindCondition(chapter, label)
            ?? throw new InvalidOperationException($"조건 라벨 '{label}'이 없습니다.");

        int index = chapter.Conditions.IndexOf(condition);

        Mutate(() => chapter.Conditions[index] = condition with
        {
            Expression = expression,
            Description = description ?? condition.Description,

            // ⚠ 해석은 담지 않는다 — ToGraphModel이 식에서 다시 푼다. 여기서 옛 해석을
            //    들고 있으면 검증이 고치기 전 값으로 참을 말한다.
            Parsed = [],
            IsValid = false
        });
    }


    /// <summary>
    /// <b>스탯 개명이 참조를 끌고 간다</b> (R7 후속 · 2026-09-17 소유자 — 계층을 하나로).
    ///
    /// ⛔ <b>개명이 쉬워지는 것이 곧 위험이다.</b> 지금까지 스탯 키는 엑셀에 살아서 고치기가
    /// 무서웠고, 그래서 아무도 안 고쳤다. 툴이 정의를 쥐면 한 번에 쉬워지는데 —
    /// <b>쉬운데 안전하지 않은 것이 어려운데 안전하지 않은 것보다 나쁘다.</b>
    ///
    /// 키가 사는 자리는 넷이고 <b>한 번의 변경</b>으로 함께 간다:
    ///
    /// <list type="number">
    /// <item><c>스탯</c> 정의 행 — 표시이름이 키와 같았으면 그것도 따라간다(안 정한 이름이다).</item>
    /// <item>간선의 <c>스탯변화</c> — 해석된 채로 살아서 키만 갈면 된다.</item>
    /// <item>챕터 <c>조건</c> 식 — 원문이라 문법을 아는 <see cref="ConditionExpressionParser.ReplaceStatKey"/>가 간다.</item>
    /// <item>⭐ <b>판에 공급된 조건</b> — 아래.</item>
    /// </list>
    ///
    /// ⚠ <b>넷째를 빠뜨리면 대사 갈래가 통째로 고아가 된다.</b> 판의 공급 노드는 챕터 조건을
    /// 번역해 들고 있고(<c>stat("trust") &gt;= 3</c>), 줄에 매달린 전환은 그 조건의 <b>Id</b>로
    /// 잇는다. 그런데 <c>ChapterBoardSupply.RenameSuppliedCondition</c>은 <b>식으로 짝을
    /// 찾는다</b> — 스탯을 갈면 식이 달라져 짝을 못 찾고, 다음 동기화가 <b>새 Id로 다시
    /// 만든다</b>. 그 자리 주석이 경고하는 바로 그 고아다. 그래서 여기서 <b>Id를 지킨 채</b>
    /// 식만 갈아 끼운다.
    ///
    /// ⚠ <c>game.definition.json</c>의 변수는 <b>안 건드린다</b>. 그쪽은 게임 전역 어휘이고
    /// 챕터 스탯은 만들 때 거기서 씨앗만 받는다 — 남의 이름이다.
    /// </summary>
    /// <returns>바꾼 자리의 수. 거절되면 <c>Applied</c>가 false이고 아무것도 안 건드렸다.</returns>
    public StatRenameOutcome RenameChapterStat(string chapterId, string oldKey, string newKey)
    {
        ChapterDocument chapter = RequireChapter(chapterId);

        string from = (oldKey ?? string.Empty).Trim();
        string to = (newKey ?? string.Empty).Trim();

        if (from.Length == 0 || to.Length == 0)
        {
            return StatRenameOutcome.Refuse("스탯 키가 비어 있습니다.");
        }

        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return StatRenameOutcome.Refuse("같은 이름입니다 — 바꿀 것이 없습니다.");
        }

        int index = chapter.Stats.FindIndex(stat =>
            string.Equals(stat.Key, from, StringComparison.Ordinal));

        if (index < 0)
        {
            return StatRenameOutcome.Refuse($"스탯 '{from}'이 이 챕터에 없습니다.");
        }

        if (chapter.Stats.Any(stat => string.Equals(stat.Key, to, StringComparison.Ordinal)))
        {
            return StatRenameOutcome.Refuse($"스탯 '{to}'가 이미 있습니다 — 둘을 합치려면 먼저 하나를 지우세요.");
        }

        // ⚠ 내보낼 때 Yarn 식별자로 정규화되므로, 정규화 뒤에 겹치면 <b>대사에서 같은
        //    스탯이 된다</b>. 화면에서는 달라 보이는데 게임에서 하나가 되는 자리라 막는다.
        string normalized = Rendering.YarnSyntax.SanitizeVariableName(to);

        if (chapter.Stats.Any(stat =>
                !string.Equals(stat.Key, from, StringComparison.Ordinal) &&
                string.Equals(Rendering.YarnSyntax.SanitizeVariableName(stat.Key), normalized, StringComparison.Ordinal)))
        {
            return StatRenameOutcome.Refuse(
                $"'{to}'는 Yarn에서 '{normalized}'가 되어 이미 있는 스탯과 겹칩니다.");
        }

        ChapterStat renamed = chapter.Stats[index] with
        {
            Key = to,

            // 표시이름을 따로 안 정했으면(= 키와 같았으면) 함께 간다. 정해 뒀으면 사람의 글자다.
            DisplayName = string.Equals(chapter.Stats[index].DisplayName, from, StringComparison.Ordinal)
                ? to
                : chapter.Stats[index].DisplayName
        };

        var edges = new List<(int Index, ChapterEdge Edge)>();

        for (int i = 0; i < chapter.Edges.Count; i++)
        {
            if (!chapter.Edges[i].StatChanges.Any(delta =>
                    string.Equals(delta.Key, from, StringComparison.Ordinal)))
            {
                continue;
            }

            edges.Add((i, chapter.Edges[i] with
            {
                StatChanges = chapter.Edges[i].StatChanges
                    .Select(delta => string.Equals(delta.Key, from, StringComparison.Ordinal)
                        ? delta with { Key = to }
                        : delta)
                    .ToList()
            }));
        }

        var conditions = new List<(int Index, ChapterCondition Condition)>();

        for (int i = 0; i < chapter.Conditions.Count; i++)
        {
            string rewritten = ConditionExpressionParser.ReplaceStatKey(
                chapter.Conditions[i].Expression, from, to);

            if (string.Equals(rewritten, chapter.Conditions[i].Expression, StringComparison.Ordinal))
            {
                continue;
            }

            conditions.Add((i, chapter.Conditions[i] with
            {
                Expression = rewritten,

                // ⚠ 옛 해석을 들고 있으면 검증이 고치기 전 값으로 참을 말한다 —
                //    `UpdateChapterCondition`과 같은 규율이다. ToGraphModel이 다시 푼다.
                Parsed = [],
                IsValid = false
            }));
        }

        List<ConditionDefinition> supplied = SuppliedConditionsReading(chapterId, from);

        Mutate(() =>
        {
            chapter.Stats[index] = renamed;

            foreach ((int at, ChapterEdge edge) in edges)
            {
                chapter.Edges[at] = edge;
            }

            foreach ((int at, ChapterCondition condition) in conditions)
            {
                chapter.Conditions[at] = condition;
            }

            foreach (ConditionDefinition condition in supplied)
            {
                // Id를 지킨 채 식만 간다 — 줄에 매달린 전환이 이 Id로 잇는다.
                condition.Expression = condition.Expression.Replace(
                    Rendering.YarnSyntax.StatRead(from),
                    Rendering.YarnSyntax.StatRead(to),
                    StringComparison.Ordinal);
            }
        });

        return new StatRenameOutcome(true, edges.Count, conditions.Count, supplied.Count, null);
    }

    /// <summary>
    /// 그 챕터 판의 공급 노드가 들고 있는 조건 중 <b>이 스탯을 읽는</b> 것들.
    /// 판이 없거나 공급 노드가 아직 없으면 빈 목록이다.
    /// </summary>
    private List<ConditionDefinition> SuppliedConditionsReading(string chapterId, string statKey)
    {
        string read = Rendering.YarnSyntax.StatRead(statKey);

        return Project.EnumerateNodes().OfType<SetNode>()
            .Where(node => ChapterBoardSupply.IsConditionSupplyNodeName(node.Name))
            .SelectMany(node => node.Conditions)
            .Where(condition => condition.Expression.Contains(read, StringComparison.Ordinal))
            .ToList();
    }
    // ── 잔손 ────────────────────────────────────────────────────────────────

    private static ChapterEpisode? FindEpisode(ChapterDocument chapter, string episodeId) =>
        chapter.Episodes.FirstOrDefault(episode =>
            string.Equals(episode.EpisodeId, episodeId, StringComparison.Ordinal));

    private static int RequireEpisodeIndex(ChapterDocument chapter, string episodeId)
    {
        int index = chapter.Episodes.FindIndex(episode =>
            string.Equals(episode.EpisodeId, episodeId, StringComparison.Ordinal));

        return index >= 0
            ? index
            : throw new InvalidOperationException($"에피소드 '{episodeId}'가 없습니다.");
    }

    /// <summary>
    /// 간선 찾기 — (출발, 도착)으로 좁히고, 문구가 주어지면 거기까지 맞춘다(같은 도착으로
    /// 문구 여럿일 때의 정확한 신원 — v9).
    /// </summary>
    private static int RequireEdgeIndex(
        ChapterDocument chapter, string fromEpisodeId, string toEpisodeId, string? optionLabel)
    {
        int index = chapter.Edges.FindIndex(edge =>
            string.Equals(edge.FromEpisodeId, fromEpisodeId, StringComparison.Ordinal) &&
            string.Equals(edge.ToEpisodeId, toEpisodeId, StringComparison.Ordinal) &&
            (optionLabel is null ||
             string.Equals(edge.OptionLabel?.Trim() ?? string.Empty, optionLabel.Trim(), StringComparison.Ordinal)));

        return index >= 0
            ? index
            : throw new InvalidOperationException($"간선 {fromEpisodeId}→{toEpisodeId}이 없습니다.");
    }

    private ChapterEdge NewEdge(
        ChapterDocument chapter,
        string fromEpisodeId,
        string toEpisodeId,
        string? optionLabel,
        string? conditionLabel = null,
        string? statChanges = null,
        bool auto = false)
    {
        if (chapter.Edges.Any(edge =>
                string.Equals(edge.FromEpisodeId, fromEpisodeId, StringComparison.Ordinal) &&
                string.Equals(edge.ToEpisodeId, toEpisodeId, StringComparison.Ordinal) &&
                string.Equals(
                    edge.OptionLabel?.Trim() ?? string.Empty,
                    optionLabel?.Trim() ?? string.Empty,
                    StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"간선 {fromEpisodeId}→{toEpisodeId}이 같은 선택지 문구로 이미 있습니다.");
        }

        return new ChapterEdge(fromEpisodeId, toEpisodeId, optionLabel, conditionLabel, LockedMessage: null, SourceRow: 0)
        {
            Auto = auto,
            StatChanges = statChanges is null ? [] : ParseDeltas(chapter, statChanges)
        };
    }

    /// <summary>
    /// 사전에 없는 낱말이면 올려 둔다 — 다음부터 드롭다운에서 고른다(옛 <c>EnsureChoiceLabel</c>).
    ///
    /// ⚠ <c>Mutate</c> 안에서만 부른다 — 이것만으로 되돌리기 항목을 따로 만들지 않는다.
    /// </summary>
    private static void LearnChoiceLabel(ChapterDocument chapter, string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || FindChoice(chapter, text) is not null)
        {
            return;
        }

        // 인덱스는 사전 안의 순서일 뿐이다 — 10 단위로 세어 사람이 사이에 끼울 자리를 남긴다.
        string index = ((chapter.ChoiceOptions.Count + 1) * 10).ToString(
            System.Globalization.CultureInfo.InvariantCulture);

        chapter.ChoiceOptions.Add(new ChapterChoiceOption(index, text.Trim(), Memo: null, SourceRow: 0));
    }

    private static ChapterChoiceOption? FindChoice(ChapterDocument chapter, string text) =>
        chapter.ChoiceOptions.FirstOrDefault(option =>
            string.Equals(option.Text.Trim(), text.Trim(), StringComparison.Ordinal));

    private static ChapterCondition? FindCondition(ChapterDocument chapter, string label) =>
        chapter.Conditions.FirstOrDefault(condition =>
            string.Equals(condition.Label, label, StringComparison.Ordinal));

    /// <summary>
    /// 픽스처의 고정 선택이 가리키는 에피소드를 따라가게 한다 — 개명이면 새 Id로,
    /// 삭제(<paramref name="newId"/>가 null)면 그 항목을 걷는다.
    /// </summary>
    private static void Retarget(ChapterDocument chapter, string oldId, string? newId)
    {
        for (int index = 0; index < chapter.Fixtures.Count; index++)
        {
            ChapterFixture fixture = chapter.Fixtures[index];

            List<ChapterFixtureChoice> choices = fixture.Choices
                .Select(choice => new ChapterFixtureChoice(
                    string.Equals(choice.From, oldId, StringComparison.Ordinal) ? newId ?? oldId : choice.From,
                    string.Equals(choice.To, oldId, StringComparison.Ordinal) ? newId ?? oldId : choice.To))
                .Where(choice => newId is not null ||
                    (!string.Equals(choice.From, oldId, StringComparison.Ordinal) &&
                     !string.Equals(choice.To, oldId, StringComparison.Ordinal)))
                .ToList();

            chapter.Fixtures[index] = fixture with { Choices = choices };
        }

        if (newId is null)
        {
            return;
        }

        for (int index = 0; index < chapter.Edges.Count; index++)
        {
            ChapterEdge edge = chapter.Edges[index];

            chapter.Edges[index] = edge with
            {
                FromEpisodeId = string.Equals(edge.FromEpisodeId, oldId, StringComparison.Ordinal)
                    ? newId
                    : edge.FromEpisodeId,
                ToEpisodeId = string.Equals(edge.ToEpisodeId, oldId, StringComparison.Ordinal)
                    ? newId
                    : edge.ToEpisodeId
            };
        }
    }

    /// <summary>
    /// `스탯변화` 문법(`trust +1; anger -1`)을 푼다 — 문법을 읽는 자리는
    /// <see cref="StatDeltaParser"/> 하나다.
    ///
    /// ⚠ <b>못 읽은 것은 조용히 버리지 않는다.</b> 옛 경로에서는 셀에 원문이 남아 리더가
    /// 다음 읽기에 짚어 줬는데, 이제 다시 읽는 일이 없어 여기서 안 막으면 영영 사라진다.
    /// </summary>
    private static IReadOnlyList<StatDelta> ParseDeltas(ChapterDocument chapter, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        StatDeltaParseResult parsed = StatDeltaParser.Parse(
            text, chapter.Stats.Select(stat => stat.Key).ToList());

        return parsed.IsValid
            ? parsed.Deltas
            : throw new InvalidOperationException(
                $"스탯변화 '{text}': {parsed.Problems[0].Message}");
    }

    /// <summary>
    /// <b>간선에 매달렸던 연출 씬을 길 가운데로 올린다</b> (R7 P-6 · 결정 ⑤ · 2026-09-17).
    ///
    /// ⛔ <c>ViaNode</c>는 <b>재생 순서를 말하는 두 번째 방법</b>이었다. `A —문구→ B` 간선에
    /// 씬을 매달아 "가는 길에 이걸 먼저 틀어라"라고 적었는데, 같은 순서를 간선 두 개로
    /// 그대로 말할 수 있다:
    ///
    /// <code>
    /// 전: A —"문구"→ B   (간선에 Via가 매달려 있다)
    /// 후: A —"문구"→ Via —자동→ B
    /// </code>
    ///
    /// 런타임이 A를 틀고, 고른 뒤 Via를 틀고, 묻지 않고 B로 간다 — <b>순서가 똑같다</b>.
    /// 같은 것을 두 데서 말하면 갈린다는 것이 이 저장소가 반복해서 다친 자리이고, 그래서
    /// 곁칸이 아니라 간선이 남는다.
    ///
    /// <para>규칙 셋이 이 모양을 강제한다:</para>
    /// <list type="bullet">
    /// <item>Via는 <b>도착의 장면</b>에 둔다 — 자동 길은 장면을 못 넘는다(<c>AutoEdgeCrossesScene</c>).</item>
    /// <item>문구·조건·스탯은 <b>앞 간선에 남는다</b> — 사람이 고른 자리가 거기다.
    /// 자동 길은 무조건·무증감이어야 한다(<c>AutoEdgeHasChoiceLabel</c>·<c>AutoEdgeHasSiblings</c>).</item>
    /// <item>이미 에피소드인 노드는 <b>안 건드린다</b> — 그건 연출 씬이 아니라 잘못 이어진
    /// 배선이고, <c>WarnExitsIntoExcelNodes</c>가 이미 그것을 짚는다.</item>
    /// </list>
    ///
    /// ⚠ 한 번 올리면 <c>ChoiceExits</c>에서 지운다 — 남겨 두면 같은 씬이 <b>두 번</b>
    /// 재생된다(간선으로 한 번, 곁칸으로 한 번).
    /// </summary>
    /// <returns>올린 씬들. 아무것도 없으면 빈 목록이고 편집 기록도 안 남는다.</returns>
    public IReadOnlyList<LiftedViaScene> LiftViaScenes(string chapterId)
    {
        ChapterDocument chapter = RequireChapter(chapterId);

        if (Project.Files.FirstOrDefault(item =>
                string.Equals(item.Name, chapterId, StringComparison.Ordinal)) is not { } board)
        {
            return [];
        }

        var plans = new List<(ChapterEdge Edge, DialogueNode Source, DialogueNode Via, string ViaEpisodeId)>();

        foreach (ChapterEdge edge in chapter.Edges.ToList())
        {
            if (edge.OptionLabel is not { Length: > 0 } label ||
                FindEpisode(chapter, edge.FromEpisodeId) is not { } from ||
                NodeOnBoard(board, chapter, from) is not { } source ||
                !source.ChoiceExits.TryGetValue(label, out string? targetId) ||
                Project.FindNode(targetId) is not DialogueNode via ||
                !board.Nodes.Contains(via) ||
                EpisodeNaming.EpisodeFor(chapter, via) is not null)
            {
                continue;
            }

            string episodeId = via.Name;

            if (FindEpisode(chapter, episodeId) is not null ||
                plans.Any(plan => string.Equals(plan.ViaEpisodeId, episodeId, StringComparison.Ordinal)))
            {
                // 이름이 겹치면 건드리지 않는다 — 조용히 개명하면 어느 씬이 어디로 갔는지
                // 아무도 모른다. 사람이 판에서 이름을 고치면 다음 번에 올라간다.
                continue;
            }

            plans.Add((edge, source, via, episodeId));
        }

        if (plans.Count == 0)
        {
            return [];
        }

        var lifted = new List<LiftedViaScene>();

        Mutate(() =>
        {
            foreach ((ChapterEdge edge, DialogueNode source, DialogueNode via, string episodeId) in plans)
            {
                int index = chapter.Edges.IndexOf(edge);

                chapter.Episodes.Add(new ChapterEpisode(
                    episodeId,
                    episodeId,
                    Index: string.Empty,
                    DialogueEntry: episodeId,
                    Math.Round(via.Layout.X, 2),
                    Math.Round(via.Layout.Y, 2),
                    Memo: null,
                    SourceRow: 0)
                {
                    // 자동 길은 장면을 못 넘는다 — 도착의 장면에 선다.
                    SceneId = FindEpisode(chapter, edge.ToEpisodeId)?.SceneId
                });

                via.ExcelEpisodeId = episodeId;

                chapter.Edges[index] = edge with { ToEpisodeId = episodeId };
                chapter.Edges.Insert(index + 1, new ChapterEdge(
                    episodeId, edge.ToEpisodeId, OptionLabel: null,
                    ConditionLabel: null, LockedMessage: null, SourceRow: 0)
                {
                    Auto = true
                });

                source.ChoiceExits.Remove(edge.OptionLabel!);

                lifted.Add(new LiftedViaScene(
                    chapterId, episodeId, edge.FromEpisodeId, edge.ToEpisodeId));
            }
        });

        return lifted;
    }

    /// <summary>그 에피소드를 재생하는 판 위의 카드. 아직 없으면 <c>null</c>이다.</summary>
    private static DialogueNode? NodeOnBoard(
        StoryFile board, ChapterDocument chapter, ChapterEpisode episode) =>
        board.Nodes.OfType<DialogueNode>().FirstOrDefault(node =>
            EpisodeNaming.EpisodeFor(chapter, node) is { } found &&
            string.Equals(found.EpisodeId, episode.EpisodeId, StringComparison.Ordinal));

    /// <summary>
    /// <b>판에 남은 옛 자유 씬을 에피소드로 올린다</b> (R7 P-6 · 결정 ⑤ · 2026-09-17).
    ///
    /// ⛔ 자유 씬은 <b>종류가 아니라 빈자리</b>였다 — 에피소드가 없는 대사 노드. 종류가
    /// 없어졌으므로 그 빈자리도 없어진다: 챕터 판의 대사 노드는 전부 에피소드다.
    ///
    /// ⚠ <b>들어오는 간선이 없다.</b> 이것들은 대본의 갈래가 <c>&lt;&lt;detour&gt;&gt;</c>로
    /// 부르던 곁가지라 챕터 진행에는 안 실려 있었다. 그래서 <c>도달불가 허용</c>을 켜서
    /// 올린다 — 도달성 증명은 <b>한 줄도 안 건드린다</b>(저쪽 런타임의 오라클이다).
    ///
    /// ⚠ <b>부른 쪽의 장면</b>에 둔다. 곁가지는 제 본줄 옆에 있어야 판에서 읽힌다.
    ///
    /// ⚠ <see cref="LiftViaScenes"/> <b>다음에</b> 부른다 — 간선에 매달렸던 씬은 그쪽이
    /// 진짜 간선을 달아 주므로 <c>도달불가 허용</c>이 필요 없다. 순서를 뒤집으면 멀쩡히
    /// 이어질 씬에 허용 표가 붙는다.
    /// </summary>
    /// <returns>올린 에피소드Id들. 올릴 것이 없으면 빈 목록이고 프로젝트를 안 건드린다.</returns>
    public IReadOnlyList<string> LiftFreeScenes(string chapterId)
    {
        ChapterDocument chapter = RequireChapter(chapterId);

        if (Project.Files.FirstOrDefault(item =>
                string.Equals(item.Name, chapterId, StringComparison.Ordinal)) is not { } board)
        {
            return [];
        }

        var taken = new HashSet<string>(
            chapter.Episodes.Select(episode => episode.EpisodeId), StringComparer.Ordinal);
        var plans = new List<(DialogueNode Node, string EpisodeId)>();

        foreach (DialogueNode node in board.Nodes.OfType<DialogueNode>())
        {
            // ⚠ 설정노드(A계층 조건 배관)는 애초에 `DialogueNode`가 아니라 여기 안 걸린다.
            if (EpisodeNaming.EpisodeFor(chapter, node) is not null || !taken.Add(node.Name))
            {
                // 이름이 겹치면 건드리지 않는다 — 조용히 개명하면 어느 씬이 어디로 갔는지
                // 아무도 모른다. 사람이 판에서 고치면 다음 번에 올라간다.
                continue;
            }

            plans.Add((node, node.Name));
        }

        if (plans.Count == 0)
        {
            return [];
        }

        Mutate(() =>
        {
            foreach ((DialogueNode node, string episodeId) in plans)
            {
                node.ExcelEpisodeId = episodeId;

                chapter.Episodes.Add(new ChapterEpisode(
                    episodeId,
                    episodeId,
                    Index: string.Empty,
                    DialogueEntry: episodeId,
                    Math.Round(node.Layout.X, 2),
                    Math.Round(node.Layout.Y, 2),
                    Memo: null,
                    SourceRow: 0,
                    AllowUnreachable: true)
                {
                    SceneId = SceneOfCaller(board, chapter, node)
                });
            }
        });

        return plans.Select(plan => plan.EpisodeId).ToList();
    }

    /// <summary>
    /// 이 곁가지를 <c>&lt;&lt;detour&gt;&gt;</c>로 부르는 카드의 <b>장면</b>.
    /// 아무도 안 부르면 <c>null</c>이다 — 그때는 장면 밖에 선다.
    /// </summary>
    private static string? SceneOfCaller(
        StoryFile board, ChapterDocument chapter, DialogueNode target)
    {
        foreach (DialogueNode caller in board.Nodes.OfType<DialogueNode>())
        {
            bool calls =
                caller.BranchExits.Values.Any(id =>
                    string.Equals(id, target.Id, StringComparison.Ordinal)) ||
                caller.LineExtensions.Any(extension =>
                    string.Equals(extension.DetourTargetNodeId, target.Id, StringComparison.Ordinal)) ||
                caller.ChoiceExits.Values.Any(id =>
                    string.Equals(id, target.Id, StringComparison.Ordinal));

            if (calls && EpisodeNaming.EpisodeFor(chapter, caller) is { } episode)
            {
                return episode.SceneId;
            }
        }

        return null;
    }
}

/// <summary>
/// 길 가운데로 올라온 옛 연출 씬 하나 (R7 P-6 · 결정 ⑤).
/// <c>A —문구→ Via —자동→ To</c>로 펴졌다는 보고다.
/// </summary>
public sealed record LiftedViaScene(
    string ChapterId,
    string ViaEpisodeId,
    string FromEpisodeId,
    string ToEpisodeId);

/// <summary>
/// 스탯 개명 한 판의 결과 (2026-09-17).
/// </summary>
/// <param name="Applied">개명했는가. false면 <b>아무것도 안 건드렸다</b>.</param>
/// <param name="Edges">`스탯변화`가 바뀐 간선 수.</param>
/// <param name="Conditions">식이 바뀐 챕터 조건 수.</param>
/// <param name="SuppliedConditions">
/// 판에 공급된 조건 중 식이 바뀐 수 — <b>Id를 지킨 채</b> 갈았다. 이걸 빠뜨리면 다음
/// 동기화가 새 Id로 다시 만들어 줄에 매달린 갈래가 전부 고아가 된다.
/// </param>
/// <param name="Refusal">거절 사유. <paramref name="Applied"/>가 false일 때만 있다.</param>
public sealed record StatRenameOutcome(
    bool Applied,
    int Edges,
    int Conditions,
    int SuppliedConditions,
    string? Refusal)
{
    public static StatRenameOutcome Refuse(string reason) => new(false, 0, 0, 0, reason);
}
