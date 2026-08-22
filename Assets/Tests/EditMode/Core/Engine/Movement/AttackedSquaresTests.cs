using NUnit.Framework;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Movement;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Movement
{
    /// <summary>
    /// Pins the IPieceMovement.GetAttackedSquares attack-map primitive per piece type. "Attacked"
    /// means "could capture an enemy here", judged by geometry + line-of-sight against the current
    /// occupancy and independent of what actually sits on the square. This primitive is shared by
    /// ChessEngine.IsSquareUnderAttack (check detection) and ChessEngine.GetBetrayalTargets (Act
    /// generation), so its exact contract is load-bearing for both correctness and search speed.
    /// </summary>
    [TestFixture]
    public class AttackedSquaresTests
    {
        private List<Vector2Int> _buffer;

        [SetUp]
        public void Setup() => _buffer = new List<Vector2Int>();

        private HashSet<Vector2Int> AttackedBy(BoardState board, string square)
        {
            Vector2Int pos = TestBoardSetupUtility.AlgebraicToVector(square);
            PieceData piece = board.GetPiece(pos);
            IPieceMovement strategy = MovementFactory.GetStrategy(piece.Type);

            _buffer.Clear();
            strategy.GetAttackedSquares(board, piece, pos, _buffer);
            return new HashSet<Vector2Int>(_buffer);
        }

        private bool Attacks(BoardState board, string from, string target) =>
            AttackedBy(board, from).Contains(TestBoardSetupUtility.AlgebraicToVector(target));

        #region Sliders — up to and INCLUDING the first blocker, of either team

        [Test]
        public void Rook_AttacksEmptySquaresAndFirstBlocker_RegardlessOfBlockerTeam()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("a4", Team.White, ChessPieceType.Pawn)   // friendly blocker up-ray
                .WithPiece("d1", Team.Black, ChessPieceType.Pawn);  // enemy blocker right-ray

            var attacked = AttackedBy(board, "a1");

            // Up-ray: a2, a3 empty then a4 (friendly) is attacked; a5+ is not.
            Assert.That(attacked.Contains(TestBoardSetupUtility.AlgebraicToVector("a2")), Is.True);
            Assert.That(attacked.Contains(TestBoardSetupUtility.AlgebraicToVector("a3")), Is.True);
            Assert.That(attacked.Contains(TestBoardSetupUtility.AlgebraicToVector("a4")), Is.True,
                "The first blocker square is attacked even when a friendly piece sits there.");
            Assert.That(attacked.Contains(TestBoardSetupUtility.AlgebraicToVector("a5")), Is.False,
                "Nothing past the blocker is attacked.");

            // Right-ray: b1, c1 empty then d1 (enemy) is attacked; e1+ is not.
            Assert.That(attacked.Contains(TestBoardSetupUtility.AlgebraicToVector("d1")), Is.True);
            Assert.That(attacked.Contains(TestBoardSetupUtility.AlgebraicToVector("e1")), Is.False);
        }

        [Test]
        public void Bishop_StopsAtFirstBlockerPerDiagonal()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("c1", Team.White, ChessPieceType.Bishop)
                .WithPiece("e3", Team.White, ChessPieceType.Knight); // blocker on the up-right diagonal

            Assert.That(Attacks(board, "c1", "d2"), Is.True);
            Assert.That(Attacks(board, "c1", "e3"), Is.True, "First blocker on the diagonal is attacked.");
            Assert.That(Attacks(board, "c1", "f4"), Is.False, "Past the blocker is not attacked.");
        }

        [Test]
        public void Queen_CombinesOrthogonalAndDiagonalRays()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("d4", Team.White, ChessPieceType.Queen);

            var attacked = AttackedBy(board, "d4");

            // On an otherwise empty board a centralized queen attacks 27 squares.
            Assert.That(attacked.Count, Is.EqualTo(27));
        }

        #endregion

        #region Jumpers — every in-bounds offset, occupancy irrelevant

        [Test]
        public void Knight_AttacksAllInBoundsOffsets_EvenThroughBlockers()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("d4", Team.White, ChessPieceType.Knight)
                // Surround it fully — a jumper is unaffected by adjacent blockers.
                .WithPiece("d5", Team.White, ChessPieceType.Pawn)
                .WithPiece("d3", Team.White, ChessPieceType.Pawn)
                .WithPiece("c4", Team.White, ChessPieceType.Pawn)
                .WithPiece("e4", Team.White, ChessPieceType.Pawn);

            Assert.That(AttackedBy(board, "d4").Count, Is.EqualTo(8),
                "A knight in the center attacks all 8 L-squares regardless of blockers.");
        }

        [Test]
        public void Knight_OffBoardOffsetsExcluded()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.Knight);

            Assert.That(AttackedBy(board, "a1").Count, Is.EqualTo(2), "A corner knight attacks only b3 and c2.");
            Assert.That(Attacks(board, "a1", "b3"), Is.True);
            Assert.That(Attacks(board, "a1", "c2"), Is.True);
        }

        [Test]
        public void King_AttacksAdjacentSquaresOnly_NeverCastlingTargets()
        {
            // A King with intact castling rights and a clear path must still NOT attack the castling
            // landing square — castling is a move, never a capture, so it has no place in an attack map.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("h1", Team.White, ChessPieceType.Rook);

            var attacked = AttackedBy(board, "e1");

            Assert.That(attacked.Count, Is.EqualTo(5), "Back-rank king attacks its 5 in-bounds neighbours.");
            Assert.That(attacked.Contains(TestBoardSetupUtility.AlgebraicToVector("f1")), Is.True);
            Assert.That(attacked.Contains(TestBoardSetupUtility.AlgebraicToVector("g1")), Is.False,
                "The kingside castling landing square is not an attacked square.");
        }

        #endregion

        #region Pawn — two diagonals only, occupancy-independent

        [Test]
        public void WhitePawn_AttacksBothForwardDiagonals_EvenWhenEmpty()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e4", Team.White, ChessPieceType.Pawn);

            var attacked = AttackedBy(board, "e4");

            Assert.That(attacked.Count, Is.EqualTo(2));
            Assert.That(attacked.Contains(TestBoardSetupUtility.AlgebraicToVector("d5")), Is.True);
            Assert.That(attacked.Contains(TestBoardSetupUtility.AlgebraicToVector("f5")), Is.True);
        }

        [Test]
        public void WhitePawn_NeverAttacksForwardPushSquares()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e2", Team.White, ChessPieceType.Pawn); // unmoved — double push available as a MOVE

            var attacked = AttackedBy(board, "e2");

            Assert.That(attacked.Contains(TestBoardSetupUtility.AlgebraicToVector("e3")), Is.False, "Single push is a move, not an attack.");
            Assert.That(attacked.Contains(TestBoardSetupUtility.AlgebraicToVector("e4")), Is.False, "Double push is a move, not an attack.");
        }

        [Test]
        public void BlackPawn_AttacksDownwardDiagonals()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e5", Team.Black, ChessPieceType.Pawn);

            var attacked = AttackedBy(board, "e5");

            Assert.That(attacked.Count, Is.EqualTo(2));
            Assert.That(attacked.Contains(TestBoardSetupUtility.AlgebraicToVector("d4")), Is.True);
            Assert.That(attacked.Contains(TestBoardSetupUtility.AlgebraicToVector("f4")), Is.True);
        }

        [Test]
        public void EdgePawn_OnlyOneDiagonalInBounds()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a4", Team.White, ChessPieceType.Pawn);

            var attacked = AttackedBy(board, "a4");

            Assert.That(attacked.Count, Is.EqualTo(1), "An a-file pawn attacks only b5.");
            Assert.That(attacked.Contains(TestBoardSetupUtility.AlgebraicToVector("b5")), Is.True);
        }

        #endregion
    }
}
