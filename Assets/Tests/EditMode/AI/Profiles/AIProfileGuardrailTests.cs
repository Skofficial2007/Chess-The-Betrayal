using NUnit.Framework;
using ChessTheBetrayal.AI.Profiles;

namespace ChessTheBetrayal.Tests.EditMode.AI.Profiles
{
    /// <summary>
    /// Pins the shallow-search guardrail: a profile with MaxDepth below the threshold can't carry
    /// a strong AttackDefenseBias/BetrayalAggression, because a shallow search can't vet a
    /// reshaped evaluator before acting on it. Covers both the raw clamp math and the call site
    /// that applies it, AIProfileTableProvider.Resolve.
    /// </summary>
    [TestFixture]
    public class AIProfileGuardrailTests
    {
        private static AIProfile ShallowProfile(float attackDefenseBias, float betrayalAggression) =>
            new AIProfile("test", maxDepth: AIProfileGuardrails.ShallowSearchDepthThreshold - 1,
                timeBudget: new AITimeBudget(1000, 1500), blunderRate: 0f, blunderMarginCp: 0,
                betrayalAggression: betrayalAggression, attackDefenseBias: attackDefenseBias,
                tieBreakWindowCp: 0, useOpeningBook: false);

        private static AIProfile DeepProfile(float attackDefenseBias, float betrayalAggression) =>
            new AIProfile("test", maxDepth: AIProfileGuardrails.ShallowSearchDepthThreshold,
                timeBudget: new AITimeBudget(1000, 1500), blunderRate: 0f, blunderMarginCp: 0,
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
            var source = new AIProfile("test", maxDepth: 2, timeBudget: new AITimeBudget(1234, 1999),
                blunderRate: 0.5f, blunderMarginCp: 77, betrayalAggression: 1f,
                attackDefenseBias: 2f, tieBreakWindowCp: 99, useOpeningBook: true,
                openingBookDepthPlies: 6);

            AIProfile result = AIProfileGuardrails.Apply(source);

            Assert.That(result.Id, Is.EqualTo("test"));
            Assert.That(result.MaxDepth, Is.EqualTo(2));
            Assert.That(result.TimeBudget.SoftMs, Is.EqualTo(1234));
            Assert.That(result.TimeBudget.HardMs, Is.EqualTo(1999));
            Assert.That(result.BlunderRate, Is.EqualTo(0.5f));
            Assert.That(result.BlunderMarginCp, Is.EqualTo(77));
            Assert.That(result.TieBreakWindowCp, Is.EqualTo(99));
            Assert.That(result.UseOpeningBook, Is.True);
            Assert.That(result.OpeningBookDepthPlies, Is.EqualTo(6));
        }

        [Test]
        public void Apply_ShallowDepth_KeepsTheOpeningBookAllowance()
        {
            // The clamp only rebuilds a profile when the search is shallow, and 'easy' is the only
            // shipped tier shallow enough to go down that path. So a field the rebuild forgets to
            // carry is lost on exactly one tier while every other tier keeps working — which is
            // both the hardest version of this bug to notice and, for a book allowance, the tier
            // that most needs one.
            AIProfile shallow = new AIProfile("shallow", maxDepth: AIProfileGuardrails.ShallowSearchDepthThreshold - 1,
                timeBudget: new AITimeBudget(1000, 1500), blunderRate: 0f, blunderMarginCp: 0,
                betrayalAggression: 0f, attackDefenseBias: 1f, tieBreakWindowCp: 0,
                useOpeningBook: true, openingBookDepthPlies: 4);

            Assert.That(AIProfileGuardrails.Apply(shallow).OpeningBookDepthPlies, Is.EqualTo(4),
                "The clamp rebuilds shallow profiles field by field and dropped the book allowance.");
        }

        [Test]
        public void Resolve_EveryTier_KeepsItsOpeningBookAllowance()
        {
            IAIProfileProvider provider = new AIProfileTableProvider();

            foreach (AIProfile expected in AIProfileTable.BuiltIn)
            {
                AIProfile resolved = provider.Resolve(expected.Id);

                Assert.That(resolved.OpeningBookDepthPlies, Is.EqualTo(expected.OpeningBookDepthPlies),
                    $"Resolving '{expected.Id}' dropped its opening-book allowance " +
                    $"({expected.OpeningBookDepthPlies} plies) somewhere on the way through the guardrail.");
                Assert.That(resolved.UseOpeningBook, Is.EqualTo(expected.UseOpeningBook),
                    $"Resolving '{expected.Id}' changed whether it uses the opening book at all.");
            }
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
        public void ResolvingAShallowProfileWithStrongDialsClampsThemOnTheWayOut()
        {
            // The clamp lives on the path a running match takes from a difficulty id to a profile,
            // and every shipped row already sits inside its range - so clamped and unclamped are
            // the same thing for all six, and removing the clamp from that path changed nothing
            // anywhere. This used to be checked by a stand-in provider written in this file that
            // called the guardrail itself, which could only ever prove the guardrail works.
            //
            // The real provider takes the roster now, so a row that does need clamping goes through
            // the same code a match does.
            var beyondTheGuardrail = new[]
            {
                new AIProfile("shallow-and-strident", maxDepth: AIProfileGuardrails.ShallowSearchDepthThreshold - 1,
                    timeBudget: new AITimeBudget(1000, 1500), blunderRate: 0f, blunderMarginCp: 0,
                    betrayalAggression: 1f, attackDefenseBias: 2f, tieBreakWindowCp: 0, useOpeningBook: false)
            };
            IAIProfileProvider provider = new AIProfileTableProvider(beyondTheGuardrail);

            AIProfile resolved = provider.Resolve("shallow-and-strident");

            Assert.That(resolved.AttackDefenseBias, Is.EqualTo(AIProfileGuardrails.MaxClampedAttackDefenseBias),
                "A depth-3 tier resolved with its attack dial at 2.0 still reweights the evaluator " +
                "harder than three plies of search can vet, which reads as erratic rather than hard.");
            Assert.That(resolved.BetrayalAggression, Is.EqualTo(AIProfileGuardrails.MaxClampedBetrayalAggression));
        }

        [Test]
        public void ResolvingAnUnknownIdFallsBackToADefaultThatIsAlsoClamped()
        {
            // The fallback is a second return inside the same method, so it can lose the clamp on
            // its own while the hit above keeps it.
            var beyondTheGuardrail = new[]
            {
                new AIProfile(AIProfileTable.DefaultId, maxDepth: AIProfileGuardrails.ShallowSearchDepthThreshold - 1,
                    timeBudget: new AITimeBudget(1000, 1500), blunderRate: 0f, blunderMarginCp: 0,
                    betrayalAggression: 1f, attackDefenseBias: 2f, tieBreakWindowCp: 0, useOpeningBook: false)
            };
            IAIProfileProvider provider = new AIProfileTableProvider(beyondTheGuardrail);

            AIProfile resolved = provider.Resolve("no-such-tier");

            Assert.That(resolved.Id, Is.EqualTo(AIProfileTable.DefaultId));
            Assert.That(resolved.AttackDefenseBias, Is.EqualTo(AIProfileGuardrails.MaxClampedAttackDefenseBias));
            Assert.That(resolved.BetrayalAggression, Is.EqualTo(AIProfileGuardrails.MaxClampedBetrayalAggression));
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
