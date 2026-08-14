using System;

namespace ChessTheBetrayal.UI.Controls
{
    /// <summary>
    /// What a panel must be able to do to serve as the face of a confirmation: put a question up,
    /// say whether one is currently up, and take it down again.
    ///
    /// <see cref="WarningPopup"/> is the only implementation, and this interface exists to keep the
    /// rules about <em>when</em> to ask (see <see cref="ConfirmationGate"/>) away from the panel that
    /// draws the asking. Those rules decide whether a move is spent or abandoned, so they need to be
    /// testable without a scene, a font asset and two Buttons to click.
    ///
    /// The two label arguments may be null, meaning the panel keeps whatever its buttons already say.
    /// </summary>
    public interface IConfirmationView
    {
        /// <summary>True from the moment a question goes up until an answer is taken.</summary>
        bool IsOpen { get; }

        void Show(string message, string confirmLabel, string cancelLabel, Action onConfirm, Action onCancel);

        /// <summary>Takes the question down without answering it. Neither callback runs.</summary>
        void Close();
    }
}
