using System.Collections.Generic;
using System.IO;
using System.Linq;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.EditorTools.Benchmark
{
    /// <summary>One completed run directory read back from disk, distinguishing a finished run from
    /// one that was killed partway through by a single fact: whether report.json exists. Nothing
    /// else marks partial-ness — a directory with only run.jsonl in it IS a partial run, and a
    /// caller that ignores IsPartial and prints Report's win rates bare is exactly the "small-N
    /// number mistaken for a precise one" failure this whole ticket exists to prevent.</summary>
    public sealed class TournamentRunResult
    {
        public readonly TournamentRunHeader Header;
        public readonly IReadOnlyList<TournamentRunRecord> Games;
        public readonly BenchmarkReport Report;
        public readonly bool IsPartial;

        public TournamentRunResult(TournamentRunHeader header, IReadOnlyList<TournamentRunRecord> games,
            BenchmarkReport report, bool isPartial)
        {
            Header = header;
            Games = games;
            Report = report;
            IsPartial = isPartial;
        }
    }

    /// <summary>
    /// Reads a run directory TournamentRunWriter produced. A run.jsonl written by a process that was
    /// killed mid-line ends in a torn final line — TryParse on that line fails, and the reader drops
    /// it rather than throwing, which is the whole point: every game that finished before the kill
    /// is still there and still readable.
    /// </summary>
    public static class TournamentRunReader
    {
        public const string RunFileName = "run.jsonl";
        public const string ReportFileName = "report.json";

        /// <summary>Reads every complete record from runDirectory's run.jsonl, plus the final
        /// report.json when the run finished. Returns null if runDirectory has no run.jsonl at all
        /// (not a run directory, or nothing was ever written) — a header-parse failure on an
        /// existing file still returns a result, since the header failing to parse must not hide
        /// game records that DID write successfully after it.</summary>
        public static TournamentRunResult Read(string runDirectory)
        {
            string runPath = Path.Combine(runDirectory, RunFileName);
            if (!File.Exists(runPath)) return null;

            TournamentRunHeader header = default;
            var games = new List<TournamentRunRecord>();

            using (var streamReader = new StreamReader(runPath))
            {
                string firstLine = streamReader.ReadLine();
                if (firstLine != null) TournamentRunHeader.TryParse(firstLine, out header);

                string line;
                while ((line = streamReader.ReadLine()) != null)
                {
                    if (TournamentRunRecord.TryParse(line, out TournamentRunRecord record))
                        games.Add(record);
                    // A line that fails to parse is either a torn final write from a killed
                    // process or genuine corruption — either way, silently dropping it is correct:
                    // every game recorded before it is still valid, and there is nothing useful to
                    // recover from a half-written line.
                }
            }

            string reportPath = Path.Combine(runDirectory, ReportFileName);
            bool isComplete = File.Exists(reportPath);
            BenchmarkReport report = isComplete
                ? BenchmarkBaselineIO.TryRead(reportPath)
                : BuildReportFromGames(header, games);

            return new TournamentRunResult(header, games, report, isPartial: !isComplete);
        }

        /// <summary>Reconstructs a BenchmarkReport straight from the per-game records — this is what
        /// makes a killed run's data actually usable rather than merely present. Mirrors
        /// TournamentSession.ApplyCompletedGame/BuildReport's aggregation exactly, minus the
        /// per-move search telemetry (TournamentRunRecord doesn't carry it — see its own doc
        /// comment for why the game outcome fields are what a kill needs to survive).</summary>
        private static BenchmarkReport BuildReportFromGames(TournamentRunHeader header, IReadOnlyList<TournamentRunRecord> games)
        {
            var report = new BenchmarkReport { RunSeed = header.RunSeed, Mode = header.Mode };
            var tallies = new Dictionary<(int PairIndex, string Subject, string Opponent), (int Wins, int Losses, int Draws, int Games)>();
            var pairOrder = new List<(int PairIndex, string Subject, string Opponent)>();

            foreach (TournamentRunRecord game in games)
            {
                string subjectId = game.SubjectIsWhite ? game.SubjectId : game.OpponentId;
                string opponentId = game.SubjectIsWhite ? game.OpponentId : game.SubjectId;
                var key = (game.PairIndex, game.SubjectId, game.OpponentId);

                if (!tallies.TryGetValue(key, out var tally))
                {
                    pairOrder.Add(key);
                }

                bool subjectWon = game.SubjectIsWhite
                    ? game.Outcome == MatchOutcome.WhiteWon
                    : game.Outcome == MatchOutcome.BlackWon;
                bool opponentWon = game.SubjectIsWhite
                    ? game.Outcome == MatchOutcome.BlackWon
                    : game.Outcome == MatchOutcome.WhiteWon;

                tally.Games++;
                if (subjectWon) tally.Wins++;
                else if (opponentWon) tally.Losses++;
                else tally.Draws++;
                tallies[key] = tally;
            }

            foreach (var key in pairOrder)
            {
                var tally = tallies[key];
                report.PairResults.Add(new PairResult(key.Subject, key.Opponent, tally.Games, tally.Wins, tally.Losses, tally.Draws));
            }

            return report;
        }
    }
}
