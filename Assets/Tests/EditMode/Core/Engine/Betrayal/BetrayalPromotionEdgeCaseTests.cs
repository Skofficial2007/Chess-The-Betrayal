using NUnit.Framework;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine.Betrayal
{
    /// <summary>
    /// Validates edge cases surrounding Pawn Promotion during Betrayal sequences.
    /// Ensures that the "Act" phase suppresses promotion rewards for traitors,
    /// while the "Retribution" phase properly grants them to the executioner.
    /// </summary>
    [TestFixture]
    public class BetrayalPromotionEdgeCaseTests
    {
        private List<MoveCommand> _moveBuffer;

        [SetUp]
        public void Setup()
        {
            _moveBuffer = new List<MoveCommand>();
        }

        [Test]
        public void RetributionPhase_PawnExecutesOnBackRank_GeneratesAllFourPromotionChoices()
        {
            // Arrange: White Pawn on c7. White Knight (Betrayer) on b8.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("c7", Team.White, ChessPieceType.Pawn)
                .WithPiece("b8", Team.White, ChessPieceType.Knight) // Betrayer
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPendingBetrayer("b8", Team.White)
                .WithTurn(Team.White);

            // Act
            ChessEngine.GetRetributionMoves(board, Team.White, TestBoardSetupUtility.AlgebraicToVector("b8"), _moveBuffer);

            // Assert: Standard pawn logic must spawn exactly 4 promotion variants
            Assert.That(_moveBuffer.Count, Is.EqualTo(4), "A pawn executing a betrayer on the back rank must generate exactly 4 promotion variants.");

            bool hasQueen = false, hasRook = false, hasBishop = false, hasKnight = false;

            foreach (var move in _moveBuffer)
            {
                Assert.That(move.Stage, Is.EqualTo(BetrayalStage.Retribution), "Move must be tagged as Retribution.");
                Assert.That(move.IsPromotion, Is.True, "Move must be flagged as a promotion.");

                if (move.PromotedTo == ChessPieceType.Queen) hasQueen = true;
                if (move.PromotedTo == ChessPieceType.Rook) hasRook = true;
                if (move.PromotedTo == ChessPieceType.Bishop) hasBishop = true;
                if (move.PromotedTo == ChessPieceType.Knight) hasKnight = true;
            }

            Assert.That(hasQueen && hasRook && hasBishop && hasKnight, Is.True, "All four promotion piece types must be represented in the buffer.");
        }

        [Test]
        public void RetributionPhase_PromotionExecution_MaintainsZobristConsistency()
        {
            // Arrange: White pawn prepares to execute the Betrayer and promote to Queen.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("c7", Team.White, ChessPieceType.Pawn)
                .WithPiece("b8", Team.White, ChessPieceType.Knight) // Betrayer
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPendingBetrayer("b8", Team.White)
                .WithTurn(Team.White)
                .WithComputedHash();

            Vector2Int from = TestBoardSetupUtility.AlgebraicToVector("c7");
            Vector2Int to = TestBoardSetupUtility.AlgebraicToVector("b8");
            PieceData pawn = board.GetPiece(from);
            PieceData betrayer = board.GetPiece(to);

            // Manually construct the promotion retribution move
            MoveCommand promoMove = new MoveCommand(
                from, to, pawn, betrayer,
                SpecialMove.None, ChessPieceType.Queen,
                null, null, null,
                board.CastlingRights, board.EnPassantFile,
                long.MaxValue, long.MaxValue,
                BetrayalStage.Retribution
            );

            // Act
            ChessEngine.ApplyMoveToBoard(board, promoMove);
            board.NextTurn(); // FIX: Sync the CurrentTurn to Black to align with the incremental Zobrist hash toggle!

            // Assert
            PieceData promotedPiece = board.GetPiece(to);
            Assert.That(promotedPiece.Type, Is.EqualTo(ChessPieceType.Queen), "The pawn must successfully transform into a Queen.");
            Assert.That(promotedPiece.Team, Is.EqualTo(Team.White), "The promoted piece must remain White.");
            Assert.DoesNotThrow(() => board.AssertZobristConsistency(), "Complex capture-promotion XOR hash updates must remain perfectly synced.");
        }

        [Test]
        public void ActPhase_PawnOnPenultimateRank_SuppressesPromotionForAllTargetTypes()
        {
            // Tests that a pawn capturing a friendly piece on the back rank NEVER triggers promotion variants,
            // regardless of which piece type it is capturing.
            ChessPieceType[] targetTypes = { ChessPieceType.Queen, ChessPieceType.Rook, ChessPieceType.Bishop, ChessPieceType.Knight };

            foreach (var targetType in targetTypes)
            {
                _moveBuffer.Clear();
                BoardState board = TestBoardSetupUtility.CreateEmpty()
                    .WithPiece("a1", Team.White, ChessPieceType.King)
                    .WithPiece("b7", Team.White, ChessPieceType.Pawn)
                    .WithPiece("c8", Team.White, targetType) // Iterate through all target types
                    .WithTurn(Team.White)
                    .WithBetrayalRight(true);

                ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("b7"), _moveBuffer);

                Assert.That(_moveBuffer.Count, Is.EqualTo(1), $"Pawn targeting {targetType} should generate exactly 1 Act move.");

                MoveCommand actMove = _moveBuffer[0];
                Assert.That(actMove.Stage, Is.EqualTo(BetrayalStage.Act), "Move must be tagged as Act.");
                Assert.That(actMove.IsPromotion, Is.False, $"Promotion must be suppressed when betraying a {targetType}.");
                Assert.That(actMove.PromotedTo, Is.EqualTo(ChessPieceType.None));
            }
        }
    }
}