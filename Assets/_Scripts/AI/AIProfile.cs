namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// Data-driven behavioral tier for the AI: one row of this struct fully describes a
    /// difficulty/personality preset. Adding a new tier is adding a new row to
    /// <see cref="AIProfileTable"/> (or a new <see cref="AIProfileDefinition"/> asset later) —
    /// never a code change. See ADR_AI23_Profile_EventStream_OpeningBook.md Section 1.
    ///
    /// <see cref="MaxDepth"/>/<see cref="SoftTimeBudgetMs"/> shape the search itself (via
    /// AISearchSettings.FromProfile); <see cref="BlunderRate"/>/<see cref="BlunderMarginCp"/>/
    /// <see cref="TieBreakWindowCp"/>/<see cref="BetrayalAggression"/> shape which of the search's
    /// own ranked root moves gets picked (via MoveSelectionPolicy). <see cref="AttackDefenseBias"/>
    /// and <see cref="UseOpeningBook"/> remain inert data for the evaluator-weighting and
    /// opening-book tickets that follow.
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

        /// <summary>Zero-dial sentinel used where no AI personality is being modeled (e.g. a bare
        /// AsyncAIAgent construction with no profile injected). MoveSelectionPolicy's zero-dial
        /// fast path returns the search's own best move for this, unconditionally.</summary>
        public static readonly AIProfile None = new AIProfile(
            id: "none", maxDepth: 1, softTimeBudgetMs: 1000,
            blunderRate: 0f, blunderMarginCp: 0, betrayalAggression: 0f,
            attackDefenseBias: 1.0f, tieBreakWindowCp: 0, useOpeningBook: false);
    }
}
