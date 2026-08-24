using ChessTheBetrayal.Core.Diagnostics;

namespace ChessTheBetrayal.Core.Data
{
    /// <summary>
    /// Where the search, its tooling and its tests get the standard chess starting position.
    ///
    /// That setup had grown four independent copies — the curated opening lines, the test board
    /// utility, the opening book compiler and the book impact runner — all placing the same
    /// thirty-two pieces with the same arguments and differing only in whether the Betrayal right
    /// starts live. Four copies of the rules of chess is four chances for one of them to drift, and
    /// a board that differs by a single flag still measures perfectly well; it just measures
    /// something else. The opening book is keyed by position hash, so a drifted copy would silently
    /// stop matching the book rather than fail.
    /// </summary>
    public static class StandardChessPosition
    {
        /// <summary>Files a to h. A chessboard's width is a rule of the game, not a setting, so
        /// every caller that needs to size a board for it reads it from here.</summary>
        public const int Files = 8;

        /// <summary>Ranks 1 to 8. See <see cref="Files"/>.</summary>
        public const int Ranks = 8;

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
            var board = new BoardState(Files, Ranks);

            // Also resets turn to White, castling to all rights, en passant to none, and clears move
            // history — so nothing below has to restate the parts of a fresh board that are already
            // the starting position by definition.
            board.Clear();

            PlacePieces(board);

            board.BetrayalRightAvailable = betrayalRightAvailable;
            board.ComputeFullZobristHash();
            return board;
        }

        /// <summary>
        /// Puts the opening thirty-two pieces on an already-cleared board. Separate from
        /// <see cref="Create"/> because a match reuses one board across games rather than building a
        /// new one each time, and where the pieces start is a rule of chess that both paths have to
        /// agree on exactly — the opening book is keyed by position hash, so two placements that
        /// differ anywhere would leave the book quietly never matching.
        ///
        /// Leaves the hash alone. The caller decides when to compute it, because it also decides
        /// what else about the board it still has to set first.
        /// </summary>
        public static void PlacePieces(BoardState board)
        {
            if (board.TileCountX != Files || board.TileCountY != Ranks)
            {
                throw new DomainException(
                    DomainEventCode.Board_UnsupportedDimensions,
                    $"The standard chess position needs a {Files}x{Ranks} board, not " +
                    $"{board.TileCountX}x{board.TileCountY}. A position on a board of another size " +
                    "is a different position and needs its own source.");
            }

            for (int x = 0; x < Files; x++)
            {
                board.SetPiece(new PieceData(Team.White, BackRank[x], 1, 0), x, 0);
                board.SetPiece(new PieceData(Team.White, ChessPieceType.Pawn, 1, 1), x, 1);
                board.SetPiece(new PieceData(Team.Black, ChessPieceType.Pawn, -1, Ranks - 2), x, Ranks - 2);
                board.SetPiece(new PieceData(Team.Black, BackRank[x], -1, Ranks - 1), x, Ranks - 1);
            }
        }
    }
}
