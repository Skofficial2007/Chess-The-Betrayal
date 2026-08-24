using UnityEngine;

namespace ChessTheBetrayal.View
{
    /// <summary>
    /// One frame's worth of raw pointer facts, whatever hardware produced them. Deliberately
    /// carries no judgement about what those facts mean — every rule about when a position is
    /// worth reading and what counts as a tap lives in PointerTapRecognizer, which can be tested
    /// without a device attached. Everything behind this interface is a straight mirror of the
    /// engine's input API and therefore cannot be tested at all, so the less it decides the better.
    /// </summary>
    public interface IPointerDevice
    {
        /// <summary>
        /// True for hardware that always has a meaningful cursor position, whether or not a button
        /// is held — a mouse. False for a finger, which has no position between taps: the last
        /// place it touched is stale data, not a hover.
        /// </summary>
        bool ReportsPositionWhileIdle { get; }

        /// <summary>True while the primary button/finger is down. False on the frame it lifts.</summary>
        bool IsPressed { get; }

        /// <summary>True only on the frame the primary button/finger went down.</summary>
        bool WasPressed { get; }

        /// <summary>True only on the frame the primary button/finger came up.</summary>
        bool WasReleased { get; }

        /// <summary>Screen-space position. Only meaningful on frames the recognizer accepts.</summary>
        Vector2 Position { get; }
    }
}
