using System;
using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Match
{
    /// <summary>
    /// The dump is what a bug report about a wrong checkmate call is read from, so it is only worth
    /// anything if the position on the page is the position in memory. Everything about it is a
    /// convention somebody has to agree with — which way up the ranks print, which case means which
    /// side, what an empty square looks like — and none of those conventions is checked by anything
    /// that uses it. So the whole rendering is pinned character for character.
    /// </summary>
    [TestFixture]
    public class BoardStateDumpTests
    {
        private static string[] LinesOf(string dump) =>
            dump.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

        /// <summary>
        /// The opening position exercises every letter in both cases and the empty middle in one
        /// go, and it is the one position a reader can check against without setting anything up.
        /// Ranks read down the page from Black's back rank, the way a player sitting behind White
        /// sees the board.
        /// </summary>
        [Test]
        public void ToAscii_ForTheOpeningPosition_RendersTheBoardAsAPlayerSeesIt()
        {
            string[] expected =
            {
                "r n b q k b n r   (rank y=7)",
                "p p p p p p p p   (rank y=6)",
                ". . . . . . . .   (rank y=5)",
                ". . . . . . . .   (rank y=4)",
                ". . . . . . . .   (rank y=3)",
                ". . . . . . . .   (rank y=2)",
                "P P P P P P P P   (rank y=1)",
                "R N B Q K B N R   (rank y=0)",
                "CurrentTurn=White CastlingRights=15 EnPassantFile=none " +
                "PendingBetrayerSquare=none BetrayalInitiator=none"
            };

            string dump = BoardStateDump.ToAscii(StandardChessPosition.Create(betrayalRightAvailable: true));

            Assert.That(LinesOf(dump), Is.EqualTo(expected));
        }

        /// <summary>
        /// The three optional fields are the ones a report is most likely to need and least likely
        /// to have — a Betrayal in progress, or an en passant capture that is about to stop being
        /// available. They are set here rather than played into place because the dump reports the
        /// board's fields verbatim and has no rules of its own to break; what is under test is that
        /// a value present prints as itself instead of falling back to "none".
        /// </summary>
        [Test]
        public void ToAscii_WhenTheOptionalStateIsSet_PrintsItInsteadOfNone()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithTurn(Team.Black)
                .WithEnPassantFile(4)
                .WithPendingBetrayer("d2", Team.White)
                .WithComputedHash();

            string[] lines = LinesOf(BoardStateDump.ToAscii(board));

            Assert.That(lines[lines.Length - 1], Is.EqualTo(
                "CurrentTurn=Black CastlingRights=0 EnPassantFile=4 " +
                "PendingBetrayerSquare=(3, 1) BetrayalInitiator=White"));
        }
    }
}
