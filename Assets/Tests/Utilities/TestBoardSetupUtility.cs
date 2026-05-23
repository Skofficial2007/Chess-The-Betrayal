using System;
using System.Collections.Generic;
using ChessTheMasterPiece.Data;
using ChessTheMasterPiece.Logic;

namespace ChessTheMasterPiece.Tests.Utilities
{
    public static class TestBoardSetupUtility
    {
        /// <summary>
        /// Returns a completely empty 8x8 BoardState with no pieces and all castling
        /// rights cleared. Use as the base for custom surgical board arrangements where
        /// only specific pieces matter. Prevents accidental interactions with default
        /// pieces when testing a single movement rule.
        /// </summary>
        public static BoardState CreateEmpty()
        {
            BoardState board = new BoardState(8, 8);
            board.Clear();
            // Clear() resets CastlingRights to CastlingAllRights. We explicitly want 0 for empty boards.
            board.CastlingRights = 0;
            board.CurrentTurn = Team.White;
            board.EnPassantFile = null;
            return board;
        }

        /// <summary>
        /// Returns a BoardState initialised to the standard chess starting position,
        /// including all 32 pieces, full castling rights, and a freshly computed
        /// Zobrist hash. Equivalent to the opening position after no moves.
        /// </summary>
        public static BoardState CreateStandard()
        {
            BoardState board = new BoardState(8, 8);
            board.Clear(); // Clears board and sets CastlingRights to CastlingAllRights (15)

            ChessPieceType[] backRank = new ChessPieceType[]
            {
                ChessPieceType.Rook,   ChessPieceType.Knight, ChessPieceType.Bishop,
                ChessPieceType.Queen,  ChessPieceType.King,   ChessPieceType.Bishop,
                ChessPieceType.Knight, ChessPieceType.Rook
            };

            for (int x = 0; x < 8; x++)
            {
                // White setup
                board.SetPiece(new PieceData(Team.White, backRank[x], 1, 0), x, 0);
                board.SetPiece(new PieceData(Team.White, ChessPieceType.Pawn, 1, 1), x, 1);

                // Black setup
                board.SetPiece(new PieceData(Team.Black, ChessPieceType.Pawn, -1, 6), x, 6);
                board.SetPiece(new PieceData(Team.Black, backRank[x], -1, 7), x, 7);
            }

            board.ComputeFullZobristHash();
            return board;
        }

        /// <summary>
        /// Places a single piece at the specified algebraic coordinate and returns the
        /// board. Designed for fluent chaining: CreateEmpty().WithPiece(...).WithPiece(...).
        /// </summary>
        public static BoardState WithPiece(this BoardState board, string algebraic, Team team, ChessPieceType type, bool hasMoved = false)
        {
            Vector2Int pos = AlgebraicToVector(algebraic);
            int moveDir = team == Team.White ? 1 : -1;

            // We use pos.y as the startRow so pawn double-push tests behave correctly based on where they are initialized.
            PieceData piece = new PieceData(team, type, moveDir, pos.y, hasMoved);

            // Unpack Vector2Int to x,y since SetPiece doesn't have a Vector2Int overload
            board.SetPiece(piece, pos.x, pos.y);
            return board;
        }

        /// <summary>
        /// Converts standard algebraic notation to the engine's internal Vector2Int
        /// coordinate. File letters a–h map to x = 0–7. Ranks 1–8 map to y = 0–7.
        /// Throws ArgumentException on malformed input.
        /// </summary>
        public static Vector2Int AlgebraicToVector(string algebraic)
        {
            if (string.IsNullOrEmpty(algebraic) || algebraic.Length != 2)
                throw new ArgumentException($"Invalid algebraic notation: '{algebraic}'. Expected format like 'e4'.");

            char fileChar = char.ToLower(algebraic[0]);
            char rankChar = algebraic[1];

            if (fileChar < 'a' || fileChar > 'h' || rankChar < '1' || rankChar > '8')
                throw new ArgumentException($"Coordinate out of bounds: '{algebraic}'. Must be between a1 and h8.");

            int x = fileChar - 'a';
            int y = rankChar - '1';

            return new Vector2Int(x, y);
        }

        /// <summary>
        /// Convenience overload: extracts all EndPosition values from a list of
        /// MoveCommands and returns them as a HashSet for O(1) containment checks.
        /// </summary>
        public static HashSet<Vector2Int> GetDestinations(IEnumerable<MoveCommand> moves)
        {
            HashSet<Vector2Int> destinations = new HashSet<Vector2Int>();
            foreach (var move in moves)
            {
                destinations.Add(move.EndPosition);
            }
            return destinations;
        }

        /// <summary>
        /// Sets the en passant file directly on the board state, bypassing the normal
        /// move-execution path. Required for testing en passant capture in isolation.
        /// </summary>
        public static BoardState WithEnPassantFile(this BoardState board, int file)
        {
            board.EnPassantFile = file;
            return board;
        }

        /// <summary>
        /// Sets the castling rights bitmask directly on the board state.
        /// Use the BoardState constants (CastlingWhiteKingside, etc.) as inputs.
        /// </summary>
        public static BoardState WithCastlingRights(this BoardState board, int mask)
        {
            board.CastlingRights = mask;
            return board;
        }

        /// <summary>
        /// Sets whose turn it is on the board. Required for tests where Black's
        /// movement rules are being verified.
        /// </summary>
        public static BoardState WithTurn(this BoardState board, Team team)
        {
            board.CurrentTurn = team;
            return board;
        }

        /// <summary>
        /// Recomputes and stores the full Zobrist hash for the board's current piece
        /// configuration. Call this after manually placing pieces via WithPiece().
        /// </summary>
        public static BoardState WithComputedHash(this BoardState board)
        {
            board.ComputeFullZobristHash();
            return board;
        }
    }
}