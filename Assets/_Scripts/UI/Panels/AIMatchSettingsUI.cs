using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PrimeTween;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Diagnostics;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// Manages the Practice Match Setup panel: Betrayal / Defend Only / Skip Retribution toggle
    /// rows plus the AI Difficulty cycler. Maps the confirmed choices to a PracticeMatchSettings
    /// and fires it on Done — mirrors GameModeSelectorUI's structure and validation style.
    /// </summary>
    public class AIMatchSettingsUI : MonoBehaviour
    {
        public event Action<PracticeMatchSettings> OnSettingsConfirmed;

        [Header("Betrayal Row")]
        [SerializeField] private Toggle _betrayalOn;
        [SerializeField] private Toggle _betrayalOff;

        [Header("Defend Only Row")]
        [SerializeField] private Toggle _defendOnlyOn;
        [SerializeField] private Toggle _defendOnlyOff;

        [Header("Skip Retribution Row")]
        [SerializeField] private Toggle _skipOn;
        [SerializeField] private Toggle _skipOff;

        [Header("Difficulty Row")]
        [SerializeField] private RectTransform _levelsMask;
        [Tooltip("Index-parallel to AIProfileTable.BuiltIn — one label per tier, in the same order (easy, normal, hard, aggressive, extreme, impossible).")]
        [SerializeField] private RectTransform[] _difficultyLabels;
        [SerializeField] private Button _difficultyPrevBtn;
        [SerializeField] private Button _difficultyNextBtn;

        [Header("Difficulty Slide Animation")]
        [SerializeField] private float _difficultySlideDistance = 60f;
        [SerializeField] private float _difficultySlideDuration = 0.28f;

        [Tooltip("Outgoing label: accelerates away, no bounce — a bounce on exit reads as a glitch, not juice.")]
        [SerializeField] private Ease _outgoingSlideEase = Ease.InCubic;

        [Tooltip("Incoming label: overshoots slightly past home then settles — this is what actually sells 'juicy.'")]
        [SerializeField] private Ease _incomingSlideEase = Ease.OutBack;

        [SerializeField] private Ease _fadeEase = Ease.OutSine;

        [Tooltip("Incoming label starts at this scale and pops to 1 as it slides in — extra squash/pop layered on top of the slide+fade.")]
        [SerializeField, Range(0.5f, 1f)] private float _incomingStartScale = 0.7f;

        [Header("Controls")]
        [SerializeField] private Button _doneButton;

        private TMP_Text[] _difficultyLabelTexts;
        private int _difficultyIndex = 1; // Normal by default

        private Sequence _difficultySlideSequence;
        private Vector2 _difficultyLabelHomePosition;

        // Set just before each PlayDifficultySlide's Sequence starts; read back in its OnComplete
        // via the zero-alloc Tween.OnComplete(this, ...) overload instead of a captured closure.
        private RectTransform _outgoingDifficultyLabel;

        private void Awake()
        {
            ValidateRequiredFields();
        }

        private void Start()
        {
            if (_difficultyLabels.Length != AIProfileTable.BuiltIn.Count)
            {
                Debug.LogError($"[{nameof(AIMatchSettingsUI)}] {nameof(_difficultyLabels)} has {_difficultyLabels.Length} entries, expected {AIProfileTable.BuiltIn.Count} (one per AIProfileTable.BuiltIn row).", this);
            }

            _difficultyLabelTexts = new TMP_Text[_difficultyLabels.Length];
            for (int i = 0; i < _difficultyLabels.Length; i++)
            {
                _difficultyLabelTexts[i] = _difficultyLabels[i].GetComponent<TMP_Text>();
                InspectorGuard.Require(_difficultyLabelTexts[i], $"{nameof(_difficultyLabels)}[{i}].TMP_Text", this);
            }

            _difficultyLabelHomePosition = _difficultyLabels[_difficultyIndex].anchoredPosition;

            _difficultyPrevBtn.onClick.AddListener(() => StepDifficulty(-1));
            _difficultyNextBtn.onClick.AddListener(() => StepDifficulty(1));
            _doneButton.onClick.AddListener(HandleDoneClicked);

            // Betrayal Off disables the Defend Only row entirely — a plain-chess match has no
            // agent Betrayal policy to configure. Wire that reactively rather than just at Start,
            // since the player can flip Betrayal off after touching Defend Only.
            _betrayalOn.onValueChanged.AddListener((_) => UpdateDefendOnlyInteractable());
            _betrayalOff.onValueChanged.AddListener((_) => UpdateDefendOnlyInteractable());

            ApplySettingsToControls(PracticeMatchSettingsStorage.Load());
            SnapDifficultyLabelToCurrentIndex();
            UpdateDefendOnlyInteractable();
        }

        /// <summary>
        /// Snaps every toggle/difficulty index to a loaded (or default) PracticeMatchSettings —
        /// the inverse of HandleDoneClicked's read. Setting isOn on one Toggle of a ToggleGroup
        /// pair is enough; Unity's ToggleGroup turns the sibling off automatically.
        /// </summary>
        private void ApplySettingsToControls(PracticeMatchSettings settings)
        {
            _betrayalOn.isOn = settings.BetrayalEnabled;
            _betrayalOff.isOn = !settings.BetrayalEnabled;

            _defendOnlyOn.isOn = settings.AiDefendOnly;
            _defendOnlyOff.isOn = !settings.AiDefendOnly;

            _skipOn.isOn = settings.RetributionSkipAllowed;
            _skipOff.isOn = !settings.RetributionSkipAllowed;

            _difficultyIndex = IndexForProfileId(settings.AiProfileId);
        }

        private static int IndexForProfileId(string id)
        {
            var table = AIProfileTable.BuiltIn;
            for (int i = 0; i < table.Count; i++)
            {
                if (string.Equals(table[i].Id, id, StringComparison.OrdinalIgnoreCase)) return i;
            }
            for (int i = 0; i < table.Count; i++)
            {
                if (string.Equals(table[i].Id, AIProfileTable.DefaultId, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return 0;
        }

        private void OnDisable()
        {
            // A panel closed mid-transition must not resume next time it's shown with a label
            // stuck off-position/scaled/faded — snap everything back to a clean resting state.
            SnapDifficultyLabelToCurrentIndex();
        }

        private void ValidateRequiredFields()
        {
            InspectorGuard.Require(_betrayalOn, nameof(_betrayalOn), this);
            InspectorGuard.Require(_betrayalOff, nameof(_betrayalOff), this);
            InspectorGuard.Require(_defendOnlyOn, nameof(_defendOnlyOn), this);
            InspectorGuard.Require(_defendOnlyOff, nameof(_defendOnlyOff), this);
            InspectorGuard.Require(_skipOn, nameof(_skipOn), this);
            InspectorGuard.Require(_skipOff, nameof(_skipOff), this);
            InspectorGuard.Require(_levelsMask, nameof(_levelsMask), this);
            for (int i = 0; i < _difficultyLabels.Length; i++)
            {
                InspectorGuard.Require(_difficultyLabels[i], $"{nameof(_difficultyLabels)}[{i}]", this);
            }
            InspectorGuard.Require(_difficultyPrevBtn, nameof(_difficultyPrevBtn), this);
            InspectorGuard.Require(_difficultyNextBtn, nameof(_difficultyNextBtn), this);
            InspectorGuard.Require(_doneButton, nameof(_doneButton), this);
        }

        /// <summary>Defend Only is meaningless with Betrayal disabled — grey it out rather than
        /// letting the player configure a policy that can never trigger.</summary>
        private void UpdateDefendOnlyInteractable()
        {
            bool betrayalActive = _betrayalOn.isOn;
            _defendOnlyOn.interactable = betrayalActive;
            _defendOnlyOff.interactable = betrayalActive;
        }

        /// <summary>
        /// Infinite carousel: wraps modulo the label count in both directions, so Next past Hard
        /// lands on Easy and Prev before Easy lands on Hard. Both buttons stay permanently
        /// interactable — there is no "end" to disable against.
        /// </summary>
        private void StepDifficulty(int delta)
        {
            int count = _difficultyLabels.Length;
            int newIndex = ((_difficultyIndex + delta) % count + count) % count;

            int previousIndex = _difficultyIndex;
            _difficultyIndex = newIndex;
            PlayDifficultySlide(previousIndex, newIndex, delta);
        }

        /// <summary>
        /// Instantly shows the current difficulty's label with no animation and hides the other
        /// two — used on load so the panel never briefly flashes the wrong label or has more than
        /// one label visible before the player has clicked anything.
        /// </summary>
        private void SnapDifficultyLabelToCurrentIndex()
        {
            _difficultySlideSequence.Stop();

            for (int i = 0; i < _difficultyLabels.Length; i++)
            {
                RectTransform label = _difficultyLabels[i];
                bool isCurrent = i == _difficultyIndex;

                label.gameObject.SetActive(isCurrent);
                label.anchoredPosition = _difficultyLabelHomePosition;
                label.localScale = Vector3.one;
                _difficultyLabelTexts[i].alpha = isCurrent ? 1f : 0f;
            }
        }

        /// <summary>
        /// Slides the outgoing label out (toward -direction) while the incoming label slides in
        /// from the opposite edge (from +direction) toward the shared home position, both fading
        /// simultaneously — a synchronized two-label carousel transition, clipped by the
        /// RectMask2D on _levelsMask so neither label is ever visible outside the Levels window.
        /// Grouped in one Sequence (not two independent tweens) so Stop()-ing it always leaves both
        /// labels in a consistent state, matching GameHUD/PrimeTweenPieceAnimator's
        /// Stop-before-restart idiom for rapid re-clicking.
        /// </summary>
        private void PlayDifficultySlide(int previousIndex, int newIndex, int direction)
        {
            _difficultySlideSequence.Stop();

            RectTransform outgoing = _difficultyLabels[previousIndex];
            RectTransform incoming = _difficultyLabels[newIndex];
            TMP_Text incomingText = _difficultyLabelTexts[newIndex];

            // A Stop() above can interrupt an in-flight transition mid-fade, leaving some third
            // label (neither this call's outgoing nor incoming) visible/off-position if the player
            // clicked again before the previous slide's OnComplete fired. Force every label that
            // isn't part of THIS transition back to a clean hidden state before starting.
            for (int i = 0; i < _difficultyLabels.Length; i++)
            {
                if (i == previousIndex || i == newIndex) continue;

                _difficultyLabels[i].gameObject.SetActive(false);
                _difficultyLabels[i].anchoredPosition = _difficultyLabelHomePosition;
                _difficultyLabels[i].localScale = Vector3.one;
                _difficultyLabelTexts[i].alpha = 0f;
            }

            Vector2 outgoingExitPosition = _difficultyLabelHomePosition - Vector2.left * direction * _difficultySlideDistance;
            Vector2 incomingEnterPosition = _difficultyLabelHomePosition + Vector2.left * direction * _difficultySlideDistance;

            incoming.gameObject.SetActive(true);
            incoming.anchoredPosition = incomingEnterPosition;
            incoming.localScale = Vector3.one * _incomingStartScale;
            incomingText.alpha = 0f;

            _outgoingDifficultyLabel = outgoing;

            _difficultySlideSequence = Sequence.Create()
                .Group(Tween.UIAnchoredPosition(outgoing, outgoingExitPosition, _difficultySlideDuration, _outgoingSlideEase))
                .Group(Tween.Alpha(_difficultyLabelTexts[previousIndex], 0f, _difficultySlideDuration, _fadeEase))
                .Group(Tween.UIAnchoredPosition(incoming, _difficultyLabelHomePosition, _difficultySlideDuration, _incomingSlideEase))
                .Group(Tween.Alpha(incomingText, 1f, _difficultySlideDuration, _fadeEase))
                .Group(Tween.Scale(incoming, 1f, _difficultySlideDuration, _incomingSlideEase))
                .OnComplete(this, self =>
                {
                    self._outgoingDifficultyLabel.gameObject.SetActive(false);
                    self._outgoingDifficultyLabel.anchoredPosition = self._difficultyLabelHomePosition;
                    self._outgoingDifficultyLabel.localScale = Vector3.one;
                });
        }

        private void HandleDoneClicked()
        {
            var settings = new PracticeMatchSettings(
                betrayalEnabled: _betrayalOn.isOn,
                aiDefendOnly: _defendOnlyOn.isOn,
                retributionSkipAllowed: _skipOn.isOn,
                aiProfileId: AIProfileTable.BuiltIn[_difficultyIndex].Id);

            PracticeMatchSettingsStorage.Save(settings);
            OnSettingsConfirmed?.Invoke(settings);
        }

        public void SetActive(bool active)
        {
            gameObject.SetActive(active);
        }
    }
}
