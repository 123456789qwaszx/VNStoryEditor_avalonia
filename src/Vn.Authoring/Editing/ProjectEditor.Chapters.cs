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

    /// <summary>챕터 하나를 통째로 걷는다 — 워크북 삭제와 짝이다.</summary>
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
    public ChapterEpisode AddEpisode(string chapterId, string episodeId, string title, double x, double y)
    {
        ChapterDocument chapter = RequireChapter(chapterId);

        if (FindEpisode(chapter, episodeId) is not null)
        {
            throw new InvalidOperationException($"EpisodeId '{episodeId}'가 이미 있습니다.");
        }

        var episode = new ChapterEpisode(
            episodeId, title, Index: string.Empty, DialogueEntry: episodeId,
            Math.Round(x, 2), Math.Round(y, 2), Memo: null, SourceRow: 0);

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
        string? optionLabel = null)
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
            Math.Round(x, 2), Math.Round(y, 2), Memo: null, SourceRow: 0);

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
}
