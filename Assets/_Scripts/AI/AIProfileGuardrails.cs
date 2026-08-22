namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// The one rule every AIProfile must satisfy regardless of where it came from (a built-in
    /// table row, an authored ScriptableObject, or a hand-constructed instance): a shallow search
    /// can't be trusted to reweight the evaluator by much, because the reweighting only gets
    /// vetted by the search depth that follows it. At low depth there isn't enough search left
    /// to catch a reshaped evaluator walking into a bad line, so a shallow profile with a strong
    /// bias reads as erratic rather than as a coherent difficulty or personality.
    /// </summary>
    public static class AIProfileGuardrails
    {
        /// <summary>Below this depth, bias/aggression get clamped — see class summary for why.</summary>
        public const int ShallowSearchDepthThreshold = 4;

        public const float MinClampedAttackDefenseBias = 0.8f;
        public const float MaxClampedAttackDefenseBias = 1.2f;
        public const float MinClampedBetrayalAggression = -0.3f;
        public const float MaxClampedBetrayalAggression = 0.3f;

        /// <summary>True if maxDepth is shallow enough that bias/aggression must be clamped.</summary>
        public static bool RequiresClamp(int maxDepth) => maxDepth < ShallowSearchDepthThreshold;

        public static float ClampAttackDefenseBias(int maxDepth, float attackDefenseBias)
        {
            if (!RequiresClamp(maxDepth)) return attackDefenseBias;
            return Clamp(attackDefenseBias, MinClampedAttackDefenseBias, MaxClampedAttackDefenseBias);
        }

        public static float ClampBetrayalAggression(int maxDepth, float betrayalAggression)
        {
            if (!RequiresClamp(maxDepth)) return betrayalAggression;
            return Clamp(betrayalAggression, MinClampedBetrayalAggression, MaxClampedBetrayalAggression);
        }

        /// <summary>Applies both clamps at once, returning a profile that's always safe to search with.</summary>
        public static AIProfile Apply(AIProfile profile)
        {
            if (!RequiresClamp(profile.MaxDepth)) return profile;

            return new AIProfile(
                profile.Id,
                profile.MaxDepth,
                profile.TimeBudget,
                profile.BlunderRate,
                profile.BlunderMarginCp,
                ClampBetrayalAggression(profile.MaxDepth, profile.BetrayalAggression),
                ClampAttackDefenseBias(profile.MaxDepth, profile.AttackDefenseBias),
                profile.TieBreakWindowCp,
                profile.UseOpeningBook);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
