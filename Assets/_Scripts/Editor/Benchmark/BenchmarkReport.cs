using System;
using System.Collections.Generic;

namespace ChessTheBetrayal.EditorTools.Benchmark
{
    /// <summary>How much of the tournament matrix a run covers.</summary>
    public enum BenchmarkMode
    {
        /// <summary>Adjacent strength-chain pairs only, reduced game count — the routine/on-demand check.</summary>
        Quick,

        /// <summary>The full round-robin matrix at full game count — the tuning instrument, nightly-class cost.</summary>
        Full
    }

    /// <summary>One pairing's win/draw/loss record for the profile listed first (Subject).</summary>
    [Serializable]
    public sealed class PairResult
    {
        public string Subject;
        public string Opponent;
        public int Games;
        public int SubjectWins;
        public int OpponentWins;
        public int Draws;
        public float SubjectWinRate;

        public PairResult() { }

        public PairResult(string subject, string opponent, int games, int subjectWins, int opponentWins, int draws)
        {
            Subject = subject;
            Opponent = opponent;
            Games = games;
            SubjectWins = subjectWins;
            OpponentWins = opponentWins;
            Draws = draws;
            SubjectWinRate = games == 0 ? 0f : (subjectWins + 0.5f * draws) / games;
        }
    }

    /// <summary>Search/perf telemetry for one profile tier, averaged across every move it made this run.</summary>
    [Serializable]
    public sealed class TierPerformance
    {
        public string ProfileId;
        public int MovesSampled;
        public double MeanNodesPerMove;
        public double MeanMsPerMove;

        /// <summary>The single deepest ply any one move reached this run — a "what is reachable"
        /// number. It is a maximum, not typical, so one cheap position can make a tier look like it
        /// reaches a depth it rarely does in practice; MeanCompletedDepth below is the typical-play
        /// number a depth-ceiling decision should actually read.</summary>
        public int DeepestCompletedDepth;
        public double MeanCompletedDepth;
        public int ShallowestCompletedDepth;

        /// <summary>Move count per completed depth, index = depth (0 unused). See
        /// MatchSideStats.DepthHistogramCapacity for the fixed size and overflow-folding rule.</summary>
        public int[] DepthHistogram;

        public float ObservedBlunderActuationRate;

        /// <summary>How many of this tier's played moves this run were a Betrayal Act, and how
        /// that Act resolved — an ally executing the betrayer (Retribution) versus the betrayer
        /// permanently switching sides (Defection, resolved with no move of its own whenever no
        /// legal Retribution existed). The two branches cost the initiator very differently, so
        /// they're reported separately rather than as one undifferentiated Act count.</summary>
        public int ActsPlayed;
        public int ActsResolvedByRetribution;
        public int ActsResolvedByDefection;

        public TierPerformance() { }

        public TierPerformance(string profileId, int movesSampled, double meanNodesPerMove,
            double meanMsPerMove, int deepestCompletedDepth, double meanCompletedDepth,
            int shallowestCompletedDepth, int[] depthHistogram, float observedBlunderActuationRate,
            int actsPlayed = 0, int actsResolvedByRetribution = 0, int actsResolvedByDefection = 0)
        {
            ProfileId = profileId;
            MovesSampled = movesSampled;
            MeanNodesPerMove = meanNodesPerMove;
            MeanMsPerMove = meanMsPerMove;
            DeepestCompletedDepth = deepestCompletedDepth;
            MeanCompletedDepth = meanCompletedDepth;
            ShallowestCompletedDepth = shallowestCompletedDepth;
            DepthHistogram = depthHistogram;
            ObservedBlunderActuationRate = observedBlunderActuationRate;
            ActsPlayed = actsPlayed;
            ActsResolvedByRetribution = actsResolvedByRetribution;
            ActsResolvedByDefection = actsResolvedByDefection;
        }

        public float ActRate => MovesSampled == 0 ? 0f : (float)ActsPlayed / MovesSampled;
    }

    /// <summary>
    /// One benchmark run's full output: the artifact both the strength-drift check and the
    /// performance-drift check read from, captured in a single tournament pass. This is the shape
    /// serialized to Docs/Benchmarks/baseline.json.
    /// </summary>
    [Serializable]
    public sealed class BenchmarkReport
    {
        public int RunSeed;
        public string Mode;
        public List<PairResult> PairResults = new List<PairResult>();
        public List<TierPerformance> TierPerformances = new List<TierPerformance>();

        public PairResult FindPair(string subject, string opponent)
        {
            foreach (PairResult pair in PairResults)
                if (pair.Subject == subject && pair.Opponent == opponent) return pair;
            return null;
        }

        public TierPerformance FindTier(string profileId)
        {
            foreach (TierPerformance tier in TierPerformances)
                if (tier.ProfileId == profileId) return tier;
            return null;
        }
    }
}
