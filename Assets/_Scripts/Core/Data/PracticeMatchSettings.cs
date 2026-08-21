namespace ChessTheBetrayal.Core.Data
{
    /// <summary>
    /// Immutable value type aggregating every player-configurable choice on the Practice Match Setup
    /// screen. Mirrors GameModeConfig's shape and placement (zero heap pressure, Core-only, no
    /// Unity dependency).
    ///
    /// Consumed end to end by MatchFlowCoordinator.HandleTeamAnimationComplete: BetrayalEnabled sets
    /// BoardState.BetrayalRightAvailable, AiDefendOnly maps to AISearchSettings.BetrayalUsage (kept
    /// as a Core-safe bool here since Core.Data must not depend on ChessTheBetrayal.AI),
    /// RetributionSkipAllowed gates GameHUD's Skip button. Difficulty is stored but not yet branched
    /// on — every level currently resolves to the same AISearchSettings.Ultimate() search until the
    /// dedicated difficulty ticket lands.
    /// </summary>
    public readonly struct PracticeMatchSettings
    {
        /// <summary>Board-level Option 1 "Normal Mode". False sets BetrayalRightAvailable = false
        /// at match init, symmetric for both sides, zero AI code involved.</summary>
        public readonly bool BetrayalEnabled;

        /// <summary>Agent-level Betrayal policy (Issue B / AISearchSettings.BetrayalUsage), pre-mapped
        /// to a Core-safe bool: true means DefendOnly, false means Full. Only meaningful when
        /// BetrayalEnabled is true.</summary>
        public readonly bool AiDefendOnly;

        /// <summary>Human-only voluntary Retribution skip (Option 3). Never modeled as an AI
        /// search branch — see AI_System_Design doc.</summary>
        public readonly bool RetributionSkipAllowed;

        public readonly AIDifficulty Difficulty;

        public PracticeMatchSettings(bool betrayalEnabled, bool aiDefendOnly,
            bool retributionSkipAllowed, AIDifficulty difficulty)
        {
            BetrayalEnabled = betrayalEnabled;
            AiDefendOnly = aiDefendOnly;
            RetributionSkipAllowed = retributionSkipAllowed;
            Difficulty = difficulty;
        }

        /// <summary>Sensible defaults matching the panel's default toggle states.</summary>
        public static PracticeMatchSettings Default => new PracticeMatchSettings(
            betrayalEnabled: true,
            aiDefendOnly: false,
            retributionSkipAllowed: true,
            difficulty: AIDifficulty.Normal);
    }
}
