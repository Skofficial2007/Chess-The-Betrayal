using ChessTheBetrayal.UI.Controls;

namespace ChessTheBetrayal.UI.Manager
{
    /// <summary>
    /// Whether anything on screen should stop the board reacting to a tap.
    ///
    /// Kept apart from the manager that answers it because the manager can only answer it inside a
    /// running scene, and this is the half worth checking. A panel covering the board is the obvious
    /// case and the one nobody gets wrong. The question is the other one: a confirmation already
    /// blocks everything drawn on the same canvas, so it looks handled, but a tap on the board is a
    /// raycast into the world and knows nothing about canvases. Without the second half of this rule
    /// a player can tap twice through the dimmed panel, pick a piece up and light its moves behind
    /// the very question asking about the last one.
    /// </summary>
    public static class BoardInputBlocking
    {
        /// <summary>
        /// <paramref name="aPanelIsCoveringTheBoard"/> is the manager's own read of its panels, which
        /// needs a scene. <paramref name="question"/> is null before the gate exists and while there
        /// is nothing to ask with, neither of which blocks anything.
        /// </summary>
        public static bool BlocksTheBoard(bool aPanelIsCoveringTheBoard, IConfirmationGate question) =>
            aPanelIsCoveringTheBoard || (question != null && question.IsOpen);
    }
}
