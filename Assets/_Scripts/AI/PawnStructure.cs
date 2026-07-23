using System;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// Scores one team's pawns by file structure: doubled and isolated pawns are penalized, a pawn
    /// with no enemy pawn ahead of it on its own or either neighboring file (a passed pawn) is
    /// rewarded, more so the closer it is to promotion. Reads the team's live pawn positions every
    /// call — a pawn that changes side via Defection is scored on its new team's structure on the
    /// very next call, with nothing cached from before the flip.
    /// </summary>
    internal static class PawnStructure
    {
        private const int DoubledPenaltyPerExtraPawn = 12;
        private const int IsolatedPenalty = 15;

        // Indexed by how many ranks the pawn has advanced from its own second rank (0 = still on
        // the second rank, 5 = one step from promotion). A pawn newly arrived on its starting rank
        // is not yet a meaningful threat, so the first two entries are 0; the bonus then grows
        // sharply the closer the pawn gets to queening, topping out just under the pawn table's own
        // 7th-rank peak of 50 — a passed pawn near promotion is worth roughly a pawn's worth extra,
        // which is the standard weight for this term.
        private static readonly int[] PassedBonusByAdvance = { 0, 0, 10, 20, 35, 60, 100 };

        // Hard per-side ceilings so the maximum possible gap between the two sides' pawn scores has
        // a provable closed form (see AlphaBetaSearch.MaxPositionalSwing) instead of depending on an
        // argument about which board shapes are "realistic." However many passed pawns or however
        // penalized a side's structure gets, its contribution never exceeds these bounds — a side
        // that would score more just gets scaled back down to the ceiling, proportionally across its
        // own attack/defense split so the bucketing stays internally consistent.
        internal const int MaxPassedBonusPerSide = 250;
        internal const int MaxPenaltyPerSide = 120;

        // Sentinel meaning "no enemy pawn seen on this file yet" for the min/max scans below.
        private const int NoEnemyPawn = -1;

        /// <summary>
        /// Net pawn-structure score for one team, already split into the same attack/defense buckets
        /// PieceSquareTables.Bonus uses (row 0 is the scoring side's own back rank; row >= 4 is on or
        /// past the midline). Each pawn's passed/isolated/doubled contribution is bucketed by its own
        /// square, exactly like a PST bonus would be, so the personality dials keep applying to
        /// whichever bucket a pawn actually landed in.
        /// </summary>
        public static void Score(BoardState board, Team team, out int attack, out int defense)
        {
            Team enemy = team == Team.White ? Team.Black : Team.White;
            var friendlyIndices = board.GetPieceIndices(team);
            var enemyIndices = board.GetPieceIndices(enemy);
            int fileCount = board.TileCountX;

            Span<int> friendlyFileCount = stackalloc int[fileCount];
            // Per file: how far a friendly pawn on that file could advance (in RAW board rows, not
            // team-relative) before running into an enemy pawn on that file or an adjacent one.
            // Stored as the raw row of the nearest blocking enemy pawn, from the friendly side's
            // direction of travel; NoEnemyPawn means the file (and its neighbors) are clear all the
            // way to the edge of the board on the enemy's side.
            Span<int> enemyRowOnFile = stackalloc int[fileCount];
            for (int f = 0; f < fileCount; f++) enemyRowOnFile[f] = NoEnemyPawn;

            for (int i = 0; i < friendlyIndices.Count; i++)
            {
                int idx = friendlyIndices[i];
                int x = idx % board.TileCountX;
                int y = idx / board.TileCountX;
                if (board.GetPiece(x, y).Type != ChessPieceType.Pawn) continue;
                friendlyFileCount[x]++;
            }

            bool friendlyMovesUpward = team == Team.White; // White advances toward row 7

            for (int i = 0; i < enemyIndices.Count; i++)
            {
                int idx = enemyIndices[i];
                int x = idx % board.TileCountX;
                int y = idx / board.TileCountX;
                if (board.GetPiece(x, y).Type != ChessPieceType.Pawn) continue;

                // Keep whichever enemy pawn on this file sits CLOSEST to our own back rank — that is
                // the one a friendly pawn would meet first travelling toward promotion, so it is the
                // one that actually decides whether a friendly pawn on this file is passed.
                if (enemyRowOnFile[x] == NoEnemyPawn)
                {
                    enemyRowOnFile[x] = y;
                }
                else if (friendlyMovesUpward ? y < enemyRowOnFile[x] : y > enemyRowOnFile[x])
                {
                    enemyRowOnFile[x] = y;
                }
            }

            int passedAttack = 0, passedDefense = 0;
            int penaltyAttack = 0, penaltyDefense = 0;
            int passedTotal = 0, penaltyTotal = 0;

            for (int i = 0; i < friendlyIndices.Count; i++)
            {
                int idx = friendlyIndices[i];
                int x = idx % board.TileCountX;
                int y = idx / board.TileCountX;
                PieceData piece = board.GetPiece(x, y);
                if (piece.Type != ChessPieceType.Pawn) continue;

                int advance = friendlyMovesUpward ? y : board.TileCountY - 1 - y;
                int row = advance; // pawn advancement IS the row-normalized rank already

                int passedBonus = 0;
                if (IsPassed(x, y, fileCount, friendlyMovesUpward, enemyRowOnFile))
                {
                    int clampedAdvance = advance >= PassedBonusByAdvance.Length
                        ? PassedBonusByAdvance.Length - 1
                        : advance;
                    passedBonus = PassedBonusByAdvance[clampedAdvance];
                }

                int penalty = 0;
                bool doubled = friendlyFileCount[x] > 1;
                bool isolated = (x == 0 || friendlyFileCount[x - 1] == 0)
                                && (x == fileCount - 1 || friendlyFileCount[x + 1] == 0);
                if (doubled) penalty += DoubledPenaltyPerExtraPawn;
                if (isolated) penalty += IsolatedPenalty;

                passedTotal += passedBonus;
                penaltyTotal += penalty;

                if (row >= 4)
                {
                    passedAttack += passedBonus;
                    penaltyAttack += penalty;
                }
                else
                {
                    passedDefense += passedBonus;
                    penaltyDefense += penalty;
                }
            }

            // Clamp the two totals independently, then scale each bucket by the same ratio so the
            // attack/defense split stays proportionally correct even at the ceiling. Ordinary
            // positions never approach these ceilings and this is a no-op (scale == 1) for them.
            float passedScale = passedTotal > MaxPassedBonusPerSide ? (float)MaxPassedBonusPerSide / passedTotal : 1f;
            float penaltyScale = penaltyTotal > MaxPenaltyPerSide ? (float)MaxPenaltyPerSide / penaltyTotal : 1f;

            attack = (int)(passedAttack * passedScale) - (int)(penaltyAttack * penaltyScale);
            defense = (int)(passedDefense * passedScale) - (int)(penaltyDefense * penaltyScale);
        }

        /// <summary>
        /// A friendly pawn at (file, row) is passed if no enemy pawn on its own file or either
        /// adjacent file sits between it and its own promotion rank — checked directly in raw board
        /// rows along the friendly side's own direction of travel, so this reads correctly for
        /// either color without relying on a second, team-relative coordinate scheme.
        /// </summary>
        private static bool IsPassed(int file, int row, int fileCount, bool friendlyMovesUpward, Span<int> enemyRowOnFile)
        {
            for (int f = file - 1; f <= file + 1; f++)
            {
                if (f < 0 || f >= fileCount) continue;
                int enemyRow = enemyRowOnFile[f];
                if (enemyRow == NoEnemyPawn) continue;

                bool enemyIsAheadOrLevel = friendlyMovesUpward ? enemyRow >= row : enemyRow <= row;
                if (enemyIsAheadOrLevel) return false;
            }
            return true;
        }
    }
}
