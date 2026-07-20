using System;
using System.Collections.Generic;
using System.IO;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.EditorTools.Benchmark
{
    /// <summary>
    /// Batch entry point over TournamentSession: drains a whole tournament in one blocking call and
    /// returns the report — the shape a menu command or a CI -executeMethod invocation wants. Games
    /// are played across several worker threads via ParallelTournamentExecutor by default (see its
    /// own doc comment for why that's safe and still deterministic); pass parallel:false for the
    /// old one-thread-at-a-time behavior, useful when isolating whether a discrepancy is caused by
    /// the parallel path itself. The interactive tournament window drives the same TournamentSession
    /// one game per editor tick on its own thread instead — both paths share the session (and its
    /// seeding) verbatim, so they play the same pairings from the same positions with the same RNG
    /// streams. See TournamentSession's own doc comment for the one caveat time-budgeted searches
    /// put on exact outcome reproduction.
    ///
    /// Dev/editor-only. Nothing in Core, AI, or the shipped player build ever references this —
    /// it's the same category of tool as the opening-book compiler, not a player-facing feature.
    /// </summary>
    public static class BenchmarkRunner
    {
        public static BenchmarkReport RunAll(int runSeed, BenchmarkMode mode) =>
            RunAll(runSeed, mode, AIProfileTable.BuiltIn, MatchSimulator.DefaultPlyCap);

        /// <summary>
        /// Same tournament shape as the two-argument overload, but resolves profile ids against
        /// <paramref name="roster"/> instead of the shipped built-in table, and caps each game at
        /// <paramref name="plyCap"/> plies. Exists so tests can exercise the tournament contract
        /// (pairing count, reproducibility, report shape) against fast, shallow fixture profiles
        /// with a tight ply cap, without paying the real tournament's search-depth-and-length cost
        /// — the real tournament (against AIProfileTable.BuiltIn, full ply cap) is only ever run
        /// from the menu/batchmode/window entry points.
        ///
        /// persistRunsUnderDirectory, when supplied, streams every finished game to a timestamped
        /// subdirectory as it completes and writes the final report/summary once the run ends —
        /// see TournamentRunWriter's own doc comment for why a run killed mid-way still leaves real
        /// data on disk when this is set. Left null by every existing test so persistence never
        /// touches disk unless a caller explicitly opts in.
        /// </summary>
        public static BenchmarkReport RunAll(int runSeed, BenchmarkMode mode,
            IReadOnlyList<AIProfile> roster, int plyCap = MatchSimulator.DefaultPlyCap, bool parallel = true,
            MatchTimeControl timeControl = MatchTimeControl.ProductionBudget, ITournamentProgress progress = null,
            string persistRunsUnderDirectory = null)
        {
            TournamentSession session = mode == BenchmarkMode.Quick
                ? TournamentSession.CreateQuick(runSeed, roster, plyCap, timeControl)
                : TournamentSession.CreateFull(runSeed, roster, plyCap, timeControl);

            TournamentRunWriter runWriter = persistRunsUnderDirectory == null
                ? null
                : CreateRunWriter(persistRunsUnderDirectory, mode, runSeed, session.TotalGames, timeControl, parallel);

            try
            {
                if (parallel)
                {
                    ParallelTournamentExecutor.RunRemainingGames(session, progress: progress, runWriter: runWriter);
                }
                else
                {
                    int total = session.TotalGames;
                    progress ??= NullTournamentProgress.Instance;
                    while (session.RunNextGame())
                    {
                        progress.ReportGameCompleted(session.GamesCompleted, total);
                    }
                }

                BenchmarkReport report = session.BuildReport();

                if (runWriter != null)
                {
                    WriteCompletionArtifacts(runWriter.RunDirectory, report);
                }

                return report;
            }
            finally
            {
                runWriter?.Dispose();
            }
        }

        private static TournamentRunWriter CreateRunWriter(string baseDirectory, BenchmarkMode mode,
            int runSeed, int totalGames, MatchTimeControl timeControl, bool parallel)
        {
            DateTime startUtc = DateTime.UtcNow;
            string runFolderName = $"{mode}-{runSeed}-{startUtc:yyyyMMdd-HHmmss}";
            string runDirectory = Path.Combine(baseDirectory, runFolderName);
            int workerCount = parallel ? ParallelTournamentExecutor.DefaultMaxDegreeOfParallelism : 1;

            string headerLine = TournamentRunWriter.BuildHeaderLine(
                schemaVersion: 1, mode.ToString(), runSeed, totalGames, timeControl.ToString(), workerCount, startUtc);

            return new TournamentRunWriter(runDirectory, headerLine);
        }

        /// <summary>Written once, only after every game has finished — its presence is the signal
        /// a reader uses to tell a completed run from a partial one (see TournamentRunReader), so
        /// this must never be written from anywhere but the very end of a successful run.</summary>
        private static void WriteCompletionArtifacts(string runDirectory, BenchmarkReport report)
        {
            string reportPath = Path.Combine(runDirectory, TournamentRunReader.ReportFileName);
            BenchmarkBaselineIO.Write(report, reportPath);

            string summaryPath = Path.Combine(runDirectory, "summary.md");
            File.WriteAllText(summaryPath, BenchmarkReportFormatter.ToMarkdown(report));
        }
    }
}
