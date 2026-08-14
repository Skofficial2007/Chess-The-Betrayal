using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.AI.Evaluation
{
    /// <summary>
    /// Rewards the attacking king closing the distance to a lone (or nearly lone) enemy king once
    /// this side holds mating material and the enemy has none — the missing gradient a real King+
    /// Queen vs King probe exposed: the queen alone confined the enemy king to the edge correctly,
    /// then every root move tied in score because nothing rewarded the attacking king finishing the
    /// job, and the search cycled forever between equally-scored non-progressing moves.
    ///
    /// Deliberately narrow. This is NOT general mating-technique or KPK knowledge — a king-and-pawn
    /// race already converts without it (MaterialPhase.Weight in a pawn-only endgame doesn't gate
    /// this on, see below), and adding a broad "walk toward the enemy king" bias outside a genuine
    /// bare-king mating scenario would actively hurt play (a king should not chase the enemy king
    /// while real material is still on the board). The gate below requires the DEFENDING side to
    /// have zero non-pawn material — the exact shape a KRK/KQK/KNNK-family finish needs, and
    /// nothing broader.
    /// </summary>
    internal static class EndgameKingApproach
    {
        // Scaled so fully closing the distance is worth a clean fraction of a pawn — enough to break
        // a tie between "approach" and "shuffle" without competing with material or mate-distance
        // scoring, which this term never overrides (both sides route through full search, so an
        // actual mate always outscores getting one square closer).
        private const int MaxApproachBonus = 60;

        internal const int MaxKingApproachPerSide = MaxApproachBonus;

        /// <summary>
        /// One team's king-approach bonus: 0 unless this team holds mating material and the enemy
        /// has none, in which case it rewards a smaller Chebyshev distance between the two kings.
        /// Always attack-side by construction — closing in on the enemy king is offense, the mirror
        /// of KingSafety's defense-only danger score.
        /// </summary>
        public static int Score(BoardState board, Team team)
        {
            Team enemy = team == Team.White ? Team.Black : Team.White;

            if (!HasNonPawnMaterial(board, team) || HasNonPawnMaterial(board, enemy)) return 0;
            if (!board.TryFindKing(team, out Vector2Int king)) return 0;
            if (!board.TryFindKing(enemy, out Vector2Int enemyKing)) return 0;

            // The board's own largest possible Chebyshev distance (corner to corner) — read live
            // rather than assumed as 8x8, since BoardState supports other dimensions elsewhere
            // (GameManager can construct a board with a configurable size).
            int maxKingDistance = System.Math.Max(board.TileCountX, board.TileCountY) - 1;
            if (maxKingDistance <= 0) return 0;

            int distance = ChebyshevDistance(king, enemyKing);
            int closed = maxKingDistance - distance;
            if (closed < 0) closed = 0;

            int bonus = (closed * MaxApproachBonus) / maxKingDistance;
            return bonus > MaxApproachBonus ? MaxApproachBonus : bonus;
        }

        private static bool HasNonPawnMaterial(BoardState board, Team team)
        {
            var indices = board.GetPieceIndices(team);
            for (int i = 0; i < indices.Count; i++)
            {
                int idx = indices[i];
                ChessPieceType type = board.GetPiece(idx % board.TileCountX, idx / board.TileCountX).Type;
                if (type != ChessPieceType.King && type != ChessPieceType.Pawn) return true;
            }
            return false;
        }

        private static int ChebyshevDistance(Vector2Int a, Vector2Int b)
        {
            int dx = a.x - b.x;
            int dy = a.y - b.y;
            if (dx < 0) dx = -dx;
            if (dy < 0) dy = -dy;
            return dx > dy ? dx : dy;
        }
    }
}
