using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.EditorTools.Benchmark;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins the whole point of the persistence layer: a tournament run that gets killed midway
    /// still leaves real, readable results on disk, and a reader can tell that run apart from one
    /// that actually finished. Every test here uses a real temp directory under the OS temp path,
    /// cleaned up afterward, since this is exactly the file-writing code path being verified.
    /// </summary>
    [TestFixture]
    public class TournamentRunPersistenceTests
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

        private string _tempRoot;

        [SetUp]
        public void CreateTempRoot()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "TournamentRunPersistenceTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TearDown]
        public void DeleteTempRoot()
        {
            if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
        }

        // --- Record round-trip ---

        [Test]
        public void TournamentRunRecord_ToLineThenTryParse_RoundTripsEveryField()
        {
            var written = new TournamentRunRecord(
                gameIndex: 7, pairIndex: 2, subjectId: "hard", opponentId: "normal",
                subjectIsWhite: true, positionIndex: 3, outcome: MatchOutcome.WhiteWon,
                plyCount: 44, reachedPlyCap: false, elapsedMs: 1234.5);

            bool parsed = TournamentRunRecord.TryParse(written.ToLine(), out TournamentRunRecord read);

            Assert.That(parsed, Is.True);
            Assert.That(read.GameIndex, Is.EqualTo(written.GameIndex));
            Assert.That(read.PairIndex, Is.EqualTo(written.PairIndex));
            Assert.That(read.SubjectId, Is.EqualTo(written.SubjectId));
            Assert.That(read.OpponentId, Is.EqualTo(written.OpponentId));
            Assert.That(read.SubjectIsWhite, Is.EqualTo(written.SubjectIsWhite));
            Assert.That(read.PositionIndex, Is.EqualTo(written.PositionIndex));
            Assert.That(read.Outcome, Is.EqualTo(written.Outcome));
            Assert.That(read.PlyCount, Is.EqualTo(written.PlyCount));
            Assert.That(read.ReachedPlyCap, Is.EqualTo(written.ReachedPlyCap));
            Assert.That(read.ElapsedMs, Is.EqualTo(written.ElapsedMs).Within(0.05));
        }

        [Test]
        public void TournamentRunRecord_TryParse_TornLine_ReturnsFalseWithoutThrowing()
        {
            // What a process killed mid-write leaves behind: a line cut off partway through, never
            // a whole malformed record. The reader must treat this as "not there," not crash.
            bool parsed = TournamentRunRecord.TryParse("7\t2\thard\tnorm", out _);

            Assert.That(parsed, Is.False);
        }

        [Test]
        public void TournamentRunRecord_TryParse_EmptyOrNullLine_ReturnsFalse()
        {
            Assert.That(TournamentRunRecord.TryParse("", out _), Is.False);
            Assert.That(TournamentRunRecord.TryParse(null, out _), Is.False);
        }

        // --- Writer + reader, real run ---

        [Test]
        public void RunAll_WithPersistence_CompletedRun_ReaderReportsNotPartial_AndMatchesInMemoryReport()
        {
            BenchmarkReport inMemory = BenchmarkRunner.RunAll(
                runSeed: 501, BenchmarkMode.Quick, FastFixtureRoster, TestPlyCap,
                timeControl: MatchTimeControl.Uncapped, persistRunsUnderDirectory: _tempRoot);

            string runDirectory = FindTheOneRunDirectory();
            TournamentRunResult result = TournamentRunReader.Read(runDirectory);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.IsPartial, Is.False, "a run that finished must not read back as partial.");
            Assert.That(File.Exists(Path.Combine(runDirectory, TournamentRunReader.ReportFileName)), Is.True);
            Assert.That(File.Exists(Path.Combine(runDirectory, "summary.md")), Is.True);

            Assert.That(result.Report.PairResults.Count, Is.EqualTo(inMemory.PairResults.Count));
            foreach (PairResult expected in inMemory.PairResults)
            {
                PairResult actual = result.Report.FindPair(expected.Subject, expected.Opponent);
                Assert.That(actual, Is.Not.Null);
                Assert.That(actual.Games, Is.EqualTo(expected.Games));
                Assert.That(actual.SubjectWins, Is.EqualTo(expected.SubjectWins));
                Assert.That(actual.OpponentWins, Is.EqualTo(expected.OpponentWins));
                Assert.That(actual.Draws, Is.EqualTo(expected.Draws));
            }

            Assert.That(result.Games.Count, Is.EqualTo(inMemory.PairResults.Sum(p => p.Games)));
        }

        [Test]
        public void TournamentRunWriter_KilledMidRun_LeavesValidParseableGamesAndNoReport()
        {
            // Simulates a kill: write some games, then Dispose without ever writing report.json —
            // exactly what happens when a process is terminated between games rather than exiting
            // cleanly. Directory.Delete in TearDown doesn't run inside the try here on purpose; the
            // writer's own Dispose is the thing under test.
            string headerLine = TournamentRunWriter.BuildHeaderLine(
                1, "Quick", runSeed: 909, totalGames: 10, "ProductionBudget", workerCount: 4, DateTime.UtcNow);
            string runDirectory = Path.Combine(_tempRoot, "killed-run");

            using (var writer = new TournamentRunWriter(runDirectory, headerLine))
            {
                for (int i = 0; i < 4; i++)
                {
                    writer.WriteGame(new TournamentRunRecord(
                        i, pairIndex: 0, "hard", "normal", subjectIsWhite: i % 2 == 0,
                        positionIndex: i, MatchOutcome.WhiteWon, plyCount: 30, reachedPlyCap: false, elapsedMs: 500));
                }
                // Dispose (below, via using) is the only thing that happens — no report.json write,
                // matching a run that never reached BenchmarkRunner.WriteCompletionArtifacts.
            }

            TournamentRunResult result = TournamentRunReader.Read(runDirectory);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.IsPartial, Is.True);
            Assert.That(result.Games.Count, Is.EqualTo(4));
            Assert.That(result.Report.PairResults.Single().Games, Is.EqualTo(4));
            Assert.That(File.Exists(Path.Combine(runDirectory, TournamentRunReader.ReportFileName)), Is.False);
        }

        [Test]
        public void TournamentRunReader_Read_RunFileWithTornFinalLine_DropsItAndKeepsEarlierGames()
        {
            string headerLine = TournamentRunWriter.BuildHeaderLine(
                1, "Quick", runSeed: 111, totalGames: 5, "ProductionBudget", workerCount: 1, DateTime.UtcNow);
            string runDirectory = Path.Combine(_tempRoot, "torn-run");
            Directory.CreateDirectory(runDirectory);

            var goodRecord = new TournamentRunRecord(
                0, pairIndex: 0, "hard", "normal", subjectIsWhite: true,
                positionIndex: 0, MatchOutcome.WhiteWon, plyCount: 20, reachedPlyCap: false, elapsedMs: 300);

            string runPath = Path.Combine(runDirectory, TournamentRunReader.RunFileName);
            File.WriteAllText(runPath,
                headerLine + "\n" +
                goodRecord.ToLine() + "\n" +
                "1\t0\thard\tnor"); // torn final line, no trailing newline — exactly what a kill mid-write leaves

            TournamentRunResult result = TournamentRunReader.Read(runDirectory);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.IsPartial, Is.True);
            Assert.That(result.Games.Count, Is.EqualTo(1));
            Assert.That(result.Games[0].SubjectId, Is.EqualTo("hard"));
        }

        [Test]
        public void TournamentRunReader_Read_MissingRunFile_ReturnsNull()
        {
            string emptyDirectory = Path.Combine(_tempRoot, "nothing-here");
            Directory.CreateDirectory(emptyDirectory);

            Assert.That(TournamentRunReader.Read(emptyDirectory), Is.Null);
        }

        [Test]
        public void TournamentRunWriter_WriteGame_ReturnsWithoutWaitingForDisk()
        {
            // The writer must never make a worker block on IO — WriteGame is a queue push, not a
            // file write. This can't prove "never blocks" in general, but it does pin that a burst
            // of enqueues completes fast even though the background flush interval is 250ms; if
            // WriteGame synchronously touched the file, this would be bounded below by that flush
            // interval instead.
            string headerLine = TournamentRunWriter.BuildHeaderLine(
                1, "Quick", runSeed: 1, totalGames: 200, "ProductionBudget", workerCount: 8, DateTime.UtcNow);
            string runDirectory = Path.Combine(_tempRoot, "fast-enqueue");

            using (var writer = new TournamentRunWriter(runDirectory, headerLine))
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 200; i++)
                {
                    writer.WriteGame(new TournamentRunRecord(
                        i, 0, "hard", "normal", true, 0, MatchOutcome.Draw, 10, false, 5));
                }
                stopwatch.Stop();

                Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(250),
                    "enqueuing 200 games took as long as a full flush interval — WriteGame may be blocking on IO.");
            }
        }

        private string FindTheOneRunDirectory()
        {
            string[] directories = Directory.GetDirectories(_tempRoot);
            Assert.That(directories.Length, Is.EqualTo(1), "expected exactly one run directory under the temp root.");
            return directories[0];
        }
    }
}
