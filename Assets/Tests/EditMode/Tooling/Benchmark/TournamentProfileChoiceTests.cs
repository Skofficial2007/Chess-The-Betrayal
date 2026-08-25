using System;
using System.Linq;
using NUnit.Framework;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.EditorTools.Benchmark;

namespace ChessTheBetrayal.Tests.EditMode.Tooling.Benchmark
{
    /// <summary>
    /// The part of the tournament window that can be wrong without looking wrong.
    ///
    /// The window itself is an editor window with a hand-driven update pump and has never been
    /// tested, nor smoke-tested by hand. Most of it draws things, and a drawing mistake announces
    /// itself. This does not: it turns the row somebody clicked into the profile the games are
    /// played with, and a tournament run against the wrong tier produces a result that reads
    /// exactly like a real one, gets compared against the baseline, and is believed.
    /// </summary>
    [TestFixture]
    public class TournamentProfileChoiceTests
    {
        [Test]
        public void EveryShippedTierIsOfferedAndTheCustomEntryComesLast()
        {
            string[] labels = TournamentProfileChoice.Labels();

            Assert.That(labels.Length, Is.EqualTo(AIProfileTable.BuiltIn.Count + 1),
                "The picker offers one row per tier plus the custom one, and the index it hands back " +
                "is read against the roster - a list of a different length maps clicks to the wrong tier.");
            Assert.That(labels.Last(), Is.EqualTo(TournamentProfileChoice.CustomLabel));
            Assert.That(labels.Take(AIProfileTable.BuiltIn.Count),
                Is.EqualTo(AIProfileTable.BuiltIn.Select(p => p.Id)).AsCollection,
                "Tier names must appear in roster order, since the index into one is used to read the other.");
        }

        [Test]
        public void TheCustomEntrySitsWhereTheResolverExpectsIt()
        {
            string[] labels = TournamentProfileChoice.Labels();

            // Naming the row at that index, not just checking it is the last one. Two values that
            // are wrong by the same step still agree with each other, and a version of this that
            // only compared the index against the length was satisfied by exactly that.
            Assert.That(labels[TournamentProfileChoice.CustomLabelIndex],
                Is.EqualTo(TournamentProfileChoice.CustomLabel),
                "The index the window treats as the custom row points at a tier name instead, so " +
                "picking custom would silently play a shipped tier.");
            Assert.That(TournamentProfileChoice.CustomLabelIndex, Is.EqualTo(labels.Length - 1));
        }

        [Test]
        public void PickingATierPlaysThatExactTier()
        {
            for (int i = 0; i < AIProfileTable.BuiltIn.Count; i++)
            {
                AIProfile resolved = TournamentProfileChoice.Resolve(i, CustomProfileDraft.Default("unused"));

                Assert.That(resolved.Id, Is.EqualTo(AIProfileTable.BuiltIn[i].Id),
                    $"Row {i} of the picker played '{resolved.Id}'. Every number a tournament reports " +
                    "would be filed under the wrong tier.");
                Assert.That(resolved.MaxDepth, Is.EqualTo(AIProfileTable.BuiltIn[i].MaxDepth));
                Assert.That(resolved.AttackDefenseBias, Is.EqualTo(AIProfileTable.BuiltIn[i].AttackDefenseBias),
                    "Resolving a shipped tier here must not quietly play a different one from the one " +
                    "a real match would build for the same id.");
            }
        }

        [Test]
        public void PickingCustomPlaysTheDialsAsWrittenWhenTheSearchIsDeepEnoughToTrustThem()
        {
            CustomProfileDraft draft = CustomProfileDraft.Default("hand-built");
            draft.MaxDepth = AIProfileGuardrails.ShallowSearchDepthThreshold + 1;
            draft.AttackDefenseBias = 1.8f;
            draft.BetrayalAggression = 0.9f;

            AIProfile resolved = TournamentProfileChoice.Resolve(TournamentProfileChoice.Custom, draft);

            Assert.That(resolved.Id, Is.EqualTo("hand-built"));
            Assert.That(resolved.AttackDefenseBias, Is.EqualTo(1.8f));
            Assert.That(resolved.BetrayalAggression, Is.EqualTo(0.9f));
        }

        [Test]
        public void PickingCustomWithAShallowSearchClampsTheDialsTheSameWayTheGameWould()
        {
            // A hand-built tier is the one place somebody can wind the dials past what the game
            // would ever hand a search of that depth. Measuring a player nobody can face is the
            // failure mode, and it is silent - the tournament runs perfectly and reports numbers
            // for a tier that cannot exist.
            CustomProfileDraft draft = CustomProfileDraft.Default("shallow-and-strident");
            draft.MaxDepth = AIProfileGuardrails.ShallowSearchDepthThreshold - 1;
            draft.AttackDefenseBias = 2f;
            draft.BetrayalAggression = 1f;

            AIProfile resolved = TournamentProfileChoice.Resolve(TournamentProfileChoice.Custom, draft);

            Assert.That(resolved.AttackDefenseBias, Is.EqualTo(AIProfileGuardrails.MaxClampedAttackDefenseBias));
            Assert.That(resolved.BetrayalAggression, Is.EqualTo(AIProfileGuardrails.MaxClampedBetrayalAggression));
        }

        [Test]
        public void AskingForATierThatIsNotThereSaysSoInsteadOfPlayingSomethingElse()
        {
            // The picker index is serialised with the window and survives a roster change. A stale
            // index used to reach straight into the roster, so shrinking the table would either
            // throw somewhere further away or, worse, land on a different tier.
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                TournamentProfileChoice.Resolve(AIProfileTable.BuiltIn.Count, CustomProfileDraft.Default("unused")));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                TournamentProfileChoice.Resolve(-2, CustomProfileDraft.Default("unused")));
        }
    }
}
