using System;
using ChessTheBetrayal.Core.Data;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Gameplay
{
    /// <summary>
    /// Device-agnostic tap/click intent. A tile was "activated" — pressed and released on the
    /// same tile, i.e. a tap or a click, not a drag. SelectionController reacts to this event
    /// alone and has zero knowledge of mouse, touch, or (later) keyboard/controller input.
    /// </summary>
    public interface ISelectionInput
    {
        event Action<Vector2Int> OnTileActivated;
    }
}
