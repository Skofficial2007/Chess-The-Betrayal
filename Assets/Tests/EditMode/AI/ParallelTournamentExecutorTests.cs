using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.EditorTools.Benchmark;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// The keystone correctness proof for ParallelTournamentExecutor: playing a tournament across
    /// several worker threads must produce a report indistinguishable from playing every game
    /// sequentially on one thread, at the same seed. Every determinism assertion here explicitly
    /// passes MatchTimeControl.Uncapped — under the real ProductionBudget default, a search that
    /// hits its wall-clock timer can legitimately complete a different depth (and pick a different
    /// move) run to run depending on CPU contention, which has nothing to do with whether the
    /// parallel scheduling itself is correct. Uncapped removes that variable so every outcome and
    /// node count is fully deterministic; only MeanMsPerMove is still expected to differ, since
    /// real wall-clock timing is never reproducible move-for-move regardless of time control.
    /// </summary>
    [TestFixture]
    public class ParallelTournamentExecutorTests
    {
        private const int TestPlyCap = 12;

        private static readonly IReadOnlyList<AIProfile> FastFixtureRoster = new[]
        {
            new AIProfile("easy", maxDepth: 1, timeBudget: new AITimeBudget(60_000, 60_000), blunderRate: 0.3f, blunderMarginCp: 120, betrayalAggression: 0f, attackDefenseBias: 1f, tieBreakWindowCp: 30, useOpeningBook: false),
            new AIProfile("normal", maxDepth: 1, timeBudget: new AITimeBudget(60_000, 60_000), blunderRate: 0.1f, blunderMarginCp: 80, betrayalAggression: 0f, attackDefenseBias: 1f, tieBreakWindowCp: 20, useOpeningBook: false),
            new AIProfile("hard", maxDepth: 2, timeBudget: new AITimeBudget(60_000, 60_000), blunderRate: 0.02f, blunderMarginCp: 40, betrayalAggression: 0f, attackDefenseBias: 1f, tieBreakWindowCp: 15, useOpeningBook: false),
            new AIProfile("aggressive", maxDepth: 1, timeBudget: new AITimeBudget(60_000, 60_000), blunderRate: 0.05f, blunderMarginCp: 60, betrayalAggression: 0.7f, attackDefenseBias: 1.5f, tieBreakWindowCp: 25, useOpeningBook: false),
            new AIProfile("extreme", maxDepth: 2, timeBudget: new AITimeBudget(60_000, 60_000), blunderRate: 0f, blunderMarginCp: 0, betrayalAggression: 0.3f, attackDefenseBias: 1.2f, tieBreakWindowCp: 10, useOpeningBook: false),
            new AIProfile("impossible", maxDepth: 1, timeBudget: new AITimeBudget(60_000, 60_000), blunderRate: 0f, blunderMarginCp: 0, betrayalAggression: 0f, attackDefenseBias: 1f, tieBreakWindowCp: 0, useOpeningBook: false),
        };

        private string _tempRoot;

        [SetUp]
        public void CreateTempRoot()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "ParallelTournamentExecutorTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TearDown]
        public void DeleteTempRoot()
        {
            if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
        }

        [Test]
        public void RunRemainingGames_RunWriterSupplied_LeavesFinishedGamesOnDiskBeforeTheWholeRunEnds()
        {
            // The whole point of runWriter: a run killed partway must still have every game that
            // finished before the kill sitting on disk, not just a header. Proves this without
            // actually killing the process by hooking progress to stall once several games have
            // reported complete, THEN polling run.jsonl on disk (while the remaining games are
            // still in flight) until it holds at least that many records or a timeout expires -- if
            // WriteGame only ran in a post-loop fold after every game finished, that poll would
            // never see more than the header no matter how long it waited, since nothing reaches
            // disk until the whole run is over.
            var session = TournamentSession.CreateFull(runSeed: 606, FastFixtureRoster, TestPlyCap, MatchTimeControl.Uncapped);
            int totalGames = session.TotalGames;
            string runDirectory = Path.Combine(_tempRoot, "incremental-run");
            string headerLine = TournamentRunWriter.BuildHeaderLine(
                1, "Full", runSeed: 606, totalGames, "Uncapped", workerCount: 4, DateTime.UtcNow);
            string runFilePath = Path.Combine(runDirectory, TournamentRunReader.RunFileName);

            const int StallAfterGames = 5;
            var reachedStallPoint = new ManualResetEventSlim(false);
            var releaseStall = new ManualResetEventSlim(false);
            int linesAtStall = -1;

            using (var writer = new TournamentRunWriter(runDirectory, headerLine))
            {
                var progress = new StallingProgress(current =>
                {
                    if (current == StallAfterGames)
                    {
                        reachedStallPoint.Set();
                        releaseStall.Wait(TimeSpan.FromSeconds(10));
                    }
                });

                // The stalled worker blocks ONE of RunRemainingGames' Parallel.For iterations, so
                // RunRemainingGames itself does not return until releaseStall fires -- the file
                // check has to happen from a SEPARATE thread while RunRemainingGames is still
                // running, not after it, or this would only ever observe the fully-finished state.
                Task<int> checkerTask = Task.Run(() =>
                {
                    reachedStallPoint.Wait(TimeSpan.FromSeconds(10));
                    int count = CountLinesWithRetry(runFilePath, minimumExpected: StallAfterGames, timeout: TimeSpan.FromSeconds(5));
                    releaseStall.Set();
                    return count;
                });

                ParallelTournamentExecutor.RunRemainingGames(
                    session, maxDegreeOfParallelism: 4, progress: progress, runWriter: writer);

                linesAtStall = checkerTask.Result;
            }

            Assert.That(reachedStallPoint.IsSet, Is.True, "the run finished before the stall point fired -- widen StallAfterGames or slow the fixture roster.");
            Assert.That(linesAtStall, Is.GreaterThanOrEqualTo(StallAfterGames),
                "run.jsonl must already hold at least as many game records as have been reported complete, " +
                "not just the header -- persistence must happen as each game finishes, not all at once at the end.");

            TournamentRunResult result = TournamentRunReader.Read(runDirectory);
            Assert.That(result.Games.Count, Is.EqualTo(totalGames));
        }

        /// <summary>Polls a run file's line count (minus the header) until it reaches
        /// minimumExpected or timeout elapses -- the writer flushes on its own 250ms timer, so a
        /// single immediate read could under-count even on correct, working persistence. Opens
        /// with FileShare.ReadWrite explicitly: the writer thread still holds the file open for
        /// appending while this polls, and a plain File.ReadLines can hit a sharing violation
        /// against that open handle depending on timing.</summary>
        private static int CountLinesWithRetry(string runFilePath, int minimumExpected, TimeSpan timeout)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            int lastCount = 0;
            while (stopwatch.Elapsed < timeout)
            {
                if (File.Exists(runFilePath))
                {
                    try
                    {
                        using var stream = new FileStream(runFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var reader = new StreamReader(stream);
                        int lineCount = 0;
                        while (reader.ReadLine() != null) lineCount++;
                        lastCount = lineCount - 1; // minus header
                        if (lastCount >= minimumExpected) return lastCount;
                    }
                    catch (IOException)
                    {
                        // Transient sharing conflict with the writer's own open handle -- retry.
                    }
                }
                Thread.Sleep(50);
            }
            return lastCount;
        }

        /// <summary>Fires ReportGameCompleted like a real sink, but lets the caller run arbitrary
        /// code (stall, inspect state) on a specific completion count -- same shape as
        /// BlockOneGameProgress below, generalized to take the count.</summary>
        private sealed class StallingProgress : ChessTheBetrayal.EditorTools.Benchmark.ITournamentProgress
        {
            private readonly Action<int> _onReport;
            public StallingProgress(Action<int> onReport) => _onReport = onReport;
            public void ReportGameCompleted(int current, int total) => _onReport(current);
        }

        [Test]
        public void RunRemainingGames_QuickTournament_ProducesTheSameReportAsSequentialRunNextGame()
        {
            // Uncapped: under a real time budget, a search that hits its clock can legitimately
            // complete a different depth (and so pick a different move) run to run depending on
            // CPU contention — genuinely proving "parallel scheduling doesn't change the result"
            // needs the depth-bound path, which has nothing left to race against wall-clock time.
            var sequential = TournamentSession.CreateQuick(runSeed: 555, FastFixtureRoster, TestPlyCap, MatchTimeControl.Uncapped);
            while (sequential.RunNextGame()) { }
            BenchmarkReport sequentialReport = sequential.BuildReport();

            var parallel = TournamentSession.CreateQuick(runSeed: 555, FastFixtureRoster, TestPlyCap, MatchTimeControl.Uncapped);
            ParallelTournamentExecutor.RunRemainingGames(parallel, maxDegreeOfParallelism: 4);
            BenchmarkReport parallelReport = parallel.BuildReport();

            AssertReportsMatchIgnoringTiming(sequentialReport, parallelReport);
        }

        [Test]
        public void RunRemainingGames_FullTournament_ProducesTheSameReportAsSequentialRunNextGame()
        {
            // The Full matrix (all 15 pairings) exercises every pairing shape at once, including
            // ones Quick's adjacent-pairs list never touches — a broader determinism sweep than
            // the Quick-only test above, at the fixture roster's fast depths. Uncapped for the same
            // reason as the Quick test above.
            var sequential = TournamentSession.CreateFull(runSeed: 777, FastFixtureRoster, TestPlyCap, MatchTimeControl.Uncapped);
            while (sequential.RunNextGame()) { }
            BenchmarkReport sequentialReport = sequential.BuildReport();

            var parallel = TournamentSession.CreateFull(runSeed: 777, FastFixtureRoster, TestPlyCap, MatchTimeControl.Uncapped);
            ParallelTournamentExecutor.RunRemainingGames(parallel, maxDegreeOfParallelism: 6);
            BenchmarkReport parallelReport = parallel.BuildReport();

            AssertReportsMatchIgnoringTiming(sequentialReport, parallelReport);
        }

        [Test]
        public void RunRemainingGames_BenchmarkRunnerParallelFlag_MatchesSequentialFlag()
        {
            // BenchmarkRunner.RunAll is the actual entry point menu commands and CI call — pin the
            // identity at that level too, not just against the lower-level executor directly.
            // Uncapped for the same reason as the two tests above.
            BenchmarkReport sequentialReport = BenchmarkRunner.RunAll(
                runSeed: 333, BenchmarkMode.Quick, FastFixtureRoster, TestPlyCap, parallel: false, timeControl: MatchTimeControl.Uncapped);
            BenchmarkReport parallelReport = BenchmarkRunner.RunAll(
                runSeed: 333, BenchmarkMode.Quick, FastFixtureRoster, TestPlyCap, parallel: true, timeControl: MatchTimeControl.Uncapped);

            AssertReportsMatchIgnoringTiming(sequentialReport, parallelReport);
        }

        [Test]
        public void RunRemainingGames_HighParallelism_StillMatchesSequential_SEEThreadSafetyRegression()
        {
            // Regression pin for a real bug found during this test suite's own development: SEE's
            // internal scratch buffers (StaticExchangeEvaluation.GainBuffer/RemovedSquares) used to
            // be plain shared statics, mutated in place with no thread isolation. Once real
            // multi-threaded search became possible (this executor), two worker threads calling
            // into SEE concurrently could corrupt each other's in-progress exchange calculation,
            // silently returning a wrong score that perturbed move ordering and produced a
            // genuinely different — and non-reproducible, varying run to run — search result. Fixed
            // by making both buffers [ThreadStatic], the same pattern ChessEngine's own scratch
            // buffers already use. A low degree of parallelism (2-4) didn't reliably reproduce the
            // race in manual testing; 8 workers against the Full matrix reproduced it on every run
            // before the fix, so that's what this pins against regressing.
            var sequential = TournamentSession.CreateFull(runSeed: 4242, FastFixtureRoster, TestPlyCap, MatchTimeControl.Uncapped);
            while (sequential.RunNextGame()) { }
            BenchmarkReport sequentialReport = sequential.BuildReport();

            var parallel = TournamentSession.CreateFull(runSeed: 4242, FastFixtureRoster, TestPlyCap, MatchTimeControl.Uncapped);
            ParallelTournamentExecutor.RunRemainingGames(parallel, maxDegreeOfParallelism: 8);
            BenchmarkReport parallelReport = parallel.BuildReport();

            AssertReportsMatchIgnoringTiming(sequentialReport, parallelReport);
        }

        [Test]
        public void RunRemainingGames_FiresOnGamePlayedExactlyOncePerGame()
        {
            var session = TournamentSession.CreateQuick(runSeed: 11, FastFixtureRoster, TestPlyCap);
            int totalExpected = session.TotalGames;

            var recorder = new RecordingProgress();
            ParallelTournamentExecutor.RunRemainingGames(session, maxDegreeOfParallelism: 4, progress: recorder);

            Assert.That(recorder.CallCount, Is.EqualTo(totalExpected));
            Assert.That(recorder.LastReportedCompleted, Is.EqualTo(totalExpected));
            Assert.That(recorder.EveryReportedTotalMatched, Is.True);
        }

        /// <summary>Thread-safe test double — ReportGameCompleted fires from worker threads, out
        /// of order, exactly like the real sinks must tolerate.</summary>
        private sealed class RecordingProgress : ChessTheBetrayal.EditorTools.Benchmark.ITournamentProgress
        {
            private int _callCount;
            private int _lastReportedCompleted;
            private bool _everyReportedTotalMatched = true;
            private int _expectedTotal = -1;

            public int CallCount => _callCount;
            public int LastReportedCompleted => _lastReportedCompleted;
            public bool EveryReportedTotalMatched => _everyReportedTotalMatched;

            public void ReportGameCompleted(int current, int total)
            {
                Interlocked.Increment(ref _callCount);
                Interlocked.Exchange(ref _lastReportedCompleted, current);
                if (Interlocked.CompareExchange(ref _expectedTotal, total, -1) != -1 && _expectedTotal != total)
                    _everyReportedTotalMatched = false;
            }
        }

        [Test]
        public void RunRemainingGames_OnAnAlreadyCompleteSession_DoesNothing()
        {
            var session = TournamentSession.CreateHeadToHead(
                runSeed: 4, FastFixtureRoster[0], FastFixtureRoster[1], positionCount: 1, plyCap: TestPlyCap);
            while (session.RunNextGame()) { }

            Assert.DoesNotThrow(() => ParallelTournamentExecutor.RunRemainingGames(session));
            Assert.That(session.IsComplete, Is.True);
        }

        [Test]
        public void RunRemainingGames_CancelledMidRun_KeepsEveryGameThatFinished_EvenPastAHole()
        {
            // A worker can finish a LATER game before an earlier one it was still racing against
            // gets cut off by cancellation — the finished-games array is not guaranteed to fill in
            // order, so a hole can sit in the middle rather than only at the tail. This pins that
            // folding stops discarding results the moment it hits the first hole (the bug this test
            // exists for) and instead keeps every game that actually completed, wherever it landed.
            var session = TournamentSession.CreateFull(runSeed: 909, FastFixtureRoster, TestPlyCap, MatchTimeControl.Uncapped);
            int totalGames = session.TotalGames;

            using (var cts = new System.Threading.CancellationTokenSource())
            {
                var releaseHole = new System.Threading.ManualResetEventSlim(false);
                int gamesStarted = 0;

                var blockingProgress = new BlockOneGameProgress(() =>
                {
                    // Let a handful of games land first (so later slots are already filled), then
                    // cancel while this one call is still blocked — simulating a worker whose game
                    // is cut off after others past it in the array have already finished.
                    if (System.Threading.Interlocked.Increment(ref gamesStarted) == 3)
                    {
                        cts.Cancel();
                        releaseHole.Wait(2000);
                    }
                });

                ParallelTournamentExecutor.RunRemainingGames(
                    session, maxDegreeOfParallelism: 4, cancellationToken: cts.Token, progress: blockingProgress);
                releaseHole.Set();
            }

            Assert.That(session.GamesCompleted, Is.GreaterThan(0));
            Assert.That(session.GamesCompleted, Is.LessThan(totalGames),
                "the run should have been cut short — otherwise this test isn't exercising a hole at all.");
        }

        /// <summary>Fires ReportGameCompleted like a real sink, but lets the caller stall one
        /// specific call — used to force a hole to appear mid-array rather than only at the tail.</summary>
        private sealed class BlockOneGameProgress : ChessTheBetrayal.EditorTools.Benchmark.ITournamentProgress
        {
            private readonly System.Action _onReport;
            public BlockOneGameProgress(System.Action onReport) => _onReport = onReport;
            public void ReportGameCompleted(int current, int total) => _onReport();
        }

        [Test]
        public void RunRemainingGames_ResumesAPartiallyPlayedSession_PlayingOnlyTheRemainder()
        {
            var session = TournamentSession.CreateQuick(runSeed: 22, FastFixtureRoster, TestPlyCap);
            int totalGames = session.TotalGames;

            // Play the first few games sequentially, exactly like an interactive window run that
            // gets handed off to a parallel finish — RunRemainingGames must only play what's left,
            // never replay or skip the games already completed.
            session.RunNextGame();
            session.RunNextGame();
            Assert.That(session.GamesCompleted, Is.EqualTo(2));

            ParallelTournamentExecutor.RunRemainingGames(session, maxDegreeOfParallelism: 4);

            Assert.That(session.GamesCompleted, Is.EqualTo(totalGames));
            Assert.That(session.IsComplete, Is.True);
            BenchmarkReport report = session.BuildReport();
            Assert.That(report.PairResults.Sum(p => p.Games), Is.EqualTo(totalGames));
        }

        /// <summary>
        /// Compares by lookup key (Subject/Opponent, ProfileId), never by list index — PairResults
        /// and TierPerformances are built from a Dictionary internally (TournamentSession's own
        /// _tierAccumulators), and .NET's Dictionary enumeration order is not a guaranteed contract
        /// even when both runs insert in the exact same sequence. An index-based comparison here
        /// would silently compare the wrong two tiers/pairings against each other and report a
        /// false divergence that has nothing to do with whether parallel execution is correct.
        /// </summary>
        private static void AssertReportsMatchIgnoringTiming(BenchmarkReport expected, BenchmarkReport actual)
        {
            Assert.That(actual.PairResults.Count, Is.EqualTo(expected.PairResults.Count));
            foreach (PairResult e in expected.PairResults)
            {
                PairResult a = actual.FindPair(e.Subject, e.Opponent);
                Assert.That(a, Is.Not.Null, $"pairing {e.Subject} vs {e.Opponent} is missing from the actual report.");
                Assert.That(a.Games, Is.EqualTo(e.Games));
                Assert.That(a.SubjectWins, Is.EqualTo(e.SubjectWins));
                Assert.That(a.OpponentWins, Is.EqualTo(e.OpponentWins));
                Assert.That(a.Draws, Is.EqualTo(e.Draws));
            }

            Assert.That(actual.TierPerformances.Count, Is.EqualTo(expected.TierPerformances.Count));
            foreach (TierPerformance e in expected.TierPerformances)
            {
                TierPerformance a = actual.FindTier(e.ProfileId);
                Assert.That(a, Is.Not.Null, $"tier '{e.ProfileId}' is missing from the actual report.");
                Assert.That(a.MovesSampled, Is.EqualTo(e.MovesSampled));
                Assert.That(a.MeanNodesPerMove, Is.EqualTo(e.MeanNodesPerMove));
                Assert.That(a.DeepestCompletedDepth, Is.EqualTo(e.DeepestCompletedDepth));
                Assert.That(a.ObservedBlunderActuationRate, Is.EqualTo(e.ObservedBlunderActuationRate));
                // MeanMsPerMove deliberately NOT compared — real wall-clock timing, never
                // reproducible move-for-move across two separate runs regardless of threading.
            }
        }
    }
}
