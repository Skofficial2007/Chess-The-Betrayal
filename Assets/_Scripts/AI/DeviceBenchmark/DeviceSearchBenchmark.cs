using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ChessTheBetrayal.AI.DeviceBenchmark
{
    /// <summary>
    /// A diagnostic tool kept in the project on purpose, not shipped gameplay code. Runs every
    /// built-in AIProfile tier against a fixed set of positions and reports how long each search
    /// took and how deep it got, so the per-move promise can be checked on real hardware instead of
    /// only on a desktop — the same check anyone who forks the AI or the rules will need to re-run.
    ///
    /// Owns the run and nothing else: the pacing, every read of a Unity-only API (device info,
    /// battery, build config, keeping the screen awake, locking this scene to portrait), and
    /// assembling those into the one
    /// BenchmarkReport that the screen, Debug.Log/adb logcat and an exported file all render
    /// identically. It draws nothing itself — whatever displays the run reads the state below and
    /// asks for a rendered report. The benchmark logic proper lives in MobileSearchBenchmarkRunner,
    /// which has no engine dependency at all.
    ///
    /// Nothing starts on its own. A run begins when something calls StartRun, because a benchmark
    /// that starts the instant the scene loads measures a phone that is still settling after the
    /// app launched, and cannot be repeated without relaunching.
    /// </summary>
    public class DeviceSearchBenchmark : MonoBehaviour
    {
        // A worker-thread cell can call EmitOwnLine (via HandleLine) while a display renders the
        // same report on the main thread -- BenchmarkReport keeps no lock of its own (so it stays
        // trivially testable), so every mutation and every render of it goes through this.
        private readonly object _reportLock = new object();
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private readonly IAIProfileProvider _profileProvider = new AIProfileTableProvider();

        // Both are rebuilt for every run rather than created once. The runner accumulates the
        // per-tier samples its summary is built from, so a second run against the one that already
        // ran would average two runs together and report the blend as though it were one reading.
        private MobileSearchBenchmarkRunner _runner;
        private BenchmarkReport _report;

        /// <summary>True from the moment a run is accepted until its last cell has finished.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>True once a run has finished every planned cell. Stays true until the next run
        /// starts, so whatever offers the finished report can keep offering it.</summary>
        public bool IsComplete { get; private set; }

        /// <summary>How long the current (or most recent) run has been going.</summary>
        public TimeSpan Elapsed => _stopwatch.Elapsed;

        /// <summary>The longest the default (Tester) run can take, answerable before one starts —
        /// worth showing to whoever is about to sit through it.</summary>
        public TimeSpan EstimatedWorstCase => BenchmarkPlan.Tester().EstimatedWorstCase;

        /// <summary>The longest the long-run thermal probe can take. A separate figure from
        /// <see cref="EstimatedWorstCase"/> because the two plans are an order of magnitude apart in
        /// duration, and quoting the wrong one would badly mislead whoever is about to press either
        /// button.</summary>
        public TimeSpan ThermalEstimatedWorstCase => BenchmarkPlan.Thermal().EstimatedWorstCase;

        /// <summary>Changes whenever the report does. Cheap to poll; a display can compare it
        /// against the last value it drew and skip re-rendering when nothing has happened.</summary>
        public int ReportRevision
        {
            get { lock (_reportLock) { return _report?.Revision ?? 0; } }
        }

        /// <summary>
        /// The report as text, or null before the first run has produced one. maxDetailLines caps
        /// how much of the scrolling log comes back — leave it at the default for a file or a
        /// clipboard copy, and pass a bound for a screen, which has a limit on how much text it can
        /// actually draw.
        /// </summary>
        public string RenderReport(ReportStyle style = ReportStyle.Plain, int maxDetailLines = int.MaxValue)
        {
            lock (_reportLock)
            {
                return _report?.Render(_stopwatch.Elapsed, style, maxDetailLines);
            }
        }

        /// <summary>
        /// Begins the default (Tester) run, or returns false if one is already going. Every press
        /// starts genuinely fresh — new runner, new report, new run id — so two readings from the
        /// same device never get blended into one.
        /// </summary>
        public bool StartRun() => StartRun(BenchmarkPlan.Tester());

        /// <summary>
        /// Begins the long-run thermal probe instead of the default plan — the same fresh-start
        /// guarantee as StartRun, just against BenchmarkPlan.Thermal(). Its own button, never folded
        /// into Start, because a ten-minute default is a run nobody finishes.
        /// </summary>
        public bool StartThermalRun() => StartRun(BenchmarkPlan.Thermal());

        private void Awake()
        {
            // This scene is portrait-only; Game Scene is landscape-only. Locking it here rather
            // than leaving it to Player Settings' global default means SceneManager.LoadScene
            // switching between the two doesn't leave the device caught on whichever orientation
            // the previous scene left it in.
            Screen.orientation = ScreenOrientation.Portrait;
        }

        private bool StartRun(BenchmarkPlan plan)
        {
            if (IsRunning) return false;

            IsRunning = true;
            IsComplete = false;

            // A full pass runs for minutes; without this the phone would lock its screen partway
            // through and the run would keep going against a sleeping display.
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            StartCoroutine(RunAll(plan));
            return true;
        }

        private void OnDisable()
        {
            // The coroutine dies with the component, so a run interrupted this way stops here.
            if (_runner != null) _runner.OnLine -= HandleLine;
            IsRunning = false;
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
        }

        /// <summary>
        /// A coroutine (not a straight synchronous call) purely so the report actually renders as
        /// results arrive, instead of the app looking hung for the full duration of the run — a
        /// silent frozen screen is indistinguishable from a crash.
        ///
        /// Most cells are dispatched onto a thread-pool worker the same way AsyncAIAgent actually
        /// searches; a few run directly on this coroutine's own thread as a control, so a
        /// difference in scheduling, priority or contention across a given device's cores shows up
        /// as a number instead of staying an assumption. The worker cells are awaited by polling
        /// Task.IsCompleted across yields rather than blocking, so the main thread keeps rendering
        /// while they run.
        /// </summary>
        private IEnumerator RunAll(BenchmarkPlan plan)
        {
            _runner = new MobileSearchBenchmarkRunner();
            _runner.OnLine += HandleLine;

            string runId = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            lock (_reportLock)
            {
                _report = new BenchmarkReport(runId, plan.TotalCells);
                foreach (string line in BuildDeviceInfoLines()) _report.AppendHeaderLine(line);
                _report.AppendHeaderLine(BuildBatteryLine("start"));
                _report.SetEstimatedWorstCase(plan.EstimatedWorstCase);
            }
            _stopwatch.Restart();
            EmitOwnLine("Device search benchmark starting...");
            yield return null;

            foreach (BenchmarkCell cell in plan.Cells())
            {
                // Resolved through the same provider a real match uses, rather than the raw table
                // row, so a future guardrail clamp applies here exactly as it would in a real game
                // instead of silently measuring an unclamped profile.
                AIProfile profile = _profileProvider.Resolve(cell.ProfileId);

                if (cell.OnWorkerThread)
                {
                    Task workerCell = _runner.RunCellOnWorkerThread(
                        cell.PositionIndex, profile, cell.RepeatIndex, plan.IncludePlayForward);
                    while (!workerCell.IsCompleted) yield return null;
                    if (workerCell.IsFaulted)
                        Debug.LogException(workerCell.Exception);
                }
                else
                {
                    _runner.RunCell(cell.PositionIndex, profile, cell.RepeatIndex, plan.IncludePlayForward);
                }

                lock (_reportLock) { _report.RecordCellCompleted(); }
                yield return null;
            }

            IReadOnlyList<string> summaryLines = _runner.EmitTierSummaries();
            IReadOnlyList<string> thermalLines = _runner.EmitThermalBuckets();
            lock (_reportLock)
            {
                _report.SetSummaryLines(summaryLines);
                _report.SetThermalLines(thermalLines);
                _report.AppendHeaderLine(BuildBatteryLine("end"));
                _report.MarkComplete();
            }

            EmitOwnLine(MobileSearchBenchmarkRunner.CompletionMarker);

            _runner.OnLine -= HandleLine;

            // Freezes the elapsed clock at the run's real duration. Leaving it going would also
            // leave any display redrawing itself once a second forever, long after there is
            // anything new to show.
            _stopwatch.Stop();

            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            IsComplete = true;
            IsRunning = false;
        }

        /// <summary>
        /// Records something that happened around the run rather than during it — where the report
        /// was saved, say. It lands at the end of the log like any other line, so it shows up on
        /// screen and in any copy saved afterwards.
        /// </summary>
        public void AppendNote(string note)
        {
            if (string.IsNullOrEmpty(note)) return;

            lock (_reportLock)
            {
                if (_report == null) return;
                _report.AppendDetailLine(note);
            }

            Debug.Log($"[DeviceSearchBenchmark] {note}");
        }

        // May run on a thread-pool worker (the worker-thread cells) or the main thread (the control
        // cells and every self-emitted line below) -- Debug.Log itself tolerates either, but the
        // report does not, hence the lock.
        private void HandleLine(string line) => EmitOwnLine(line);

        private void EmitOwnLine(string line)
        {
            lock (_reportLock) { _report.AppendDetailLine(line); }
            Debug.Log($"[DeviceSearchBenchmark] {line}");
        }

        /// <summary>
        /// Dumps every SystemInfo field useful for correlating a timing result with the actual
        /// hardware it ran on. Unity has no direct "chipset name" API — graphicsDeviceName is the
        /// closest honest proxy (a Mali GPU implies MediaTek/Exynos/Unisoc, an Adreno GPU implies
        /// Snapdragon), so it's included alongside deviceModel/processorType rather than guessed.
        /// Battery isn't in here — it's sampled separately at both the start and the end of a run
        /// (see BuildBatteryLine), since it's the one fact that actually changes while the
        /// benchmark runs.
        /// </summary>
        private static List<string> BuildDeviceInfoLines() => new List<string>
        {
            $"Device model: {SystemInfo.deviceModel}",
            $"Device name: {SystemInfo.deviceName}",
            $"Device unique ID: {SystemInfo.deviceUniqueIdentifier}",
            $"OS: {SystemInfo.operatingSystem}",
            $"CPU: {SystemInfo.processorType} ({SystemInfo.processorCount} cores, {SystemInfo.processorFrequency}MHz)",
            $"GPU (chipset proxy): {SystemInfo.graphicsDeviceName} [{SystemInfo.graphicsDeviceVendor}], API {SystemInfo.graphicsDeviceType}",
            $"RAM: {SystemInfo.systemMemorySize}MB system, {SystemInfo.graphicsMemorySize}MB graphics",
            $"Screen: {Screen.width}x{Screen.height} @ {Screen.dpi}dpi",
            BuildConfigLine(),
        };

        // Development Build and the scripting backend are both baked in at build time, not
        // queryable from a running app the way device hardware is -- the preprocessor symbols are
        // the only honest way to report which one this build actually is.
        private static string BuildConfigLine()
        {
#if DEVELOPMENT_BUILD
            const string buildType = "Development Build";
#else
            const string buildType = "Release Build";
#endif
#if ENABLE_IL2CPP
            const string scriptingBackend = "IL2CPP";
#else
            const string scriptingBackend = "Mono";
#endif
            return $"Build: {buildType}, {scriptingBackend}, platform {Application.platform}";
        }

        private static string BuildBatteryLine(string when) =>
            $"Battery ({when}): {SystemInfo.batteryLevel * 100f:F0}% ({SystemInfo.batteryStatus})";

        /// <summary>The device model, for anything that needs to name this device outside the
        /// report — a filename, say. Kept here because this is the one place in the feature that
        /// legitimately touches Unity APIs.</summary>
        public static string DeviceModel => SystemInfo.deviceModel;
    }
}
