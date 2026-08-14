using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// Two hand-placed, fully developed middlegames with no immediate tactic for either side —
    /// distinct from CuratedOpeningLines, which reaches its positions by replaying real opening
    /// moves. These exist to separate "quiet play costs a lot at depth" from "one specific opening
    /// costs a lot at depth": a search that spends heavily on both a replayed opening and a
    /// hand-placed structural shape is spending on quiet play in general, not on an artifact of one
    /// board.
    ///
    /// Lives in the shipping assembly for the same reason CuratedOpeningLines does — the on-device
    /// benchmark needs these positions and runs on a phone, where the editor-only test fixture that
    /// used to be their only home does not exist.
    /// </summary>
    public static class DepthWallPositions
    {
        /// <summary>A developed middlegame with both sides castled, no captures pending. Betrayal
        /// right is live so the tree carries the real Act/Defection branching.</summary>
        public static BoardState QuietMidgame()
        {
            var board = new BoardState(8, 8);
            board.Clear();
            board.CastlingRights = 0;

            Place(board, "g1", Team.White, ChessPieceType.King);
            Place(board, "g8", Team.Black, ChessPieceType.King);
            Place(board, "d1", Team.White, ChessPieceType.Queen);
            Place(board, "d8", Team.Black, ChessPieceType.Queen);
            Place(board, "a1", Team.White, ChessPieceType.Rook);
            Place(board, "f1", Team.White, ChessPieceType.Rook);
            Place(board, "a8", Team.Black, ChessPieceType.Rook);
            Place(board, "f8", Team.Black, ChessPieceType.Rook);
            Place(board, "c3", Team.White, ChessPieceType.Bishop);
            Place(board, "f3", Team.White, ChessPieceType.Knight);
            Place(board, "c6", Team.Black, ChessPieceType.Bishop);
            Place(board, "f6", Team.Black, ChessPieceType.Knight);
            Place(board, "a2", Team.White, ChessPieceType.Pawn);
            Place(board, "b2", Team.White, ChessPieceType.Pawn);
            Place(board, "c2", Team.White, ChessPieceType.Pawn);
            Place(board, "e4", Team.White, ChessPieceType.Pawn);
            Place(board, "f2", Team.White, ChessPieceType.Pawn);
            Place(board, "g2", Team.White, ChessPieceType.Pawn);
            Place(board, "h2", Team.White, ChessPieceType.Pawn);
            Place(board, "a7", Team.Black, ChessPieceType.Pawn);
            Place(board, "b7", Team.Black, ChessPieceType.Pawn);
            Place(board, "c7", Team.Black, ChessPieceType.Pawn);
            Place(board, "e5", Team.Black, ChessPieceType.Pawn);
            Place(board, "f7", Team.Black, ChessPieceType.Pawn);
            Place(board, "g7", Team.Black, ChessPieceType.Pawn);
            Place(board, "h7", Team.Black, ChessPieceType.Pawn);

            board.CurrentTurn = Team.White;
            board.BetrayalRightAvailable = true;
            board.ComputeFullZobristHash();
            return board;
        }

        /// <summary>A second, structurally different quiet middlegame: an open f-file with rooks
        /// already facing off on it, unbalanced pawns (White has an isolated d-pawn, Black a
        /// backward one), no piece en prise.</summary>
        public static BoardState SemiOpenMidgame()
        {
            var board = new BoardState(8, 8);
            board.Clear();
            board.CastlingRights = 0;

            Place(board, "g1", Team.White, ChessPieceType.King);
            Place(board, "g8", Team.Black, ChessPieceType.King);
            Place(board, "d1", Team.White, ChessPieceType.Queen);
            Place(board, "d8", Team.Black, ChessPieceType.Queen);
            Place(board, "f1", Team.White, ChessPieceType.Rook);
            Place(board, "f8", Team.Black, ChessPieceType.Rook);
            Place(board, "a1", Team.White, ChessPieceType.Rook);
            Place(board, "a8", Team.Black, ChessPieceType.Rook);
            Place(board, "c3", Team.White, ChessPieceType.Knight);
            Place(board, "c6", Team.Black, ChessPieceType.Knight);
            Place(board, "b2", Team.White, ChessPieceType.Bishop);
            Place(board, "b7", Team.Black, ChessPieceType.Bishop);
            Place(board, "a2", Team.White, ChessPieceType.Pawn);
            Place(board, "b3", Team.White, ChessPieceType.Pawn);
            Place(board, "d4", Team.White, ChessPieceType.Pawn);
            Place(board, "g2", Team.White, ChessPieceType.Pawn);
            Place(board, "h2", Team.White, ChessPieceType.Pawn);
            Place(board, "a7", Team.Black, ChessPieceType.Pawn);
            Place(board, "b6", Team.Black, ChessPieceType.Pawn);
            Place(board, "d6", Team.Black, ChessPieceType.Pawn);
            Place(board, "g7", Team.Black, ChessPieceType.Pawn);
            Place(board, "h7", Team.Black, ChessPieceType.Pawn);

            board.CurrentTurn = Team.White;
            board.BetrayalRightAvailable = true;
            board.ComputeFullZobristHash();
            return board;
        }

        private static void Place(BoardState board, string algebraic, Team team, ChessPieceType type)
        {
            int file = algebraic[0] - 'a';
            int rank = algebraic[1] - '1';
            int moveDir = team == Team.White ? 1 : -1;
            int startRow = type == ChessPieceType.Pawn ? (team == Team.White ? 1 : 6) : 0;
            board.SetPiece(new PieceData(team, type, moveDir, startRow), file, rank);
        }
    }
}
