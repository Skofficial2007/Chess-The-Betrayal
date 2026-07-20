using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using PrimeTween;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Diagnostics;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// The View component for team assignment.
    /// Knows nothing about chess rules; it just plays a roulette animation and reports when finished.
    /// </summary>
    public class TeamSelectionUI : MonoBehaviour
    {
        [Header("Team Object References")]
        [SerializeField] private Transform whiteTeamObject;
        [SerializeField] private Transform blackTeamObject;

        [Header("UI References")]
        [SerializeField] private Image _whiteHighlight;
        [SerializeField] private Image _blackHighlight;

        [Header("Timing")]
        [SerializeField] private float _startDelay = 0.5f;
        [SerializeField] private float _rouletteDuration = 2.5f;
        [SerializeField] private float _suspensePause = 0.4f;
        [SerializeField] private float _winnerRevealDuration = 0.8f;

        [Header("Roulette Animation")]
        [SerializeField] private float _initialToggleSpeed = 0.06f;
        [SerializeField] private float _finalToggleSpeed = 0.35f;
        [SerializeField] private float _toggleSlowdownRate = 1.12f;
        [SerializeField] private float _activePulseScale = 1.08f;
        [SerializeField] private float _inactiveScale = 0.92f;

        [Header("Winner Animation")]
        [SerializeField] private float _winnerPunchScale = 1.25f;
        [SerializeField] private float _loserShrinkScale = 0.85f;

        public event Action OnRollRequested;
        public event Action OnRouletteComplete;

        private Tween _whitePulseTween;
        private Tween _blackPulseTween;

        private void Awake()
        {
            ValidateRequiredFields();
        }

        private void OnEnable()
        {
            ResetVisuals();
            StartCoroutine(AutoStartRoulette());
        }

        private void ValidateRequiredFields()
        {
            InspectorGuard.Require(whiteTeamObject, nameof(whiteTeamObject), this);
            InspectorGuard.Require(blackTeamObject, nameof(blackTeamObject), this);
            InspectorGuard.Require(_whiteHighlight, nameof(_whiteHighlight), this);
            InspectorGuard.Require(_blackHighlight, nameof(_blackHighlight), this);
        }

        private void OnDisable()
        {
            KillActiveTweens();
        }

        /// <summary>
        /// Resets all visual elements to their initial state.
        /// </summary>
        private void ResetVisuals()
        {
            KillActiveTweens();

            // White starts highlighted, Black not
            if (_whiteHighlight != null) _whiteHighlight.enabled = true;
            if (_blackHighlight != null) _blackHighlight.enabled = false;

            // Reset scales
            if (whiteTeamObject != null) whiteTeamObject.localScale = Vector3.one;
            if (blackTeamObject != null) blackTeamObject.localScale = Vector3.one;
        }

        /// <summary>
        /// Stops any active PrimeTween animations to prevent conflicts.
        /// </summary>
        private void KillActiveTweens()
        {
            _whitePulseTween.Stop();
            _blackPulseTween.Stop();

            if (whiteTeamObject != null) Tween.StopAll(whiteTeamObject);
            if (blackTeamObject != null) Tween.StopAll(blackTeamObject);
        }

        private IEnumerator AutoStartRoulette()
        {
            yield return new WaitForSeconds(_startDelay);
            OnRollRequested?.Invoke();
        }

        /// <summary>
        /// Commences the visual gamble. Called by GameManager once the random math is decided.
        /// </summary>
        public void PlayRoulette(Team assignedTeam)
        {
            StartCoroutine(RouletteRoutine(assignedTeam));
        }

        private IEnumerator RouletteRoutine(Team assignedTeam)
        {
            float elapsed = 0f;
            float toggleInterval = _initialToggleSpeed;
            bool isWhiteActive = true;

            // Phase 1: Rapid flipping with scale pulses
            while (elapsed < _rouletteDuration)
            {
                isWhiteActive = !isWhiteActive;

                // Update highlights
                if (_whiteHighlight != null) _whiteHighlight.enabled = isWhiteActive;
                if (_blackHighlight != null) _blackHighlight.enabled = !isWhiteActive;

                // Pulse the active team, shrink the inactive
                PulseTeamObject(isWhiteActive);

                // Count the interval down by hand rather than yielding a new WaitForSeconds each
                // loop, which would allocate on every pulse.
                float timer = 0f;
                while (timer < toggleInterval)
                {
                    timer += Time.deltaTime;
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                // Decelerate the toggle speed
                toggleInterval = Mathf.Min(_finalToggleSpeed, toggleInterval * _toggleSlowdownRate);
            }

            // Phase 2: Suspense pause - both dim, slight shrink. Each object only tweens if it isn't
            // already resting at the target scale (the last toggle may have left one there), which
            // also avoids PrimeTween's "endValue equals current value" no-op warning.
            KillActiveTweens();

            if (_whiteHighlight != null) _whiteHighlight.enabled = false;
            if (_blackHighlight != null) _blackHighlight.enabled = false;

            TweenToInactiveScale(whiteTeamObject);
            TweenToInactiveScale(blackTeamObject);

            yield return new WaitForSeconds(_suspensePause);

            // Phase 3: Winner reveal with impact
            yield return StartCoroutine(PlayWinnerReveal(assignedTeam));

            // Phase 4: Signal completion
            OnRouletteComplete?.Invoke();
        }

        private void TweenToInactiveScale(Transform target)
        {
            if (target == null) return;
            if (Mathf.Approximately(target.localScale.x, _inactiveScale)) return;
            Tween.Scale(target, _inactiveScale, _suspensePause * 0.5f, Ease.InOutSine);
        }

        /// <summary>
        /// Animates the active team object with a quick scale pulse.
        /// </summary>
        private void PulseTeamObject(bool isWhiteActive)
        {
            Transform activeObject = isWhiteActive ? whiteTeamObject : blackTeamObject;
            Transform inactiveObject = isWhiteActive ? blackTeamObject : whiteTeamObject;

            // Kill previous pulses
            _whitePulseTween.Stop();
            _blackPulseTween.Stop();

            if (activeObject != null)
            {
                activeObject.localScale = Vector3.one * _activePulseScale;
                var tween = Tween.Scale(activeObject, 1f, 0.08f, Ease.OutQuad);

                if (isWhiteActive)
                    _whitePulseTween = tween;
                else
                    _blackPulseTween = tween;
            }

            if (inactiveObject != null)
            {
                inactiveObject.localScale = Vector3.one * _inactiveScale;
            }
        }

        /// <summary>
        /// Plays the dramatic winner reveal animation sequence.
        /// </summary>
        private IEnumerator PlayWinnerReveal(Team assignedTeam)
        {
            Transform winnerObject = assignedTeam == Team.White ? whiteTeamObject : blackTeamObject;
            Transform loserObject = assignedTeam == Team.White ? blackTeamObject : whiteTeamObject;
            Image winnerHighlight = assignedTeam == Team.White ? _whiteHighlight : _blackHighlight;

            // Enable winner highlight
            if (winnerHighlight != null) winnerHighlight.enabled = true;

            // Loser shrinks away
            if (loserObject != null)
            {
                Tween.Scale(loserObject, _loserShrinkScale, _winnerRevealDuration * 0.4f, Ease.InBack);
            }

            // Winner punches in with elastic bounce. The celebration duration is baked into a local
            // before the tween starts so the completion callback only captures the winner Transform.
            if (winnerObject != null)
            {
                float celebrationDuration = _winnerRevealDuration * 0.7f;
                Easing celebrationEase = Easing.Elastic(0.8f, 0.3f);

                Tween.Scale(winnerObject, _winnerPunchScale, _winnerRevealDuration * 0.3f, Easing.Overshoot(2f))
                    .OnComplete(winnerObject, target => Tween.Scale(target, 1f, celebrationDuration, celebrationEase));
            }

            yield return new WaitForSeconds(_winnerRevealDuration);
        }

        public void SetActive(bool active) => gameObject.SetActive(active);
    }
}
