using System.Text;

namespace ChessTheBetrayal.EditorTools.Benchmark
{
    /// <summary>
    /// Renders a BenchmarkReport as human-readable text — one implementation shared by the console
    /// log (BenchmarkMenu) and the summary.md a persisted run writes to disk, so the two never drift
    /// apart into two slightly different descriptions of the same numbers.
    /// </summary>
    public static class BenchmarkReportFormatter
    {
        /// <summary>Plain-text form, one line per pairing/tier — what BenchmarkMenu has always
        /// logged. isPartial prefixes the output with a banner naming how many games actually
        /// completed, so a killed run's numbers are never read as if they were the finished
        /// article — see TournamentRunReader.</summary>
        public static string ToPlainText(BenchmarkReport report, BenchmarkReport baseline,
            bool isPartial = false, int gamesCompleted = 0, int gamesPlanned = 0)
        {
            var sb = new StringBuilder();
            AppendPartialBanner(sb, isPartial, gamesCompleted, gamesPlanned);
            sb.AppendLine($"Benchmark run — mode={report.Mode} seed={report.RunSeed}");

            foreach (PairResult pair in report.PairResults)
            {
                float margin = TournamentStatistics.WinRateMargin95(pair.Games);
                sb.AppendLine($"  {pair.Subject} vs {pair.Opponent}: {pair.SubjectWinRate:P1} +/-{margin:P1} " +
                    $"({pair.SubjectWins}W {pair.OpponentWins}L {pair.Draws}D over {pair.Games} games)");
            }

            foreach (TierPerformance tier in report.TierPerformances)
            {
                sb.AppendLine($"  [{tier.ProfileId}] {tier.MovesSampled} moves, " +
                    $"{tier.MeanNodesPerMove:F0} nodes/move, {tier.MeanMsPerMove:F0}ms/move, " +
                    $"depth reached {tier.DeepestCompletedDepth}, blunder-actuation {tier.ObservedBlunderActuationRate:P1}");
            }

            var findings = BenchmarkDriftAnalyzer.Analyze(report, baseline);
            if (findings.Count == 0)
            {
                sb.AppendLine(baseline == null
                    ? "  No baseline on disk yet — nothing to diff against."
                    : "  No drift findings.");
            }
            else
            {
                foreach (var finding in findings)
                    sb.AppendLine($"  [{finding.Severity}] {finding.Message}");
            }

            return sb.ToString();
        }

        /// <summary>Markdown form for a run directory's summary.md — a contributor with no context
        /// should be able to open this file alone and understand what ran and what it found.</summary>
        public static string ToMarkdown(BenchmarkReport report, bool isPartial = false,
            int gamesCompleted = 0, int gamesPlanned = 0)
        {
            var sb = new StringBuilder();

            if (isPartial)
            {
                sb.AppendLine($"# PARTIAL RUN — {gamesCompleted} of {gamesPlanned} games completed");
                sb.AppendLine();
                sb.AppendLine("This run did not finish. The numbers below are real — every game listed actually " +
                    "played — but the sample sizes and confidence intervals are for however many games completed " +
                    "before the run stopped, not the full plan.");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("# Benchmark run");
                sb.AppendLine();
            }

            sb.AppendLine($"Mode: `{report.Mode}` &nbsp; Seed: `{report.RunSeed}`");
            sb.AppendLine();
            sb.AppendLine("## Pairings");
            sb.AppendLine();
            sb.AppendLine("| Subject | Opponent | Win rate | 95% CI | W / L / D | Games |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (PairResult pair in report.PairResults)
            {
                float margin = TournamentStatistics.WinRateMargin95(pair.Games);
                sb.AppendLine($"| {pair.Subject} | {pair.Opponent} | {pair.SubjectWinRate:P1} | " +
                    $"+/-{margin:P1} | {pair.SubjectWins} / {pair.OpponentWins} / {pair.Draws} | {pair.Games} |");
            }

            sb.AppendLine();
            sb.AppendLine("## Tier performance");
            sb.AppendLine();
            sb.AppendLine("| Tier | Moves | Nodes/move | Ms/move | Depth reached | Blunder actuation |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (TierPerformance tier in report.TierPerformances)
            {
                sb.AppendLine($"| {tier.ProfileId} | {tier.MovesSampled} | {tier.MeanNodesPerMove:F0} | " +
                    $"{tier.MeanMsPerMove:F0} | {tier.DeepestCompletedDepth} | {tier.ObservedBlunderActuationRate:P1} |");
            }

            return sb.ToString();
        }

        private static void AppendPartialBanner(StringBuilder sb, bool isPartial, int gamesCompleted, int gamesPlanned)
        {
            if (!isPartial) return;
            sb.AppendLine($"PARTIAL RUN — {gamesCompleted} of {gamesPlanned} games completed. Every number below is real, but confidence intervals reflect this smaller sample, not the full plan.");
        }
    }
}
