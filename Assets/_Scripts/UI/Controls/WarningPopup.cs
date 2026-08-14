using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PrimeTween;
using ChessTheBetrayal.Core.Diagnostics;

namespace ChessTheBetrayal.UI.Controls
{
    /// <summary>
    /// A two-choice confirmation panel: a message, a button that goes ahead, and a button that
    /// backs out. Knows nothing about what it is asking — the caller supplies the words and both
    /// answers — so one prefab covers every "are you sure" this project ever needs rather than a
    /// new panel and a new script each time.
    ///
    /// Switches its own GameObject off when closed, which takes everything under it with it: the
    /// dimmed backdrop, the box, and any decoration added later without this script being told
    /// about it. Toggling named children instead would mean a backdrop added tomorrow is one nobody
    /// remembered to hide, and a layer left active with only its alpha at zero still eats every tap
    /// that lands on it.
    ///
    /// Because of that it is safe to leave inactive in the scene. Setup is done on first use rather
    /// than only in Awake — a component on an inactive object never gets an Awake, so a popup
    /// authored hidden would otherwise come up with buttons that quietly do nothing.
    ///
    /// The box scales while the backdrop only fades, because they are doing different jobs: a panel
    /// arriving wants to feel like it sprang from somewhere, while a dimming layer that scales reads
    /// as a growing rectangle rather than as the room going dark. That split needs no reference to
    /// the backdrop at all — the whole subtree fades together through one CanvasGroup here, and only
    /// the box is named, because only the box scales.
    ///
    /// Inspector Setup:
    ///   WarningParent (this component + CanvasGroup)    ← may be left inactive
    ///     ├─ Background   (Image)                       ← not referenced; fades with everything else
    ///     └─ WarningPopUp (RectTransform)               ← assign as Panel
    ///          └─ InsidePanel
    ///               ├─ Title
    ///               ├─ InfoArea    (TMP_Text)           ← assign as Message Text
    ///               └─ ButtonPanel
    ///                    ├─ Continue                    ← assign as Confirm Button
    ///                    └─ Back                        ← assign as Cancel Button
    ///
    /// The two button label fields are optional. Leave them empty and the buttons keep whatever the
    /// prefab says; assign them and a caller can rename the pair per question — "Continue/Back" for
    /// one, "Yes/No" or "Delete/Keep" for another — off the same prefab.
    ///
    /// Callers that reach this through <see cref="IConfirmationView"/> get it via
    /// <see cref="ConfirmationGate"/> instead of holding it directly, which is what lets the rules
    /// about when to ask be tested away from the scene. A screen that owns its own popup outright
    /// (the device benchmark does) is welcome to keep calling it straight.
    /// </summary>
    public class WarningPopup : MonoBehaviour, IConfirmationView
    {
        [Header("Panel")]
        [Tooltip("The box that scales. A child of this object, never this object — scaling the root would scale the backdrop with it.")]
        [SerializeField] private RectTransform _panel;

        [Header("Content")]
        [SerializeField] private TMP_Text _messageText;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _cancelButton;

        [Header("Button Labels (optional)")]
        [Tooltip("Assign to let a caller rename this button per question. Left empty, the prefab's own label stands.")]
        [SerializeField] private TMP_Text _confirmLabel;
        [SerializeField] private TMP_Text _cancelLabel;

        [Header("Animation")]
        [Tooltip("Scale the box springs up from. Slightly under 1 reads as a pop; starting at 0 reads as rubber.")]
        [SerializeField, Range(0.1f, 1f)] private float _openFromScale = 0.85f;

        [Tooltip("Scale the box shrinks toward as it leaves.")]
        [SerializeField, Range(0.1f, 1f)] private float _closeToScale = 0.92f;

        [SerializeField, Min(0.01f)] private float _openDuration = 0.22f;

        // Shorter than opening on purpose: appearing wants presence, leaving wants to be out of the
        // way. A dismissal that takes as long as the entrance feels like the app is arguing.
        [SerializeField, Min(0.01f)] private float _closeDuration = 0.13f;

        [Tooltip("Overshoot on the way in — the box passes its resting size and settles back.")]
        [SerializeField] private Ease _openEase = Ease.OutBack;

        [SerializeField] private Ease _closeEase = Ease.InQuad;

        private CanvasGroup _group;
        private Sequence _animation;
        private Action _onConfirm;
        private Action _onCancel;
        private Vector3 _restScale = Vector3.one;
        private bool _isReady;

        /// <summary>True from the moment a question goes up until an answer is taken.</summary>
        public bool IsOpen { get; private set; }

        private void Awake()
        {
            EnsureReady();

            // However the prefab was left after editing, a popup nobody has asked for must not be
            // on screen at startup. Guarded by IsOpen because Show sets that flag before activating
            // this object, and activating it is exactly what triggers this Awake the first time —
            // without the guard, a popup would switch itself off in the middle of being shown.
            if (!IsOpen) gameObject.SetActive(false);
        }

        private void OnDestroy() => _animation.Stop();

