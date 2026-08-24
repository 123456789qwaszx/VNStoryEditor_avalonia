namespace Ked.Progression
{
    // 선택지 하나의 판정 결과
    public readonly struct ResolvedOption
    {
        public EpisodeOption Option { get; }

        // 고를 수 있는가. 못 고르면 아래 둘이 왜인지를 말한다.
        public bool IsSelectable { get; }

        // 저작자가 쓴 잠금 안내문.
        public string LockedReason { get; }

        // 미달 조건 중 첫번째 것만 반환.
        public ProgressionCondition BlockingCondition { get; }

        private ResolvedOption(
            EpisodeOption option,
            bool isSelectable,
            string lockedReason,
            ProgressionCondition blockingCondition)
        {
            Option = option;
            IsSelectable = isSelectable;
            LockedReason = lockedReason;
            BlockingCondition = blockingCondition;
        }

        internal static ResolvedOption Shown(EpisodeOption option) =>
            new(option, true, string.Empty, default);

        internal static ResolvedOption Locked(EpisodeOption option, ProgressionCondition blocking) =>
            new(option, false, option.LockedReasonText, blocking);

        public override string ToString() =>
            IsSelectable ? $"{Option}" : $"{Option} [잠김: {BlockingCondition}]";
    }
}