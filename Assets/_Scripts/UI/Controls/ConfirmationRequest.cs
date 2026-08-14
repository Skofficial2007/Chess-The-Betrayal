namespace ChessTheBetrayal.UI.Controls
{
    /// <summary>
    /// One question, ready to be asked: the words, and optionally what the two buttons should say.
    ///
    /// Separating the question from the asking is what lets a caller build its wording once, as a
    /// static readonly field, and hand the same value over on every press — the copy is fixed, so
    /// rebuilding the string each time a player taps would be work done for nothing.
    ///
    /// Leave either label null and the panel keeps what its buttons already read, which is the
    /// common case: most questions in this game are answered with the same Continue and Back.
    /// </summary>
    public readonly struct ConfirmationRequest
    {
        public readonly string Message;
        public readonly string ConfirmLabel;
        public readonly string CancelLabel;

        public ConfirmationRequest(string message, string confirmLabel = null, string cancelLabel = null)
        {
            Message = message;
            ConfirmLabel = confirmLabel;
            CancelLabel = cancelLabel;
        }

        /// <summary>
        /// Whether this is worth putting in front of someone. A question with nothing written on it
        /// is worse than no question at all: the panel still covers the screen and still swallows
        /// every tap, so the player is left staring at a blank box with no way to tell what agreeing
        /// would even mean.
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(Message);
    }
}
