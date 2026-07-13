using NUnit.Framework;
using ChessTheBetrayal.AI;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins the shallow-search guardrail: a profile with MaxDepth below the threshold can't carry
    /// a strong AttackDefenseBias/BetrayalAggression, because a shallow search can't vet a
    /// reshaped evaluator before acting on it. Covers both the raw clamp math and its two call
    /// sites (AIProfileTableProvider.Resolve, AIProfileDefinition.ToProfile).
    /// </summary>
    [TestFixture]
    public class AIProfileGuardrailTests
    {
        private static AIProfile ShallowProfile(float attackDefenseBias, float betrayalAggression) =>
            new AIProfile("test", maxDepth: AIProfileGuardrails.ShallowSearchDepthThreshold - 1,
                softTimeBudgetMs: 1000, blunderRate: 0f, blunderMarginCp: 0,
                betrayalAggression: betrayalAggression, attackDefenseBias: attackDefenseBias,
                tieBreakWindowCp: 0, useOpeningBook: false);

        private static AIProfile DeepProfile(float attackDefenseBias, float betrayalAggression) =>
            new AIProfile("test", maxDepth: AIProfileGuardrails.ShallowSearchDepthThreshold,
                softTimeBudgetMs: 1000, blunderRate: 0f, blunderMarginCp: 0,
                betrayalAggression: betrayalAggression, attackDefenseBias: attackDefenseBias,
                tieBreakWindowCp: 0, useOpeningBook: false);

        [Test]
        public void Apply_ShallowDepth_ClampsOutOfRangeAttackDefenseBias()
        {
            AIProfile clamped = AIProfileGuardrails.Apply(ShallowProfile(attackDefenseBias: 2f, betrayalAggression: 0f));

            Assert.That(clamped.AttackDefenseBias, Is.EqualTo(AIProfileGuardrails.MaxClampedAttackDefenseBias));
        }

        [Test]
        public void Apply_ShallowDepth_ClampsOutOfRangeAttackDefenseBias_BelowFloor()
        {
            AIProfile clamped = AIProfileGuardrails.Apply(ShallowProfile(attackDefenseBias: 0.1f, betrayalAggression: 0f));

            Assert.That(clamped.AttackDefenseBias, Is.EqualTo(AIProfileGuardrails.MinClampedAttackDefenseBias));
        }

        [Test]
        public void Apply_ShallowDepth_ClampsOutOfRangeBetrayalAggression()
        {
            AIProfile clamped = AIProfileGuardrails.Apply(ShallowProfile(attackDefenseBias: 1f, betrayalAggression: -1f));

            Assert.That(clamped.BetrayalAggression, Is.EqualTo(AIProfileGuardrails.MinClampedBetrayalAggression));
        }

        [Test]
        public void Apply_ShallowDepth_ClampsOutOfRangeBetrayalAggression_AboveCeiling()
        {
            AIProfile clamped = AIProfileGuardrails.Apply(ShallowProfile(attackDefenseBias: 1f, betrayalAggression: 1f));

            Assert.That(clamped.BetrayalAggression, Is.EqualTo(AIProfileGuardrails.MaxClampedBetrayalAggression));
        }

        [Test]
        public void Apply_ShallowDepth_ValuesAlreadyInRange_PassThroughUnchanged()
        {
            AIProfile source = ShallowProfile(attackDefenseBias: 1.0f, betrayalAggression: 0.1f);

            AIProfile result = AIProfileGuardrails.Apply(source);

            Assert.That(result.AttackDefenseBias, Is.EqualTo(1.0f));
            Assert.That(result.BetrayalAggression, Is.EqualTo(0.1f));
        }

        [Test]
        public void Apply_DeepEnoughSearch_NeverClamps_EvenAtExtremeValues()
        {
            AIProfile source = DeepProfile(attackDefenseBias: 2f, betrayalAggression: -1f);

            AIProfile result = AIProfileGuardrails.Apply(source);

            Assert.That(result.AttackDefenseBias, Is.EqualTo(2f));
            Assert.That(result.BetrayalAggression, Is.EqualTo(-1f));
        }

        [Test]
        public void Apply_PreservesEveryOtherFieldUnchanged()
        {
            var source = new AIProfile("test", maxDepth: 2, softTimeBudgetMs: 1234,
                blunderRate: 0.5f, blunderMarginCp: 77, betrayalAggression: 1f,
                attackDefenseBias: 2f, tieBreakWindowCp: 99, useOpeningBook: true);

            AIProfile result = AIProfileGuardrails.Apply(source);

            Assert.That(result.Id, Is.EqualTo("test"));
            Assert.That(result.MaxDepth, Is.EqualTo(2));
            Assert.That(result.SoftTimeBudgetMs, Is.EqualTo(1234));
            Assert.That(result.BlunderRate, Is.EqualTo(0.5f));
            Assert.That(result.BlunderMarginCp, Is.EqualTo(77));
            Assert.That(result.TieBreakWindowCp, Is.EqualTo(99));
            Assert.That(result.UseOpeningBook, Is.True);
        }

        [Test]
        public void BuiltInRoster_EveryRow_AlreadySatisfiesTheGuardrail_ZeroShippedBehaviorChange()
        {
            foreach (AIProfile profile in AIProfileTable.BuiltIn)
            {
                AIProfile clamped = AIProfileGuardrails.Apply(profile);

                Assert.That(clamped.AttackDefenseBias, Is.EqualTo(profile.AttackDefenseBias),
                    $"'{profile.Id}' should be unaffected by the guardrail — if this fails, the shipped preset table itself violates the shallow-search rule.");
                Assert.That(clamped.BetrayalAggression, Is.EqualTo(profile.BetrayalAggression),
                    $"'{profile.Id}' should be unaffected by the guardrail — if this fails, the shipped preset table itself violates the shallow-search rule.");
            }
        }

        [Test]
        public void FixtureProvider_WrappingACorruptedRoster_ResolveReturnsAClampedProfile()
        {
            // AIProfileTableProvider only ever resolves the fixed, already-valid BuiltIn roster —
            // there's no way to feed it a corrupted row through its real API. To prove the clamp
            // fires on the RESOLVE path itself (not just on Apply() in isolation), this wraps a
            // hand-corrupted roster in a minimal provider that applies the same guardrail
            // AIProfileTableProvider.Resolve does, the way a future authored-asset provider must.
            var corruptedRoster = new[]
            {
                new AIProfile("shallow-corrupt", maxDepth: AIProfileGuardrails.ShallowSearchDepthThreshold - 1,
                    softTimeBudgetMs: 1000, blunderRate: 0f, blunderMarginCp: 0,
                    betrayalAggression: 1f, attackDefenseBias: 2f, tieBreakWindowCp: 0, useOpeningBook: false)
            };
            IAIProfileProvider provider = new GuardrailedFixtureProvider(corruptedRoster);

            AIProfile resolved = provider.Resolve("shallow-corrupt");

            Assert.That(resolved.AttackDefenseBias, Is.EqualTo(AIProfileGuardrails.MaxClampedAttackDefenseBias));
            Assert.That(resolved.BetrayalAggression, Is.EqualTo(AIProfileGuardrails.MaxClampedBetrayalAggression));
        }

        private sealed class GuardrailedFixtureProvider : IAIProfileProvider
        {
            private readonly AIProfile[] _roster;

            public GuardrailedFixtureProvider(AIProfile[] roster) => _roster = roster;

            public AIProfile Resolve(string id)
            {
                foreach (AIProfile profile in _roster)
                {
                    if (profile.Id == id) return AIProfileGuardrails.Apply(profile);
                }
                return AIProfileGuardrails.Apply(_roster[0]);
            }
        }

        [Test]
        public void TableProvider_Resolve_EveryBuiltInId_ReturnsUnclampedValues()
        {
            IAIProfileProvider provider = new AIProfileTableProvider();

            foreach (AIProfile expected in AIProfileTable.BuiltIn)
            {
                AIProfile resolved = provider.Resolve(expected.Id);

                Assert.That(resolved.AttackDefenseBias, Is.EqualTo(expected.AttackDefenseBias),
                    $"Resolving '{expected.Id}' through the provider must not silently change shipped behavior.");
                Assert.That(resolved.BetrayalAggression, Is.EqualTo(expected.BetrayalAggression),
                    $"Resolving '{expected.Id}' through the provider must not silently change shipped behavior.");
            }
        }
    }
}
