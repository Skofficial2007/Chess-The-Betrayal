namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// Scales BetrayalAwareEvaluator's non-material terms so AIProfile.AttackDefenseBias/
    /// BetrayalAggression actually change how a tier plays, not just how deep it searches.
    /// Identity (1,1,1) is bit-identical to the evaluator's pre-AI-25 behavior — every profile
    /// with AttackDefenseBias=1/BetrayalAggression=0 (every tier except Aggressive/Extreme) must
    /// still score positions exactly as before.
    /// </summary>
    public readonly struct EvaluationWeights
    {
        public readonly float AttackScale;
        public readonly float DefenseScale;
        public readonly float BetrayalOptionScale;

        public EvaluationWeights(float attackScale, float defenseScale, float betrayalOptionScale)
        {
            AttackScale = attackScale;
            DefenseScale = defenseScale;
            BetrayalOptionScale = betrayalOptionScale;
        }

        public static readonly EvaluationWeights Identity = new EvaluationWeights(1f, 1f, 1f);

        // AttackDefenseBias is documented over [0.5, 2.0]; DefenseScale = 2 - bias mirrors that
        // range symmetrically. The floor keeps defense from vanishing at the bias ceiling; the
        // ceiling is bias's own floor reflected through the same mapping. Inert for every current
        // AIProfileTable row (bias in [1.0, 1.5]) — pure defensive programming against a future tier.
        private const float MinDefenseScale = 0.5f;
        private const float MaxDefenseScale = 1.5f;

        public static EvaluationWeights FromProfile(AIProfile profile)
        {
            float attackScale = profile.AttackDefenseBias;

            float defenseScale = 2f - profile.AttackDefenseBias;
            if (defenseScale < MinDefenseScale) defenseScale = MinDefenseScale;
            if (defenseScale > MaxDefenseScale) defenseScale = MaxDefenseScale;

            float betrayalOptionScale = 1f + (0.5f * profile.BetrayalAggression);

            return new EvaluationWeights(attackScale, defenseScale, betrayalOptionScale);
        }
    }
}
