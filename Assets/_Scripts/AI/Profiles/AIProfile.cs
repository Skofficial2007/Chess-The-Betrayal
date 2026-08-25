namespace ChessTheBetrayal.AI.Profiles
{
    /// <summary>
    /// Data-driven behavioral tier for the AI: one row of this struct fully describes a
    /// difficulty/personality preset. Adding a new tier is adding a row to
    /// <see cref="AIProfileTable"/>, which is where every tier the game ships is defined.
    ///
    /// <see cref="MaxDepth"/>/<see cref="TimeBudget"/> shape the search itself (via
    /// AISearchSettings.FromProfile); <see cref="BlunderRate"/>/<see cref="BlunderMarginCp"/>/
    /// <see cref="TieBreakWindowCp"/>/<see cref="BetrayalAggression"/> shape which of the search's
    /// own ranked root moves gets picked (via MoveSelectionPolicy); <see cref="AttackDefenseBias"/>
    /// reweights the evaluator (via EvaluationWeights.FromProfile); and
    /// <see cref="UseOpeningBook"/>/<see cref="OpeningBookDepthPlies"/> decide how much opening
    /// theory the tier plays from memory before it starts thinking for itself (via
    /// OpeningBookPolicy).
    /// </summary>
    public readonly struct AIProfile
    {
        public readonly string Id;
        public readonly int MaxDepth;
        public readonly AITimeBudget TimeBudget;
        public readonly float BlunderRate;
        public readonly int BlunderMarginCp;
        public readonly float BetrayalAggression;
        public readonly float AttackDefenseBias;
        public readonly int TieBreakWindowCp;
        public readonly bool UseOpeningBook;

        /// <summary>
        /// How far into a game this tier is allowed to answer from the opening book, counted in
        /// plies (single moves) played so far, or 0 for "as far as the book goes". Once the game
        /// passes this point the tier searches for every move even if the book still knows the
        /// position, which is what stops a beginner-level tier from reciting eight moves of
        /// flawless theory and then hanging a piece on the ninth.
        ///
        /// Counted in game plies rather than in book moves the AI itself played, so it means the
        /// same thing whichever colour the AI has, and so taking back a move puts the AI back
        /// where it was instead of leaving a private counter running ahead of the board.
        /// </summary>
        public readonly int OpeningBookDepthPlies;

        /// <summary>
        /// How far below the best score the root rescore pass has to look before the personality
        /// dials choose between candidates. Alpha-beta only proves the best move's score exactly;
        /// everything else comes back tightened against a window, so a dial picking a
        /// nearly-as-good move needs those scores re-established over a margin wide enough to hold
        /// every candidate it might pick. Whichever dial reaches further sets it, and zero here
        /// means no dial is asking, so the pass is skipped and costs nothing.
        /// </summary>
        public int RescoreMarginCp => BlunderMarginCp > TieBreakWindowCp ? BlunderMarginCp : TieBreakWindowCp;

        public AIProfile(
            string id,
            int maxDepth,
            AITimeBudget timeBudget,
            float blunderRate,
            int blunderMarginCp,
            float betrayalAggression,
            float attackDefenseBias,
            int tieBreakWindowCp,
            bool useOpeningBook,
            int openingBookDepthPlies = 0)
        {
            Id = id;
            MaxDepth = maxDepth;
            TimeBudget = timeBudget;
            BlunderRate = blunderRate;
            BlunderMarginCp = blunderMarginCp;
            BetrayalAggression = betrayalAggression;
            AttackDefenseBias = attackDefenseBias;
            TieBreakWindowCp = tieBreakWindowCp;
            UseOpeningBook = useOpeningBook;
            OpeningBookDepthPlies = openingBookDepthPlies;
        }

        /// <summary>Zero-dial sentinel used where no AI personality is being modeled (e.g. a bare
        /// AsyncAIAgent construction with no profile injected). MoveSelectionPolicy's zero-dial
        /// fast path returns the search's own best move for this, unconditionally.</summary>
        public static readonly AIProfile None = new AIProfile(
            id: "none", maxDepth: 1, timeBudget: new AITimeBudget(1000, 1000),
            blunderRate: 0f, blunderMarginCp: 0, betrayalAggression: 0f,
            attackDefenseBias: 1.0f, tieBreakWindowCp: 0, useOpeningBook: false);
    }
}
