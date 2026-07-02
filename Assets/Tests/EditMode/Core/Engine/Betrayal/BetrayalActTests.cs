using NUnit.Framework;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine.Betrayal
{
    /// <summary>
    /// Validates the move generation and rule enforcement for the initiation phase (The Act) 
    /// of the Betrayal mechanic. Ensures that friendly-fire targeting strictly adheres to 
    /// standard chess geometry, line-of-sight rules, and global limits.
    /// </summary>
    [TestFixture]
    public class BetrayalActTests
    {
        private List<MoveCommand> _moveBuffer;

        [SetUp]
        public void Setup()
        {
            _moveBuffer = new List<MoveCommand>();
        }

        private bool BufferContainsDestination(string algebraicCoordinate)
        {
            Vector2Int target = TestBoardSetupUtility.AlgebraicToVector(algebraicCoordinate);
            for (int i = 0; i < _moveBuffer.Count; i++)
            {
                if (_moveBuffer[i].EndPosition == target) return true;
            }
            return false;
        }

        #region Pawn Geometry

        [Test]
        public void GetBetrayalTargets_PawnStraightPush_ReturnsEmpty()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King) // FIX: King required for check validation
                .WithPiece("e4", Team.White, ChessPieceType.Pawn)
                .WithPiece("e5", Team.White, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithBetrayalRight(true);

            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("e4"), _moveBuffer);

            Assert.That(_moveBuffer.Count, Is.EqualTo(0), "A straight pawn push is a movement-only action, never a capture pattern.");
        }

        [Test]
        public void GetBetrayalTargets_PawnDiagonalCapture_IdentifiesFriendlyTargets()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King) // FIX: King required
                .WithPiece("e4", Team.White, ChessPieceType.Pawn)
                .WithPiece("d5", Team.White, ChessPieceType.Rook)
                .WithPiece("f5", Team.White, ChessPieceType.Knight)
                .WithTurn(Team.White)
                .WithBetrayalRight(true);

            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("e4"), _moveBuffer);

            Assert.That(_moveBuffer.Count, Is.EqualTo(2), "Pawn must target friendly pieces on diagonal capture squares.");
            Assert.That(BufferContainsDestination("d5"), Is.True);
            Assert.That(BufferContainsDestination("f5"), Is.True);
        }

        [Test]
        public void GetBetrayalTargets_BlackPawn_RespectsForwardDirection()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e8", Team.Black, ChessPieceType.King) // FIX: Black King required
                .WithPiece("e5", Team.Black, ChessPieceType.Pawn)
                .WithPiece("e4", Team.Black, ChessPieceType.Pawn)
                .WithPiece("d4", Team.Black, ChessPieceType.Rook)
                .WithPiece("f4", Team.Black, ChessPieceType.Knight)
                .WithTurn(Team.Black)
                .WithBetrayalRight(true);

            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("e5"), _moveBuffer);

            Assert.That(_moveBuffer.Count, Is.EqualTo(2), "Black pawn must only target diagonal squares in its forward (-1 y) direction.");
            Assert.That(BufferContainsDestination("d4"), Is.True);
            Assert.That(BufferContainsDestination("f4"), Is.True);
        }

        [Test]
        public void GetBetrayalTargets_PawnDoublePush_ReturnsEmpty()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King) // FIX: King required
                .WithPiece("e2", Team.White, ChessPieceType.Pawn, hasMoved: false)
                .WithPiece("e4", Team.White, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithBetrayalRight(true);

            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("e2"), _moveBuffer);

            Assert.That(_moveBuffer.Count, Is.EqualTo(0), "A pawn's initial double push is not a valid capture geometry.");
        }

        [Test]
        public void GetBetrayalTargets_EnPassant_FriendlyPawnIgnored()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King) // FIX: King required
                .WithPiece("d5", Team.White, ChessPieceType.Pawn)
                .WithPiece("e5", Team.White, ChessPieceType.Pawn)
                .WithEnPassantFile(4)
                .WithTurn(Team.White)
                .WithBetrayalRight(true);

            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("d5"), _moveBuffer);

            Assert.That(_moveBuffer.Count, Is.EqualTo(0), "En passant logic requires an enemy pawn; friendlies cannot be targeted via en passant.");
        }

        [Test]
        public void GetBetrayalTargets_PawnOnPenultimateRank_GeneratesStandardMoveWithoutPromotion()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King) // FIX: King required
                .WithPiece("b7", Team.White, ChessPieceType.Pawn)
                .WithPiece("c8", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithBetrayalRight(true);

            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("b7"), _moveBuffer);

            Assert.That(_moveBuffer.Count, Is.EqualTo(1), "Pawn should target the piece without duplicating for promotion variants.");

            MoveCommand actMove = _moveBuffer[0];
            Assert.That(actMove.Stage, Is.EqualTo(BetrayalStage.Act), "The move must be tagged strictly as an Act.");
            Assert.That(actMove.IsPromotion, Is.False, "Promotion is suppressed when initiating a betrayal on the back rank.");
            Assert.That(actMove.PromotedTo, Is.EqualTo(ChessPieceType.None));
        }

        #endregion

        #region Knight Geometry

        [Test]
        public void GetBetrayalTargets_Knight_TargetsAllValidOffsetsAndIgnoresBlockers()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King) // FIX: King required
                .WithPiece("d4", Team.White, ChessPieceType.Knight)
                .WithPiece("d5", Team.White, ChessPieceType.Pawn).WithPiece("d3", Team.White, ChessPieceType.Pawn)
                .WithPiece("c4", Team.White, ChessPieceType.Pawn).WithPiece("e4", Team.White, ChessPieceType.Pawn)
                .WithPiece("c5", Team.White, ChessPieceType.Pawn).WithPiece("e5", Team.White, ChessPieceType.Pawn)
                .WithPiece("c3", Team.White, ChessPieceType.Pawn).WithPiece("e3", Team.White, ChessPieceType.Pawn)
                .WithPiece("c6", Team.White, ChessPieceType.Rook).WithPiece("e6", Team.White, ChessPieceType.Rook)
                .WithPiece("b5", Team.White, ChessPieceType.Rook).WithPiece("f5", Team.White, ChessPieceType.Rook)
                .WithPiece("b3", Team.White, ChessPieceType.Rook).WithPiece("f3", Team.White, ChessPieceType.Rook)
                .WithPiece("c2", Team.White, ChessPieceType.Rook).WithPiece("e2", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithBetrayalRight(true);

            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("d4"), _moveBuffer);

            Assert.That(_moveBuffer.Count, Is.EqualTo(8), "Knight should target all 8 friendlies, jumping over adjacent blockers.");
        }

        #endregion

        #region Sliding Pieces Geometry (Bishop, Rook, Queen)

        [Test]
        public void GetBetrayalTargets_Bishop_LineOfSightStopsAtFirstPiece()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King) // FIX: King required
                .WithPiece("c1", Team.White, ChessPieceType.Bishop)
                .WithPiece("d2", Team.White, ChessPieceType.Pawn)
                .WithPiece("e3", Team.White, ChessPieceType.Knight)
                .WithTurn(Team.White)
                .WithBetrayalRight(true);

            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("c1"), _moveBuffer);

            Assert.That(_moveBuffer.Count, Is.EqualTo(1));
            Assert.That(_moveBuffer[0].EndPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("d2")), "Bishop stops at the first friendly piece encountered.");
        }

        [Test]
        public void GetBetrayalTargets_Rook_LineOfSightStopsAtFirstPiece()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e8", Team.White, ChessPieceType.King) // FIX: King required
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("a2", Team.White, ChessPieceType.Pawn)
                .WithPiece("a3", Team.White, ChessPieceType.Knight)
                .WithTurn(Team.White)
                .WithBetrayalRight(true);

            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("a1"), _moveBuffer);

            Assert.That(_moveBuffer.Count, Is.EqualTo(1));
            Assert.That(_moveBuffer[0].EndPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("a2")), "Rook stops at the first friendly piece encountered.");
        }

        [Test]
        public void GetBetrayalTargets_Queen_CombinesOrthogonalAndDiagonalLineOfSight()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King) // FIX: King required
                .WithPiece("d4", Team.White, ChessPieceType.Queen)
                .WithPiece("d5", Team.White, ChessPieceType.Pawn)
                .WithPiece("d6", Team.White, ChessPieceType.Rook)
                .WithPiece("e5", Team.White, ChessPieceType.Pawn)
                .WithPiece("f6", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithBetrayalRight(true);

            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("d4"), _moveBuffer);

            Assert.That(_moveBuffer.Count, Is.EqualTo(2), "Queen should find exactly one target per valid unblocked direction.");
        }

        #endregion

        #region King Exclusions

        [Test]
        public void GetBetrayalTargets_KingAsBetrayer_ReturnsEmpty()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e2", Team.White, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithBetrayalRight(true);

            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("e1"), _moveBuffer);

            Assert.That(_moveBuffer.Count, Is.EqualTo(0), "The King is strictly immune from acting as a Betrayer.");
        }

        [Test]
        public void GetBetrayalTargets_FriendlyKingAsVictim_ReturnsEmpty()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithTurn(Team.White)
                .WithBetrayalRight(true);

            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("d1"), _moveBuffer);

            Assert.That(_moveBuffer.Count, Is.EqualTo(0), "The King is strictly immune from being targeted as a Victim.");
        }

        #endregion

        #region Global Rules & Check Restrictions

        [Test]
        public void GetBetrayalTargets_GlobalRightExhausted_ReturnsEmpty()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King) // FIX: King required
                .WithPiece("e4", Team.White, ChessPieceType.Rook)
                .WithPiece("e5", Team.White, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithBetrayalRight(false);

            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("e4"), _moveBuffer);

            Assert.That(_moveBuffer.Count, Is.EqualTo(0), "Cannot initiate Betrayal if the global right for the current match is exhausted.");
        }

        [Test]
        public void GetBetrayalTargets_MoveExposingOwnKingToCheck_ExcludedFromResults()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Rook) // Pinned to the King
                .WithPiece("e8", Team.Black, ChessPieceType.Rook) // Enemy pinning piece
                .WithPiece("d4", Team.White, ChessPieceType.Pawn) // Valid geometric target
                .WithTurn(Team.White)
                .WithBetrayalRight(true);

            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("e4"), _moveBuffer);

            Assert.That(_moveBuffer.Count, Is.EqualTo(0), "A pinned piece cannot initiate an action that breaks the pin and exposes the King.");
        }

        #endregion
    }
}