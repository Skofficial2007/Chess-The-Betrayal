namespace ChessTheBetrayal.Core.Engine
{
    /// <summary>
    /// Squeezes a move down to nineteen bits — From(6) | To(6) | PromotedTo(4) | Stage(3) — so it
    /// can be compared, held in a transposition table slot or written into an opening book without
    /// carrying a whole MoveCommand around. It reads nothing but the four fields that identify a
    /// move, which is why it sits beside MoveCommand rather than beside any one of its callers.
    ///
    /// It is lossy on purpose and is never turned back into a MoveCommand. Everything that reads
    /// one matches it against the packed form of a freshly generated legal move, so a stale or
    /// hash-collided value can mis-order a node or miss a book entry, but can never produce a move
    /// that isn't legal in the position at hand.
    /// </summary>
    public static class PackedMove
    {
        public static uint Pack(MoveCommand m)
        {
            uint from = (uint)(m.StartPosition.y * 8 + m.StartPosition.x) & 0x3F;
            uint to = (uint)(m.EndPosition.y * 8 + m.EndPosition.x) & 0x3F;
            uint promo = (uint)m.PromotedTo & 0xF;
            uint stage = (uint)m.Stage & 0x7;
            return from | (to << 6) | (promo << 12) | (stage << 16);
        }
    }
}
