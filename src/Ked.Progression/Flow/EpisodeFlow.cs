using System;
using Ked.Progression.Dto;

namespace Ked.Progression
{
    /// <summary>
    /// 에피소드 하나를 트랜잭션으로 굴리고, 챕터 경계를 넘긴다.
    ///
    /// <code>
    ///   EpisodeEntered ──[대사]──► OptionsResolved ──┬─► AwaitingChoice ──[선택]──┐
    ///        ▲                                        ├─► AutoAdvancing ──────────┤
    ///        │                                        │                           ▼
    ///        │                                        │                    (ViaPlaying)
    ///        │                                        │                           │
    ///        └────────[세이브]──── Committed ◄─────────┼───────────────────────────┘
    ///                                                 │
    ///                                                 └─► ChapterEnded ──► 시나리오 층
    /// </code>
    ///
    /// <b>이 흐름은 끌려간다.</b> 스스로 무엇을 부르지 않고, 지금 필요한 것을
    /// <see cref="Pending"/>으로 내놓은 뒤 호스트가 마쳤다고 알려 주기를 기다린다. 그래서
    /// 인터페이스도 콜백도 <c>async</c>도 없고, 유니티와 Avalonia가 같은 흐름을 공유한다.
    ///
    /// <b>판단과 실행이 시간으로 갈라져 있다</b>(P4) — <see cref="DialogueCompleted"/>가
    /// 한 번 판정하고, 그 뒤 <see cref="Choose"/>는 이미 정해진 목록에서 고르기만 한다.
    /// 판정을 다시 하지 않으므로 화면에 뜬 것과 실제로 일어나는 일이 갈릴 수 없다.
    /// </summary>
    public sealed class EpisodeFlow
    {
        private readonly ScenarioProgression _scenario;

        private ProgressionState _state;
        private ChapterAdvance _advance;
        private EpisodeOption _chosen;

        public EpisodePhase Phase { get; private set; } = EpisodePhase.None;

        /// <summary>지금 호스트가 해야 하는 일. 통과 자리에서는 비어 있다.</summary>
        public FlowRequest Pending { get; private set; }

        /// <summary>
        /// 지금 진행. <b>이 흐름이 들고 있는 유일한 저장 대상이다</b> —
        /// <see cref="Pending"/>도 <see cref="Phase"/>도 세이브에 가지 않는다(P3).
        /// </summary>
        public ProgressionState State => _state;

        public bool IsFinished =>
            Phase == EpisodePhase.ScenarioFinished || Phase == EpisodePhase.DeadEnd;

        private EpisodeFlow(ScenarioProgression scenario, ProgressionState state)
        {
            _scenario = scenario;
            _state = state;
        }

        /// <summary>새 게임. 시작값은 <b>시나리오가 한 번만</b> 세운다(D1).</summary>
        public static EpisodeFlow Begin(ScenarioProgression scenario)
        {
            if (scenario == null)
                throw new ArgumentNullException(nameof(scenario));

            var flow = new EpisodeFlow(scenario, scenario.CreateInitialState());
            flow.EnterEpisode();

            return flow;
        }

        /// <summary>
        /// 이어 하기.
        ///
        /// ⚠ <paramref name="state"/>는 <see cref="ScenarioProgression.CreateInitialState"/>나
        /// <see cref="ProgressionSave.Restore"/>가 낸 것이어야 한다. <b>둘 다 이 시나리오에
        /// 대해 이미 검증된 상태다</b> — 그래서 이 안쪽은 전체 함수로 남고 방어 코드가 없다(P2).
        /// 검증되지 않은 상태를 손으로 만들어 넣으면 그 순간 경계가 새는 것이다.
        /// </summary>
        public static EpisodeFlow Resume(ScenarioProgression scenario, ProgressionState state)
        {
            if (scenario == null)
                throw new ArgumentNullException(nameof(scenario));

            if (state == null)
                throw new ArgumentNullException(nameof(state));

            var flow = new EpisodeFlow(scenario, state);
            flow.EnterEpisode();

            return flow;
        }

        // ── 호스트가 마쳤다고 알리는 것들 ────────────────────────────
        //
        // 넷 다 한 자리에서만 유효하다. 자리가 아니면 던진다 — 무효 조합을 타입으로 못
        // 올렸으므로 생성자 급에서 막는다(규칙을 위로 올린다: 타입 > 생성자 > 로더).

        /// <summary>
        /// 대사가 끝났다. <b>이 호출이 이 회차의 판단을 확정한다</b> — 조건 판정은
        /// 커밋 전 값으로 여기서 한 번만 한다(§G6).
        /// </summary>
        public void DialogueCompleted()
        {
            Require(EpisodePhase.EpisodeEntered, nameof(DialogueCompleted));

            _advance = ChapterTransition.Resolve(CurrentChapter(), _state);
            Phase = EpisodePhase.OptionsResolved;

            switch (_advance.Kind)
            {
                case ChapterAdvanceKind.AwaitPlayerChoice:
                    Phase = EpisodePhase.AwaitingChoice;
                    Pending = FlowRequest.PresentOptions(_advance.Options, _advance.HiddenCount);
                    return;

                case ChapterAdvanceKind.AutoAdvance:
                    Phase = EpisodePhase.AutoAdvancing;
                    Take(_advance.AutoOption);
                    return;

                default:
                    CrossChapterBoundary();
                    return;
            }
        }

