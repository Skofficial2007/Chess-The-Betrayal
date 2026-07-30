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
    /// TEMPORARY diagnostic tool, not shipped gameplay code. Drop this on any GameObject in an
    /// empty scene and press Play (or build to a device) to time every built-in AIProfile tier
    /// against the EditMode search benchmarks' desktop numbers. Purely a presentation shell around
    /// MobileSearchBenchmarkRunner (all actual benchmark logic lives there, Unity-free) — this
    /// class owns the coroutine pacing, every read of a Unity-only API (device info, battery,
    /// build config, keeping the screen awake), and assembling those into the one BenchmarkReport
    /// that the screen, Debug.Log/adb logcat, and eventually the clipboard and an exported file
    /// all render identically. Delete this whole folder once real device throughput has been
    /// measured across enough devices and a mobile-tier perf plan exists.
    /// </summary>
    public class DeviceSearchBenchmark : MonoBehaviour
    {
        // A worker-thread cell can call EmitOwnLine (via HandleLine) while OnGUI reads the same
        // report on the main thread every frame -- BenchmarkReport keeps no lock of its own (so it
        // stays trivially testable), so every mutation and every render of it goes through this.
        private readonly object _reportLock = new object();
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private readonly MobileSearchBenchmarkRunner _runner = new MobileSearchBenchmarkRunner();
        private readonly IAIProfileProvider _profileProvider = new AIProfileTableProvider();
        private BenchmarkReport _report;
        private Vector2 _scrollPosition;

        private void OnEnable() => _runner.OnLine += HandleLine;
        private void OnDisable() => _runner.OnLine -= HandleLine;

        private void Start()
        {
            // A full pass over the matrix can run for many minutes; without this the phone would
            // lock its screen partway through and the run would keep going against a sleeping
            // display, invisible to whoever is watching it.
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            StartCoroutine(RunAll());
        }

        /// <summary>
        /// A coroutine (not a synchronous call from Start) purely so the report actually renders as
        /// results arrive, instead of the app looking hung for the full duration of the run — the
        /// slower tiers (extreme/impossible) can plausibly take several seconds each on real mobile
        /// hardware, and a silent frozen screen is indistinguishable from a crash.
        ///
        /// Every cell runs twice: once dispatched onto a thread-pool worker the same way
        /// AsyncAIAgent actually searches, and once directly on this coroutine's own thread (the
        /// main thread) as a control, so a difference in scheduling/priority/contention on a given
        /// device's cores shows up as a number instead of staying an assumption. The worker-thread
        /// pass is awaited by polling Task.IsCompleted across yields rather than blocking, so the
        /// main thread keeps rendering while it runs.
        ///
        /// Position outermost, tier next, repeat innermost: if the run is interrupted partway
        /// through, whatever positions finished have complete coverage across every tier and both
        /// thread contexts, rather than one combination finishing everything while the rest never
        /// started.
        /// </summary>
        private IEnumerator RunAll()
        {
            BenchmarkPlan plan = BenchmarkPlan.Tester();
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

            // The worker pass is the real measurement: production always searches on a thread-pool
            // worker, never on the thread driving the frame.
            foreach (int positionIndex in plan.PositionIndices)
            {
                foreach (AIProfile row in AIProfileTable.BuiltIn)
                {
                    // Resolved through the same provider a real match uses, rather than the raw
                    // table row, so a future guardrail clamp applies here exactly as it would in a
                    // real game instead of silently measuring an unclamped profile.
                    AIProfile profile = _profileProvider.Resolve(row.Id);

                    for (int repeatIndex = 0; repeatIndex < plan.RepeatCount; repeatIndex++)
                    {
                        Task workerCell = _runner.RunCellOnWorkerThread(
                            positionIndex, profile, repeatIndex, plan.IncludePlayForward);
                        while (!workerCell.IsCompleted) yield return null;
                        if (workerCell.IsFaulted)
                            Debug.LogException(workerCell.Exception);

                        lock (_reportLock) { _report.RecordCellCompleted(); }
                        yield return null;
                    }
                }
            }

            // A small control on the calling thread, purely to show whether this device's scheduler
            // treats a worker differently from the frame thread.
            for (int i = 0; i < plan.MainThreadControlPositions && i < plan.PositionIndices.Count; i++)
            {
                foreach (AIProfile row in AIProfileTable.BuiltIn)
                {
                    AIProfile profile = _profileProvider.Resolve(row.Id);

                    for (int repeatIndex = 0; repeatIndex < plan.MainThreadControlRepeats; repeatIndex++)
                    {
                        _runner.RunCell(plan.PositionIndices[i], profile, repeatIndex, plan.IncludePlayForward);

                        lock (_reportLock) { _report.RecordCellCompleted(); }
                        yield return null;
                    }
                }
            }

            IReadOnlyList<string> summaryLines = _runner.EmitTierSummaries();
            lock (_reportLock)
            {
                _report.SetSummaryLines(summaryLines);
                _report.AppendHeaderLine(BuildBatteryLine("end"));
                _report.MarkComplete();
            }

            EmitOwnLine(MobileSearchBenchmarkRunner.CompletionMarker);
        }

        // May run on a thread-pool worker (the worker-thread pass) or the main thread (the control
        // pass and every self-emitted line below) -- Debug.Log itself tolerates either, but the
        // report does not, hence the lock.
        private void HandleLine(string line) => EmitOwnLine(line);

        private void EmitOwnLine(string line)
        {
            lock (_reportLock) { _report.AppendDetailLine(line); }
            Debug.Log($"[DeviceSearchBenchmark] {line}");
        }

        private void OnGUI()
        {
            GUI.skin.label.fontSize = Mathf.Max(24, Screen.width / 40);

            GUILayout.BeginArea(new Rect(20, 20, Screen.width - 40, Screen.height - 40));

            string reportText;
            lock (_reportLock)
            {
                reportText = _report != null ? _report.Render(_stopwatch.Elapsed) : "Waiting to start...";
            }

            // Scrollable so the full report stays reachable by finger-drag even once it runs past
            // one screen — a screenshot alone can't capture output that has scrolled off, but a
            // scroll view at least lets a human read (or a screen-recording capture) all of it.
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));
            GUILayout.Label(reportText, GUI.skin.label);
            GUILayout.EndScrollView();

            GUILayout.EndArea();
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
            "--- Device info ---",
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
    }
}
