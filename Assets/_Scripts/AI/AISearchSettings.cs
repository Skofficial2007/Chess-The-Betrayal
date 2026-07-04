namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// How the AGENT is permitted to use the Betrayal mechanic. This is an agent policy, NOT a
    /// board rule — it must never touch BoardState.BetrayalRightAvailable.
    ///
    /// The distinction is the single most important correctness point in the whole AI:
    ///
    ///   Full        : the agent may initiate Betrayal (Act moves are in its root move list),
    ///                 and it defends/responds to the opponent's Betrayal normally.
    ///
    ///   DefendOnly  : the agent will NEVER initiate a Betrayal (Act moves stripped from the
    ///                 agent's ROOT choices), but the human opponent can still trigger one, and
    ///                 the search tree MUST still explore the opponent doing so — otherwise the
    ///                 AI plays blind into a human Betrayal. So we strip Act at the root only,
    ///                 never inside the recursion.
    ///
    /// This maps directly to your Settings toggle "Enable/Disable AI use of Betrayal".
    /// </summary>
    public enum BetrayalUsage
    {
        Full,
        DefendOnly
    }

    /// <summary>
    /// Immutable per-search configuration. Constructed on the main thread from the player's
    /// Settings, then handed to the background search — never mutated mid-search, so it's safe
    /// to read from the worker thread without locking.
    /// </summary>
    public readonly struct AISearchSettings
    {
        /// <summary>Hard depth cap for iterative deepening. "Ultimate" (untimed) mode leans on this
        /// instead of a clock, so the search is deterministic and reproducible across runs.</summary>
        public readonly int MaxDepth;

        /// <summary>Soft wall-clock budget in ms. Iterative deepening returns the best move from the
        /// last fully-completed depth once this elapses. Even in "no timer" mode we keep a budget so a
        /// pathological position can't hang the worker thread forever; set high (e.g. 5000) for Ultimate.</summary>
        public readonly int SoftTimeBudgetMs;

        /// <summary>Agent-level Betrayal policy (Issue B). Board-level "plain chess" (Option 1) is
        /// handled separately in Core via BoardState.BetrayalRightAvailable == false and does not
        /// belong here.</summary>
        public readonly BetrayalUsage BetrayalUsage;

        public AISearchSettings(int maxDepth, int softTimeBudgetMs, BetrayalUsage betrayalUsage)
        {
            MaxDepth = maxDepth;
            SoftTimeBudgetMs = softTimeBudgetMs;
            BetrayalUsage = betrayalUsage;
        }

        public static AISearchSettings Ultimate(BetrayalUsage usage) =>
            new AISearchSettings(maxDepth: 7, softTimeBudgetMs: 5000, usage);
    }
}
