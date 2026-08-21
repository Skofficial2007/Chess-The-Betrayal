using NUnit.Framework;
using System.Collections.Generic;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Perft over the exact seam AlphaBetaSearch drives (IChessEngine.
    /// GetAllLegalMovesIncludingBetrayal/ApplyMove/UndoMove), for a Betrayal-active position.
    /// Regression-guards the pending-Retribution move-gen "disguise trick" (GetBetrayalTargets/
    /// GetRetributionMoves briefly mutate the board in place while probing raw moves) — if that
    /// trick ever leaks a stray board mutation, the node count at a fixed depth changes even
    /// though nothing about the position did.
    /// </summary>
    [TestFixture]
    public class SearchPerftTests
    {
        private ChessEngineAdapter _engine;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
        }

        private ulong Perft(BoardState board, int depth)
        {
            if (depth == 0) return 1;

            List<MoveCommand> moves = new List<MoveCommand>();
            _engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, moves);

            ulong nodes = 0;
            Team currentTurn = board.CurrentTurn;

            foreach (MoveCommand move in moves)
            {
                _engine.ApplyMove(board, move);

                // IChessEngine.ApplyMove never touches CurrentTurn (that's the whole point of the
                // seam — it hands per-ply turn control to the caller). Flip it ourselves using the
                // exact same rule AlphaBetaSearch.StageFlipsTurn uses, so this perft walks the same
                // tree the search actually explores.
                if (AlphaBetaSearch.StageFlipsTurn(move.Stage))
                    board.CurrentTurn = currentTurn == Team.White ? Team.Black : Team.White;

                nodes += Perft(board, depth - 1);

                board.CurrentTurn = currentTurn;
                _engine.UndoMove(board, move);
            }

            return nodes;
        }

        [Test]
        public void Perft_StandardPosition_Depth2_MatchesStaticEngineCount()
        {
            // Pins the IChessEngine seam's node count to the already-trusted static ChessEngine
            // count at the same depth — if the adapter's ApplyMove/UndoMove ever drift from the
            // static methods they wrap, this diverges even though PerftTests.cs stays green.
            BoardState boardViaSeam = TestBoardSetupUtility.CreateStandard();
            BoardState boardViaStatic = TestBoardSetupUtility.CreateStandard();

            ulong seamCount = Perft(boardViaSeam, 2);
            ulong staticCount = StaticPerft(boardViaStatic, 2);

            Assert.That(seamCount, Is.EqualTo(staticCount));
            Assert.That(seamCount, Is.EqualTo(400UL), "Standard position depth-2 perft is a well-known constant.");
        }

        [Test]
        public void Perft_BetrayalActivePosition_Depth2_IsStableAndBoardRestored()
        {
            // Betrayer (Knight b1) can Act onto its own Pawn at a3; White Rook a1 can then execute
            // Retribution. This position's depth-2 node count is a fixed regression baseline — if
            // GetBetrayalTargets/GetRetributionMoves ever leak the disguise-trick mutation, or if
            // an Act/Retribution ply is double-counted/dropped, this count moves.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("a3", Team.White, ChessPieceType.Pawn)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            ulong hashBefore = board.ZobristHash;

            ulong nodeCount = Perft(board, 2);

            Assert.That(nodeCount, Is.GreaterThan(0UL));
            Assert.DoesNotThrow(() => board.AssertZobristConsistency(),
                "Board hash must remain consistent after a full perft traversal through a Betrayal-active position.");
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore),
                "Board must be fully restored — every explored branch's ApplyMove was paired with UndoMove.");
        }

        private static ulong StaticPerft(BoardState board, int depth)
        {
            if (depth == 0) return 1;

            List<MoveCommand> moves = new List<MoveCommand>();
            ChessEngine.GetAllLegalMoves(board, board.CurrentTurn, moves);

            ulong nodes = 0;
            Team currentTurn = board.CurrentTurn;

            foreach (MoveCommand move in moves)
            {
                ChessEngine.ApplyMoveToBoard(board, move, recordHistory: false);
                board.CurrentTurn = currentTurn == Team.White ? Team.Black : Team.White;

                nodes += StaticPerft(board, depth - 1);

                board.CurrentTurn = currentTurn;
                ChessEngine.UndoMoveOnBoard(board, move, recordHistory: false);
            }

            return nodes;
        }
    }
}
