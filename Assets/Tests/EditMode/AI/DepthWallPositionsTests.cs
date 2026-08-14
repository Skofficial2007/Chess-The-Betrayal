using NUnit.Framework;
using ChessTheBetrayal.AI.Positions;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins DepthWallPositions' two boards as literals. They used to be built only inside
    /// SearchDepthProfileCaptureTests, an editor-only fixture the on-device benchmark cannot
    /// reference; moving the builders into the shipping AI assembly risks the same thing collapsing
    /// StandardChessPosition guarded against — a hand-placed board that drifts by one square or one
    /// flag is still a legal board, so nothing would fail loudly. The hashes below were captured from
    /// the original editor-only builders before the move, so they prove the port changed neither.
    /// </summary>
    [TestFixture]
    public class DepthWallPositionsTests
    {
        private const ulong GoldenQuietMidgameHash = 9567791423313919642UL;
        private const ulong GoldenSemiOpenMidgameHash = 18257401590649323034UL;

        [Test]
        public void QuietMidgameStillMatchesTheHashCapturedBeforeItMovedIntoTheShippingAssembly()
        {
            BoardState board = DepthWallPositions.QuietMidgame();
            Assert.That(board.ZobristHash, Is.EqualTo(GoldenQuietMidgameHash));
        }

        [Test]
        public void SemiOpenMidgameStillMatchesTheHashCapturedBeforeItMovedIntoTheShippingAssembly()
        {
            BoardState board = DepthWallPositions.SemiOpenMidgame();
            Assert.That(board.ZobristHash, Is.EqualTo(GoldenSemiOpenMidgameHash));
        }

        [Test]
        public void TheEditorOnlyFixtureStillHandsBackTheSameBoardsAfterDelegating()
        {
            Assert.That(SearchDepthProfileCaptureTests.QuietMidgame().ZobristHash,
                Is.EqualTo(GoldenQuietMidgameHash));
            Assert.That(SearchDepthProfileCaptureTests.SemiOpenMidgame().ZobristHash,
                Is.EqualTo(GoldenSemiOpenMidgameHash));
        }

        [Test]
        public void BothPositionsHaveBetrayalRightLiveAndWhiteToMove()
        {
            BoardState quiet = DepthWallPositions.QuietMidgame();
            BoardState semiOpen = DepthWallPositions.SemiOpenMidgame();

            Assert.That(quiet.BetrayalRightAvailable, Is.True);
            Assert.That(quiet.CurrentTurn, Is.EqualTo(Team.White));
            Assert.That(semiOpen.BetrayalRightAvailable, Is.True);
            Assert.That(semiOpen.CurrentTurn, Is.EqualTo(Team.White));
        }
    }
}
