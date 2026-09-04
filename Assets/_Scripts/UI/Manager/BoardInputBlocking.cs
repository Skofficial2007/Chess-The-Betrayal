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
        /// Names whatever is stopping the board, or null when nothing is.
        ///
        /// Worth a name rather than a flag because the two causes look identical from the board's
        /// side and nothing alike from the player's. A panel is something they can see and close. A
        /// question that never drew itself leaves a live-looking board that ignores every tap, and
        /// told apart only by a yes or no, that case has to be reconstructed from memory afterwards.
        ///
        /// <paramref name="coveringPanel"/> is the manager's own read of its panels, which needs a
        /// scene. <paramref name="question"/> is null before the gate exists and while there is
        /// nothing to ask with, neither of which blocks anything.
        /// </summary>
        public static string WhatBlocksTheBoard(string coveringPanel, IConfirmationGate question) =>
            coveringPanel ?? (question != null && question.IsOpen ? "a confirmation question" : null);

        /// <summary>
        /// The same rule as a yes or no, for callers with nothing to say about the answer.
        /// </summary>
        public static bool BlocksTheBoard(bool aPanelIsCoveringTheBoard, IConfirmationGate question) =>
            WhatBlocksTheBoard(aPanelIsCoveringTheBoard ? "a panel" : null, question) != null;
    }
}
