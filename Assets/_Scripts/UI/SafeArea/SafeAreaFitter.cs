using UnityEngine;

namespace ChessTheBetrayal.UI.SafeArea
{
    /// <summary>
    /// Anchors this RectTransform to the part of the screen the OS actually lets us draw in, so
    /// content stays clear of camera cutouts, rounded corners and the gesture bar. Each edge can be
    /// opted out of, which lets a background stretch to the physical edge of the display while the
    /// content inside it stays inset.
    ///
    /// Runs at runtime only, on purpose. An earlier version also ran while editing, and that turned
    /// out to be actively harmful: the editor reports the size of whichever view it happens to be
    /// drawing, so every repaint wrote a different set of anchors into the scene, and whatever
    /// happened to be computed last was what got saved. The anchors stored in a scene are supposed
    /// to be the authored layout; they cannot also be a cache of one editor view's insets. To check
    /// the layout against a real device's cutouts, enter play mode with the device simulator — which
    /// is the only place those insets are simulated anyway.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        [SerializeField] private bool applyLeft = true;
        [SerializeField] private bool applyRight = true;
        [SerializeField] private bool applyTop = true;
        [SerializeField] private bool applyBottom = true;

        private RectTransform _rectTransform;
        private Rect _lastSafeArea;
        private ScreenOrientation _lastOrientation;
        private int _lastScreenWidth;
        private int _lastScreenHeight;

        // Whether the anchors have been written at least once since this component was enabled.
        // Tracked explicitly rather than inferred from the cached screen values, because the cache
        // starts out matching a zero-sized screen — so without this flag a first frame that reports
        // no screen size would count as "already handled" and the anchors would never be applied.
        private bool _hasApplied;

        private RectTransform RectTransform => _rectTransform ??= GetComponent<RectTransform>();

        private void OnEnable()
        {
            _hasApplied = false;
            Apply(Screen.safeArea);
        }

        private void Update()
        {
            if (_hasApplied && !HasScreenStateChanged())
            {
                return;
            }

            Apply(Screen.safeArea);
        }

        private bool HasScreenStateChanged()
        {
            return Screen.safeArea != _lastSafeArea
                || Screen.orientation != _lastOrientation
                || Screen.width != _lastScreenWidth
                || Screen.height != _lastScreenHeight;
        }

        private void Apply(Rect safeArea)
        {
            int screenWidth = Screen.width;
            int screenHeight = Screen.height;

            if (!TryComputeNormalizedSafeArea(safeArea, screenWidth, screenHeight,
                    out Vector2 anchorMin, out Vector2 anchorMax))
            {
                return;
            }

            // Opting an edge out pins it back to the full extent of the parent. These are exact
            // literals, so they cannot reintroduce a value the check above just rejected.
            if (!applyLeft) anchorMin.x = 0f;
            if (!applyBottom) anchorMin.y = 0f;
            if (!applyRight) anchorMax.x = 1f;
            if (!applyTop) anchorMax.y = 1f;

            RectTransform.anchorMin = anchorMin;
            RectTransform.anchorMax = anchorMax;

            // Recorded only after a successful write. Caching it earlier would mark a screen state
            // as handled even when the anchors were refused, so the component would sit out the rest
            // of that state instead of retrying once the screen reports a real size.
            _lastSafeArea = safeArea;
            _lastOrientation = Screen.orientation;
            _lastScreenWidth = screenWidth;
            _lastScreenHeight = screenHeight;
            _hasApplied = true;
        }

        /// <summary>
        /// Converts a safe area in pixels into the 0..1 anchor fractions a RectTransform wants, or
        /// returns false when the inputs cannot produce a usable answer.
        ///
        /// The screen dimensions are the divisor for the entire calculation, so a zero has to be
        /// refused rather than clamped or substituted — a screen with no size has no fraction of
        /// itself to express, and any number picked to stand in for it would be a fabricated layout.
        /// Zero shows up more readily than it sounds: a view that has not been laid out yet, or a
        /// display being reconfigured, both report it.
        ///
        /// Refusing matters because of where the damage would otherwise surface. Dividing by zero in
        /// floating point does not throw; it quietly yields infinity, or a NaN when the numerator is
        /// zero too. Written into a RectTransform, that spreads through every child's layout and is
        /// reported later as corrupted bounds, from a call stack that names neither this component
        /// nor the frame the division happened in — which is a great deal of work to trace back to
        /// one missing check.
        /// </summary>
        public static bool TryComputeNormalizedSafeArea(Rect safeArea, int screenWidth, int screenHeight,
            out Vector2 anchorMin, out Vector2 anchorMax)
        {
            anchorMin = Vector2.zero;
            anchorMax = Vector2.one;

            if (screenWidth <= 0 || screenHeight <= 0)
            {
                return false;
            }

            // A safe area with no width or height would collapse the rect to a line or a point,
            // which is a degenerate layout rather than an inset one. Treated the same as an
            // unusable screen size: leave the anchors alone and try again next frame.
            if (safeArea.width <= 0f || safeArea.height <= 0f)
            {
                return false;
            }

            Vector2 computedMin = safeArea.position;
            Vector2 computedMax = safeArea.position + safeArea.size;

            computedMin.x /= screenWidth;
            computedMin.y /= screenHeight;
            computedMax.x /= screenWidth;
            computedMax.y /= screenHeight;

            // The guards above cover every cause known today. This one covers the rest: an input
            // that was already infinite or NaN before it got here survives the division unchanged,
            // and the cost of catching it is one frame of a slightly stale layout.
            if (!IsUsable(computedMin) || !IsUsable(computedMax))
            {
                return false;
            }

            anchorMin = computedMin;
            anchorMax = computedMax;
            return true;
        }

        private static bool IsUsable(Vector2 anchor) =>
            !float.IsNaN(anchor.x) && !float.IsNaN(anchor.y)
            && !float.IsInfinity(anchor.x) && !float.IsInfinity(anchor.y);
    }
}
