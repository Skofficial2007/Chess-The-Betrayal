using System.Collections;
using System.Text;
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
        private readonly StringBuilder _log = new StringBuilder();
        private readonly MobileSearchBenchmarkRunner _runner = new MobileSearchBenchmarkRunner();
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
        /// and each profile's result actually render as they complete, instead of the app looking
        /// hung for the full duration of all twelve runs — the slower tiers (extreme/impossible)
        /// can plausibly take several seconds each on real mobile hardware, and a silent frozen
        /// screen is indistinguishable from a crash. Each yield return null lets one frame render
        /// between profiles; the search call itself is still a single blocking main-thread call,
        /// same as AsyncAIAgent's worker-thread search would be — this measures the same cost,
        /// just without backgrounding it.
        /// </summary>
        private IEnumerator RunAll()
        {
            _running = true;
            _runner.EmitStartBanner();
            yield return null;

            foreach (AIProfile profile in AIProfileTable.BuiltIn)
            {
                _runner.RunProfile(profile);
                yield return null;
            }

            _runner.EmitCompletionBanner();

            _running = false;
            _done = true;
        }

        private void HandleLine(string line)
        {
            _log.AppendLine(line);
            Debug.Log($"[DeviceSearchBenchmark] {line}");
        }

        private void OnGUI()
        {
            GUI.skin.label.fontSize = Mathf.Max(24, Screen.width / 40);

            GUILayout.BeginArea(new Rect(20, 20, Screen.width - 40, Screen.height - 40));

            string header = _running ? "Running benchmark..." : _done ? "Benchmark complete — see final line below." : "Waiting...";
            GUILayout.Label(header, GUI.skin.label);
            GUILayout.Space(10);

            // Scrollable so the full log stays reachable by finger-drag even once it runs past
            // one screen — a screenshot alone can't capture output that has scrolled off, but a
            // scroll view at least lets a human read (or a screen-recording capture) all of it.
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));
            GUILayout.Label(_log.ToString(), GUI.skin.label);
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }
    }
}
