using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    [TestFixture]
    public class MaterialPhaseTests
    {
        private static BoardState FullNonPawnMaterialBoard() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("h1", Team.White, ChessPieceType.Rook)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("g1", Team.White, ChessPieceType.Knight)
                .WithPiece("c1", Team.White, ChessPieceType.Bishop)
                .WithPiece("f1", Team.White, ChessPieceType.Bishop)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a8", Team.Black, ChessPieceType.Rook)
                .WithPiece("h8", Team.Black, ChessPieceType.Rook)
                .WithPiece("b8", Team.Black, ChessPieceType.Knight)
                .WithPiece("g8", Team.Black, ChessPieceType.Knight)
                .WithPiece("c8", Team.Black, ChessPieceType.Bishop)
                .WithPiece("f8", Team.Black, ChessPieceType.Bishop)
                .WithPiece("d8", Team.Black, ChessPieceType.Queen);

        private static BoardState BareKingsBoard() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King);

        [Test]
        public void Weight_FullNonPawnMaterialBothSides_ReadsFullPhaseWeight()
        {
            Assert.That(MaterialPhase.Weight(FullNonPawnMaterialBoard()), Is.EqualTo(MaterialPhase.FullPhaseWeight));
        }

        [Test]
        public void Weight_BareKings_ReadsZero()
        {
            Assert.That(MaterialPhase.Weight(BareKingsBoard()), Is.EqualTo(0));
        }

        [Test]
        public void Weight_RemovingAQueen_MovesStrictlyTowardEndgame()
        {
            BoardState fullBoard = FullNonPawnMaterialBoard();
            int fullWeight = MaterialPhase.Weight(fullBoard);

            fullBoard.RemovePiece(TestBoardSetupUtility.AlgebraicToVector("d1"));
            int afterQueenRemoved = MaterialPhase.Weight(fullBoard);

            Assert.That(afterQueenRemoved, Is.LessThan(fullWeight));
        }

        [Test]
        public void Weight_ExceedingOpeningMaterial_ClampsToFullPhaseWeight()
        {
            // A promoted extra queen pushes total non-pawn material above the opening baseline —
            // must still read as "fully midgame," not overshoot the blend range a caller applies
            // this weight to.
            BoardState board = FullNonPawnMaterialBoard()
                .WithPiece("d4", Team.White, ChessPieceType.Queen);

            Assert.That(MaterialPhase.Weight(board), Is.EqualTo(MaterialPhase.FullPhaseWeight));
        }

        [Test]
        public void Weight_InvariantAcrossADefection()
        {
            // A defection changes which side OWNS a piece, never how much material is on the
            // board — the whole point of summing both teams together. Use a knight (not on either
            // king's own square) so DefectPiece has something real to flip.
            BoardState board = FullNonPawnMaterialBoard();
            int weightBeforeDefection = MaterialPhase.Weight(board);

            board.DefectPiece(TestBoardSetupUtility.AlgebraicToVector("b1"));

            Assert.That(MaterialPhase.Weight(board), Is.EqualTo(weightBeforeDefection));
        }
    }
}
