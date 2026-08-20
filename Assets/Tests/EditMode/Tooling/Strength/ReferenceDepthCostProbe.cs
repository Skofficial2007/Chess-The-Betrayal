using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tooling;

namespace ChessTheBetrayal.Tests.EditMode.Tooling.Strength
{
    /// <summary>
    /// Measures what a reference search costs at each candidate depth, so the choice of reference
    /// depth is made from real numbers rather than a guess. Explicit: it is a measurement, not an
    /// assertion about correctness, and it deliberately runs far past any per-commit budget.
    /// </summary>
    [TestFixture]
    [Explicit("Measurement only — run by hand when choosing the reference depth.")]
    public class ReferenceDepthCostProbe
    {
        private static AIProfile TopTier => AIProfileTable.BuiltIn.Single(p => p.Id == "impossible");

        [TestCase(8)]
        [TestCase(9)]
        [TestCase(10)]
        [TestCase(11)]
        [TestCase(12)]
        public void MeasureReferenceCost(int depth)
        {
            var oracle = new ReferenceMoveOracle(TopTier, depth);
            double totalMs = 0;

            for (int index = 0; index < 3; index++)
            {
                BoardState board = CuratedPositionSuite.Build(index);
                ReferenceMove answer = oracle.Answer(board);
                totalMs += answer.ElapsedMs;
                Debug.Log($"depth {depth} position {index}: {answer.ElapsedMs:F0}ms " +
                    $"move {answer.Move.StartPosition}->{answer.Move.EndPosition} score {answer.ScoreCp}cp");
            }

            Debug.Log($"DEPTH {depth} TOTAL over 3 positions: {totalMs:F0}ms (mean {totalMs / 3:F0}ms)");
        }
    }
}