        /// <summary>
        /// Everything Awake would normally do, done on demand instead. A component on an inactive
        /// GameObject gets no Awake at all, so a prefab authored hidden would reach its first Show
        /// with no button listeners and no CanvasGroup — buttons that look right and do nothing.
        /// Runs once; every call after the first is a bool check.
        /// </summary>
        private void EnsureReady()
        {
            if (_isReady) return;
            _isReady = true;

            InspectorGuard.Require(_panel, nameof(_panel), this);
            InspectorGuard.Require(_messageText, nameof(_messageText), this);
            InspectorGuard.Require(_confirmButton, nameof(_confirmButton), this);
            InspectorGuard.Require(_cancelButton, nameof(_cancelButton), this);

            if (_panel != null) _restScale = _panel.localScale;

            // A CanvasGroup has nothing to configure, so requiring it in the Inspector would be one
            // more thing to forget for no decision gained. Both calls reach an inactive object fine,
            // which is how this one is expected to sit.
            if (!TryGetComponent(out _group)) _group = gameObject.AddComponent<CanvasGroup>();

            if (_confirmButton != null) _confirmButton.onClick.AddListener(HandleConfirmClicked);
            if (_cancelButton != null) _cancelButton.onClick.AddListener(HandleCancelClicked);
        }

        /// <summary>
        /// Asks the question, keeping whatever labels the prefab already carries.
        /// </summary>
        public void Show(string message, Action onConfirm, Action onCancel = null) =>
            Show(message, null, null, onConfirm, onCancel);

        /// <summary>
        /// Asks the question and renames both buttons for it. Pass null for either label to leave
        /// that button reading whatever the prefab says.
        ///
        /// Showing while a question is already up replaces it outright — the one on screen is
        /// answered by nobody and neither of its callbacks runs, since a caller that puts up a
        /// second question has decided the first no longer matters.
        /// </summary>
        public void Show(string message, string confirmLabel, string cancelLabel,
            Action onConfirm, Action onCancel = null)
        {
            EnsureReady();
            if (_panel == null) return;

            _onConfirm = onConfirm;
            _onCancel = onCancel;

            // Set before this object is activated, so the Awake that activation may trigger can see
            // a question is being asked and leave it alone (see Awake).
            IsOpen = true;

            if (_messageText != null) _messageText.text = message;
            if (_confirmLabel != null && confirmLabel != null) _confirmLabel.text = confirmLabel;
            if (_cancelLabel != null && cancelLabel != null) _cancelLabel.text = cancelLabel;

            gameObject.SetActive(true);
            SetInputEnabled(true);
            PlayOpen();
        }

        /// <summary>
        /// Takes the question down without answering it — for a caller whose reason for asking has
        /// gone away. Neither callback runs.
        /// </summary>
        public void Close()
        {
            if (!IsOpen) return;

            _onConfirm = null;
            _onCancel = null;
            Dismiss();
        }

        private void HandleConfirmClicked() => Answer(_onConfirm);

        private void HandleCancelClicked() => Answer(_onCancel);

        /// <summary>
        /// The callback is read out and the popup fully closed down BEFORE it runs. That ordering is
        /// the whole trick: a callback is free to put this same popup straight back up with a
        /// different question, and if it did so while this method still had state to clear
        /// afterwards, it would be the new question that got cleared.
        /// </summary>
        private void Answer(Action callback)
        {
            if (!IsOpen) return;

            Action chosen = callback;
            _onConfirm = null;
            _onCancel = null;
            Dismiss();

            chosen?.Invoke();
        }

        private void Dismiss()
        {
            IsOpen = false;

            // Both buttons go dead the instant one is pressed. Without this, a second tap landing
            // during the closing animation would answer the same question twice.
            SetInputEnabled(false);
            PlayClose();
        }

        private void PlayOpen()
        {
            _animation.Stop();

            _panel.localScale = _restScale * _openFromScale;
            _group.alpha = 0f;

            _animation = Sequence.Create(useUnscaledTime: true)
                .Group(Tween.Scale(_panel, _restScale, _openDuration, _openEase, useUnscaledTime: true))
                .Group(Tween.Alpha(_group, 1f, _openDuration, Ease.OutQuad, useUnscaledTime: true));
        }

        private void PlayClose()
        {
            _animation.Stop();

            _animation = Sequence.Create(useUnscaledTime: true)
                .Group(Tween.Scale(_panel, _restScale * _closeToScale, _closeDuration, _closeEase,
                    useUnscaledTime: true))
                .Group(Tween.Alpha(_group, 0f, _closeDuration, _closeEase, useUnscaledTime: true))
                // Switched off rather than left at zero alpha, and the box's scale put back, so the
                // next Show starts from a known state instead of wherever this animation ended.
                .OnComplete(this, self =>
                {
                    self._panel.localScale = self._restScale;
                    self.gameObject.SetActive(false);
                });
        }

        /// <summary>
        /// Blocks the whole subtree, backdrop included — that is most of what a backdrop is for.
        /// Without it a stray tap either side of the box reaches whatever is underneath while a
        /// question is still on screen.
        /// </summary>
        private void SetInputEnabled(bool enabled)
        {
            if (_group == null) return;

            _group.interactable = enabled;
            _group.blocksRaycasts = enabled;
        }
    }
}
