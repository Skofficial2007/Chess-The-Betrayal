using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Infrastructure;
using ChessTheBetrayal.UI.Controls;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// Handles the game-over panel UI (winner text, replay and exit actions), plus an optional
    /// button offering to share what the AI did this match — shown only when there is actually a
    /// report to share, so no scene needs a separate toggle for it.
    /// </summary>
    public class GameOverUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TextMeshProUGUI winnerText;
        [SerializeField] private Button replayButton;
        [SerializeField] private Button exitButton;

        [Header("AI Match Report (optional)")]
        [SerializeField] private Button shareAiReportButton;

        public event Action OnReplay;
        public event Action OnExit;

        private void Awake()
        {
            if (replayButton != null)
            {
                replayButton.onClick.AddListener(() => OnReplay?.Invoke());
            }

            if (exitButton != null)
            {
                exitButton.onClick.AddListener(() => OnExit?.Invoke());
            }
        }

        public void SetActive(bool active)
        {
            gameObject.SetActive(active);

            if (active) RefreshShareAiReportButton();
        }

        /// <param name="reason">Why the game ended. A drawn game used to be announced as a stalemate
        /// whatever had happened, which was true while stalemate was the only draw there was — a
        /// player who has just repeated a position three times is told something that did not happen
        /// otherwise, and has no way to tell why the game stopped.</param>
        public void SetWinnerText(Team? winnerTeam, bool byTimeout = false,
            ChessTheBetrayal.Events.Payloads.GameEndReason reason =
                ChessTheBetrayal.Events.Payloads.GameEndReason.Checkmate)
        {
            if (winnerText == null) return;

            winnerText.text = GameOverMessage.Build(winnerTeam, reason, byTimeout);
        }

        /// <summary>
        /// Resolved fresh every time the panel is shown, not cached in Awake — GameManager may not
        /// have registered itself in the ServiceLocator yet at scene-load time, and the answer can
        /// change from one match to the next (a Replay could switch between an AI match and a
        /// human-vs-human one). Hidden with no report to offer, rather than shown and doing nothing.
        /// </summary>
        private void RefreshShareAiReportButton()
        {
            if (shareAiReportButton == null) return;

            string report = ServiceLocator.Instance.TryResolve(out IAiMatchTelemetryProvider provider)
                ? provider.GetLastAiMatchReport()
                : null;

            shareAiReportButton.gameObject.SetActive(report != null);

            // The panel can be shown again across several matches (Replay) without being
            // recreated, so the previous match's report must not still be the one a stale
            // listener would share.
            shareAiReportButton.onClick.RemoveAllListeners();
            if (report != null)
            {
                shareAiReportButton.onClick.AddListener(() => ShareAiReport(report));
            }
        }

        private static void ShareAiReport(string report)
        {
            string fileName = $"chess-ai-match-report_{DateTime.Now:yyyyMMdd-HHmmss}.txt";
            ReportExportResult result = ReportExporter.Save(fileName, report);
            Debug.Log($"[GameOverUI] {result.Message}");
        }
    }
}
