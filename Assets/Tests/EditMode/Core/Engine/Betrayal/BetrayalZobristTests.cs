using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine.Betrayal
{
    [TestFixture]
    public class BetrayalZobristTests
    {
        [Test]
        public void ZobristHash_BetrayalRightConsumed_AltersHash()
        {
            // Arrange
            BoardState boardWithRight = TestBoardSetupUtility.CreateStandard().WithBetrayalRight(true);
            BoardState boardWithoutRight = TestBoardSetupUtility.CreateStandard().WithBetrayalRight(false);

            // Act
            boardWithRight.ComputeFullZobristHash();
            boardWithoutRight.ComputeFullZobristHash();

            // Assert
            Assert.That(boardWithRight.ZobristHash, Is.Not.EqualTo(boardWithoutRight.ZobristHash),
                "Consuming the right must alter the hash to prevent transposition table errors.");
        }

        [Test]
        public void ZobristHash_ActMoveThenFullUndo_RestoresOriginalHash()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Rook)
                .WithPiece("e5", Team.White, ChessPieceType.Pawn)
                .WithBetrayalRight(true);

            board.ComputeFullZobristHash();
            ulong originalHash = board.ZobristHash;

            MoveCommand actMove = MoveCommand.CreateStandardMove(
                TestBoardSetupUtility.AlgebraicToVector("e4"),
                TestBoardSetupUtility.AlgebraicToVector("e5"),
                board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e4")),
                board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e5")),
                board).WithStage(BetrayalStage.Act);

            // Act
            ChessEngine.ApplyMoveToBoard(board, actMove, false);
            ChessEngine.UndoMoveOnBoard(board, actMove, false);

            // Assert
            Assert.That(board.ZobristHash, Is.EqualTo(originalHash));
            Assert.DoesNotThrow(() => board.AssertZobristConsistency());
        }

        [Test]
        public void ZobristHash_ResolveFailedRetributionThenUndoMoveOnBoard_RestoresOriginalHash()
        {
            // Arrange: drives Defection through the same ApplyMoveToBoard/UndoMoveOnBoard seam
            // an AI search uses, rather than the raw BoardState.DefectPiece primitive directly.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Knight)
                .WithPendingBetrayer("e4", Team.White);

            board.ComputeFullZobristHash();
            ulong originalHash = board.ZobristHash;

            // Act
            DefectionOutcome outcome = ChessEngine.ResolveFailedRetribution(board);
            ChessEngine.UndoMoveOnBoard(board, outcome.DefectionMove, recordHistory: false);

            // Assert
            Assert.That(board.ZobristHash, Is.EqualTo(originalHash));
            Assert.DoesNotThrow(() => board.AssertZobristConsistency());
        }

        [Test]
        public void ZobristHash_DifferentPendingBetrayerSquare_ProducesDifferentHash()
        {
            // Two positions, identical piece placement, differing only in which square holds
            // the pending Betrayer. Without hashing PendingBetrayerSquare/BetrayalInitiator,
            // these would collide and poison the transposition table during the exact
            // high-branching Act/Retribution sub-phase where collisions are most costly.
            BoardState boardBetrayerAtE4 = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Knight)
                .WithPiece("d4", Team.White, ChessPieceType.Pawn)
                .WithBetrayalRight(false)
                .WithPendingBetrayer("e4", Team.White);

            BoardState boardBetrayerAtD4 = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Knight)
                .WithPiece("d4", Team.White, ChessPieceType.Pawn)
                .WithBetrayalRight(false)
                .WithPendingBetrayer("d4", Team.White);

            boardBetrayerAtE4.ComputeFullZobristHash();
            boardBetrayerAtD4.ComputeFullZobristHash();

            Assert.That(boardBetrayerAtE4.ZobristHash, Is.Not.EqualTo(boardBetrayerAtD4.ZobristHash),
                "Identical piece placement with a different pending Betrayer square must hash differently.");
        }

        [Test]
        public void ZobristHash_DifferentBetrayalInitiator_ProducesDifferentHash()
        {
            // Same pending square, differing only in which side initiated — must also
            // produce distinct hashes, since GetRetributionMoves/GetForcedSaveMoves branch on it.
            BoardState boardWhiteInitiator = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Knight)
                .WithBetrayalRight(false)
                .WithPendingBetrayer("e4", Team.White);

            BoardState boardBlackInitiator = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Knight)
                .WithBetrayalRight(false)
                .WithPendingBetrayer("e4", Team.Black);

            boardWhiteInitiator.ComputeFullZobristHash();
            boardBlackInitiator.ComputeFullZobristHash();

            Assert.That(boardWhiteInitiator.ZobristHash, Is.Not.EqualTo(boardBlackInitiator.ZobristHash),
                "Identical pending square with a different Betrayal initiator must hash differently.");
        }

        [Test]
        public void ZobristHash_ActMoveTogglesSubState_UndoRestoresExactly()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("a3", Team.White, ChessPieceType.Pawn)
                .WithBetrayalRight(true);

            board.ComputeFullZobristHash();
            ulong originalHash = board.ZobristHash;

            MoveCommand actMove = MoveCommand.CreateStandardMove(
                TestBoardSetupUtility.AlgebraicToVector("b1"),
                TestBoardSetupUtility.AlgebraicToVector("a3"),
                board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("b1")),
                board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("a3")),
                board).WithStage(BetrayalStage.Act);

            // Act
            ChessEngine.ApplyMoveToBoard(board, actMove, false);
            ulong hashAfterAct = board.ZobristHash;
            Assert.DoesNotThrow(() => board.AssertZobristConsistency(), "Incremental hash must match a full recompute right after Act.");

            ChessEngine.UndoMoveOnBoard(board, actMove, false);

            // Assert
            Assert.That(hashAfterAct, Is.Not.EqualTo(originalHash), "Act must alter the hash via the new pending-betrayer sub-state keys.");
            Assert.That(board.ZobristHash, Is.EqualTo(originalHash), "Undo must restore the exact original hash, including the sub-state keys.");
            Assert.DoesNotThrow(() => board.AssertZobristConsistency());
        }
    }
}