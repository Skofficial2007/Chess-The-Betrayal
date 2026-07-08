using UnityEngine;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.View
{
    /// <summary>
    /// The narrow hover/hit-test surface pointer input needs from the board's visual layer.
    /// Separated from the concrete <see cref="BoardVisuals"/> so a consumer that only resolves
    /// screen taps to tiles and drives the hover highlight doesn't also gain compile-time
    /// visibility into piece spawning, move animation, or every other BoardVisuals responsibility.
    /// </summary>
    public interface IBoardHitTest
    {
        /// <summary>Converts a raycast hit transform into a grid coordinate (walks up the hierarchy to find the tile).</summary>
        Vector2Int GetTileIndexFromTransform(Transform t);

        /// <summary>Updates the hover highlight to the given tile (Vector2Int.Invalid clears it).</summary>
        void UpdateHoverHighlight(Vector2Int idx);

        /// <summary>Clears the hover highlight.</summary>
        void ClearHoverHighlight();
    }
}
