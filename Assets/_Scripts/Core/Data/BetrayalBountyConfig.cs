namespace ChessTheBetrayal.Core.Data
{
    /// <summary>
    /// Immutable value type describing the Retribution time-bounty schedule, tiered by base time
    /// control. No Unity dependency — mirrors <see cref="GameModeConfig"/>'s shape and placement.
    /// Values are configured on GameManager's Inspector and handed to
    /// ChessTheBetrayal.Gameplay.MatchDriver once per match via MatchDriver.SetBountyConfig.
    /// </summary>
    public readonly struct BetrayalBountyConfig
    {
        public readonly long BulletMs;
        public readonly long Bullet2Ms;
        public readonly long BlitzMs;
        public readonly long Blitz5Ms;
        public readonly long RapidMs;
        public readonly long Rapid15Ms;

        public BetrayalBountyConfig(long bulletMs, long bullet2Ms, long blitzMs, long blitz5Ms, long rapidMs, long rapid15Ms)
        {
            BulletMs = bulletMs;
            Bullet2Ms = bullet2Ms;
            BlitzMs = blitzMs;
            Blitz5Ms = blitz5Ms;
            RapidMs = rapidMs;
            Rapid15Ms = rapid15Ms;
        }
    }
}
