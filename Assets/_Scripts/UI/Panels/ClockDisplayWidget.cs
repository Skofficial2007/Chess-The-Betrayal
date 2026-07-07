using System.Text;
using UnityEngine;
using TMPro;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Logic;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// Shows the remaining time for each side on the clock HUD.
    /// Reads the clock state every frame and writes to TextMeshPro through its StringBuilder
    /// overload, reusing one buffer so the per-frame update does not build a new string each time.
    /// </summary>
    public class ClockDisplayWidget : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TextMeshProUGUI _whiteClockText;
        [SerializeField] private TextMeshProUGUI _blackClockText;
        
        [Header("Visual Settings")]
        [SerializeField] private Color _normalColor = Color.white;
        [SerializeField] private Color _lowTimeColor = Color.red;
        [SerializeField] private long _lowTimeFlashThresholdMs = 10_000L;

        [Header("Data Source")]
        [SerializeField] private ChessTheBetrayal.Events.SharedClockStateSO _sharedClockState;

        // Reused each frame so the per-frame time update does not build a new string.
        private readonly StringBuilder _sb = new StringBuilder(8);

        // Remember the last values shown so we skip redundant TextMeshPro vertex rebuilds.
        private long _lastWhiteMs = -1L;
        private long _lastBlackMs = -1L;

        private void Update()
        {
            // Read directly from the shared data bridge
            ClockState? snapshot = _sharedClockState?.Value;
            if (!snapshot.HasValue)
            {
                return;
            }

            ClockState state = snapshot.Value;

            if (state.WhiteRemainingMs != _lastWhiteMs)
            {
                _lastWhiteMs = state.WhiteRemainingMs;
                ClockFormatter.FormatInto(_sb, state.WhiteRemainingMs);
                _whiteClockText.SetText(_sb);
                _whiteClockText.color = state.WhiteRemainingMs <= _lowTimeFlashThresholdMs
                    ? _lowTimeColor : _normalColor;
            }

            if (state.BlackRemainingMs != _lastBlackMs)
            {
                _lastBlackMs = state.BlackRemainingMs;
                ClockFormatter.FormatInto(_sb, state.BlackRemainingMs);
                _blackClockText.SetText(_sb);
                _blackClockText.color = state.BlackRemainingMs <= _lowTimeFlashThresholdMs
                    ? _lowTimeColor : _normalColor;
            }
        }
    }
}
