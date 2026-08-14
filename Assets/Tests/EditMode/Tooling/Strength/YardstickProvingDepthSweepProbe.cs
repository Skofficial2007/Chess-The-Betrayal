using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Tooling.Strength
{
    /// <summary>
    /// Asks whether a quiet move can be proven at all by searching deeper, and if so whether the
    /// depth it needs is one the real search ever reaches.
    ///
    /// A material-only proof can only separate two moves once a line forces material to change
    /// inside the proving depth. A positional move's payoff is by definition further away than
    /// that, so at a shallow depth every reasonable move ties and the proof cannot pick one. Pushing
    /// the depth out until the payoff lands inside it is the obvious answer, and it runs straight
    /// into the opposite constraint: a position needing more plies than the AI actually searches
    /// would measure the search's speed rather than its judgement.
    ///
    /// This probe reports both halves of that trade for one position at a time — the depth at which
    /// the proof first separates the moves, and the depth the real search reaches — so the question
    /// is settled with numbers instead of argument. Explicit: a single sweep runs a full search per
    /// legal move per depth and costs minutes.
    /// </summary>
    [TestFixture]
    [Explicit("Measurement only — run by hand when deciding whether a candidate is provable.")]
    public class YardstickProvingDepthSweepProbe
    {
        private static AIProfile TopTier => AIProfileTable.BuiltIn.Single(p => p.Id == "impossible");

        /// <summary>The depths a sweep walks. Starts where the ordinary screening probe gives up and
        /// stops well past anything the real search reaches, so a position that never separates is
        /// visibly a lost cause rather than merely untested.</summary>
        private static readonly int[] SweepDepths = { 6, 8, 10, 12, 14 };

        private static IEnumerable<TestCaseData> Candidates()
        {
            for (int i = 0; i < YardstickCandidateSuite.All.Count; i++)
                yield return new TestCaseData(i).SetName($"Sweep_{YardstickCandidateSuite.All[i].Name}");
        }

        [TestCaseSource(nameof(Candidates))]
        public void SweepProvingDepth(int index)
        {
            YardstickCandidateSuite.Candidate candidate = YardstickCandidateSuite.All[index];

            Debug.Log($"=== {candidate.Name} ===");
            Debug.Log($"proposed move: {candidate.ExpectedFrom}->{candidate.ExpectedTo}");

            int firstSeparatingDepth = -1;

            foreach (int depth in SweepDepths)
            {
                BoardState board = candidate.BuildBoard();
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                List<ProvenMoveScore> scored = QuietMoveProver.ScoreAllMoves(board, depth);
                stopwatch.Stop();

                if (scored.Count == 0)
                {
                    Debug.Log($"depth {depth}: no legal moves");
                    continue;
                }

                ProvenMoveScore best = scored[0];
                bool proposedIsBest = best.Move.StartPosition == candidate.ExpectedFrom
                    && best.Move.EndPosition == candidate.ExpectedTo;
                int margin = scored.Count > 1 ? best.ScoreCp - scored[1].ScoreCp : int.MaxValue;

                Debug.Log($"depth {depth}: best {best}, margin {(margin == int.MaxValue ? "n/a" : margin + "cp")}, "
                    + $"proposed-is-best={proposedIsBest}, {stopwatch.Elapsed.TotalSeconds:F1}s");

                if (proposedIsBest && margin > 0 && margin != int.MaxValue && firstSeparatingDepth < 0)
                    firstSeparatingDepth = depth;
            }

            if (firstSeparatingDepth < 0)
            {
                Debug.Log("SWEEP RESULT: never separates — the proposed move is not provably best at any "
                    + "depth tried, so no proving depth can rescue this position.");
                return;
            }

            // The other half of the trade: what the real search, under its real clock, actually
            // reaches on this position.
            var asPosition = new YardstickPosition(candidate.Name, YardstickProofClass.QuietPositionalGain,
                candidate.Rationale, candidate.BuildBoard, candidate.ExpectedFrom, candidate.ExpectedTo,
                BetrayalStage.None, firstSeparatingDepth, 1);
            YardstickResult result = YardstickRunner.Run(asPosition, TopTier);

            Debug.Log($"SWEEP RESULT: separates first at depth {firstSeparatingDepth}; "
                + $"top tier reaches depth {result.DepthReached} in {result.ElapsedMs:F0}ms, solved={result.Solved}. "
                + (result.DepthReached >= firstSeparatingDepth
                    ? "USABLE — provable inside the depth the search reaches."
                    : "UNUSABLE — needs more plies than the search gets, so it would measure speed."));
        }
    }
}
