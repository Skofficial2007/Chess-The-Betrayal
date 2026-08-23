using System.Collections.Generic;

namespace ChessTheBetrayal.EditorTools.Benchmark
{
    public enum DriftSeverity
    {
        Warn,
        Fail,

        /// <summary>The observed number crossed a threshold, but the sample is small enough that
        /// its own 95% confidence interval overlaps the threshold — the run cannot actually tell a
        /// real regression apart from ordinary sampling noise at this N. Reported instead of Fail
        /// so a low-N run never asserts a confident failure it has no statistical power to back up
        /// (see TournamentStatistics.WinRateMargin95); reported instead of silence so the finding
        /// isn't simply dropped and lost.</summary>
        Inconclusive
    }

    /// <summary>One threshold check's outcome against a baseline.</summary>
    public sealed class DriftFinding
    {
        public readonly DriftSeverity Severity;
        public readonly string Message;

        public DriftFinding(DriftSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }
    }

    /// <summary>
    /// Compares one BenchmarkReport against a committed baseline and flags drift, per the fixed
    /// threshold table: an ordering violation or a depth-7-class timing regression fails outright
    /// (blocks the tuning-table/search change from merging); everything else warns (prompts
    /// investigation without blocking unrelated work).
    ///
    /// The win-rate floor is applied ONLY to pairings the preset table actually makes a claim about,
    /// in the direction it claims — see TournamentSession.AdjacentPairs. A report from a full
    /// round-robin contains every pair in roster order, so most of its rows read weaker-vs-stronger
    /// and carry no promise at all; grading those against a floor meant for the other direction
    /// would report a correctly-ordered ladder as a wall of failures.
    /// </summary>
    public static class BenchmarkDriftAnalyzer
    {
        public const float AdjacentPairHardFloor = 0.55f;
        public const float AdjacentWinRateDriftWarnPoints = 0.15f;
        public const double DepthSevenTimingFailMs = 3000.0;
        public const float NodesPerSecondRegressionWarnFraction = 0.30f;
        public const float BlunderActuationDriftWarnPoints = 0.05f;

