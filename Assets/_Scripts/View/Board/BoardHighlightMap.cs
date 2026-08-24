using System.Collections.Generic;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.View
{
    /// <summary>
    /// The shape drawn on top of a square. Each one is a different silhouette, not just a different
    /// colour, so the board still reads for a player who cannot separate the green from the red.
    /// </summary>
    public enum SquareMarker
    {
        None,
        QuietMove,
        Capture,
        BetrayalTarget,
        BetrayerAtLarge,
        BetrayerTargeted,
        Check,
        Selected
    }

    /// <summary>
    /// What a whole square carries underneath any marker — a wash for the states that follow what
    /// the player is doing right now, and an outline for the two that record the move just played.
    ///
    /// The last move's two squares are drawn as complements: the origin as bars along its edges with
    /// the corners left open, the destination as those same corners with the edges left open. One is
    /// an enclosure, the other a mark, which is what tells them apart at a glance.
    /// </summary>
    public enum SquareTint
    {
        None,
        Selected,
        Hover,
        LastMoveFrom,
        LastMoveTo
    }

    /// <summary>
    /// What one square should show: at most one tint and at most one marker.
    /// </summary>
    public readonly struct SquareHighlight
    {
        public readonly SquareTint Tint;
        public readonly SquareMarker Marker;

        public SquareHighlight(SquareTint tint, SquareMarker marker)
        {
            Tint = tint;
            Marker = marker;
        }

        public bool IsEmpty => Tint == SquareTint.None && Marker == SquareMarker.None;
    }

    /// <summary>
    /// Decides what every square should show, from the handful of facts the board knows: which piece
    /// is picked up, where the pointer is, where the last move went, whether a king is in check, and
    /// where the picked-up piece may legally go.
    ///
    /// This is deliberately plain C# with no engine types. Which square gets which marker is a rule,
    /// and rules belong somewhere they can be tested without a scene — the drawing code downstream
    /// only reads the answer and never decides anything.
    /// </summary>
    public sealed class BoardHighlightMap
    {
        private readonly HashSet<Vector2Int> _quietMoves = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> _captures = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> _betrayalTargets = new HashSet<Vector2Int>();

        public Vector2Int Selected { get; set; } = Vector2Int.Invalid;
        public Vector2Int Hover { get; set; } = Vector2Int.Invalid;
        public Vector2Int CheckSquare { get; set; } = Vector2Int.Invalid;
        public Vector2Int LastMoveFrom { get; set; } = Vector2Int.Invalid;
        public Vector2Int LastMoveTo { get; set; } = Vector2Int.Invalid;
        public Vector2Int BetrayerSquare { get; set; } = Vector2Int.Invalid;

        /// <summary>
        /// Records a legal destination. A move that opens a Betrayal wins over one that merely
        /// captures, because the same square can be reachable both ways and the Betrayal is the more
        /// consequential thing to tell the player about.
        /// </summary>
        public void AddDestination(Vector2Int square, bool isCapture, bool isBetrayal)
        {
            if (isBetrayal)
            {
                _betrayalTargets.Add(square);
                _captures.Remove(square);
                _quietMoves.Remove(square);
                return;
            }

            if (_betrayalTargets.Contains(square)) return;

            if (isCapture)
            {
                _captures.Add(square);
                _quietMoves.Remove(square);
                return;
            }

            if (!_captures.Contains(square))
            {
                _quietMoves.Add(square);
            }
        }

        /// <summary>
        /// Forgets every legal destination, leaving selection, hover, check and last-move intact —
        /// those outlive the moment a piece is put back down.
        /// </summary>
        public void ClearDestinations()
        {
            _quietMoves.Clear();
            _captures.Clear();
            _betrayalTargets.Clear();
        }

        /// <summary>
        /// Forgets everything. Used when the board itself is torn down.
        /// </summary>
        public void Clear()
        {
            ClearDestinations();
            Selected = Vector2Int.Invalid;
            Hover = Vector2Int.Invalid;
            CheckSquare = Vector2Int.Invalid;
            LastMoveFrom = Vector2Int.Invalid;
            LastMoveTo = Vector2Int.Invalid;
            BetrayerSquare = Vector2Int.Invalid;
        }

        /// <summary>
        /// What this square should show.
        ///
        /// Markers rank check first — a king in danger must never be hidden behind another marker —
        /// then the square a Betrayer is currently standing on, then betrayal, capture, quiet move,
        /// and finally the selected square's own corner ticks, which yield to anything that describes
        /// what a tap would do. The Betrayer's square ranks above Capture deliberately: while
        /// Retribution is pending, that square is itself a legal capture destination for the piece
        /// that can execute it, and the hazard is the more consequential thing to show there.
        ///
        /// That outranking used to cost the player the one thing they needed to know. Picking an
        /// executioner marks the Betrayer's square as a capture destination, and the capture marker
        /// was then swallowed, so choosing a piece that could carry out the Retribution looked
        /// exactly like choosing one that could not. The Betrayer's square therefore has two states
        /// rather than one, and reports which: at large on its own, targeted once some selected
        /// piece can actually reach it.
        ///
        /// Tints rank the picked-up square over the pointer, and the pointer over the last move,
        /// so the fainter, longer-lived hint always gives way to the more immediate one.
        /// </summary>
        public SquareHighlight Resolve(Vector2Int square)
        {
            SquareMarker marker = ResolveMarker(square);
            return new SquareHighlight(ResolveTint(square, marker), marker);
        }

        private SquareMarker ResolveMarker(Vector2Int square)
        {
            if (square == CheckSquare) return SquareMarker.Check;
            if (square == BetrayerSquare)
            {
                return _captures.Contains(square) ? SquareMarker.BetrayerTargeted : SquareMarker.BetrayerAtLarge;
            }
            if (_betrayalTargets.Contains(square)) return SquareMarker.BetrayalTarget;
            if (_captures.Contains(square)) return SquareMarker.Capture;
            if (_quietMoves.Contains(square)) return SquareMarker.QuietMove;
            if (square == Selected) return SquareMarker.Selected;
            return SquareMarker.None;
        }

        private SquareTint ResolveTint(Vector2Int square, SquareMarker marker)
        {
            if (square == Selected) return SquareTint.Selected;
            if (square == Hover) return SquareTint.Hover;
            if (square == LastMoveFrom) return SquareTint.LastMoveFrom;

            // The destination mark is drawn in the tile's corners, and so is every marker that means
            // something is about to happen on that square. Two of them there at once is unreadable,
            // and the one describing what you can do now outranks the one describing what already
            // happened — so the older mark simply stands down.
            //
            // The square a Betrayer occupies is the square the Act moved it to, so this is also what
            // clears the last move out of the way of a Retribution.
            if (square == LastMoveTo)
            {
                return OccupiesCorners(marker) ? SquareTint.None : SquareTint.LastMoveTo;
            }

            return SquareTint.None;
        }

        /// <summary>
        /// Whether this marker claims the tile's corners. The origin mark is deliberately absent from
        /// this reckoning: it draws along the edges instead, and the square a piece has just left is
        /// empty, so nothing that owns corners can be sitting on it anyway.
        /// </summary>
        private static bool OccupiesCorners(SquareMarker marker)
        {
            switch (marker)
            {
                case SquareMarker.Capture:
                case SquareMarker.BetrayalTarget:
                case SquareMarker.BetrayerAtLarge:
                case SquareMarker.BetrayerTargeted:
                case SquareMarker.Selected:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Fills the list with every square that currently shows anything, so a caller can redraw
        /// exactly those and leave the rest of the board alone. Cleared before filling.
        /// </summary>
        public void CollectActiveSquares(List<Vector2Int> into)
        {
            into.Clear();

            AddIfValid(into, Selected);
            AddIfValid(into, Hover);
            AddIfValid(into, CheckSquare);
            AddIfValid(into, LastMoveFrom);
            AddIfValid(into, LastMoveTo);
            AddIfValid(into, BetrayerSquare);

            AddRange(into, _quietMoves);
            AddRange(into, _captures);
            AddRange(into, _betrayalTargets);
        }

        private static void AddIfValid(List<Vector2Int> into, Vector2Int square)
        {
            if (square != Vector2Int.Invalid && !into.Contains(square))
            {
                into.Add(square);
            }
        }

        private static void AddRange(List<Vector2Int> into, HashSet<Vector2Int> squares)
        {
            foreach (Vector2Int square in squares)
            {
                if (!into.Contains(square))
                {
                    into.Add(square);
                }
            }
        }
    }
}
