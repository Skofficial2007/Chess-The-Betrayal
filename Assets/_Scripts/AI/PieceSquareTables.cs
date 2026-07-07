using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// Static midgame piece-square bonuses, one flat 64-entry table per piece type, indexed
    /// White's-perspective-first: index 0 is White's back rank (a1), index 63 is Black's back
    /// rank (h8), matching BoardState's own y=0-is-White's-first-rank convention. Black reads the
    /// same table mirrored vertically — see <see cref="Bonus"/>.
    /// </summary>
    internal static class PieceSquareTables
    {
        public static int Bonus(ChessPieceType type, int x, int y, Team team, int tileCountX, int tileCountY)
        {
            int[] table = TableFor(type);
            if (table == null) return 0;

            int row = team == Team.White ? y : tileCountY - 1 - y;
            int index = (row * tileCountX) + x;
            if (index < 0 || index >= table.Length) return 0;

            return table[index];
        }

        private static int[] TableFor(ChessPieceType type) => type switch
        {
            ChessPieceType.Pawn => Pawn,
            ChessPieceType.Knight => Knight,
            ChessPieceType.Bishop => Bishop,
            ChessPieceType.Rook => Rook,
            ChessPieceType.Queen => Queen,
            ChessPieceType.King => King,
            _ => null
        };

        // Rank 1 first (White's back rank), rank 8 last — mirrored for Black in Bonus().
        private static readonly int[] Pawn =
        {
              0,   0,   0,   0,   0,   0,   0,   0,
              5,  10,  10, -20, -20,  10,  10,   5,
              5,  -5, -10,   0,   0, -10,  -5,   5,
              0,   0,   0,  20,  20,   0,   0,   0,
              5,   5,  10,  25,  25,  10,   5,   5,
             10,  10,  20,  30,  30,  20,  10,  10,
             50,  50,  50,  50,  50,  50,  50,  50,
              0,   0,   0,   0,   0,   0,   0,   0,
        };

        private static readonly int[] Knight =
        {
            -50, -40, -30, -30, -30, -30, -40, -50,
            -40, -20,   0,   5,   5,   0, -20, -40,
            -30,   5,  10,  15,  15,  10,   5, -30,
            -30,   0,  15,  20,  20,  15,   0, -30,
            -30,   5,  15,  20,  20,  15,   5, -30,
            -30,   0,  10,  15,  15,  10,   0, -30,
            -40, -20,   0,   0,   0,   0, -20, -40,
            -50, -40, -30, -30, -30, -30, -40, -50,
        };

        private static readonly int[] Bishop =
        {
            -20, -10, -10, -10, -10, -10, -10, -20,
            -10,   5,   0,   0,   0,   0,   5, -10,
            -10,  10,  10,  10,  10,  10,  10, -10,
            -10,   0,  10,  10,  10,  10,   0, -10,
            -10,   5,   5,  10,  10,   5,   5, -10,
            -10,   0,   5,  10,  10,   5,   0, -10,
            -10,   0,   0,   0,   0,   0,   0, -10,
            -20, -10, -10, -10, -10, -10, -10, -20,
        };

        private static readonly int[] Rook =
        {
              0,   0,   0,   5,   5,   0,   0,   0,
             -5,   0,   0,   0,   0,   0,   0,  -5,
             -5,   0,   0,   0,   0,   0,   0,  -5,
             -5,   0,   0,   0,   0,   0,   0,  -5,
             -5,   0,   0,   0,   0,   0,   0,  -5,
             -5,   0,   0,   0,   0,   0,   0,  -5,
              5,  10,  10,  10,  10,  10,  10,   5,
              0,   0,   0,   0,   0,   0,   0,   0,
        };

        private static readonly int[] Queen =
        {
            -20, -10, -10,  -5,  -5, -10, -10, -20,
            -10,   0,   5,   0,   0,   0,   0, -10,
            -10,   5,   5,   5,   5,   5,   0, -10,
              0,   0,   5,   5,   5,   5,   0,  -5,
             -5,   0,   5,   5,   5,   5,   0,  -5,
            -10,   0,   5,   5,   5,   5,   0, -10,
            -10,   0,   0,   0,   0,   0,   0, -10,
            -20, -10, -10,  -5,  -5, -10, -10, -20,
        };

        // Midgame king safety: reward staying tucked behind the back rank, punish the center.
        private static readonly int[] King =
        {
             20,  30,  10,   0,   0,  10,  30,  20,
             20,  20,   0,   0,   0,   0,  20,  20,
            -10, -20, -20, -20, -20, -20, -20, -10,
            -20, -30, -30, -40, -40, -30, -30, -20,
            -30, -40, -40, -50, -50, -40, -40, -30,
            -30, -40, -40, -50, -50, -40, -40, -30,
            -30, -40, -40, -50, -50, -40, -40, -30,
            -30, -40, -40, -50, -50, -40, -40, -30,
        };
    }
}
