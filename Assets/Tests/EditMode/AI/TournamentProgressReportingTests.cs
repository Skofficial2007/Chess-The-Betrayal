using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ChessTheBetrayal.EditorTools.Benchmark;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins the enriched per-game logging this ticket added: a bare "N/total" counter proves a run
    /// is alive but says nothing about whether it's healthy, so TournamentGameLogger renders the
    /// pairing, the result, and a running score per pairing whenever a game finishes.
    /// </summary>
    [TestFixture]
    public class TournamentProgressReportingTests
    {
        private static TournamentGameRecord BuildRecord(int pairIndex, string whiteId, string blackId,
            bool subjectIsWhite, MatchOutcome outcome)
        {
            var matchResult = new MatchResult(outcome, plyCount: 40, reachedPlyCap: false);
            var whiteStats = new MatchSideStats(4, 100, 20, 5, 500.0, 0, 0, completedDepthSum: 20, shallowestCompletedDepth: 5, depthHistogram: null);
            var blackStats = new MatchSideStats(4, 90, 18, 5, 450.0, 0, 0, completedDepthSum: 20, shallowestCompletedDepth: 5, depthHistogram: null);
            var statsResult = new MatchStatsResult(matchResult, whiteStats, blackStats);

            return new TournamentGameRecord(pairIndex, positionIndex: 0, whiteId, blackId, statsResult, subjectIsWhite);
        }

        [Test]
        public void HandleGameCompleted_LogsPairingAndResult()
        {
            var logger = new TournamentGameLogger("TestRun");
            TournamentGameRecord record = BuildRecord(0, "hard", "normal", subjectIsWhite: true, MatchOutcome.WhiteWon);

            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[TestRun\] hard vs normal — win.*running 1-0-0"));

            logger.HandleGameCompleted(record);
        }

        [Test]
        public void HandleGameCompleted_RunningScore_IsOnePairTotal_AcrossBothColors()
        {
            // The same pairing plays both colors to cancel first-move advantage — the running
            // score must accumulate as ONE total for the pairing, not reset/split depending on
            // which color the subject happened to play that particular game.
            var logger = new TournamentGameLogger("TestRun");

            LogAssert.ignoreFailingMessages = true;

            // Game 1: subject (extreme) plays White, White wins -> a win for the subject.
            logger.HandleGameCompleted(BuildRecord(2, "extreme", "hard", subjectIsWhite: true, MatchOutcome.WhiteWon));
            // Game 2: subject (extreme) plays Black this time, White wins -> a loss for the subject.
            logger.HandleGameCompleted(BuildRecord(2, "hard", "extreme", subjectIsWhite: false, MatchOutcome.WhiteWon));

            LogAssert.ignoreFailingMessages = false;

            // Game 3: subject plays White again, draw -> running total is 1 win, 1 loss, 1 draw.
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[TestRun\].*— draw.*running 1-1-1"));
            logger.HandleGameCompleted(BuildRecord(2, "extreme", "hard", subjectIsWhite: true, MatchOutcome.Draw));
        }

        [Test]
        public void DebugLogProgressSink_SmallRun_LogsEveryGame_NoThrottle()
        {
            var sink = new DebugLogProgressSink("Quick");

            for (int i = 1; i <= 10; i++)
                LogAssert.Expect(LogType.Log, $"[Quick] {i}/10 games complete");

            for (int i = 1; i <= 10; i++)
                sink.ReportGameCompleted(i, 10);
        }

        [Test]
        public void DebugLogProgressSink_LargeRun_ThrottlesToEveryFifthGame_PlusFinal()
        {
            var sink = new DebugLogProgressSink("Full", reportEveryNGames: 5);
            int total = 300;

            LogAssert.ignoreFailingMessages = true;
            for (int i = 1; i < total; i++)
                sink.ReportGameCompleted(i, total);
            LogAssert.ignoreFailingMessages = false;

            LogAssert.Expect(LogType.Log, $"[Full] {total}/{total} games complete");
            sink.ReportGameCompleted(total, total);
        }
    }
}
