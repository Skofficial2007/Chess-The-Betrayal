using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;
using ChessTheBetrayal.AI.DeviceBenchmark;
using ChessTheBetrayal.Core.Diagnostics;
using ChessTheBetrayal.Infrastructure;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// The on-screen face of the device benchmark: two buttons that each start a different run, a
    /// scrolling text view of the run's progress, a button that only lights up once there is a
    /// finished report to share, and a Back button that abandons a run in progress and returns to
    /// the game.
    ///
    /// Deliberately holds no benchmark logic at all — it starts a run, reads state, and draws the
    /// report DeviceSearchBenchmark hands it. Everything worth asserting about a report (what it
    /// contains and in what order) is therefore testable without a scene, and this class stays
    /// small enough to be checked by looking at it.
    /// </summary>
    public class BenchmarkScreen : MonoBehaviour
    {
        [Header("Benchmark")]
        [SerializeField] private DeviceSearchBenchmark _benchmark;

        [Header("UI References")]
        [SerializeField] private TMP_Text _resultText;
        [SerializeField] private Button _startButton;
        [SerializeField] private Button _longRunButton;
        [FormerlySerializedAs("_downloadButton")]
        [SerializeField] private Button _shareButton;
        [FormerlySerializedAs("_downloadButtonGraphic")]
        [SerializeField] private Image _shareButtonGraphic;
        [SerializeField] private Button _backButton;

        [Header("Share Button Colors")]
        [FormerlySerializedAs("_downloadActiveColor")]
        [SerializeField] private Color _shareActiveColor = new Color(1f, 253f / 255f, 178f / 255f); // #FFFDB2
        [FormerlySerializedAs("_downloadInactiveColor")]
        [SerializeField] private Color _shareInactiveColor = new Color(147f / 255f, 147f / 255f, 147f / 255f); // #939393

        [Header("On-screen Log")]
        // TextMeshPro silently stops drawing a text object past roughly sixteen thousand
        // characters, so the newest lines would be the ones to disappear if the log were left
        // unbounded. Only the on-screen copy is trimmed; a saved report keeps everything.
        [SerializeField] private int _maxDetailLinesOnScreen = 40;

        private int _renderedRevision = -1;
        private int _renderedSecond = -1;
        private bool _wasRunning;

        private void Awake()
        {
            ValidateRequiredFields();

            if (_startButton != null)
            {
                _startButton.onClick.AddListener(HandleStartClicked);
            }

            if (_longRunButton != null)
            {
                _longRunButton.onClick.AddListener(HandleLongRunClicked);
            }

            if (_shareButton != null)
            {
                _shareButton.onClick.AddListener(HandleShareClicked);
            }

            if (_backButton != null)
            {
                _backButton.onClick.AddListener(HandleBackClicked);
            }

            // Nothing to share until a run has produced a report.
            SetShareEnabled(false);
            ShowIdleText();
        }

        private void ValidateRequiredFields()
        {
            InspectorGuard.Require(_benchmark, nameof(_benchmark), this);
            InspectorGuard.Require(_resultText, nameof(_resultText), this);
            InspectorGuard.Require(_startButton, nameof(_startButton), this);
            InspectorGuard.Require(_longRunButton, nameof(_longRunButton), this);
            InspectorGuard.Require(_shareButton, nameof(_shareButton), this);
            InspectorGuard.Require(_shareButtonGraphic, nameof(_shareButtonGraphic), this);
            InspectorGuard.Require(_backButton, nameof(_backButton), this);
        }

        private void Update()
        {
            if (_benchmark == null) return;

            RefreshReportText();
            RefreshButtons();
        }

        /// <summary>
        /// Redraws only when the report has actually changed or the elapsed clock has ticked over a
        /// second. Assigning text to TextMeshPro re-parses and re-lays-out the whole string, which
        /// for a report this size is real work — doing it every frame would have the display
        /// competing for the very cores the run is trying to measure.
        /// </summary>
        private void RefreshReportText()
        {
            if (_resultText == null) return;

            int revision = _benchmark.ReportRevision;
            int second = (int)_benchmark.Elapsed.TotalSeconds;
            if (revision == _renderedRevision && second == _renderedSecond) return;

            _renderedRevision = revision;
            _renderedSecond = second;

            string report = _benchmark.RenderReport(ReportStyle.RichText, _maxDetailLinesOnScreen);
            if (report != null) _resultText.text = report;
        }

        private void RefreshButtons()
        {
            bool running = _benchmark.IsRunning;
            if (running == _wasRunning) return;
            _wasRunning = running;

            if (_startButton != null) _startButton.interactable = !running;
            if (_longRunButton != null) _longRunButton.interactable = !running;
            SetShareEnabled(_benchmark.IsComplete);
        }

        private void HandleStartClicked()
        {
            if (_benchmark == null || !_benchmark.StartRun()) return;
            OnRunStarted();
        }

        private void HandleLongRunClicked()
        {
            if (_benchmark == null || !_benchmark.StartThermalRun()) return;
            OnRunStarted();
        }

        private void OnRunStarted()
        {
            // A finished report from the previous run is no longer the one on screen, so the
            // Share button must not keep offering it.
            SetShareEnabled(false);

            // Force the next frame to redraw even if the new run has not emitted a line yet.
            _renderedRevision = -1;
            _renderedSecond = -1;
        }

        /// <summary>
        /// Unconditional — a run in progress is simply abandoned, not confirmed. Loading Game Scene
        /// unloads this one, which destroys DeviceSearchBenchmark and fires its own OnDisable
        /// (unsubscribes the runner, resets the sleep timeout, stops the coroutine dead). A search
        /// already in flight on the worker thread keeps computing until its own budget expires (at
        /// most 3 seconds) and its result is then simply never read — a bounded cost, not a leak,
        /// and not worth a CancellationToken threaded in just for this.
        /// </summary>
        private void HandleBackClicked() => SceneManager.LoadScene(SceneNames.Game);

        /// <summary>
        /// Saves and shares the finished report, unstyled and complete — not the trimmed, marked-up
        /// copy the screen is showing. Where it went is appended to the log so the tester can read
        /// the path off the same place they have been watching.
        /// </summary>
        private void HandleShareClicked()
        {
            if (_benchmark == null || !_benchmark.IsComplete) return;

            string report = _benchmark.RenderReport();
            if (string.IsNullOrEmpty(report)) return;

            string fileName = BenchmarkReportFileName.Build(DeviceSearchBenchmark.DeviceModel, DateTime.Now);
            ReportExportResult result = ReportExporter.Save(fileName, report);
            _benchmark.AppendNote(result.Message);
        }

        /// <summary>
        /// Greyed out and inert, or lit and pressable — the same interactable-plus-color pairing the
        /// rest of the project's buttons use, so a disabled button here looks disabled everywhere
        /// else it appears.
        /// </summary>
        private void SetShareEnabled(bool enabled)
        {
            if (_shareButton != null)
            {
                _shareButton.interactable = enabled;
            }

            if (_shareButtonGraphic != null)
            {
                _shareButtonGraphic.color = enabled ? _shareActiveColor : _shareInactiveColor;
            }
        }

        private void ShowIdleText()
        {
            if (_resultText == null || _benchmark == null) return;

            _resultText.text =
                "<b>Ready.</b>\n\n"
                + $"Press Start to time this device's AI search. It takes at most {_benchmark.EstimatedWorstCase:mm\\:ss}, "
                + "and cannot be paused once it begins.\n\n"
                + "Press Long Run to check whether the impossible tier's search depth holds steady over "
                + $"sustained play. It takes at most {_benchmark.ThermalEstimatedWorstCase:mm\\:ss}.\n\n"
                + "Leave the app in the foreground while either run is going — the screen is kept awake for you.";
        }
    }
}
