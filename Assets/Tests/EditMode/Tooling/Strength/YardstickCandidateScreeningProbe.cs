using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tooling.Agreement;
using ChessTheBetrayal.Tooling.Strength;
using ChessTheBetrayal.Tests.EditMode.Support;

namespace ChessTheBetrayal.Tests.EditMode.Tooling.Strength
{
    /// <summary>
    /// Decides which proposed yardstick positions are actually usable, and says why the rest are
    /// not.
    ///
    /// Three things have to hold before a position can measure anything, and none of them can be
    /// settled by inspection:
    ///
    ///   1. The material-only proof has to find a single sharply-best move. If the top two moves
    ///      score within a rounding error, the AI choosing either is defensible and the position
    ///      punishes a reasonable answer.
    ///   2. The best move has to hold still as the search gets deeper. Some positions genuinely
    ///      alternate their answer from one ply to the next, and on those the "correct" move is a
    ///      fact about the depth that was searched rather than about the position.
    ///   3. The real search has to reach the proving depth in the time it actually gets. A position
    ///      only solvable deeper than the AI can see fails as a speed problem and teaches nothing
    ///      about evaluation, which is the thing being measured.
    ///
    /// Explicit, and one candidate per case: screening searches several depths and costs minutes
    /// in total, well past a per-commit budget and past the default per-test timeout if batched.
    /// </summary>
    [TestFixture]
    [Explicit("Measurement only — run by hand when growing the yardstick suite.")]
    [Category(TestCategories.OnDemand)]
    public class YardstickCandidateScreeningProbe
    {
        private static AIProfile TopTier => AIProfileTable.BuiltIn.Single(p => p.Id == "impossible");

        private static IEnumerable<TestCaseData> Candidates()
        {
            for (int i = 0; i < YardstickCandidateSuite.All.Count; i++)
            {
                YardstickCandidateSuite.Candidate candidate = YardstickCandidateSuite.All[i];
                yield return new TestCaseData(i).SetName($"Screen_{candidate.Name}");
            }
        }

        [TestCaseSource(nameof(Candidates))]
        public void ScreenCandidate(int index)
        {
            YardstickCandidateSuite.Candidate candidate = YardstickCandidateSuite.All[index];
            BoardState board = candidate.BuildBoard();

            Debug.Log($"=== {candidate.Name} ===");
            Debug.Log($"rationale: {candidate.Rationale}");

            // Gate 1 — is there a sharply best move by forced material, and is it the one proposed?
            List<ProvenMoveScore> scored = QuietMoveProver.ScoreAllMoves(board, candidate.ProposedProvingDepth);
            if (scored.Count == 0)
            {
                Debug.Log("VERDICT: REJECT — no legal moves.");
                return;
            }

            ProvenMoveScore best = scored[0];
            bool proposedIsBest = best.Move.StartPosition == candidate.ExpectedFrom
                && best.Move.EndPosition == candidate.ExpectedTo;

            int margin = scored.Count > 1 ? best.ScoreCp - scored[1].ScoreCp : int.MaxValue;

            Debug.Log($"proof at depth {candidate.ProposedProvingDepth}: best {best}, "
                + $"margin over next {(margin == int.MaxValue ? "n/a (only move)" : margin + "cp")}");
            Debug.Log($"ranking: {string.Join(", ", scored.Take(6).Select(s => s.ToString()))}");

            if (!proposedIsBest)
            {
                Debug.Log($"VERDICT: REJECT — the proposed move {candidate.ExpectedFrom}->{candidate.ExpectedTo} "
                    + "is not the best move by forced material.");
                return;
            }

            // Gate 2 — does the answer hold still across depths, or is it parity noise?
            var oracle = new ReferenceMoveOracle(TopTier);
            bool stable = oracle.IsStableAcrossDepths(board);
            ReferenceMove reference = oracle.Answer(board);
            Debug.Log($"reference at depth {oracle.ReferenceDepth}: "
                + $"{reference.Move.StartPosition}->{reference.Move.EndPosition} "
                + $"({reference.ScoreCp}cp, {reference.ElapsedMs:F0}ms), stable across depths: {stable}");

            if (!stable)
            {
                Debug.Log("VERDICT: REJECT — the best move changes with depth, so any agreement measured "
                    + "here would be ply parity rather than strength.");
                return;
            }

            // Gate 3 — can the real search, under its real clock, reach the depth that proves it,
            // and does it actually play the move?
            var asPosition = new YardstickPosition(candidate.Name, YardstickProofClass.QuietPositionalGain,
                candidate.Rationale, candidate.BuildBoard, candidate.ExpectedFrom, candidate.ExpectedTo,
                BetrayalStage.None, candidate.ProposedProvingDepth, 1);
            YardstickResult result = YardstickRunner.Run(asPosition, TopTier);

            Debug.Log($"top tier: solved={result.Solved}, depth reached {result.DepthReached} "
                + $"(proof needs {candidate.ProposedProvingDepth}), {result.ElapsedMs:F0}ms");

            if (result.DepthReached < candidate.ProposedProvingDepth)
            {
                Debug.Log("VERDICT: REJECT — the search cannot reach the depth this position's proof needs, "
                    + "so a failure here would measure speed rather than evaluation.");
                return;
            }

            Debug.Log($"VERDICT: ADMIT — proven best by {margin}cp, stable across depths, "
                + $"reachable at depth {result.DepthReached}. Currently solved: {result.Solved}.");
        }
    }
}
