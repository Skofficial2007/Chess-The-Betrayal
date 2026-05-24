using NUnit.Framework;
using ChessTheMasterPiece.Data;
using ChessTheMasterPiece.Logic;
using ChessTheMasterPiece.Tests.Utilities;

namespace ChessTheMasterPiece.Tests.EditMode.Data
{
    [TestFixture]
    public class BoardStateTests
    {
        [Test]
        public void BoardState_CloneForSnapshot_IsDeepCopy_ModifyingCloneDoesNotAffectOriginal()
        {
            // Arrange
            BoardState original = TestBoardSetupUtility.CreateStandard();

            // Act
            BoardState clone = original.CloneForSnapshot();

            // Modify the clone by placing a Queen on a1 (replacing the Rook)
            clone.SetPiece(new PieceData(Team.White, ChessPieceType.Queen, 1, 0, false), 0, 0);

            // Assert
            Assert.That(original.GetPiece(0, 0).Type, Is.EqualTo(ChessPieceType.Rook), "Original board should not be affected by clone modifications.");
            Assert.That(clone.GetPiece(0, 0).Type, Is.EqualTo(ChessPieceType.Queen), "Clone should reflect the new piece.");
        }

        [Test]
        public void BoardState_CloneForSnapshot_ZobristHashIsCopied()
        {
            // Arrange
            BoardState original = TestBoardSetupUtility.CreateStandard();
            // CreateStandard already computes the hash, but we explicitly compute to be safe
            original.ComputeFullZobristHash();

            // Act
            BoardState clone = original.CloneForSnapshot();

            // Assert
            Assert.That(clone.ZobristHash, Is.EqualTo(original.ZobristHash), "Zobrist hash must carry over exactly to the clone.");
            Assert.That(clone.ZobristHash, Is.Not.EqualTo(0UL));
        }

        [Test]
        public void BoardState_CloneForSnapshot_MoveHistoryIsDeepCopied_NotSharedList()
        {
            // Arrange
            BoardState original = TestBoardSetupUtility.CreateStandard();
            original.MoveHistory.Add(new Vector2Int(0, 0)); // Add 1 dummy position

            // Act
            BoardState clone = original.CloneForSnapshot();
            original.MoveHistory.Add(new Vector2Int(1, 1)); // Add a 2nd position ONLY to the original

            // Assert
            Assert.That(clone.MoveHistory.Count, Is.EqualTo(1), "Clone's history list should not update when original's list does.");
            Assert.That(original.MoveHistory.Count, Is.EqualTo(2), "Original should have 2 positions. Lists must not be shared by reference.");
        }

        [Test]
        public void BoardState_GetPieceIndices_ReturnsCorrectCountForEachTeam()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateStandard();

            // Act
            var whiteIndices = board.GetPieceIndices(Team.White);
            var blackIndices = board.GetPieceIndices(Team.Black);

            // Assert
            Assert.That(whiteIndices.Count, Is.EqualTo(16), "Standard board should have 16 White pieces indexed.");
            Assert.That(blackIndices.Count, Is.EqualTo(16), "Standard board should have 16 Black pieces indexed.");
        }

        [Test]
        public void BoardState_GetPieceIndices_AfterCapture_CountDecremented()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateStandard();

            // Verify initial state
            Assert.That(board.GetPieceIndices(Team.Black).Count, Is.EqualTo(16));

            // Act
            // Manually remove a black piece to simulate a capture (e7 Black Pawn)
            board.SetPiece(PieceData.Empty, 4, 6);

            // Assert
            var updatedBlackIndices = board.GetPieceIndices(Team.Black);
            Assert.That(updatedBlackIndices.Count, Is.EqualTo(15), "Piece index count must automatically update when a piece is removed.");
        }
    }
}