using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Gameplay.Manager;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Manager
{
    /// <summary>
    /// The board a real match starts from and the one Core hands the search, the opening book and
    /// every test fixture have to be the same board. They are built by different code down different
    /// paths, and the opening book is keyed by position hash, so a difference of one flag would not
    /// fail anywhere — the book would simply stop matching, and the AI would quietly play its own
    /// moves from move one.
    ///
    /// What legitimately differs is recorded here too: the match path reuses one board across games,
    /// so it has repetition history to reset, and Core's builder makes a fresh board that has none.
    /// </summary>
    [TestFixture]
    public class GameSetupStartingPositionTests
    {
        private const int Files = 8;
        private const int Ranks = 8;

        private static BoardState BoardTheMatchStartsFrom()
        {
            var board = new BoardState(Files, Ranks);

            // Exactly what MatchFlowCoordinator.ConfigureMatch does: clear the reused board, then
            // let GameSetup fill it.
            board.Clear();
            new GameSetup(logMoves: false).PlaceStandardPieces(board, Files, Ranks);

            return board;
        }

        [Test]
        public void TheMatchBoardHoldsEveryPieceCoreWouldPutThere()
        {
            BoardState match = BoardTheMatchStartsFrom();
            BoardState core = StandardChessPosition.Create(betrayalRightAvailable: true);

            for (int y = 0; y < Ranks; y++)
            {
                for (int x = 0; x < Files; x++)
                {
                    PieceData fromMatch = match.GetPiece(x, y);
                    PieceData fromCore = core.GetPiece(x, y);
                    string square = $"{(char)('a' + x)}{y + 1}";

                    Assert.That(fromMatch.Type, Is.EqualTo(fromCore.Type), $"Piece type differs on {square}.");
                    Assert.That(fromMatch.Team, Is.EqualTo(fromCore.Team), $"Team differs on {square}.");
                    Assert.That(fromMatch.MoveDirection, Is.EqualTo(fromCore.MoveDirection), $"Move direction differs on {square}.");
                    Assert.That(fromMatch.StartRow, Is.EqualTo(fromCore.StartRow), $"Start row differs on {square}.");
                    Assert.That(fromMatch.HasMoved, Is.EqualTo(fromCore.HasMoved), $"Moved flag differs on {square}.");
                }
            }
        }

        [Test]
        public void TheMatchBoardAgreesWithCoreOnHashAndEveryOpeningFlag()
        {
            BoardState match = BoardTheMatchStartsFrom();
            BoardState core = StandardChessPosition.Create(betrayalRightAvailable: true);

            Assert.That(match.ZobristHash, Is.EqualTo(core.ZobristHash),
                "The opening book is keyed by this hash. A mismatch means the book silently stops " +
                "matching from the very first move rather than failing.");
            Assert.That(match.CurrentTurn, Is.EqualTo(core.CurrentTurn));
            Assert.That(match.CastlingRights, Is.EqualTo(core.CastlingRights));
            Assert.That(match.EnPassantFile, Is.EqualTo(core.EnPassantFile));
            Assert.That(match.BetrayalRightAvailable, Is.EqualTo(core.BetrayalRightAvailable));
        }

        [Test]
        public void TheMatchBoardHoldsThirtyTwoPiecesAndNothingLeftOver()
        {
            BoardState match = BoardTheMatchStartsFrom();
            int occupied = 0;

            for (int y = 0; y < Ranks; y++)
            {
                for (int x = 0; x < Files; x++)
                {
                    if (!match.GetPiece(x, y).IsEmpty) occupied++;
                }
            }

            Assert.That(occupied, Is.EqualTo(32),
                "A board carrying anything but the opening thirty-two means the reused board was not " +
                "cleared before it was filled, and the previous game's pieces are still on it.");
        }

        [Test]
        public void OnlyTheMatchPathSeedsTheOpeningPositionIntoRepetitionHistory()
        {
            // The difference between the two paths, asserted rather than left to be rediscovered.
            // A reused board carries the last game's positions, so the match path clears them and
            // records the opening position as the first one a repetition can count against. Core's
            // builder returns a fresh board, where there is nothing to clear and no match to count.
            Assert.That(BoardTheMatchStartsFrom().PositionCount, Is.EqualTo(1),
                "The match path must record the opening position, or the first repetition after it " +
                "would go uncounted.");
            Assert.That(StandardChessPosition.Create(betrayalRightAvailable: true).PositionCount, Is.Zero);
        }
    }
}
