using System;
using UnityEngine;

namespace ChessTheBetrayal.UI.Controls
{
    /// <summary>
    /// Decides whether an action gets a second look before it happens, and carries the answer back.
    ///
    /// The one promise every caller is built on: <see cref="Ask"/> always answers. Exactly one of
    /// the two callbacks runs — immediately when there is nothing to ask with, otherwise when the
    /// player presses something. A caller that sets a move aside while waiting can therefore never
    /// be left holding it, which is the difference between a cancelled tap and a match the player
    /// can no longer play.
    ///
    /// Which way round each of the "nothing to ask with" cases falls is a judgement, and the two
    /// judgements point in opposite directions on purpose:
    ///
    ///   No panel wired      → go ahead. A scene missing its panel must lose the confirmation, not
    ///                         the action — the alternative is a player who cannot take their turn
    ///                         because of an Inspector slot nobody filled in.
    ///   Nothing to say      → back out. An empty question still covers the screen and still eats
    ///                         every tap, so agreeing to it would mean agreeing to something nobody
    ///                         could read.
    ///   Already asking      → back out, for the newcomer only. The question on screen belongs to
    ///                         somebody who is still waiting on it, and answering it for them is
    ///                         exactly the harm this class exists to prevent.
    ///
    /// Holding the callbacks here rather than handing them straight to the panel is what makes
    /// <see cref="Dismiss"/> honest: a match that ends underneath an open question can take it down
    /// and still tell whoever asked.
    ///
    /// No Unity types beyond the console, so all of the above can be pinned by tests without a
    /// scene, a font asset and two buttons to click.
    /// </summary>
    public sealed class ConfirmationGate : IConfirmationGate
    {
        private readonly IConfirmationView _view;

        // Built once, not per press. Every question routes its two buttons through the same pair,
        // so a player who keeps changing their mind costs nothing.
        private readonly Action _handleConfirmed;
        private readonly Action _handleCancelled;

        private Action _onConfirm;
        private Action _onCancel;

        public bool IsOpen { get; private set; }

        /// <param name="view">The panel that asks. Null is allowed and handled — see the class doc.</param>
        public ConfirmationGate(IConfirmationView view)
        {
            _view = view;
            _handleConfirmed = HandleConfirmed;
            _handleCancelled = HandleCancelled;
        }

        public void Ask(bool enabled, in ConfirmationRequest request, Action onConfirm, Action onCancel = null)
        {
            if (IsOpen)
            {
                Debug.LogError("[ConfirmationGate] A question is already on screen; the new one was refused.");
                onCancel?.Invoke();
                return;
            }

            if (!enabled)
            {
                onConfirm?.Invoke();
                return;
            }

            if (_view == null)
            {
                Debug.LogError("[ConfirmationGate] No panel to ask with, so the action went ahead unconfirmed.");
                onConfirm?.Invoke();
                return;
            }

            if (!request.IsValid)
            {
                Debug.LogError("[ConfirmationGate] A question with no words was refused.");
                onCancel?.Invoke();
                return;
            }

            _onConfirm = onConfirm;
            _onCancel = onCancel;
            IsOpen = true;

            _view.Show(request.Message, request.ConfirmLabel, request.CancelLabel, _handleConfirmed, _handleCancelled);
        }

        public void Dismiss()
        {
            if (!IsOpen) return;

            Action cancelled = _onCancel;
            ClearPending();

            // Closed before the callback runs, so a caller that answers by asking something else
            // doesn't have its new question shut again on the way out.
            _view?.Close();
            cancelled?.Invoke();
        }

        private void HandleConfirmed() => Answer(_onConfirm);

        private void HandleCancelled() => Answer(_onCancel);

        /// <summary>
        /// The callback is read out and this gate fully reset BEFORE it runs, so a caller is free to
        /// ask the next question from inside its own answer.
        /// </summary>
        private void Answer(Action callback)
        {
            if (!IsOpen) return;

            Action chosen = callback;
            ClearPending();
            chosen?.Invoke();
        }

        private void ClearPending()
        {
            IsOpen = false;
            _onConfirm = null;
            _onCancel = null;
        }
    }
}
