namespace Ked.Progression
{
    public sealed class UnreachableEpisode
    {
        public string EpisodeId { get; }
        public UnreachableCause Cause { get; }

        // UnreachableCause.BlockedByCondition
        public ProgressionCondition BlockingCondition { get; }

        public UnreachableEpisode(
            string episodeId, UnreachableCause cause, ProgressionCondition blockingCondition)
        {
            EpisodeId = episodeId;
            Cause = cause;
            BlockingCondition = blockingCondition;
        }

        public override string ToString() =>
            BlockingCondition.IsConstructed
                ? $"{EpisodeId}: {Cause}({BlockingCondition})"
                : $"{EpisodeId}: {Cause}";
    }
}