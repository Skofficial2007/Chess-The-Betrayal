using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine.Betrayal
{
    [TestFixture]
    public class BetrayalDefectionTests
    {
        [Test]
        public void DefectPiece_FlipsTeamInPlace_PositionUnchanged()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e4", Team.White, ChessPieceType.Knight);

            // Act
            board.DefectPiece(TestBoardSetupUtility.AlgebraicToVector("e4"));

            // Assert
            PieceData piece = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e4"));
            Assert.That(piece.Team, Is.EqualTo(Team.Black));
            Assert.That(piece.Type, Is.EqualTo(ChessPieceType.Knight));
        }

        [Test]
        public void DefectPiece_UpdatesPieceIndices_OldTeamListShrinks_NewTeamListGrows()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e4", Team.White, ChessPieceType.Knight);

            // Act
            board.DefectPiece(TestBoardSetupUtility.AlgebraicToVector("e4"));

            // Assert
            Assert.That(board.GetPieceIndices(Team.White).Count, Is.EqualTo(0));
            Assert.That(board.GetPieceIndices(Team.Black).Count, Is.EqualTo(1));
        }

        [Test]
        public void ResolveFailedRetribution_ThenUndoMoveOnBoard_RestoresExactOriginalState()
        {
            // Arrange: a pending Betrayer with no legal executioner, so ResolveFailedRetribution
            // produces a real Defection MoveCommand — the only production-correct way to make and
            // then unmake a Defection is ApplyMoveToBoard/UndoMoveOnBoard driven by that move.
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
            Assert.That(board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e4")).Team, Is.EqualTo(Team.White));
            Assert.That(board.GetPieceIndices(Team.White).Count, Is.EqualTo(2)); // King + Knight
            Assert.That(board.ZobristHash, Is.EqualTo(originalHash), "UndoMoveOnBoard must restore exact Zobrist state after a Defection.");
        }

        [Test]
        public void ResolveFailedRetribution_DefectionDoesNotCauseSelfCheck_RequiresForcedSaveIsFalse()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("h8", Team.White, ChessPieceType.Knight) // Betrayer, defecting far from King
                .WithPendingBetrayer("h8", Team.White);

            // Act
            DefectionOutcome outcome = ChessEngine.ResolveFailedRetribution(board);

            // Assert
            Assert.That(outcome.RequiresForcedSave, Is.False);
            Assert.That(outcome.DefectedSquare, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("h8")));
        }

        [Test]
        public void ResolveFailedRetribution_DefectionCausesSelfCheck_RequiresForcedSaveIsTrue()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Rook) // Betrayer. Once it defects to Black, it checks e1.
                .WithPendingBetrayer("e4", Team.White);

            // Act
            DefectionOutcome outcome = ChessEngine.ResolveFailedRetribution(board);

            // Assert
            Assert.That(outcome.RequiresForcedSave, Is.True, "Defecting piece opened an attack line on its former King.");
        }
    }
}