        /// <summary>
        /// 하나를 골랐다. <b>이 패키지는 고르지 않는다</b> — 목록을 만들 뿐이다.
        /// 잠긴 것을 고르면 <b>무엇 때문에 잠겼는지를 지목해서</b> 던진다(P5).
        /// </summary>
        public void Choose(int optionIndex)
        {
            Require(EpisodePhase.AwaitingChoice, nameof(Choose));

            if (optionIndex < 0 || optionIndex >= _advance.Options.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(optionIndex),
                    $"선택지는 {_advance.Options.Count}개인데 {optionIndex}번을 골랐다.");
            }

            ResolvedOption picked = _advance.Options[optionIndex];

            if (!picked.IsSelectable)
            {
                throw new InvalidOperationException(
                    $"잠긴 선택지다: [{picked.Option.ChoiceLabel}] — {picked.BlockingCondition}");
            }

            Take(picked.Option);
        }

        /// <summary>연출이 끝났다. 지나가는 자리이므로 상태는 아직 안 바뀌었다.</summary>
        public void ViaCompleted()
        {
            Require(EpisodePhase.ViaPlaying, nameof(ViaCompleted));

            CommitChosen();
        }

        /// <summary>
        /// 세이브를 썼다. 여기서부터 다음 에피소드가 시작한다.
        ///
        /// 에피소드 안에서 굽든 챕터 경계에서 굽든 <b>돌아오는 자리가 같다</b> — 경계를
        /// 넘는 것과 안에서 옮겨 가는 것이 이 지점에서 하나로 합쳐진다.
        /// </summary>
        public void SavePersisted()
        {
            if (Phase != EpisodePhase.Committed && Phase != EpisodePhase.ChapterBoundaryCommitted)
            {
                throw new InvalidOperationException(
                    $"{nameof(SavePersisted)}는 Committed 또는 ChapterBoundaryCommitted에서만 " +
                    $"부를 수 있다. 지금은 {Phase}다.");
            }

            EnterEpisode();
        }

        // ── 안쪽 ────────────────────────────────────────────────────

        private void EnterEpisode()
        {
            Phase = EpisodePhase.EpisodeEntered;
            _advance = default;
            _chosen = null;

            Pending = FlowRequest.PlayDialogue(CurrentNode().DialogueEntryId);
        }

        private void Take(EpisodeOption option)
        {
            _chosen = option;

            // 연출은 지나가며 거쳐 갈 뿐이라 상태를 안 바꾼다. 이름이 비면 곧장 간다.
            if (option.HasVia)
            {
                Phase = EpisodePhase.ViaPlaying;
                Pending = FlowRequest.PlayVia(option.ViaNodeId);

                return;
            }

            CommitChosen();
        }

        private void CommitChosen()
        {
            // 스탯 반영 · 클리어 표시 · 이동이 한 연산이다. 그래서 "스탯만 오르고 안 옮겨 간"
            // 상태가 세이브에 실릴 수 없다 — 트랜잭션 경계가 곧 저장 경계다.
            _state = _state.Commit(CurrentChapter(), _chosen);
            _chosen = null;

            Phase = EpisodePhase.Committed;
            Pending = FlowRequest.PersistSave(ProgressionSave.Capture(_scenario, _state));
        }

        /// <summary>
        /// <b>에피소드 층과 시나리오 층이 만나는 유일한 자리.</b>
        ///
        /// 건너가는 것은 <b>엔딩키 문자열 하나</b>다. 노드가 그 키를 지고 있고(D2),
        /// 시나리오 층은 그 키로 규칙표를 조회한다. 챕터끼리 서로를 모르는 이유가 이것이고,
        /// 그래서 챕터를 떼어다 단독으로 증명할 수 있다.
        ///
        /// 여기서 <see cref="ScenarioTransition"/>이 <b>먼저 정하고</b>
        /// <see cref="ProgressionState.CommitChapterEnding"/>이 <b>적용만 한다</b> —
        /// 판단과 실행이 여기서도 갈라져 있다.
        /// </summary>
        private void CrossChapterBoundary()
        {
            Phase = EpisodePhase.ChapterEnded;

            ScenarioAdvance next = ScenarioTransition.Resolve(_scenario, _state);

            if (next.Kind != ScenarioAdvanceKind.NextChapter)
            {
                // 의도한 종착과 막다른 곳을 섞지 않는다 — 화면에서 구별되어야 한다.
                Phase = next.Kind == ScenarioAdvanceKind.ScenarioEnded
                    ? EpisodePhase.ScenarioFinished
                    : EpisodePhase.DeadEnd;

                Pending = FlowRequest.Finished(next);

                return;
            }

            // 스탯은 다시 세우지 않는다. 챕터를 넘어도 그대로 간다(D1).
            _state = _state.CommitChapterEnding(_scenario, next);

            Phase = EpisodePhase.ChapterBoundaryCommitted;
            Pending = FlowRequest.PersistSave(ProgressionSave.Capture(_scenario, _state));
        }

        // 로더가 이미 참조를 다 검증했고 Commit은 검증된 곳으로만 옮긴다. 그래서 둘 다
        // 전체 함수다 — 여기에 방어 코드가 생기면 경계가 샜다는 신호다(P2).
        private ChapterProgression CurrentChapter()
        {
            _scenario.TryGetChapter(_state.CurrentChapterId, out ChapterProgression chapter);

            return chapter;
        }

        private EpisodeNode CurrentNode()
        {
            CurrentChapter().TryGetNode(_state.CurrentEpisodeId, out EpisodeNode node);

            return node;
        }

        private void Require(EpisodePhase expected, string called)
        {
            if (Phase != expected)
            {
                throw new InvalidOperationException(
                    $"{called}는 {expected}에서만 부를 수 있다. 지금은 {Phase}다.");
            }
        }

        public override string ToString() =>
            $"{_state.CurrentChapterId}/{_state.CurrentEpisodeId} [{Phase}] {Pending}";
    }
}
