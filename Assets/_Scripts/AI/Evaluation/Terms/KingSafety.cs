using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// Scores how exposed one team's king is: enemy pieces massing near it, files with no friendly
    /// pawn shielding it, and — the threat class unique to this variant — the team's own piece
    /// standing in the king's zone while it is the pending Betrayer, one Defection away from
    /// reappearing as an enemy piece right next to the king. Reads live piece positions every call,
    /// same as PawnStructure; nothing is cached across a move.
    /// </summary>
    internal static class KingSafety
    {
        private const int ZoneRadius = 2;

        private const int OpenFilePenalty = 12;

        // A pending Betrayer that belongs to the king's own team, sitting in the king's own zone, is
        // about to defect and reappear as an enemy piece on that same square with no time spent
        // getting there — real, situational tempo danger a standard king-safety scan can't see.
        private const int DefectorTempoDanger = 30;

        // Hard per-side ceiling so the maximum possible king-safety swing has a provable closed form
        // (see AlphaBetaSearch.MaxPositionalSwing), exactly like PawnStructure's own ceilings. The
        // term never scores below -MaxKingSafetyPerSide however many attackers/open files it finds.
        internal const int MaxKingSafetyPerSide = 100;

        /// <summary>
        /// One team's king-safety score: 0 for a fully sheltered king, more negative the more
        /// exposed it is, clamped at -MaxKingSafetyPerSide. Always defense-side by construction —
        /// danger to your own king is never an attacking-side concept the way a passed pawn is.
        /// </summary>
        public static int Score(BoardState board, Team team)
        {
            if (!board.TryFindKing(team, out Vector2Int kingPos)) return 0;

            int danger = ZoneAttackDanger(board, team, kingPos) + OpenFileDanger(board, team, kingPos);

            if (board.PendingBetrayerSquare.HasValue
                && board.BetrayalInitiator == team
                && InZone(kingPos, board.PendingBetrayerSquare.Value))
            {
                danger += DefectorTempoDanger;
            }

            if (danger > MaxKingSafetyPerSide) danger = MaxKingSafetyPerSide;

            return -danger;
        }

        private static int ZoneAttackDanger(BoardState board, Team team, Vector2Int kingPos)
        {
            Team enemy = team == Team.White ? Team.Black : Team.White;
            var enemyIndices = board.GetPieceIndices(enemy);

            int danger = 0;
            for (int i = 0; i < enemyIndices.Count; i++)
            {
                int idx = enemyIndices[i];
                var pos = new Vector2Int(idx % board.TileCountX, idx / board.TileCountX);
                if (!InZone(kingPos, pos)) continue;

                PieceData piece = board.GetPiece(pos.x, pos.y);
                danger += AttackerWeight(piece.Type);
            }
            return danger;
        }

        private static int OpenFileDanger(BoardState board, Team team, Vector2Int kingPos)
        {
            var friendlyIndices = board.GetPieceIndices(team);
            int friendlyPawnFiles = 0;
            for (int i = 0; i < friendlyIndices.Count; i++)
            {
                int idx = friendlyIndices[i];
                int x = idx % board.TileCountX;
                int y = idx / board.TileCountX;
                if (board.GetPiece(x, y).Type == ChessPieceType.Pawn) friendlyPawnFiles |= 1 << x;
            }

            int danger = 0;
            for (int file = kingPos.x - 1; file <= kingPos.x + 1; file++)
            {
                if (file < 0 || file >= board.TileCountX) continue;
                if ((friendlyPawnFiles & (1 << file)) == 0) danger += OpenFilePenalty;
            }
            return danger;
        }

        private static bool InZone(Vector2Int kingPos, Vector2Int square)
        {
            int dx = square.x - kingPos.x;
            int dy = square.y - kingPos.y;
            if (dx < 0) dx = -dx;
            if (dy < 0) dy = -dy;
            return (dx > dy ? dx : dy) <= ZoneRadius;
        }

        private static int AttackerWeight(ChessPieceType type) => type switch
        {
            ChessPieceType.Queen  => 24,
            ChessPieceType.Rook   => 12,
            ChessPieceType.Bishop => 9,
            ChessPieceType.Knight => 9,
            ChessPieceType.Pawn   => 4,
            _ => 0
        };
    }
}
