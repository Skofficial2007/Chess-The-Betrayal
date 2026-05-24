namespace ChessTheBetrayal.Core.Data
{
    /// <summary>
    /// Immutable value type describing a chess clock configuration.
    /// Passed by copy with zero heap pressure on every read.
    /// </summary>
    public readonly struct GameModeConfig
    {
        public readonly string Label;
        public readonly long   BaseTimeMs;    // Starting time per player in milliseconds
        public readonly long   IncrementMs;   // Fischer increment added after each confirmed move
        public readonly bool   IsUnlimited;   // When true: no clock, no tick, no expiry

        /// <summary>
        /// Standard timed-mode constructor. Sets IsUnlimited = false.
        /// </summary>
        public GameModeConfig(string label, long baseTimeMs, long incrementMs)
        {
            Label        = label;
            BaseTimeMs   = baseTimeMs;
            IncrementMs  = incrementMs;
            IsUnlimited  = false;
        }

        // Private sentinel constructor used only by the Unlimited static property.
        private GameModeConfig(string label)
        {
            Label        = label;
            BaseTimeMs   = long.MaxValue;
            IncrementMs  = 0;
            IsUnlimited  = true;
        }

        /// <summary>
        /// The single canonical "no clock" sentinel for the entire codebase.
        /// GameManager defaults to this. AI matches always use this.
        /// </summary>
        public static GameModeConfig Unlimited => new GameModeConfig("Unlimited");
    }
}
