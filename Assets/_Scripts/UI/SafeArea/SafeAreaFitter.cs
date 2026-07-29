using UnityEngine;

namespace ChessTheBetrayal.UI.SafeArea
{
    [ExecuteAlways]
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

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
        }

        private void OnEnable()
        {
            Apply(Screen.safeArea);
        }

        private void Update()
        {
            if (!HasScreenStateChanged())
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
            _lastSafeArea = safeArea;
            _lastOrientation = Screen.orientation;
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;

            Vector2 anchorMin = safeArea.position;
            Vector2 anchorMax = safeArea.position + safeArea.size;

            anchorMin.x /= Screen.width;
            anchorMin.y /= Screen.height;
            anchorMax.x /= Screen.width;
            anchorMax.y /= Screen.height;

            if (!applyLeft) anchorMin.x = 0f;
            if (!applyBottom) anchorMin.y = 0f;
            if (!applyRight) anchorMax.x = 1f;
            if (!applyTop) anchorMax.y = 1f;

            _rectTransform.anchorMin = anchorMin;
            _rectTransform.anchorMax = anchorMax;
        }
    }
}
