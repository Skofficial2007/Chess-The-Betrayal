using System;
using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.AI;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins the "new tier = new data row, zero code change" contract from
    /// ADR_AI23_Profile_EventStream_OpeningBook.md Section 1.1/1.3.
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
                Assert.That(settings.SoftTimeBudgetMs, Is.EqualTo(profile.SoftTimeBudgetMs));
                Assert.That(settings.SoftTimeBudgetMs, Is.GreaterThan(0));
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
                new AIProfile("berserker", maxDepth: 4, softTimeBudgetMs: 1000,
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
