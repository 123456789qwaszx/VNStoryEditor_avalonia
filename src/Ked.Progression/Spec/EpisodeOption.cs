using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    public enum OptionKind
    {
        PlayerChoice = 0,

        // 고를 수 있는 것이 하나도 없을 때 자동으로 타는 길.
        // 에피소드당 하나 제한. 문구나 게이트 없음.
        AutoAdvance = 1,
    }

    // 에피소드에서 나가는 길 - 저작 쪽 `간선` 시트의 한 행에 대응.
    //
    // 이전에는 에피소드 노드가 표시조건·해금조건을 들고 있었는데, 이젠 간선으로 책임.
    // 그래서 "같은 곳으로 가되 조건이 다른 길"을 여럿 둘 수 있음.
    public sealed class EpisodeOption
    {
        public OptionKind Kind { get; }

        // 화면에 뜨는 문구. "OptionKind.AutoAdvance"시 비워둠.
        public string ChoiceLabel { get; }

        public string TargetEpisodeId { get; }

        // 표기조건 미달.
        public IReadOnlyList<ProgressionCondition> VisibleConditions { get; }

        // 해금 조건 미달.
        // 왜 잠겼는지 알려 줌.
        public IReadOnlyList<ProgressionCondition> Conditions { get; }

        public bool HideWhenLocked { get; }
        public string LockedReasonText { get; }

        // 스탯이 변하는 유일한 자리
        public IReadOnlyList<StatChange> StatChanges { get; }

        // 연출을 매다는 자리
        // 여기서 "노드"는 에피소드 노드가 아니라 Yarn 노드다.
        public string ViaNodeId { get; }

        // 이 길에 연출이 달려 있는지 체크.
        public bool HasVia => ViaNodeId.Length != 0;

        private EpisodeOption(
            OptionKind kind,
            string choiceLabel,
            string targetEpisodeId,
            IReadOnlyList<ProgressionCondition> visibleConditions,
            IReadOnlyList<ProgressionCondition> conditions,
            bool hideWhenLocked,
            string lockedReasonText,
            IReadOnlyList<StatChange> statChanges,
            string viaNodeId)
        {
            if (string.IsNullOrEmpty(targetEpisodeId))
            {
                throw new ArgumentException(
                    "선택지의 도착 에피소드가 비어 있다. 아무 데도 가지 않는 길은 둘 수 없다.",
                    nameof(targetEpisodeId));
            }

            // 저작 쪽은 "없음"과 "빈 것"을 구분하지 않음.(조건 없는 걸 그냥 빈 간선으로 내보내서 기획자가 채우도록.)
            Kind = kind;
            ChoiceLabel = choiceLabel ?? string.Empty;
            LockedReasonText = lockedReasonText ?? string.Empty;
            VisibleConditions = visibleConditions ?? Array.Empty<ProgressionCondition>();
            Conditions = conditions ?? Array.Empty<ProgressionCondition>();
            StatChanges = statChanges ?? Array.Empty<StatChange>();

            TargetEpisodeId = targetEpisodeId;
            HideWhenLocked = hideWhenLocked;
            ViaNodeId = viaNodeId ?? string.Empty;

            ProgressionCondition.RequireAllConstructed(
                VisibleConditions, nameof(visibleConditions));
            ProgressionCondition.RequireAllConstructed(Conditions, nameof(conditions));
        }

        // 플레이어가 고르는 선택지.
        // 문구 없는 길은 Kind.AutoAdvance로써 단일경로 취급.
        public static EpisodeOption Choice(
            string choiceLabel,
            string targetEpisodeId,
            IReadOnlyList<ProgressionCondition> visibleConditions = null,
            IReadOnlyList<ProgressionCondition> conditions = null,
            bool hideWhenLocked = false,
            string lockedReasonText = null,
            IReadOnlyList<StatChange> statChanges = null,
            string viaNodeId = null)
        {
            if (string.IsNullOrEmpty(choiceLabel))
            {
                throw new ArgumentException(
                    $"'{targetEpisodeId}'(으)로 가는 선택지의 문구가 비어 있다. " +
                    "문구 없이 자동으로 타는 길이라면 EpisodeOption.Auto()를 쓸 것.",
                    nameof(choiceLabel));
            }

            return new EpisodeOption(
                OptionKind.PlayerChoice,
                choiceLabel,
                targetEpisodeId,
                visibleConditions,
                conditions,
                hideWhenLocked,
                lockedReasonText,
                statChanges,
                viaNodeId);
        }

        // 고를 수 있는 것이 하나도 없을 때 타는 디폴트 길. 에피소드당 하나.
        public static EpisodeOption Auto(
            string targetEpisodeId,
            IReadOnlyList<StatChange> statChanges = null,
            string viaNodeId = null)
        {
            return new EpisodeOption(
                OptionKind.AutoAdvance,
                string.Empty,
                targetEpisodeId,
                null,
                null,
                false,
                null,
                statChanges,
                viaNodeId);
        }

        public override string ToString()
        {
            string via = HasVia ? $" ~{ViaNodeId}~" : string.Empty;

            return Kind == OptionKind.AutoAdvance
                ? $"(자동){via} → {TargetEpisodeId}"
                : $"\"{ChoiceLabel}\"{via} → {TargetEpisodeId}";
        }
    }
}