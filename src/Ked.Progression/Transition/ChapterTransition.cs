using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    /// <summary>
    /// 화면에 뜬 선택지가 고를 수 있는가.
    ///
    /// ⚠ <b><c>Hidden</c>이 없다.</b> §G5의 "표시조건 미달이면 목록에 <b>만들지 않는다</b>"를
    /// 그대로 옮기면 숨긴 것은 목록에 <b>없는 것</b>이지 <c>Hidden</c>으로 표시된 항목이
    /// 아니다. 목록에 넣어 두면 호스트가 전부 그리다가 숨겨야 할 것을 보여 주는 사고가
    /// 열리고, 그 값은 어차피 아무도 그리면 안 되므로 <b>영원히 안 타는 분기</b>가 된다
    /// (<see cref="ComparisonOp"/>에서 <c>NotEqual</c>을 뺀 것과 같은 판단).
    /// 몇 개가 숨겨졌는지는 <see cref="ChapterAdvance.HiddenCount"/>가 진다.
    /// </summary>
    public enum OptionVisibility
    {
        /// <summary>고를 수 있다.</summary>
        Shown = 0,

        /// <summary>보이지만 고를 수 없다. 사유를 함께 낸다.</summary>
        Locked = 1,
    }

    /// <summary>
    /// 판정이 끝난 선택지 하나. <b>해석 결과이지 상태가 아니다</b> — 저장하지 않는다(P3).
    /// </summary>
    public readonly struct ResolvedOption
    {
        public EpisodeOption Option { get; }
        public OptionVisibility Visibility { get; }

        /// <summary>
        /// 저작자가 쓴 잠금 안내문. <b>비어 있을 수 있다</b> — 이 패키지는 대신 문장을
        /// 지어내지 않는다(무해석성). 무엇 때문에 잠겼는지는
        /// <see cref="BlockingCondition"/>이 기계가 읽을 수 있는 모양으로 진다.
        /// </summary>
        public string LockedReason { get; }

        /// <summary>
        /// 미달인 <b>첫 조건</b>. 툴의 도달성 증명이 이미 원인 조건을 지목하므로 런타임도
        /// 같은 것을 낼 수 있어야 규약 사본이 생기지 않는다(P5).
        /// <see cref="OptionVisibility.Shown"/>이면 만들어지지 않은 값이다
        /// (<c>IsConstructed == false</c>).
        /// </summary>
        public ProgressionCondition BlockingCondition { get; }

        public bool IsSelectable => Visibility == OptionVisibility.Shown;

        private ResolvedOption(
            EpisodeOption option,
            OptionVisibility visibility,
            string lockedReason,
            ProgressionCondition blockingCondition)
        {
            Option = option;
            Visibility = visibility;
            LockedReason = lockedReason;
            BlockingCondition = blockingCondition;
        }

        internal static ResolvedOption Shown(EpisodeOption option) =>
            new ResolvedOption(option, OptionVisibility.Shown, string.Empty, default);

        internal static ResolvedOption Locked(EpisodeOption option, ProgressionCondition blocking) =>
            new ResolvedOption(
                option, OptionVisibility.Locked, option.LockedReasonText, blocking);

        public override string ToString() =>
            Visibility == OptionVisibility.Shown
                ? $"{Option}"
                : $"{Option} [잠김: {BlockingCondition}]";
    }

    /// <summary>§G6의 세 갈래.</summary>
    public enum ChapterAdvanceKind
    {
        /// <summary>고를 수 있는 것이 하나 이상 있다. 플레이어의 입력을 기다린다.</summary>
        AwaitPlayerChoice = 0,

        /// <summary>고를 수 있는 것이 없고, 문구 없는 간선이 있다. 자동으로 진행한다.</summary>
        AutoAdvance = 1,

        /// <summary>그것도 없다 — 챕터 런이 여기서 끝난다.</summary>
        ChapterEnded = 2,
    }

    /// <summary>
    /// 지금 에피소드에서 무엇이 일어나야 하는가. <b>해석 결과이지 상태가 아니다</b> —
    /// 저장하지 않는다(P3). 구 런타임이 <c>LockedEpisodeIds</c>·<c>VisibleEpisodeIds</c>를
    /// 상태에 넣어 세이브에 옛 판정이 섞일 길을 열어 두었던 자리다.
    /// </summary>
    public readonly struct ChapterAdvance
    {
        public ChapterAdvanceKind Kind { get; }

        /// <summary>
        /// 화면에 그릴 목록. <b>배열 순서가 곧 화면 순서다</b>(§G6) — 정렬하지 않는다.
        /// <see cref="ChapterAdvanceKind.AwaitPlayerChoice"/>가 아니면 비어 있다.
        /// </summary>
        public IReadOnlyList<ResolvedOption> Options { get; }

        /// <summary><see cref="ChapterAdvanceKind.AutoAdvance"/>일 때만 채워진다.</summary>
        public EpisodeOption AutoOption { get; }

        /// <summary>
        /// <see cref="ChapterAdvanceKind.ChapterEnded"/>일 때 지금 노드의 엔딩키.
        /// 엔딩 후보가 아닌 노드에서 끝났으면 빈 문자열이다 — 그 경우
        /// <see cref="ScenarioTransition"/>이 <see cref="ScenarioAdvanceKind.DeadEnd"/>를 낸다.
        /// </summary>
        public string EndingKey { get; }

        /// <summary>
        /// 표시조건 미달 또는 <c>HideWhenLocked</c>로 목록에서 빠진 개수.
        /// 게임에는 쓰이지 않고 <b>저작 도구와 로그가 본다</b> — 숨긴 것이 몇 개인지조차
        /// 모르면 "왜 선택지가 안 뜨지"를 추적할 방법이 없다.
        /// </summary>
        public int HiddenCount { get; }

        internal ChapterAdvance(
            ChapterAdvanceKind kind,
            IReadOnlyList<ResolvedOption> options,
            EpisodeOption autoOption,
            string endingKey,
            int hiddenCount)
        {
            Kind = kind;
            Options = options;
            AutoOption = autoOption;
            EndingKey = endingKey;
            HiddenCount = hiddenCount;
        }

        public override string ToString() => $"{Kind}(보임 {Options.Count}, 숨김 {HiddenCount})";
    }

    /// <summary>
    /// 지금 에피소드에서 갈 수 있는 길을 확정한다. 순수 함수다 — 상태를 읽기만 한다(규율 4).
    ///
    /// <b>P4 — 계획을 먼저 확정하고 그 뒤엔 고르기만 한다.</b> 여기서 한 번 판정하면
    /// 그 결과가 화면에 그대로 뜨고, 플레이어가 고른 뒤에야 값이 바뀐다.
    /// ⚠ 그래서 판정은 <b>커밋 전 값</b>으로 한다(§G6) — 플레이어가 선택지를 보는 시점의 값이다.
    /// </summary>
    public static class ChapterTransition
    {
        private static readonly ResolvedOption[] None = new ResolvedOption[0];

        public static ChapterAdvance Resolve(ChapterProgression chapter, ProgressionState state)
        {
            if (chapter == null)
                throw new ArgumentNullException(nameof(chapter));

            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (!chapter.TryGetNode(state.CurrentEpisodeId, out EpisodeNode node))
            {
                throw new ArgumentException(
                    $"지금 에피소드 '{state.CurrentEpisodeId}'가 챕터 '{chapter.ChapterId}'에 없다.",
                    nameof(state));
            }

            var shownOrLocked = new List<ResolvedOption>();
            int hidden = 0;
            bool anySelectable = false;

            IReadOnlyList<EpisodeOption> options = node.NextOptions;

            for (int i = 0; i < options.Count; i++)
            {
                EpisodeOption option = options[i];

                // 자동 진행 간선은 선택지가 아니다 — 고를 것이 없을 때의 마지막 길이다.
                if (option.Kind == OptionKind.AutoAdvance)
                {
                    continue;
                }

                if (FirstUnmet(option.VisibleConditions, state).IsConstructed)
                {
                    // §G5 — 표시조건 미달이면 목록에 만들지 않는다.
                    // 플레이어는 그런 선택지가 있었다는 사실 자체를 모른다.
                    hidden++;
                    continue;
                }

                ProgressionCondition blocking = FirstUnmet(option.Conditions, state);

                if (!blocking.IsConstructed)
                {
                    shownOrLocked.Add(ResolvedOption.Shown(option));
                    anySelectable = true;
                    continue;
                }

                if (option.HideWhenLocked)
                {
                    hidden++;
                    continue;
                }

                shownOrLocked.Add(ResolvedOption.Locked(option, blocking));
            }

            if (anySelectable)
            {
                return new ChapterAdvance(
                    ChapterAdvanceKind.AwaitPlayerChoice,
                    shownOrLocked, null, string.Empty, hidden);
            }

            // 고를 수 있는 것이 하나도 없다. 잠긴 것들은 화면에 세우지 않는다 —
            // 누를 수 없는 목록을 띄워 놓고 아무 일도 안 일어나는 것이 더 나쁘다(§G6-2).
            if (node.TryGetAutoOption(out EpisodeOption auto))
            {
                return new ChapterAdvance(
                    ChapterAdvanceKind.AutoAdvance, None, auto, string.Empty, hidden);
            }

            return new ChapterAdvance(
                ChapterAdvanceKind.ChapterEnded, None, null, node.EndingKey, hidden);
        }

        /// <summary>
        /// 미달인 첫 조건. 전부 충족이면 <c>default</c>(= <c>IsConstructed == false</c>)다.
        ///
        /// <b>"몇 개가 미달인가"가 아니라 "무엇 때문인가"를 낸다.</b> 화면에 이유를 하나
        /// 세우면 충분하고, 그 하나가 저작자가 고칠 자리를 짚는다.
        /// </summary>
        private static ProgressionCondition FirstUnmet(
            IReadOnlyList<ProgressionCondition> conditions, ProgressionState state)
        {
            for (int i = 0; i < conditions.Count; i++)
            {
                if (!ConditionEvaluator.IsMet(conditions[i], state))
                    return conditions[i];
            }

            return default;
        }
    }
}
