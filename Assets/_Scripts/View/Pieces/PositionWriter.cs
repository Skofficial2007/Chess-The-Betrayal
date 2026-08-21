namespace ChessTheBetrayal.View.Pieces
{
    /// <summary>
    /// The animations that move a piece around the board, and so the ones that have to take turns.
    ///
    /// They all set the piece's position outright rather than nudging it, so any two running
    /// together are overwriting each other every frame and the piece ends up wherever the one that
    /// finished last left it. Naming them makes "only one of these at a time" something that can be
    /// stated and checked instead of remembered.
    /// </summary>
    internal enum PositionWriter
    {
        /// <summary>Nothing is moving the piece.</summary>
        None = 0,

        /// <summary>A board move, including a knight's hop and a snap back to where it started.</summary>
        Move,

        /// <summary>The rook's half of a castle, which trails the king by a beat.</summary>
        Castle,

        /// <summary>A pawn walking onto the last rank before it becomes anything.</summary>
        Promotion,

        /// <summary>The run-up, leap and landing of a capture.</summary>
        Stamp,

        /// <summary>A piece on its way to its side's pile, or coming back off it.</summary>
        Death,

        /// <summary>The piece is in the player's hand: raised, and drifting while it is held.</summary>
        Lift,

        /// <summary>A king rattling where it stands because it is in check.</summary>
        Shake,
    }
}
