namespace Ked.Progression
{
    // 선택지 하나의 판정 결과.
    // SourceIndex는 원본 EpisodeNode.NextOptions에서의 위치다.
    public readonly struct ResolvedOption
    {
        public EpisodeOption Option { get; }

        // 원본 NextOptions에서의 서수.
        // 화면에서 몇 번째로 보였는지가 아니라 콘텐츠 자체의 선택지 번호다.
        public int SourceIndex { get; }

        // 고를 수 있는가. 못 고르면 아래 둘이 왜인지를 말한다.
        public bool IsSelectable { get; }

        // 저작자가 쓴 잠금 안내문.
        public string LockedReason { get; }

        // 미달 조건 중 첫 번째 것만 반환.
        public ProgressionCondition BlockingCondition { get; }

        private ResolvedOption(
            EpisodeOption option,
            int sourceIndex,
            bool isSelectable,
            string lockedReason,
            ProgressionCondition blockingCondition)
        {
            Option = option;
            SourceIndex = sourceIndex;
            IsSelectable = isSelectable;
            LockedReason = lockedReason;
            BlockingCondition = blockingCondition;
        }

        internal static ResolvedOption Shown(EpisodeOption option, int sourceIndex) =>
            new(option, sourceIndex, true, string.Empty, default);

        internal static ResolvedOption Locked(
            EpisodeOption option, int sourceIndex, ProgressionCondition blocking) =>
            new(option, sourceIndex, false, option.LockedReasonText, blocking);

        public override string ToString() =>
            IsSelectable ? $"{Option}" : $"{Option} [잠김: {BlockingCondition}]";
    }
}
