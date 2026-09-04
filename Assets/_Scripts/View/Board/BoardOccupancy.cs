using System.Collections.Generic;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.View.Board
{
    /// <summary>
    /// Which piece the board is currently showing on each square.
    ///
    /// This is the view's own record and not the game's. The domain always knows where every piece
    /// stands; this only knows where the pieces on screen have been put, which is a different thing
    /// for as long as an animation is still catching up to a move that has already been applied.
    ///
    /// It began as a plain dictionary field inside BoardVisuals, which cost two things. Taking a
    /// piece off one square and putting it on another was written out longhand wherever it was
    /// needed, so the two halves could drift apart and leave a square briefly belonging to nobody.
    /// And none of it could be reached without a scene, so the rules a takeback leans on had
    /// nowhere to be stated.
    ///
    /// Generic over the occupant for the same reason the death piles are: a ChessPiece needs a
    /// prefab and a scene to exist at all, and nothing here cares what is standing on the square.
    /// </summary>
    public sealed class BoardOccupancy<T> where T : class
    {
        // Sized for a full board plus the handful of moments a promotion or a defection has two
        // pieces alive on one square at once.
        private readonly Dictionary<Vector2Int, T> _bySquare = new Dictionary<Vector2Int, T>(40);

        /// <summary>How many squares currently have somebody on them.</summary>
        public int Count => _bySquare.Count;

        /// <summary>
        /// Every occupied square and who is on it, in no particular order. For the searches that
        /// need to find a piece by what it is rather than by where it stands.
        /// </summary>
        public IEnumerable<KeyValuePair<Vector2Int, T>> Entries => _bySquare;

        /// <summary>Who is standing on <paramref name="square"/>, if anybody is.</summary>
        public bool TryGet(Vector2Int square, out T occupant) => _bySquare.TryGetValue(square, out occupant);

        /// <summary>True while somebody is standing on <paramref name="square"/>.</summary>
        public bool IsOccupied(Vector2Int square) => _bySquare.ContainsKey(square);

        /// <summary>
        /// Puts <paramref name="occupant"/> on <paramref name="square"/>, replacing whoever the
        /// board believed was standing there.
        /// </summary>
        public void Place(Vector2Int square, T occupant) => _bySquare[square] = occupant;

        /// <summary>
        /// Empties <paramref name="square"/>. True when somebody was actually standing on it.
        /// </summary>
        public bool Remove(Vector2Int square) => _bySquare.Remove(square);

        /// <summary>
        /// Takes whoever is on <paramref name="square"/> off the board and hands them back. False
        /// when the square was already empty, and then nothing is changed.
        /// </summary>
        public bool TryTake(Vector2Int square, out T occupant)
        {
            if (!_bySquare.TryGetValue(square, out occupant)) return false;

            _bySquare.Remove(square);
            return true;
        }

        /// <summary>
        /// Walks whoever is on <paramref name="from"/> across to <paramref name="to"/> and hands
        /// back the piece that moved. False when there was nobody to move, and then neither square
        /// is touched.
        ///
        /// One call rather than a take followed by a place, because those two written separately
        /// are exactly what a square left momentarily empty looks like, and no caller that moves a
        /// piece ever wants one half without the other.
        /// </summary>
        public bool TryMove(Vector2Int from, Vector2Int to, out T occupant)
        {
            if (!TryTake(from, out occupant)) return false;

            _bySquare[to] = occupant;
            return true;
        }

        /// <summary>Forgets the whole board. For teardown between matches.</summary>
        public void Clear() => _bySquare.Clear();
    }
}
