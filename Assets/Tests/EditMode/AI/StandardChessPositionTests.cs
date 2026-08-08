using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins the standard starting position as literals.
    ///
    /// This board is the root of nearly everything measurable in the project: every opening book entry
    /// is keyed by a hash descended from it, every curated position is replayed from it, and most
    /// fixtures in the suite start from it. It used to exist as four separate copies, so the risk being
    /// pinned here is the one that collapsing them could have introduced — a board that differs by a
    /// single flag is still a perfectly legal board that measures something else, and because the book
    /// is looked up by hash, a drifted copy would quietly stop matching rather than fail.
    ///
    /// The two hashes were captured from the separate copies before they were collapsed, so they also
    /// pin that the collapse changed neither variant.
    /// </summary>
    [TestFixture]
    public class StandardChessPositionTests
    {
        private const ulong GoldenHashWithBetrayalSpent = 4202191151346712887UL;
        private const ulong GoldenHashWithBetrayalLive = 9415338094144947138UL;

        [Test]
        public void TheBetrayalLiveStartIsStillTheExactBoardTheBookWasCompiledAgainst()
        {
            BoardState board = StandardChessPosition.Create(betrayalRightAvailable: true);

            Assert.That(board.ZobristHash, Is.EqualTo(GoldenHashWithBetrayalLive),
                "Every opening book entry is keyed by a hash descended from this board. If it changes, " +
                "the book stops being found at runtime without anything reporting an error.");
        }

        [Test]
        public void TheBetrayalSpentStartIsStillTheExactBoardTheFixturesExpect()
        {
            BoardState board = StandardChessPosition.Create(betrayalRightAvailable: false);

            Assert.That(board.ZobristHash, Is.EqualTo(GoldenHashWithBetrayalSpent));
        }

        [Test]
        public void TheBetrayalFlagIsTheOnlyDifferenceBetweenTheTwoVariants()
        {
            BoardState live = StandardChessPosition.Create(betrayalRightAvailable: true);
            BoardState spent = StandardChessPosition.Create(betrayalRightAvailable: false);

            Assert.That(live.BetrayalRightAvailable, Is.True);
            Assert.That(spent.BetrayalRightAvailable, Is.False);
            Assert.That(live.ZobristHash, Is.Not.EqualTo(spent.ZobristHash),
                "The Betrayal right is part of the hash, so two boards differing in it must not hash " +
                "alike — otherwise a transposition table could answer a Betrayal-live position with a " +
                "result found when the right was already spent.");

            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    Assert.That(spent.LogicalBoard[x, y].Team, Is.EqualTo(live.LogicalBoard[x, y].Team));
                    Assert.That(spent.LogicalBoard[x, y].Type, Is.EqualTo(live.LogicalBoard[x, y].Type));
                }
            }
        }

        [Test]
        public void TheBoardIsAFullStandardSetupReadyToPlayFrom()
        {
            BoardState board = StandardChessPosition.Create(betrayalRightAvailable: true);

            Assert.That(board.GetPieceIndices(Team.White).Count, Is.EqualTo(16));
            Assert.That(board.GetPieceIndices(Team.Black).Count, Is.EqualTo(16));
            Assert.That(board.CurrentTurn, Is.EqualTo(Team.White));
            Assert.That(board.CastlingRights, Is.EqualTo(BoardState.CastlingAllRights));
            Assert.That(board.EnPassantFile, Is.Null);
            Assert.That(board.PliesPlayed, Is.EqualTo(0),
                "A board that reports plies already played would put the opening book past a tier's " +
                "allowance before the first move.");
            Assert.That(board.IsGameOver, Is.False);
        }

        [Test]
        public void KingsAndQueensAreOnTheirOwnFilesRatherThanMirrored()
        {
            // The one placement mistake that survives a piece count and looks plausible on a board
            // diagram: swapping the king and queen files, which is a legal-looking setup that no longer
            // matches any published opening line.
            BoardState board = StandardChessPosition.Create(betrayalRightAvailable: true);

            Assert.That(board.LogicalBoard[3, 0].Type, Is.EqualTo(ChessPieceType.Queen), "White queen belongs on d1.");
            Assert.That(board.LogicalBoard[4, 0].Type, Is.EqualTo(ChessPieceType.King), "White king belongs on e1.");
            Assert.That(board.LogicalBoard[3, 7].Type, Is.EqualTo(ChessPieceType.Queen), "Black queen belongs on d8.");
            Assert.That(board.LogicalBoard[4, 7].Type, Is.EqualTo(ChessPieceType.King), "Black king belongs on e8.");
        }

        [Test]
        public void TheTestBoardUtilityHandsBackTheSharedBetrayalSpentBoard()
        {
            // Eighty-odd fixtures reach the shared builder through this call rather than directly, so
            // this is the pin that keeps those fixtures on the board they were written against.
            Assert.That(TestBoardSetupUtility.CreateStandard().ZobristHash,
                Is.EqualTo(GoldenHashWithBetrayalSpent));
        }
    }
}