        public static List<DriftFinding> Analyze(BenchmarkReport current, BenchmarkReport baseline)
        {
            var findings = new List<DriftFinding>();

            foreach (PairResult pair in current.PairResults)
            {
                // The floor only means anything when the Subject is the side we expect to WIN.
                // Quick mode lists its pairs stronger-first, but a round-robin lists every pair in
                // roster order, so half of them read weaker-vs-stronger — and grading those against
                // a 55% floor would report the ladder working correctly as a failure (a tier losing
                // 0% against one two steps above it is the ladder being right, not broken).
                if (!AssertsSubjectIsStronger(pair.Subject, pair.Opponent)) continue;

                if (pair.SubjectWinRate < AdjacentPairHardFloor)
                {
                    float margin = TournamentStatistics.WinRateMargin95(pair.Games);
                    bool floorIsInsideConfidenceInterval = pair.SubjectWinRate + margin >= AdjacentPairHardFloor;

                    if (floorIsInsideConfidenceInterval)
                    {
                        findings.Add(new DriftFinding(DriftSeverity.Inconclusive,
                            $"{pair.Subject} vs {pair.Opponent}: win rate {pair.SubjectWinRate:P1} +/-{margin:P1} is below the " +
                            $"{AdjacentPairHardFloor:P0} floor over only {pair.Games} games, but the {AdjacentPairHardFloor:P0} floor " +
                            "is inside this sample's own 95% confidence interval — more games are needed before calling this a real failure."));
                    }
                    else
                    {
                        findings.Add(new DriftFinding(DriftSeverity.Fail,
                            $"{pair.Subject} vs {pair.Opponent}: win rate {pair.SubjectWinRate:P1} +/-{margin:P1} is below the {AdjacentPairHardFloor:P0} hard floor over {pair.Games} games."));
                    }
                }

                PairResult baselinePair = baseline?.FindPair(pair.Subject, pair.Opponent);
                if (baselinePair != null)
                {
                    float drift = pair.SubjectWinRate - baselinePair.SubjectWinRate;
                    if (System.Math.Abs(drift) > AdjacentWinRateDriftWarnPoints)
                    {
                        findings.Add(new DriftFinding(DriftSeverity.Warn,
                            $"{pair.Subject} vs {pair.Opponent}: win rate drifted {drift:+0.0%;-0.0%} from baseline ({baselinePair.SubjectWinRate:P1} -> {pair.SubjectWinRate:P1})."));
                    }
                }
            }

            foreach (TierPerformance tier in current.TierPerformances)
            {
                if (tier.MeanMsPerMove > DepthSevenTimingFailMs)
                {
                    findings.Add(new DriftFinding(DriftSeverity.Fail,
                        $"{tier.ProfileId}: mean {tier.MeanMsPerMove:F0}ms/move exceeds the {DepthSevenTimingFailMs:F0}ms search-performance DoD."));
                }

                TierPerformance baselineTier = baseline?.FindTier(tier.ProfileId);
                if (baselineTier != null && baselineTier.MeanMsPerMove > 0)
                {
                    double baselineNodesPerSecond = baselineTier.MeanNodesPerMove / (baselineTier.MeanMsPerMove / 1000.0);
                    double currentNodesPerSecond = tier.MeanMsPerMove > 0 ? tier.MeanNodesPerMove / (tier.MeanMsPerMove / 1000.0) : 0;

                    if (baselineNodesPerSecond > 0)
                    {
                        double dropFraction = (baselineNodesPerSecond - currentNodesPerSecond) / baselineNodesPerSecond;
                        if (dropFraction > NodesPerSecondRegressionWarnFraction)
                        {
                            findings.Add(new DriftFinding(DriftSeverity.Warn,
                                $"{tier.ProfileId}: nodes/sec dropped {dropFraction:P0} from baseline ({baselineNodesPerSecond:F0} -> {currentNodesPerSecond:F0})."));
                        }
                    }
                }

                AI.AIProfile profile = FindConfiguredProfile(tier.ProfileId);
                if (profile.Id != null && profile.BlunderRate > 0f)
                {
                    float actuationDrift = tier.ObservedBlunderActuationRate - profile.BlunderRate;
                    if (System.Math.Abs(actuationDrift) > BlunderActuationDriftWarnPoints)
                    {
                        findings.Add(new DriftFinding(DriftSeverity.Warn,
                            $"{tier.ProfileId}: observed blunder-actuation rate {tier.ObservedBlunderActuationRate:P1} vs configured BlunderRate {profile.BlunderRate:P1} — drifted more than {BlunderActuationDriftWarnPoints:P0}."));
                    }
                }
            }

            return findings;
        }

        /// <summary>
        /// Whether this pairing is one the preset table actually promises an outcome for, with the
        /// Subject as the side expected to win. Only those carry a win-rate floor.
        ///
        /// Two pairings are deliberately absent. A tier is never asserted against one several rungs
        /// below it (the claim is that each step up beats the step below, and transitivity covers
        /// the rest), and `aggressive` is a PERSONALITY rather than a rung — it trades soundness for
        /// Betrayal-seeking play, so it is asserted against the tiers it should still beat and left
        /// unasserted against the deeper ones, where losing is a legitimate consequence of its
        /// dials rather than a regression.
        /// </summary>
        private static bool AssertsSubjectIsStronger(string subject, string opponent)
        {
            foreach ((string Subject, string Opponent) claim in TournamentSession.AdjacentPairs)
                if (claim.Subject == subject && claim.Opponent == opponent) return true;
            return false;
        }

        private static AI.AIProfile FindConfiguredProfile(string id)
        {
            foreach (AI.AIProfile profile in AI.AIProfileTable.BuiltIn)
                if (profile.Id == id) return profile;
            return default;
        }
    }
}
