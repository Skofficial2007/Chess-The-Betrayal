using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Core.Match
{
    /// <summary>
    /// The read/act surface the presentation layer needs from the match host (GameManager),
    /// expressed as a Core abstraction so that UI/View/interaction code never depends on the
    /// concrete GameManager MonoBehaviour.
    ///
    /// Why this exists: GameManager (top-level composition root) references UIManager, and the
    /// UI/View/input scripts in turn need to query the match — a direct concrete dependency both
    /// ways is a hard assembly cycle. Consumers resolve this interface via ServiceLocator instead,
    /// so every reference points *down* into Core and the assembly graph stays acyclic.
    ///
    /// GameManager registers itself under this interface in Awake. Every method here already
    /// delegates to MatchDriver internally — this is purely the seam, not new behavior.
    /// </summary>
    public interface IBoardQuery
    {
        /// <summary>True while a match is in progress (phase is not GameOver).</summary>
        bool IsGameActive { get; }

        /// <summary>Submits a move request. Result comes back asynchronously via the event channels.</summary>
        void RequestMove(Vector2Int from, Vector2Int to);

        /// <summary>Whether the piece at the given square may be selected this turn.</summary>
        bool CanSelectPiece(Vector2Int position);

        /// <summary>
        /// All legal moves for the piece at the given square. Returns a reused buffer — callers
        /// must consume it before the next call (SelectionController / MoveHighlightView do).
        /// </summary>
        IReadOnlyList<MoveCommand> GetLegalMovesAt(Vector2Int position);
    }
}
