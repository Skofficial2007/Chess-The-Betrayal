using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;
using ChessTheBetrayal.AI.DeviceBenchmark;
using ChessTheBetrayal.Infrastructure;
using ChessTheBetrayal.UI.Controls;

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

        [Tooltip("Asks before a run discards a report nobody saved. Unassigned, the same warning is written into the log area instead.")]
        [SerializeField] private WarningPopup _warningPopup;

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

        // What the three buttons actually say in the scene. Every line of on-screen text that tells
        // someone which button to press reads them from here, because text naming a button that
        // isn't on the screen is worse than no instruction at all -- and that is exactly what
        // happened when these were relabelled and the prose telling testers to "press Start" was
        // left behind. Change a label in the scene and these change with it, in one place.
        private const string QuickRunLabel = "Quick Run";
        private const string LongRunLabel = "Long Run";
        private const string ShareLabel = "Share Report";

        // Starting a run destroys the previous one's report, so a finished run that was never
        // shared is one button press from being lost for good. This decides when a press has to
        // warn first; the wording it warns with is this screen's business, the rule is not.
        private readonly UnsavedReportGuard _unsavedReport = new UnsavedReportGuard();

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

            // A question is on screen. With a popup the report simply sits behind it, but the
            // no-popup fallback writes its warning over the report itself, and that must stay put
            // until it is answered. Nothing would overwrite it in practice — a finished run's
            // revision and clock have both stopped moving — but relying on that would leave the
            // warning surviving by luck rather than on purpose.
            if (_unsavedReport.IsAwaitingConfirmation) return;

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

            if (!running && _benchmark.IsComplete) _unsavedReport.NoteRunCompleted();
        }

        // Lambdas rather than method groups: building a delegate from a null _benchmark would
        // throw before RequestRun ever got the chance to check it.
        private void HandleStartClicked() => RequestRun(() => _benchmark.StartRun(), QuickRunLabel);

        private void HandleLongRunClicked() => RequestRun(() => _benchmark.StartThermalRun(), LongRunLabel);

        /// <summary>
        /// Starts a run, unless doing so would discard a finished report that was never shared — in
        /// which case this press buys the warning instead and the next one goes ahead. Both start
        /// buttons feed through here, so being warned once covers either of them.
        /// </summary>
        private void RequestRun(Func<bool> start, string buttonLabel)
        {
            if (_benchmark == null) return;

            if (!_unsavedReport.RequestStart())
            {
                AskBeforeDiscarding(start, buttonLabel);
                return;
            }

            if (!start()) return;
            OnRunStarted();
        }

        /// <summary>
        /// Puts the question in front of the tester. The popup is the real answer to it; the text
        /// written into the log area is what happens when no popup is wired, and it is deliberately
        /// still here rather than replaced — a fork whose scene has no popup prefab should lose the
        /// nicer presentation, not the protection.
        /// </summary>
        private void AskBeforeDiscarding(Func<bool> start, string buttonLabel)
        {
            if (_warningPopup == null)
            {
                ShowDiscardWarningInLog(buttonLabel);
                return;
            }

            _warningPopup.Show(DiscardWarningMessage,
                onConfirm: () =>
                {
                    // Consumes the confirmation the popup just collected, which is what makes this
                    // second call return true rather than asking all over again.
                    if (!_unsavedReport.RequestStart()) return;
                    if (!start()) return;
                    OnRunStarted();
                },
                onCancel: () =>
                {
                    _unsavedReport.CancelConfirmation();
                    ForceReportRedraw();
                });
        }

        private void OnRunStarted()
        {
            // A finished report from the previous run is no longer the one on screen, so the
            // Share button must not keep offering it.
            SetShareEnabled(false);
            ForceReportRedraw();
        }

        /// <summary>
        /// Makes the next frame redraw whether or not the report says it changed. Needed twice: a
        /// new run has not emitted a line yet, and a finished run's revision and elapsed clock have
        /// both stopped moving, so a warning drawn over it would otherwise stay up for good.
        /// </summary>
        private void ForceReportRedraw()
        {
            _renderedRevision = -1;
            _renderedSecond = -1;
        }

        // The line breaks inside the detail and the action are deliberate rather than left to
        // wrapping: both read as balanced pairs at the panel's width, and letting them wrap on
        // their own puts a single orphaned word on the second line.
        private static string DiscardWarningMessage => WarningMessage.Build(
            headline: "This report has not been shared yet.",
            detail: "Starting another run replaces it,\nand there is no way to get it back afterwards.",
            action: $"Go back and press\n{ShareLabel} to keep it.");

        private void ShowDiscardWarningInLog(string buttonLabel)
        {
            if (_resultText == null) return;

            // Says the report is still here on purpose: replacing it on screen with this warning
            // looks exactly like the loss being warned about, and a tester who thinks it has
            // already gone has no reason to press the button that would save it.
            _resultText.text =
                "<b>This report has not been shared yet.</b>\n\n"
                + "It is still here — but starting another run replaces it, and there is no way to "
                + "get it back afterwards.\n\n"
                + $"Press <b>{ShareLabel}</b> to keep it, or press <b>{buttonLabel}</b> again to "
                + "discard it and start.";
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

            // Takes the warning down if one was up, and puts the report back on screen with the
            // note saying where it went — including when the write failed and the note says so.
            _unsavedReport.NoteShareAttempted(result.Saved);
            ForceReportRedraw();
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
                // The quoted figures bound the searching, not the wall clock — a frame goes by
                // between searches so results can appear as they arrive, and that adds a few
                // seconds across a whole run. Promising the bare number as a finish time reads as
                // running late to whoever is holding the phone and watching the clock.
                + $"Press {QuickRunLabel} to time this device's AI search — at most "
                + $"{_benchmark.EstimatedWorstCase:mm\\:ss} of searching, plus a few seconds for the "
                + "screen to keep up. It cannot be paused once it begins.\n\n"
                + $"Press {LongRunLabel} to check whether the impossible tier's search depth holds steady over "
                + $"sustained play — at most {_benchmark.ThermalEstimatedWorstCase:mm\\:ss} of searching.\n\n"
                + $"Either way, press {ShareLabel} when it finishes — starting another run replaces the "
                + "report and there is no way back to it.\n\n"
                + "Leave the app in the foreground while either run is going. The screen is kept awake "
                + "for you, but a run left in the background takes noticeably longer.";
        }
    }
}
