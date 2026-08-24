using System;

namespace ChessTheBetrayal.UI.Controls
{
    /// <summary>
    /// Stands between deciding to do something and doing it, so a move that cannot be taken back
    /// gets a second look first.
    ///
    /// The contract callers depend on is that <see cref="Ask"/> always answers: exactly one of the
    /// two callbacks runs, either straight away or once the player has pressed something. Nothing a
    /// caller sets aside while waiting can be left waiting forever — not when confirmation is turned
    /// off, not when the panel is missing from the scene, and not when the match ends underneath an
    /// open question.
    /// </summary>
    public interface IConfirmationGate
    {
        /// <summary>True while a question is on screen.</summary>
        bool IsOpen { get; }

        /// <summary>
        /// Runs <paramref name="onConfirm"/> once the player agrees, or <paramref name="onCancel"/>
        /// once they back out — and runs one of them immediately when there is nothing to ask with.
        /// See the implementation for exactly which, and why each way round.
        /// </summary>
        /// <param name="enabled">False skips the question entirely and goes ahead.</param>
        void Ask(bool enabled, in ConfirmationRequest request, Action onConfirm, Action onCancel = null);

        /// <summary>
        /// Takes down whatever is on screen because the reason for asking has gone — the match
        /// ended, the player left. Counts as backing out, so whoever asked is told rather than left
        /// holding something nobody will ever answer for.
        /// </summary>
        void Dismiss();
    }
}
