using System.Text;
using UnityEngine;
using TMPro;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Logic;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// A high-performance, zero-allocation clock display widget.
    /// Reads the clock state every frame and writes to TextMeshPro via the StringBuilder overload,
    /// preventing garbage collection spikes on the UI hot path.
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

        // Pre-allocated buffer to prevent string instantiation during per-frame UI updates.
        private readonly StringBuilder _sb = new StringBuilder(8);

        // Caching previous values to skip redundant TextMeshPro vertex rebuilds.
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
