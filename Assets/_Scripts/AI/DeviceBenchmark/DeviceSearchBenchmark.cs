using System.Collections;
using System.Text;
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
    /// class only owns the coroutine pacing, on-screen scrolling display, and Debug.Log mirroring
    /// so a build run over `adb logcat` still captures full timings without looking at the phone
    /// screen. Delete this whole folder once real device throughput has been measured across
    /// enough devices and a mobile-tier perf plan exists.
    /// </summary>
    public class DeviceSearchBenchmark : MonoBehaviour
    {
        // Once the worker-thread pass landed, HandleLine can run on a thread-pool thread while
        // OnGUI reads _log on the main thread every frame -- a plain StringBuilder isn't safe
        // against that, so both sides take this lock rather than only the writer.
        private readonly object _logLock = new object();
        private readonly StringBuilder _log = new StringBuilder();
        private readonly MobileSearchBenchmarkRunner _runner = new MobileSearchBenchmarkRunner();
        private readonly IAIProfileProvider _profileProvider = new AIProfileTableProvider();
        private bool _running;
        private bool _done;
        private Vector2 _scrollPosition;

        private void OnEnable() => _runner.OnLine += HandleLine;
        private void OnDisable() => _runner.OnLine -= HandleLine;

        private void Start()
        {
            StartCoroutine(RunAll());
        }

        /// <summary>
        /// A coroutine (not a synchronous call from Start) purely so the "Running..." OnGUI label
        /// and each cell's result actually render as they complete, instead of the app looking hung
        /// for the full duration of the run — the slower tiers (extreme/impossible) can plausibly
        /// take several seconds each on real mobile hardware, and a silent frozen screen is
        /// indistinguishable from a crash.
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
            _running = true;
            _runner.EmitStartBanner();
            yield return null;

            for (int positionIndex = 0; positionIndex < MobileSearchBenchmarkRunner.PositionCount; positionIndex++)
            {
                foreach (AIProfile row in AIProfileTable.BuiltIn)
                {
                    // Resolved through the same provider a real match uses, rather than the raw
                    // table row, so a future guardrail clamp applies here exactly as it would in a
                    // real game instead of silently measuring an unclamped profile.
                    AIProfile profile = _profileProvider.Resolve(row.Id);

                    for (int repeatIndex = 0; repeatIndex < MobileSearchBenchmarkRunner.DefaultRepeatCount; repeatIndex++)
                    {
                        Task workerCell = _runner.RunCellOnWorkerThread(positionIndex, profile, repeatIndex);
                        while (!workerCell.IsCompleted) yield return null;
                        if (workerCell.IsFaulted)
                            Debug.LogException(workerCell.Exception);

                        _runner.RunCell(positionIndex, profile, repeatIndex);
                        yield return null;
                    }
                }
            }

            _runner.EmitTierSummaries();
            _runner.EmitCompletionBanner();

            _running = false;
            _done = true;
        }

        // May run on a thread-pool worker (the worker-thread pass) or the main thread (the control
        // pass and every other emitted line) -- Debug.Log itself tolerates either, but _log does
        // not, hence the lock.
        private void HandleLine(string line)
        {
            lock (_logLock) { _log.AppendLine(line); }
            Debug.Log($"[DeviceSearchBenchmark] {line}");
        }

        private void OnGUI()
        {
            GUI.skin.label.fontSize = Mathf.Max(24, Screen.width / 40);

            GUILayout.BeginArea(new Rect(20, 20, Screen.width - 40, Screen.height - 40));

            string header = _running ? "Running benchmark..." : _done ? "Benchmark complete — see final line below." : "Waiting...";
            GUILayout.Label(header, GUI.skin.label);
            GUILayout.Space(10);

            string logText;
            lock (_logLock) { logText = _log.ToString(); }

            // Scrollable so the full log stays reachable by finger-drag even once it runs past
            // one screen — a screenshot alone can't capture output that has scrolled off, but a
            // scroll view at least lets a human read (or a screen-recording capture) all of it.
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));
            GUILayout.Label(logText, GUI.skin.label);
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }
    }
}
