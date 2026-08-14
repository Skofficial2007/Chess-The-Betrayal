using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.AI.Positions
{
    /// <summary>
    /// The one place the standard chess starting position is built.
    ///
    /// It had grown four independent copies — the curated opening lines, the test board utility, the
    /// opening book compiler and the book impact runner — all placing the same thirty-two pieces with
    /// the same arguments and differing only in whether the Betrayal right starts live. Four copies of
    /// the rules of chess is four chances for one of them to drift, and a board that differs by a
    /// single flag still measures perfectly well; it just measures something else. The opening book is
    /// keyed by position hash, so a drifted copy would silently stop matching the book rather than
    /// fail.
    ///
    /// This belongs in Core, next to BoardState, since it is a rule of the game rather than anything
    /// to do with the AI. It lives here because Core is held read-only for the current run of work —
    /// move it down if that ever changes, and the callers will not notice.
    /// </summary>
    public static class StandardChessPosition
    {
        /// <summary>
        /// Back rank from file a to h. Static so building a position does not allocate an array every
        /// call — the search harnesses build thousands of these across a benchmark run, and the game
        /// itself is not the only caller.
        /// </summary>
        private static readonly ChessPieceType[] BackRank =
        {
            ChessPieceType.Rook,   ChessPieceType.Knight, ChessPieceType.Bishop,
            ChessPieceType.Queen,  ChessPieceType.King,   ChessPieceType.Bishop,
            ChessPieceType.Knight, ChessPieceType.Rook
        };

        /// <summary>
        /// A full 8x8 board in the standard opening setup: White to move, all four castling rights, no
        /// en passant file, no move history, and a freshly computed Zobrist hash.
        ///
        /// <paramref name="betrayalRightAvailable"/> is the one thing callers disagree about, so it is
        /// the one thing they pass. Anything modelling a real game wants it live; fixtures isolating
        /// ordinary chess rules from the Betrayal mechanic want it spent. It is applied BEFORE the hash
        /// is computed, because the hash is built from scratch and that flag is one of its inputs —
        /// setting it afterwards would leave the board and its hash disagreeing until something
        /// recomputed it.
        /// </summary>
        public static BoardState Create(bool betrayalRightAvailable)
        {
            var board = new BoardState(8, 8);

            // Also resets turn to White, castling to all rights, en passant to none, and clears move
            // history — so nothing below has to restate the parts of a fresh board that are already
            // the starting position by definition.
            board.Clear();

            for (int x = 0; x < 8; x++)
            {
                board.SetPiece(new PieceData(Team.White, BackRank[x], 1, 0), x, 0);
                board.SetPiece(new PieceData(Team.White, ChessPieceType.Pawn, 1, 1), x, 1);
                board.SetPiece(new PieceData(Team.Black, ChessPieceType.Pawn, -1, 6), x, 6);
                board.SetPiece(new PieceData(Team.Black, BackRank[x], -1, 7), x, 7);
            }

            board.BetrayalRightAvailable = betrayalRightAvailable;
            board.ComputeFullZobristHash();
            return board;
        }
    }
}
