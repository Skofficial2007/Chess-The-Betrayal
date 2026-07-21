using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Shape checks on YardstickSuite itself, distinct from YardstickPositionProofTests' deep
    /// per-position proofs — a cheap first line of defense that catches an obviously broken fixture
    /// (duplicate name, expected move not even in the legal move list) before the expensive proof
    /// runs.
    /// </summary>
    [TestFixture]
    public class YardstickSuiteTests
    {
        [Test]
        public void Suite_HasNoDuplicateNames()
        {
            var seen = new HashSet<string>();
            foreach (YardstickPosition position in YardstickSuite.All)
                Assert.That(seen.Add(position.Name), Is.True, $"duplicate yardstick position name: {position.Name}");
        }

        [Test]
        public void Suite_IsNotEmpty()
        {
            Assert.That(YardstickSuite.All, Is.Not.Empty);
        }

        [Test]
        public void EveryPosition_ExpectedMove_IsInTheLegalMoveList()
        {
            var engine = new ChessEngineAdapter();

            foreach (YardstickPosition position in YardstickSuite.All)
            {
                var board = position.BuildBoard();
                var legalMoves = new List<MoveCommand>(32);
                engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, legalMoves);

                bool found = false;
                foreach (MoveCommand move in legalMoves)
                {
                    if (position.Matches(move)) { found = true; break; }
                }

                Assert.That(found, Is.True,
                    $"{position.Name}: expected move {position.ExpectedMoveDescription} is not a legal move in this position.");
            }
        }

        [Test]
        public void EveryPosition_HasComputedZobristHash()
        {
            // Every position is built via TestBoardSetupUtility.WithComputedHash() — a missing hash
            // would desync search/TT lookups in a way that's silent until something else breaks.
            foreach (YardstickPosition position in YardstickSuite.All)
            {
                var board = position.BuildBoard();
                Assert.DoesNotThrow(() => board.AssertZobristConsistency(),
                    $"{position.Name}: board's Zobrist hash doesn't match its own piece layout.");
            }
        }
    }
}
