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
        Check,
        Selected
    }

    /// <summary>
    /// A wash across a whole square, drawn underneath any marker.
    /// </summary>
    public enum SquareTint
    {
        None,
        Selected,
        Hover,
        LastMove
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
        }

        /// <summary>
        /// What this square should show.
        ///
        /// Markers rank check first — a king in danger must never be hidden behind another marker —
        /// then betrayal, capture, quiet move, and finally the selected square's own corner ticks,
        /// which yield to anything that describes what a tap would do.
        ///
        /// Tints rank the picked-up square over the pointer, and the pointer over the last move,
        /// so the fainter, longer-lived hint always gives way to the more immediate one.
        /// </summary>
        public SquareHighlight Resolve(Vector2Int square)
        {
            return new SquareHighlight(ResolveTint(square), ResolveMarker(square));
        }

        private SquareMarker ResolveMarker(Vector2Int square)
        {
            if (square == CheckSquare) return SquareMarker.Check;
            if (_betrayalTargets.Contains(square)) return SquareMarker.BetrayalTarget;
            if (_captures.Contains(square)) return SquareMarker.Capture;
            if (_quietMoves.Contains(square)) return SquareMarker.QuietMove;
            if (square == Selected) return SquareMarker.Selected;
            return SquareMarker.None;
        }

        private SquareTint ResolveTint(Vector2Int square)
        {
            if (square == Selected) return SquareTint.Selected;
            if (square == Hover) return SquareTint.Hover;
            if (square == LastMoveFrom || square == LastMoveTo) return SquareTint.LastMove;
            return SquareTint.None;
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
