using NUnit.Framework;
using ChessTheBetrayal.View.Board;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Tests.EditMode.View.Board
{
    /// <summary>
    /// The view's record of which piece stands where. It was a private dictionary inside a
    /// MonoBehaviour until a defect turned on the one rule nothing could assert — that moving a
    /// piece never leaves the square it came from and the square it is going to disagreeing about
    /// who is where. Occupants stand in as plain strings, since none of these rules care what is
    /// on the square.
    /// </summary>
    [TestFixture]
    public class BoardOccupancyTests
    {
        private static Vector2Int Sq(int file, int rank) => new Vector2Int(file, rank);

        private static BoardOccupancy<string> NewBoard() => new BoardOccupancy<string>();

        [Test]
        public void Place_PutsAnOccupantWhereItCanBeFoundAgain()
        {
            BoardOccupancy<string> board = NewBoard();

            board.Place(Sq(4, 0), "king");

            Assert.That(board.TryGet(Sq(4, 0), out string found), Is.True);
            Assert.That(found, Is.EqualTo("king"));
            Assert.That(board.IsOccupied(Sq(4, 0)), Is.True);
            Assert.That(board.Count, Is.EqualTo(1));
        }

        [Test]
        public void TryGet_AnEmptySquare_ReportsNobodyThere()
        {
            BoardOccupancy<string> board = NewBoard();

            Assert.That(board.TryGet(Sq(3, 3), out string found), Is.False);
            Assert.That(found, Is.Null);
            Assert.That(board.IsOccupied(Sq(3, 3)), Is.False);
        }

        [Test]
        public void Place_OnATakenSquare_HandsItToTheNewOccupant()
        {
            BoardOccupancy<string> board = NewBoard();

            board.Place(Sq(4, 4), "pawn");
            board.Place(Sq(4, 4), "queen");

            Assert.That(board.TryGet(Sq(4, 4), out string found), Is.True);
            Assert.That(found, Is.EqualTo("queen"), "A capture puts the attacker where the victim stood.");
            Assert.That(board.Count, Is.EqualTo(1), "and does not leave the victim counted as well.");
        }

        [Test]
        public void TryTake_HandsBackTheOccupantAndLeavesTheSquareEmpty()
        {
            BoardOccupancy<string> board = NewBoard();
            board.Place(Sq(2, 6), "bishop");

            Assert.That(board.TryTake(Sq(2, 6), out string taken), Is.True);
            Assert.That(taken, Is.EqualTo("bishop"));
            Assert.That(board.IsOccupied(Sq(2, 6)), Is.False);
            Assert.That(board.Count, Is.EqualTo(0));
        }

        [Test]
        public void TryTake_AnEmptySquare_ChangesNothing()
        {
            BoardOccupancy<string> board = NewBoard();
            board.Place(Sq(0, 0), "rook");

            Assert.That(board.TryTake(Sq(7, 7), out string taken), Is.False);
            Assert.That(taken, Is.Null);
            Assert.That(board.Count, Is.EqualTo(1), "The square that was occupied is still occupied.");
        }

        [Test]
        public void TryMove_CarriesTheOccupantAcrossInOneStep()
        {
            BoardOccupancy<string> board = NewBoard();
            board.Place(Sq(1, 0), "knight");

            Assert.That(board.TryMove(Sq(1, 0), Sq(2, 2), out string moved), Is.True);
            Assert.That(moved, Is.EqualTo("knight"));

            Assert.That(board.IsOccupied(Sq(1, 0)), Is.False, "The square it left is empty.");
            Assert.That(board.TryGet(Sq(2, 2), out string arrived), Is.True);
            Assert.That(arrived, Is.EqualTo("knight"), "The square it went to has it.");
            Assert.That(board.Count, Is.EqualTo(1), "One piece moved, not two pieces created.");
        }

        /// <summary>
        /// The rule this type exists for. A move is both halves or neither: a caller that finds
        /// nobody to move must not have emptied the destination on the way to finding that out,
        /// because a square quietly belonging to nobody is exactly what the visual desync bugs
        /// were made of.
        /// </summary>
        [Test]
        public void TryMove_WithNobodyToMove_LeavesBothSquaresAlone()
        {
            BoardOccupancy<string> board = NewBoard();
            board.Place(Sq(5, 5), "already here");

            Assert.That(board.TryMove(Sq(0, 7), Sq(5, 5), out string moved), Is.False);
            Assert.That(moved, Is.Null);

            Assert.That(board.TryGet(Sq(5, 5), out string untouched), Is.True);
            Assert.That(untouched, Is.EqualTo("already here"),
                "A move that could not happen must not clear the square it was aimed at.");
            Assert.That(board.Count, Is.EqualTo(1));
        }

        [Test]
        public void TryMove_OntoAnOccupiedSquare_LeavesOnlyTheArrival()
        {
            BoardOccupancy<string> board = NewBoard();
            board.Place(Sq(3, 3), "victim");
            board.Place(Sq(3, 6), "attacker");

            Assert.That(board.TryMove(Sq(3, 6), Sq(3, 3), out string moved), Is.True);
            Assert.That(moved, Is.EqualTo("attacker"));

            Assert.That(board.TryGet(Sq(3, 3), out string standing), Is.True);
            Assert.That(standing, Is.EqualTo("attacker"));
            Assert.That(board.Count, Is.EqualTo(1), "The victim is off the board, not still counted.");
        }

        [Test]
        public void Remove_EmptiesTheSquareAndSaysWhetherAnyoneWasThere()
        {
            BoardOccupancy<string> board = NewBoard();
            board.Place(Sq(6, 1), "pawn");

            Assert.That(board.Remove(Sq(6, 1)), Is.True);
            Assert.That(board.IsOccupied(Sq(6, 1)), Is.False);
            Assert.That(board.Remove(Sq(6, 1)), Is.False, "Emptying an empty square is not a change.");
        }

        [Test]
        public void Entries_NameEverySquareAndWhoIsOnIt()
        {
            BoardOccupancy<string> board = NewBoard();
            board.Place(Sq(0, 0), "a");
            board.Place(Sq(1, 1), "b");
            board.Place(Sq(2, 2), "c");

            var seen = new System.Collections.Generic.Dictionary<Vector2Int, string>();
            foreach (var entry in board.Entries) seen[entry.Key] = entry.Value;

            Assert.That(seen.Count, Is.EqualTo(3));
            Assert.That(seen[Sq(0, 0)], Is.EqualTo("a"));
            Assert.That(seen[Sq(1, 1)], Is.EqualTo("b"));
            Assert.That(seen[Sq(2, 2)], Is.EqualTo("c"));
        }

        [Test]
        public void Clear_EmptiesTheWholeBoard()
        {
            BoardOccupancy<string> board = NewBoard();
            board.Place(Sq(0, 0), "a");
            board.Place(Sq(1, 1), "b");

            board.Clear();

            Assert.That(board.Count, Is.EqualTo(0));
            Assert.That(board.TryGet(Sq(0, 0), out string _), Is.False);
        }
    }
}
