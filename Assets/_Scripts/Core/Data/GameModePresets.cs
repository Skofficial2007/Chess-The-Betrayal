namespace ChessTheBetrayal.Core.Data
{
    /// <summary>
    /// A static catalog of standard chess time controls.
    /// Values are configured in milliseconds and mirror standard FIDE and online chess formats.
    /// </summary>
    public static class GameModePresets
    {
        public static readonly GameModeConfig Bullet1_0 = new GameModeConfig("Bullet 1|0", 60_000, 0);
        public static readonly GameModeConfig Bullet2_1 = new GameModeConfig("Bullet 2|1", 120_000, 1_000);
        public static readonly GameModeConfig Blitz3_0 = new GameModeConfig("Blitz 3|0", 180_000, 0);
        public static readonly GameModeConfig Blitz5_5 = new GameModeConfig("Blitz 5|5", 300_000, 5_000);
        public static readonly GameModeConfig Rapid10_0 = new GameModeConfig("Rapid 10|0", 600_000, 0);
        public static readonly GameModeConfig Rapid15_10 = new GameModeConfig("Rapid 15|10", 900_000, 10_000);
        public static readonly GameModeConfig Unlimited = GameModeConfig.Unlimited;

        /// <summary>
        /// An ordered collection of all supported game modes for UI iteration.
        /// The Unlimited sentinel is positioned last so timed modes appear first in selection menus.
        /// </summary>
        public static readonly GameModeConfig[] All = new GameModeConfig[]
        {
            Bullet1_0, Bullet2_1, Blitz3_0, Blitz5_5, Rapid10_0, Rapid15_10, Unlimited
        };
    }
}