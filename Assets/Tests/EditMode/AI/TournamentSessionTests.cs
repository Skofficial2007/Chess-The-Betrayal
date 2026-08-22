using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.EditorTools.Benchmark;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins TournamentSession's own contract — event counts, partial-report correctness, and
    /// reproducibility — against a fast fixture roster (shallow depths, tight ply cap) rather than
    /// the real built-in table, so this stays a genuine per-commit-speed suite. The identity between
    /// this session and BenchmarkRunner.RunAll (which now just drains a session) is pinned directly:
    /// same seed through both paths must produce the same report.
    /// </summary>
    [TestFixture]
    public class TournamentSessionTests
    {
        private const int TestPlyCap = 10;

        private static readonly IReadOnlyList<AIProfile> FastFixtureRoster = new[]
        {
            new AIProfile("easy", maxDepth: 1, timeBudget: new AITimeBudget(500, 750), blunderRate: 0.3f, blunderMarginCp: 120, betrayalAggression: 0f, attackDefenseBias: 1f, tieBreakWindowCp: 30, useOpeningBook: false),
            new AIProfile("normal", maxDepth: 1, timeBudget: new AITimeBudget(500, 750), blunderRate: 0.1f, blunderMarginCp: 80, betrayalAggression: 0f, attackDefenseBias: 1f, tieBreakWindowCp: 20, useOpeningBook: false),
            new AIProfile("hard", maxDepth: 2, timeBudget: new AITimeBudget(500, 750), blunderRate: 0.02f, blunderMarginCp: 40, betrayalAggression: 0f, attackDefenseBias: 1f, tieBreakWindowCp: 15, useOpeningBook: false),
            new AIProfile("aggressive", maxDepth: 1, timeBudget: new AITimeBudget(500, 750), blunderRate: 0.05f, blunderMarginCp: 60, betrayalAggression: 0.7f, attackDefenseBias: 1.5f, tieBreakWindowCp: 25, useOpeningBook: false),
            new AIProfile("extreme", maxDepth: 2, timeBudget: new AITimeBudget(500, 750), blunderRate: 0f, blunderMarginCp: 0, betrayalAggression: 0.3f, attackDefenseBias: 1.2f, tieBreakWindowCp: 10, useOpeningBook: false),
            new AIProfile("impossible", maxDepth: 1, timeBudget: new AITimeBudget(500, 750), blunderRate: 0f, blunderMarginCp: 0, betrayalAggression: 0f, attackDefenseBias: 1f, tieBreakWindowCp: 0, useOpeningBook: false),
        };

        private static AIProfile Find(string id) => FastFixtureRoster.Single(p => p.Id == id);

        [Test]
        public void HeadToHead_TwoPositions_PlaysExactlyFourGames()
        {
            var session = TournamentSession.CreateHeadToHead(
                runSeed: 1, Find("normal"), Find("impossible"), positionCount: 2, plyCap: TestPlyCap);

            Assert.That(session.TotalGames, Is.EqualTo(4), "2 positions x 2 colors = 4 games.");

            while (session.RunNextGame()) { }

            BenchmarkReport report = session.BuildReport();
            Assert.That(report.PairResults.Count, Is.EqualTo(1));
            PairResult pair = report.PairResults[0];
            Assert.That(pair.Games, Is.EqualTo(4));
            Assert.That(pair.SubjectWins + pair.OpponentWins + pair.Draws, Is.EqualTo(4));
        }

        [Test]
        public void RunNextGame_FiresOnGameCompletedOncePerGame_AndOnSessionCompletedOnce()
        {
            var session = TournamentSession.CreateHeadToHead(
                runSeed: 2, Find("easy"), Find("hard"), positionCount: 2, plyCap: TestPlyCap);

            int gameCompletedCount = 0;
            int sessionCompletedCount = 0;
            session.OnGameCompleted += _ => gameCompletedCount++;
            session.OnSessionCompleted += () => sessionCompletedCount++;

            while (session.RunNextGame()) { }

            Assert.That(gameCompletedCount, Is.EqualTo(session.TotalGames));
            Assert.That(sessionCompletedCount, Is.EqualTo(1));
        }

        [Test]
        public void RunNextGame_AfterCompletion_ReturnsFalseAndFiresNoMoreEvents()
        {
            var session = TournamentSession.CreateHeadToHead(
                runSeed: 3, Find("easy"), Find("normal"), positionCount: 1, plyCap: TestPlyCap);

            while (session.RunNextGame()) { }

            int eventsAfterCompletion = 0;
            session.OnGameCompleted += _ => eventsAfterCompletion++;
            session.OnSessionCompleted += () => eventsAfterCompletion++;

            bool played = session.RunNextGame();

            Assert.That(played, Is.False);
            Assert.That(eventsAfterCompletion, Is.EqualTo(0));
        }

        [Test]
        public void HeadToHead_SameSeed_ProducesIdenticalWinRate()
        {
            var session1 = TournamentSession.CreateHeadToHead(
                runSeed: 42, Find("hard"), Find("aggressive"), positionCount: 3, plyCap: TestPlyCap);
            var session2 = TournamentSession.CreateHeadToHead(
                runSeed: 42, Find("hard"), Find("aggressive"), positionCount: 3, plyCap: TestPlyCap);

            while (session1.RunNextGame()) { }
            while (session2.RunNextGame()) { }

            PairResult pair1 = session1.BuildReport().PairResults[0];
            PairResult pair2 = session2.BuildReport().PairResults[0];

            Assert.That(pair2.SubjectWinRate, Is.EqualTo(pair1.SubjectWinRate));
            Assert.That(pair2.SubjectWins, Is.EqualTo(pair1.SubjectWins));
            Assert.That(pair2.OpponentWins, Is.EqualTo(pair1.OpponentWins));
            Assert.That(pair2.Draws, Is.EqualTo(pair1.Draws));
        }

        [Test]
        public void CreateQuick_DrainedSession_MatchesBenchmarkRunnerRunAll()
        {
            // Uncapped: this test asserts identity between a hand-drained session and RunAll's
            // (now parallel-by-default) drain — under a real time budget a search's own move
            // choice can legitimately vary run to run with CPU contention, which would make this
            // comparison flaky for a reason that has nothing to do with the two paths actually
            // agreeing. See TournamentSession.CreateQuick's own doc comment.
            var session = TournamentSession.CreateQuick(runSeed: 7, FastFixtureRoster, TestPlyCap, MatchTimeControl.Uncapped);
            while (session.RunNextGame()) { }
            BenchmarkReport fromSession = session.BuildReport();

            BenchmarkReport fromRunAll = BenchmarkRunner.RunAll(runSeed: 7, BenchmarkMode.Quick, FastFixtureRoster, TestPlyCap, timeControl: MatchTimeControl.Uncapped);

            Assert.That(fromSession.PairResults.Count, Is.EqualTo(fromRunAll.PairResults.Count));
            for (int i = 0; i < fromSession.PairResults.Count; i++)
            {
                Assert.That(fromSession.PairResults[i].Subject, Is.EqualTo(fromRunAll.PairResults[i].Subject));
                Assert.That(fromSession.PairResults[i].Opponent, Is.EqualTo(fromRunAll.PairResults[i].Opponent));
                Assert.That(fromSession.PairResults[i].SubjectWinRate, Is.EqualTo(fromRunAll.PairResults[i].SubjectWinRate));
                Assert.That(fromSession.PairResults[i].Games, Is.EqualTo(fromRunAll.PairResults[i].Games));
            }
        }

        [Test]
        public void PartialRun_BuildReport_SumsOnlyGamesPlayedSoFar()
        {
            var session = TournamentSession.CreateQuick(runSeed: 9, FastFixtureRoster, TestPlyCap);

            for (int i = 0; i < 3; i++)
                session.RunNextGame();

            BenchmarkReport report = session.BuildReport();
            int totalGamesInReport = report.PairResults.Sum(p => p.Games);

            Assert.That(totalGamesInReport, Is.EqualTo(3));
            Assert.That(session.GamesCompleted, Is.EqualTo(3));
            Assert.That(session.IsComplete, Is.False);
        }

        [Test]
        public void WinRateMargin95_AtOneHundredGames_IsApproximatelyTenPoints()
        {
            float margin = TournamentStatistics.WinRateMargin95(100);

            Assert.That(margin, Is.EqualTo(0.098f).Within(0.01f));
        }

        [Test]
        public void WinRateMargin95_AtZeroGames_IsSaturatedAtOne()
        {
            Assert.That(TournamentStatistics.WinRateMargin95(0), Is.EqualTo(1f));
        }
    }
}
