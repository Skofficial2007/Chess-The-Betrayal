using NUnit.Framework;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine
{
    /// <summary>
    /// Perft (performance test) suite for validating move generation completeness.
    /// Perft recursively counts all legal move paths to a given depth. The counts for
    /// the standard opening position are well-known constants verified across chess engines.
    /// Any deviation from expected values indicates a bug in move generation, legality
    /// filtering, or special move handling (castling, en passant, promotion).
    /// </summary>
    [TestFixture]
    public class PerftTests
    {
        /// <summary>
        /// Recursively counts all legal move paths (leaf nodes) to a specified depth.
        /// This is the standard Perft algorithm used to validate move generation correctness.
        /// </summary>
        private ulong Perft(BoardState board, int depth)
        {
            if (depth == 0)
            {
                return 1;
            }

            List<MoveCommand> moves = new List<MoveCommand>();
            ChessEngine.GetAllLegalMoves(board, board.CurrentTurn, moves);

            ulong nodes = 0;
            Team currentTurn = board.CurrentTurn;

            foreach (var move in moves)
            {
                ChessEngine.ApplyMoveToBoard(board, move, recordHistory: false);
                board.CurrentTurn = currentTurn == Team.White ? Team.Black : Team.White;

                nodes += Perft(board, depth - 1);

                board.CurrentTurn = currentTurn;
                ChessEngine.UndoMoveOnBoard(board, move, recordHistory: false);
            }

            return nodes;
        }

        [Test]
        public void Perft_StandardPosition_Depth1_Returns20Moves()
        {
            // Arrange: Standard chess opening position
            BoardState board = TestBoardSetupUtility.CreateStandard();

            // Act: Count all legal moves at depth 1
            ulong nodeCount = Perft(board, 1);

            // Assert: Standard position has exactly 20 legal moves
            // (16 pawn single-pushes + 4 knight moves)
            Assert.That(nodeCount, Is.EqualTo(20UL),
                "Standard opening position must have exactly 20 legal first moves. " +
                "A deviation indicates a fundamental move generation bug.");
        }

        [Test]
        public void Perft_StandardPosition_Depth2_Returns400Nodes()
        {
            // Arrange: Standard chess opening position
            BoardState board = TestBoardSetupUtility.CreateStandard();

            // Act: Count all move paths at depth 2
            ulong nodeCount = Perft(board, 2);

            // Assert: Expected node count for depth 2 is 400
            Assert.That(nodeCount, Is.EqualTo(400UL),
                "Depth 2 perft from standard position must return 400 nodes. " +
                "A deviation at depth 2 (but not depth 1) typically indicates a bug in piece-specific " +
                "move generation that only manifests after the board state changes.");
        }

        [Test]
        public void Perft_StandardPosition_Depth3_Returns8902Nodes()
        {
            // Arrange: Standard chess opening position
            BoardState board = TestBoardSetupUtility.CreateStandard();

            // Act: Count all move paths at depth 3
            ulong nodeCount = Perft(board, 3);

            // Assert: Expected node count for depth 3 is 8,902
            Assert.That(nodeCount, Is.EqualTo(8902UL),
                "Depth 3 perft from standard position must return 8,902 nodes. " +
                "This is the minimum depth to catch most move generation bugs including " +
                "pin detection failures, en passant legality issues, and castling-through-check problems.");
        }
    }
}
