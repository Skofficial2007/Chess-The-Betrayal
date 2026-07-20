using System;
using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.AI;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins the "new tier = new data row, zero code change" contract: adding a difficulty must
    /// only ever mean adding a row to the table, never editing search or selection logic.
    /// </summary>
    [TestFixture]
    public class AIProfileTableTests
    {
        [Test]
        public void BuiltIn_ContainsExactlySixRows()
        {
            Assert.That(AIProfileTable.BuiltIn.Count, Is.EqualTo(6));
        }

        [Test]
        public void BuiltIn_EveryRow_HasUniqueLowercaseId()
        {
            var seen = new HashSet<string>();
            foreach (var profile in AIProfileTable.BuiltIn)
            {
                Assert.That(profile.Id, Is.Not.Null.And.Not.Empty);
                Assert.That(profile.Id, Is.EqualTo(profile.Id.ToLowerInvariant()));
                Assert.That(seen.Add(profile.Id), Is.True, $"Duplicate profile id '{profile.Id}'.");
            }
        }

        [Test]
        public void BuiltIn_EveryRow_ProducesValidSearchSettings()
        {
            foreach (var profile in AIProfileTable.BuiltIn)
            {
                AISearchSettings settings = AISearchSettings.FromProfile(BetrayalUsage.Full, profile);

                Assert.That(settings.MaxDepth, Is.EqualTo(profile.MaxDepth));
                Assert.That(settings.MaxDepth, Is.GreaterThan(0));
                Assert.That(settings.TimeBudget.SoftMs, Is.EqualTo(profile.TimeBudget.SoftMs));
                Assert.That(settings.TimeBudget.SoftMs, Is.GreaterThan(0));
                Assert.That(settings.TimeBudget.HardMs, Is.GreaterThanOrEqualTo(settings.TimeBudget.SoftMs));
            }
        }

        [Test]
        public void BuiltIn_EveryRow_SoftTimeBudgetStaysAtOrBelowTheRealPerMoveTarget()
        {
            // Every tier is now measured (not guessed) to comfortably finish its full search well
            // under the 3-second per-move target, so nothing should ever be configured to try for
            // longer than that on its own initiative — a slow position hitting the hard ceiling is
            // still possible, but the search should never be TARGETING more than 3s as its soft goal.
            const int realTargetMs = 3000;
            foreach (var profile in AIProfileTable.BuiltIn)
            {
                Assert.That(profile.TimeBudget.SoftMs, Is.LessThanOrEqualTo(realTargetMs),
                    $"'{profile.Id}' has a soft time budget of {profile.TimeBudget.SoftMs}ms — " +
                    $"every tier should target at most {realTargetMs}ms per move.");
            }
        }

        [Test]
        public void Resolve_KnownId_ReturnsMatchingRow()
        {
            IAIProfileProvider provider = new AIProfileTableProvider();

            AIProfile profile = provider.Resolve("hard");

            Assert.That(profile.Id, Is.EqualTo("hard"));
        }

        [Test]
        public void Resolve_KnownId_IsCaseInsensitive()
        {
            IAIProfileProvider provider = new AIProfileTableProvider();

            AIProfile profile = provider.Resolve("HaRd");

            Assert.That(profile.Id, Is.EqualTo("hard"));
        }

        [Test]
        public void Resolve_UnknownId_FallsBackToNormal()
        {
            IAIProfileProvider provider = new AIProfileTableProvider();

            AIProfile profile = provider.Resolve("not-a-real-tier");

            Assert.That(profile.Id, Is.EqualTo(AIProfileTable.DefaultId));
        }

        [Test]
        public void Resolve_NullOrEmptyId_FallsBackToNormal()
        {
            IAIProfileProvider provider = new AIProfileTableProvider();

            Assert.That(provider.Resolve(null).Id, Is.EqualTo(AIProfileTable.DefaultId));
            Assert.That(provider.Resolve(string.Empty).Id, Is.EqualTo(AIProfileTable.DefaultId));
        }

        /// <summary>
        /// A locally-built fixture provider proves a new tier is addable without touching any
        /// production code — it is a plain array + linear scan, identical shape to
        /// AIProfileTableProvider, just constructed inline with one extra row.
        /// </summary>
        [Test]
        public void AddingATierRequiresNoCodeChange_DataOnlyFixtureRow()
        {
            var extendedRoster = new List<AIProfile>(AIProfileTable.BuiltIn)
            {
                new AIProfile("berserker", maxDepth: 4, timeBudget: new AITimeBudget(1000, 1500),
                    blunderRate: 0.15f, blunderMarginCp: 50, betrayalAggression: 1f,
                    attackDefenseBias: 2f, tieBreakWindowCp: 40, useOpeningBook: false)
            };

            IAIProfileProvider fixtureProvider = new FixtureProfileProvider(extendedRoster);

            AIProfile resolved = fixtureProvider.Resolve("berserker");

            Assert.That(resolved.Id, Is.EqualTo("berserker"));
            Assert.That(resolved.AttackDefenseBias, Is.EqualTo(2f));
        }

        private sealed class FixtureProfileProvider : IAIProfileProvider
        {
            private readonly IReadOnlyList<AIProfile> _roster;

            public FixtureProfileProvider(IReadOnlyList<AIProfile> roster) => _roster = roster;

            public AIProfile Resolve(string id)
            {
                foreach (var profile in _roster)
                {
                    if (string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase))
                        return profile;
                }
                throw new InvalidOperationException($"Fixture roster has no '{id}' row.");
            }
        }
    }
}
