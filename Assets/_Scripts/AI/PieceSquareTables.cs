using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// Piece-square bonuses, one 64-entry midgame table and (for the King only) one endgame table
    /// per piece type, indexed White's-perspective-first: index 0 is White's back rank (a1), index
    /// 63 is Black's back rank (h8), matching BoardState's own y=0-is-White's-first-rank
    /// convention. Black reads the same tables mirrored vertically — see <see cref="Bonus"/>.
    ///
    /// Every table except the King's is identical at both ends of the game — a knight, say, wants
    /// roughly the same squares whether material is still on the board or not, so there is no
    /// reason to introduce a second table for it here. The King is the one piece whose ideal
    /// square reverses completely: tucked behind pawns while there are enough attackers left on
    /// the board to threaten it, walking toward the center once the board has emptied out and
    /// centralization is what wins king-and-pawn races.
    /// </summary>
    internal static class PieceSquareTables
    {
        /// <summary>
        /// Blends the midgame and endgame value at (x, y) by phaseWeight, where phaseWeight runs
        /// 0 (endgame) to MaterialPhase.FullPhaseWeight (opening/midgame). At phaseWeight ==
        /// FullPhaseWeight this returns exactly the midgame table's value — bit-identical to what
        /// Bonus returned before any endgame table existed, since eg has no way to contribute.
        /// </summary>
        public static int Bonus(ChessPieceType type, int x, int y, Team team, int tileCountX, int tileCountY, int phaseWeight)
        {
            int[] mgTable = TableFor(type);
            if (mgTable == null) return 0;

            int row = team == Team.White ? y : tileCountY - 1 - y;
            int index = (row * tileCountX) + x;
            if (index < 0 || index >= mgTable.Length) return 0;

            int mgValue = mgTable[index];
            int[] egTable = EgTableFor(type);
            if (egTable == null) return mgValue;

            int egValue = egTable[index];
            return Blend(mgValue, egValue, phaseWeight);
        }

        private static int Blend(int mgValue, int egValue, int phaseWeight)
        {
            int clampedWeight = phaseWeight < 0 ? 0
                : phaseWeight > MaterialPhase.FullPhaseWeight ? MaterialPhase.FullPhaseWeight
                : phaseWeight;

            return ((mgValue * clampedWeight) + (egValue * (MaterialPhase.FullPhaseWeight - clampedWeight)))
                / MaterialPhase.FullPhaseWeight;
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

        private static int[] EgTableFor(ChessPieceType type) => type switch
        {
            ChessPieceType.King => KingEndgame,
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
        // With enough material still on the board, a centralized king is a king a queen or rook
        // can attack from multiple directions at once — this table only applies to the extent
        // Bonus's blend still weighs the midgame end.
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

        // Endgame king safety: the exact opposite instinct. With most of the attacking material
        // traded off, the king itself becomes a fighting piece — walking it toward the center
        // shortens its path to either side's pawns, which is how king-and-pawn endgames get won
        // or lost. Corners stay penalized even here: a king in the extreme corner is still worse
        // than one a single step toward the middle, just less catastrophically so than midgame.
        private static readonly int[] KingEndgame =
        {
            -50, -40, -30, -20, -20, -30, -40, -50,
            -30, -20, -10,   0,   0, -10, -20, -30,
            -30, -10,  20,  30,  30,  20, -10, -30,
            -30, -10,  30,  40,  40,  30, -10, -30,
            -30, -10,  30,  40,  40,  30, -10, -30,
            -30, -10,  20,  30,  30,  20, -10, -30,
            -30, -30,   0,   0,   0,   0, -30, -30,
            -50, -30, -30, -30, -30, -30, -30, -50,
        };
    }
}
