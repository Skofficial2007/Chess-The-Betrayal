using System.Collections.Generic;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Utils;

namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// Consumes AlphaBetaSearch's ranked root-move output (RootMoves/RootScores/BestRootIndex,
    /// read immediately after FindBestMove returns) and applies AIProfile's personality dials —
    /// blunder roll, tie-break window, Betrayal-aggression reweight — to choose the FINAL move.
    /// Never invents a move or reorders search-ranked candidates outside these dials; always
    /// returns a legal, search-ranked root move.
    ///
    /// Owns its own small pooled scratch buffers (index/weight arrays) so repeated calls allocate
    /// nothing, matching AlphaBetaSearch's own established pattern. NOT thread-safe by
    /// design — one instance per AsyncAIAgent, called only from that agent's worker thread.
    /// </summary>
    public sealed class MoveSelectionPolicy
    {
        // Must match AlphaBetaSearch's root-score array sizing so the pooled buffers below never
        // fall short of what a single search call can hand in.
        internal const int MaxRootMoves = 128;

        private int[] _candidateIndices = new int[MaxRootMoves];
        private float[] _weights = new float[MaxRootMoves];

        public MoveCommand SelectFinalMove(
            IReadOnlyList<MoveCommand> rootMoves, int[] rootScores, int rootMoveCount, int bestIndex,
            AIProfile profile, IRandomSource rng) =>
            SelectFinalMove(rootMoves, rootScores, rootMoveCount, bestIndex, profile, rng, out _);

        /// <summary>
        /// Same selection as the four-argument overload, but also reports whether the blunder roll
        /// actually fired (rolled under BlunderRate AND found a candidate within margin) — a caller
        /// measuring how often a profile's configured BlunderRate translates into a real blunder
        /// needs this distinguished from the tie-break window returning a non-best move for an
        /// unrelated reason (a Betrayal-aggression reweight can do that too, at BlunderRate == 0).
        /// </summary>
        public MoveCommand SelectFinalMove(
            IReadOnlyList<MoveCommand> rootMoves, int[] rootScores, int rootMoveCount, int bestIndex,
            AIProfile profile, IRandomSource rng, out bool blunderRollFired)
        {
            blunderRollFired = false;

            if (rootMoveCount == 0) return default;
            if (rng == null) return rootMoves[bestIndex];

            EnsureCapacity(rootMoveCount);

            MoveCommand best = rootMoves[bestIndex];
            int bestScore = rootScores[bestIndex];

            // 1. Blunder roll — bounded, seeded. A weak profile occasionally plays its own
            // search-ranked 2nd/3rd-best move instead of the top one, never a synthesized move and
            // never worse than BlunderMarginCp below the best — "sometimes misses the best idea,"
            // not "plays noise."
            if (profile.BlunderRate > 0f && rng.NextFloat() < profile.BlunderRate)
            {
                int threshold = bestScore - profile.BlunderMarginCp;
                int count = 0;
                for (int i = 0; i < rootMoveCount; i++)
                {
                    if (i == bestIndex) continue;
                    if (rootScores[i] >= threshold) _candidateIndices[count++] = i;
                }
                if (count > 0)
                {
                    int pickPos = rng.NextInt(count);
                    blunderRollFired = true;
                    return rootMoves[_candidateIndices[pickPos]];
                }
                // No candidate within margin (e.g. a lone legal move) — fall through rather than
                // forcing a "blunder" that doesn't exist.
            }

            // 2. Tie-break window, computed from scores ONLY — then Betrayal-aggression reweights
            // strictly WITHIN it. Non-negotiable: aggression can bias which in-window move gets
            // picked, but it can never pull in a move outside the window; the AI never chooses a
            // strictly worse move (beyond the declared near-tie margin) to force or avoid a
            // Betrayal.
            int windowThreshold = bestScore - profile.TieBreakWindowCp;
            int windowCount = 0;
            for (int i = 0; i < rootMoveCount; i++)
            {
                if (rootScores[i] >= windowThreshold) _candidateIndices[windowCount++] = i;
            }

            // A window of exactly the best move (Impossible tier: TieBreakWindowCp == 0, or any
            // position with a lone standout move) is deterministic — zero RNG calls.
            if (windowCount <= 1) return best;

            float totalWeight = 0f;
            for (int w = 0; w < windowCount; w++)
            {
                int idx = _candidateIndices[w];
                bool isAct = rootMoves[idx].Stage == BetrayalStage.Act;
                _weights[w] = isAct ? (1f + profile.BetrayalAggression) : 1f;
                totalWeight += _weights[w];
            }

            // A window made up entirely of Act moves at BetrayalAggression == -1 zeroes every
            // weight, leaving nothing for the roll below to land on. Fall back to the best-scored
            // move in the window rather than let the loop's own rounding fallback silently pick
            // whatever happens to be last in candidate order.
            if (totalWeight <= 0f) return best;

            float roll = rng.NextFloat() * totalWeight;
            float cumulative = 0f;
            for (int w = 0; w < windowCount; w++)
            {
                cumulative += _weights[w];
                if (roll < cumulative) return rootMoves[_candidateIndices[w]];
            }
            return rootMoves[_candidateIndices[windowCount - 1]]; // float rounding fallback
        }

        /// <summary>Grows the pooled scratch buffers to fit a pathological branching root, mirroring
        /// AlphaBetaSearch's own lazy-grow policy for its parallel root-score arrays.</summary>
        private void EnsureCapacity(int requiredCount)
        {
            if (_candidateIndices.Length >= requiredCount) return;

            int newSize = requiredCount * 2;
            System.Array.Resize(ref _candidateIndices, newSize);
            System.Array.Resize(ref _weights, newSize);
        }
    }
}
