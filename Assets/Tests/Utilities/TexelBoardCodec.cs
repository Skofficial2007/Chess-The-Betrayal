using System;
using System.Globalization;
using System.Text;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>
    /// Round-trips the slice of BoardState a static evaluation actually reads — piece placement,
    /// side to move, and whether the Betrayal right is still live — to and from a single compact
    /// string, so a sampled position can be written to a corpus file and reconstructed later without
    /// replaying the game that produced it.
    ///
    /// Deliberately narrower than a full position replay: HasMoved, MoveDirection, StartRow,
    /// en passant file, and castling rights never reach BetrayalAwareEvaluator.Evaluate, so they are
    /// not encoded. A reconstructed board is not safe to feed back into move generation or the
    /// search — only into an evaluator, which is the only consumer a Texel corpus has.
    /// </summary>
    public static class TexelBoardCodec
    {
        private const char EmptySquare = '.';

        /// <summary>Encodes board dimensions and every square's occupant (team+type, or '.' for
        /// empty) as one token, scanning rank 0 to TileCountY-1, file 0 to TileCountX-1 per rank —
        /// the same order BoardState's own storage uses, so decoding needs no separate index math.</summary>
        public static string Encode(BoardState board)
        {
            var sb = new StringBuilder(board.TileCountX * board.TileCountY + 16);
            sb.Append(board.TileCountX).Append('x').Append(board.TileCountY).Append(':');

            for (int y = 0; y < board.TileCountY; y++)
            {
                for (int x = 0; x < board.TileCountX; x++)
                {
                    PieceData piece = board.GetPiece(x, y);
                    sb.Append(SquareChar(piece));
                }
            }

            return sb.ToString();
        }

        /// <summary>Reconstructs a BoardState from Encode's output. Only piece placement is
        /// restored — callers that also need side to move / Betrayal state must set BoardState's
        /// own CurrentTurn / BetrayalRightAvailable properties after this returns, exactly as
        /// TexelPositionRecord.ToBoardState does.</summary>
        public static BoardState Decode(string encoded)
        {
            int colon = encoded.IndexOf(':');
            if (colon < 0) throw new FormatException($"Malformed board encoding, missing ':': '{encoded}'");

            string dims = encoded.Substring(0, colon);
            int xIndex = dims.IndexOf('x');
            if (xIndex < 0) throw new FormatException($"Malformed board dimensions: '{dims}'");

            int tileCountX = int.Parse(dims.Substring(0, xIndex), CultureInfo.InvariantCulture);
            int tileCountY = int.Parse(dims.Substring(xIndex + 1), CultureInfo.InvariantCulture);

            string squares = encoded.Substring(colon + 1);
            if (squares.Length != tileCountX * tileCountY)
                throw new FormatException($"Expected {tileCountX * tileCountY} square characters, got {squares.Length}: '{encoded}'");

            var board = new BoardState(tileCountX, tileCountY);
            int i = 0;
            for (int y = 0; y < tileCountY; y++)
            {
                for (int x = 0; x < tileCountX; x++)
                {
                    PieceData piece = ParseSquare(squares[i++]);
                    if (!piece.IsEmpty) board.SetPiece(piece, x, y);
                }
            }

            board.ComputeFullZobristHash();
            return board;
        }

        private static char SquareChar(PieceData piece)
        {
            if (piece.IsEmpty) return EmptySquare;
            char c = piece.Type switch
            {
                ChessPieceType.Pawn => 'p',
                ChessPieceType.Knight => 'n',
                ChessPieceType.Bishop => 'b',
                ChessPieceType.Rook => 'r',
                ChessPieceType.Queen => 'q',
                ChessPieceType.King => 'k',
                _ => throw new ArgumentOutOfRangeException(nameof(piece), piece.Type, "Unknown piece type.")
            };
            return piece.Team == Team.White ? char.ToUpperInvariant(c) : c;
        }

        private static PieceData ParseSquare(char c)
        {
            if (c == EmptySquare) return PieceData.Empty;

            Team team = char.IsUpper(c) ? Team.White : Team.Black;
            ChessPieceType type = char.ToLowerInvariant(c) switch
            {
                'p' => ChessPieceType.Pawn,
                'n' => ChessPieceType.Knight,
                'b' => ChessPieceType.Bishop,
                'r' => ChessPieceType.Rook,
                'q' => ChessPieceType.Queen,
                'k' => ChessPieceType.King,
                _ => throw new FormatException($"Unrecognized square character '{c}'.")
            };
            int moveDirection = team == Team.White ? 1 : -1;
            // StartRow only matters for pawn double-push/en-passant legality, which this codec's
            // consumer (an evaluator, never move generation) never checks — 0 is a safe placeholder.
            return new PieceData(team, type, moveDirection, startRow: 0, hasMoved: true);
        }
    }
}
