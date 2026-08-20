namespace ChessTheBetrayal.Core.Data
{
    /// <summary>
    /// Turns a square between its coordinates and the way people write it — a1 through h8.
    ///
    /// One converter rather than one per caller. The arithmetic is trivial enough that it kept
    /// getting rewritten locally, and the copies had already drifted: some lower-cased the file
    /// letter and some did not, some rejected an out-of-range square and some quietly returned a
    /// coordinate off the board.
    /// </summary>
    public static class SquareNotation
    {
        /// <summary>
        /// Reads a two-character square. Returns false rather than throwing, so each caller can
        /// fail in whatever way suits it — book import wants to name the file and line it was
        /// reading, a board builder wants an argument exception.
        /// </summary>
        public static bool TryParse(string algebraic, out Vector2Int square)
        {
            square = default;
            if (string.IsNullOrEmpty(algebraic) || algebraic.Length != 2)
                return false;

            return TryParse(algebraic[0], algebraic[1], out square);
        }

        /// <summary>Reads a square already split into its two characters, which saves a caller
        /// holding a longer token from cutting a string out of it just to be read back.</summary>
        public static bool TryParse(char file, char rank, out Vector2Int square)
        {
            square = default;

            // Invariant rather than current-culture: the letters are chess notation, not text in
            // whatever language the machine happens to be set to.
            char lowerFile = char.ToLowerInvariant(file);
            if (lowerFile < 'a' || lowerFile > 'h' || rank < '1' || rank > '8')
                return false;

            square = new Vector2Int(lowerFile - 'a', rank - '1');
            return true;
        }

        /// <summary>Writes a square the way it is spoken.</summary>
        public static string ToAlgebraic(Vector2Int square) =>
            $"{(char)('a' + square.x)}{square.y + 1}";
    }
}
