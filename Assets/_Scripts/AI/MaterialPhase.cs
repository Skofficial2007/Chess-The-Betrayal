using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// How far a position has progressed from the opening toward the endgame, measured purely
    /// from non-pawn material still on the board — nothing to do with move count or ply. Named
    /// around material rather than "phase" on its own so it can't be mistaken for TurnPhase or
    /// BetrayalPhase, which track the Betrayal turn state machine and are a completely different
    /// concept.
    ///
    /// A defected piece changes which side owns it, never how much material exists — summing both
    /// teams' non-pawn material together (rather than reading one side) makes that automatic: a
    /// defection moves a value out of one team's sum and into the other's, leaving the total, and
    /// therefore the phase, unchanged. There is deliberately no caching here. The search applies
    /// and undoes moves constantly, so any cached value would be stale within a single ply — this
    /// recomputes from the board's own live piece lists every time.
    /// </summary>
    internal static class MaterialPhase
    {
        // Non-pawn material value per side at the start of a game: 2 knights + 2 bishops +
        // 2 rooks + 1 queen, using the evaluator's own centipawn scale.
        internal const int OpeningNonPawnMaterial = (2 * 320) + (2 * 325) + (2 * 500) + 975;
        internal const int FullPhaseWeight = OpeningNonPawnMaterial * 2;

        /// <summary>
        /// A 0..FullPhaseWeight weight: FullPhaseWeight at full starting non-pawn material on both
        /// sides (opening/midgame), trending toward 0 as pieces come off the board (endgame).
        /// Clamped at FullPhaseWeight so a position with MORE non-pawn material than the opening
        /// (a promoted pawn, say) still reads as "fully midgame" rather than overshooting the
        /// blend range a caller applies this weight to.
        /// </summary>
        public static int Weight(BoardState board)
        {
            int total = TeamNonPawnMaterial(board, Team.White) + TeamNonPawnMaterial(board, Team.Black);
            return total >= FullPhaseWeight ? FullPhaseWeight : total;
        }

        private static int TeamNonPawnMaterial(BoardState board, Team team)
        {
            int material = 0;
            var indices = board.GetPieceIndices(team);

            for (int i = 0; i < indices.Count; i++)
            {
                int idx = indices[i];
                ChessPieceType type = board.GetPiece(idx % board.TileCountX, idx / board.TileCountX).Type;
                material += NonPawnValue(type);
            }

            return material;
        }

        private static int NonPawnValue(ChessPieceType type) => type switch
        {
            ChessPieceType.Knight => 320,
            ChessPieceType.Bishop => 325,
            ChessPieceType.Rook => 500,
            ChessPieceType.Queen => 975,
            _ => 0
        };
    }
}
