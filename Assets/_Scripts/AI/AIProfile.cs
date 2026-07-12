namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// Data-driven behavioral tier for the AI: one row of this struct fully describes a
    /// difficulty/personality preset. Adding a new tier is adding a new row to
    /// <see cref="AIProfileTable"/> (or a new <see cref="AIProfileDefinition"/> asset later) —
    /// never a code change. See ADR_AI23_Profile_EventStream_OpeningBook.md Section 1.
    ///
    /// Only <see cref="MaxDepth"/>/<see cref="SoftTimeBudgetMs"/> are consumed by search today
    /// (via AISearchSettings.FromProfile). The remaining dials are carried as inert data for the
    /// selection-policy and weighted-evaluator tickets that follow.
    /// </summary>
    public readonly struct AIProfile
    {
        public readonly string Id;
        public readonly int MaxDepth;
        public readonly int SoftTimeBudgetMs;
        public readonly float BlunderRate;
        public readonly int BlunderMarginCp;
        public readonly float BetrayalAggression;
        public readonly float AttackDefenseBias;
        public readonly int TieBreakWindowCp;
        public readonly bool UseOpeningBook;

        public AIProfile(
            string id,
            int maxDepth,
            int softTimeBudgetMs,
            float blunderRate,
            int blunderMarginCp,
            float betrayalAggression,
            float attackDefenseBias,
            int tieBreakWindowCp,
            bool useOpeningBook)
        {
            Id = id;
            MaxDepth = maxDepth;
            SoftTimeBudgetMs = softTimeBudgetMs;
            BlunderRate = blunderRate;
            BlunderMarginCp = blunderMarginCp;
            BetrayalAggression = betrayalAggression;
            AttackDefenseBias = attackDefenseBias;
            TieBreakWindowCp = tieBreakWindowCp;
            UseOpeningBook = useOpeningBook;
        }
    }
}
