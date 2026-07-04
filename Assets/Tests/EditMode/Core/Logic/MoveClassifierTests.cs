using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Logic;

namespace ChessTheBetrayal.Tests.EditMode.Core.Logic
{
    /// <summary>
    /// Validates MoveClassifier.ClassifyMove in isolation. This is the intent gate that
    /// LocalMoveExecutor consults before running any Betrayal validation, so a King moving
    /// onto a friendly square must resolve to Illegal rather than BetrayalAct.
    /// </summary>
    [TestFixture]
    public class MoveClassifierTests
    {
        private static PieceData Piece(Team team, ChessPieceType type) =>
            new PieceData(team, type, moveDirection: team == Team.White ? 1 : -1, startRow: 0);

        [Test]
        public void ClassifyMove_EmptyDestination_ReturnsNormalMove()
        {
            var piece = Piece(Team.White, ChessPieceType.Queen);

            var result = MoveClassifier.ClassifyMove(piece, PieceData.Empty, betrayalRightAvailable: true);

            Assert.That(result, Is.EqualTo(MoveIntent.NormalMove));
        }

        [Test]
        public void ClassifyMove_EnemyDestination_ReturnsCapture()
        {
            var piece = Piece(Team.White, ChessPieceType.Queen);
            var target = Piece(Team.Black, ChessPieceType.Pawn);

            var result = MoveClassifier.ClassifyMove(piece, target, betrayalRightAvailable: true);

            Assert.That(result, Is.EqualTo(MoveIntent.Capture));
        }

        [Test]
        public void ClassifyMove_KingOntoFriendlyNonRook_ReturnsIllegal_DoesNotThrow()
        {
            var king = Piece(Team.White, ChessPieceType.King);
            var friendlyQueen = Piece(Team.White, ChessPieceType.Queen);

            MoveIntent result = MoveIntent.NormalMove;
            Assert.DoesNotThrow(() =>
            {
                result = MoveClassifier.ClassifyMove(king, friendlyQueen, betrayalRightAvailable: true);
            });

            Assert.That(result, Is.EqualTo(MoveIntent.Illegal));
        }

        [Test]
        public void ClassifyMove_KingOntoFriendlyRook_ReturnsCastling()
        {
            var king = Piece(Team.White, ChessPieceType.King);
            var friendlyRook = Piece(Team.White, ChessPieceType.Rook);

            var result = MoveClassifier.ClassifyMove(king, friendlyRook, betrayalRightAvailable: true);

            Assert.That(result, Is.EqualTo(MoveIntent.Castling));
        }

        [Test]
        public void ClassifyMove_QueenOntoFriendlyWithRight_ReturnsBetrayalAct()
        {
            var queen = Piece(Team.White, ChessPieceType.Queen);
            var friendlyRook = Piece(Team.White, ChessPieceType.Rook);

            var result = MoveClassifier.ClassifyMove(queen, friendlyRook, betrayalRightAvailable: true);

            Assert.That(result, Is.EqualTo(MoveIntent.BetrayalAct));
        }

        [Test]
        public void ClassifyMove_QueenOntoFriendlyWithoutRight_ReturnsIllegal()
        {
            var queen = Piece(Team.White, ChessPieceType.Queen);
            var friendlyRook = Piece(Team.White, ChessPieceType.Rook);

            var result = MoveClassifier.ClassifyMove(queen, friendlyRook, betrayalRightAvailable: false);

            Assert.That(result, Is.EqualTo(MoveIntent.Illegal));
        }

        [Test]
        public void ClassifyMove_AnyPieceOntoFriendlyKing_ReturnsIllegal()
        {
            var queen = Piece(Team.White, ChessPieceType.Queen);
            var friendlyKing = Piece(Team.White, ChessPieceType.King);

            var result = MoveClassifier.ClassifyMove(queen, friendlyKing, betrayalRightAvailable: true);

            Assert.That(result, Is.EqualTo(MoveIntent.Illegal));
        }
    }
}
