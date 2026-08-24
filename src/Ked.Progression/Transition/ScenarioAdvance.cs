namespace Ked.Progression
{
    public enum ScenarioAdvanceKind
    {
        NextChapter = 0,   // Continues to the next chapter
        ScenarioEnded = 1, // Reached an ending(intentional terminal point).

        /// <summary>
        /// The chapter stopped at a node with no ending key and no outgoing path.
        /// This is not an intentional ending.
        ///
        /// If this were treated as <see cref="ScenarioEnded"/>,
        /// an unfinished node would appear identical to an intentionally authored ending in the UI.
        /// </summary>
        DeadEnd = 2,
    }
    
    // Determines what happens after a chapter ends.
    public readonly struct ScenarioAdvance
    {
        public ScenarioAdvanceKind Kind { get; }

        // Which ending was reached.
        // Empty when "Kind == ScenarioAdvanceKind.DeadEnd".
        public string EndingKey { get; }

        /// Populated only when <see cref="ScenarioAdvanceKind.NextChapter"/>.
        public string NextChapterId { get; }

        private ScenarioAdvance(
            ScenarioAdvanceKind kind, string endingKey, string nextChapterId)
        {
            Kind = kind;
            EndingKey = endingKey;
            NextChapterId = nextChapterId;
        }

        internal static ScenarioAdvance ToChapter(string endingKey, string nextChapterId) =>
            new(ScenarioAdvanceKind.NextChapter, endingKey, nextChapterId);

        internal static ScenarioAdvance Ended(string endingKey) =>
            new(ScenarioAdvanceKind.ScenarioEnded, endingKey, string.Empty);

        internal static ScenarioAdvance DeadEnd() =>
            new(ScenarioAdvanceKind.DeadEnd, string.Empty, string.Empty);

        public override string ToString() => 
            Kind == ScenarioAdvanceKind.NextChapter 
                ? $"{Kind}({EndingKey} → {NextChapterId})"
                : $"{Kind}({EndingKey})";
    }
}