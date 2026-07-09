using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using PrimeTween;
using ChessTheBetrayal.Core.Diagnostics;

namespace ChessTheBetrayal.UI.Controls
{
    /// <summary>
    /// Drop-in replacement for UnityEngine.UI.Button that adds an optional "juicy" click punch:
    /// the icon scales down then bounces back via PrimeTween, and every input path that can invoke
    /// a Button (pointer click, gamepad/keyboard Submit) is ignored for the animation's duration —
    /// the debounce IS the point, not a separate opt-out, since replaying the punch mid-flight would
    /// just look broken.
    ///
    /// Scales a separate child RectTransform (<see cref="_scaleTarget"/>), never the Button's own
    /// RectTransform: this component's root is routinely an invisible full-size hit target laid out
    /// by a HorizontalLayoutGroup/ContentSizeFitter, and scaling that root would both look wrong
    /// (the icon would drift off-center as its parent's rect resizes) and fight the layout group,
    /// which recomputes the root's size every frame regardless of any scale applied. Point
    /// <see cref="_scaleTarget"/> at the visual icon child instead — see the class doc's Inspector
    /// Setup note for the exact hierarchy this expects.
    ///
    /// Inspector Setup:
    ///   Root (this component + Image as the click target, can be alpha-0)
    ///     └─ Icon (RectTransform, anchored 0.5/0.5, pivot 0.5/0.5) ← assign as Scale Target
    ///
    /// When <see cref="_playClickAnimation"/> is false this behaves as a completely stock Button —
    /// zero overhead, zero behavior change — so it's safe to use everywhere as the project's default
    /// Button and opt into the animation per-instance.
    /// </summary>
    [AddComponentMenu("UI/Animated Button")]
    public class AnimatedButton : Button
    {
        [Header("Click Animation")]
        [SerializeField] private bool _playClickAnimation = false;

        [Tooltip("RectTransform that visually punches on click. Must be a child of this button, never the button's own RectTransform (see class doc).")]
        [SerializeField] private RectTransform _scaleTarget;

        [Tooltip("Scale factor at the bottom of the punch, relative to the icon's rest scale (e.g. 0.9 = shrinks to 90%).")]
        [SerializeField, Range(0.5f, 0.99f)] private float _punchScale = 0.9f;

        [Tooltip("Total seconds for the down+bounce-back animation. Also how long input is ignored (the debounce).")]
        [SerializeField, Min(0.01f)] private float _animationDuration = 0.2f;

        [SerializeField] private Ease _punchDownEase = Ease.OutQuad;
        [SerializeField] private Ease _punchBackEase = Ease.OutBack;

        private Sequence _punchSequence;
        private Vector3 _restScale = Vector3.one;
        private bool _isAnimating;

        protected override void Awake()
        {
            base.Awake();

            if (_playClickAnimation)
            {
                InspectorGuard.Require(_scaleTarget, nameof(_scaleTarget), this);
            }

            if (_scaleTarget != null)
            {
                _restScale = _scaleTarget.localScale;
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            // A button hidden mid-punch (e.g. its panel closes) must not resume the animation from a
            // shrunk scale the next time it's shown, and must not leave input latched shut.
            _punchSequence.Stop();
            _isAnimating = false;
            if (_scaleTarget != null)
            {
                _scaleTarget.localScale = _restScale;
            }
        }

        public override void OnPointerClick(PointerEventData eventData)
        {
            if (!ShouldPlayAnimation())
            {
                base.OnPointerClick(eventData);
                return;
            }

            if (_isAnimating) return;

            PlayClickPunch();
            base.OnPointerClick(eventData);
        }

        public override void OnSubmit(BaseEventData eventData)
        {
            if (!ShouldPlayAnimation())
            {
                base.OnSubmit(eventData);
                return;
            }

            if (_isAnimating) return;

            PlayClickPunch();
            base.OnSubmit(eventData);
        }

        private bool ShouldPlayAnimation() => _playClickAnimation && _scaleTarget != null && IsActive() && IsInteractable();

        private void PlayClickPunch()
        {
            _isAnimating = true;
            _punchSequence.Stop();

            float punchDownDuration = _animationDuration * 0.35f;
            float punchBackDuration = _animationDuration - punchDownDuration;

            _punchSequence = Sequence.Create()
                .Chain(Tween.Scale(_scaleTarget, _restScale * _punchScale, punchDownDuration, _punchDownEase))
                .Chain(Tween.Scale(_scaleTarget, _restScale, punchBackDuration, _punchBackEase))
                .OnComplete(this, self => self._isAnimating = false);
        }
    }
}
