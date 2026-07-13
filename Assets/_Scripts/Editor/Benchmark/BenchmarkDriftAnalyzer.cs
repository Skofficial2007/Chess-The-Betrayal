using System.Collections.Generic;

namespace ChessTheBetrayal.EditorTools.Benchmark
{
    public enum DriftSeverity
    {
        Warn,
        Fail
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
                if (pair.SubjectWinRate < AdjacentPairHardFloor)
                {
                    findings.Add(new DriftFinding(DriftSeverity.Fail,
                        $"{pair.Subject} vs {pair.Opponent}: win rate {pair.SubjectWinRate:P1} is below the {AdjacentPairHardFloor:P0} hard floor over {pair.Games} games."));
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

        private static AI.AIProfile FindConfiguredProfile(string id)
        {
            foreach (AI.AIProfile profile in AI.AIProfileTable.BuiltIn)
                if (profile.Id == id) return profile;
            return default;
        }
    }
}
