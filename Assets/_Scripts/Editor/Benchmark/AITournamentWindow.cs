using System.Collections.Generic;
using System.Text;
using ChessTheBetrayal.AI;
using UnityEditor;
using UnityEngine;

namespace ChessTheBetrayal.EditorTools.Benchmark
{
    /// <summary>
    /// Interactive front-end over TournamentSession: pick two tiers (or a custom dial set), run a
    /// head-to-head or a Quick/Full tournament, and watch standings, per-tier search cost, and
    /// baseline-drift findings fill in live. Strictly a presentation layer — every game is played
    /// by the same session/seeding the batch BenchmarkRunner uses, so a result seen here at seed N
    /// matches a CI run at seed N (see TournamentSession's own doc comment for the one caveat
    /// time-budgeted searches put on exact reproduction).
    ///
    /// Two ways to run: Live plays one game per editor tick on the main thread (the editor freezes
    /// for each game's duration between repaints, expect visible stalls on deep tiers) so standings
    /// fill in as it goes and a cancel is instant. Parallel (Quick/Full only) hands the whole run to
    /// ParallelTournamentExecutor on a background thread instead, polling a single cancelable
    /// progress bar — dramatically faster wall-clock, at the cost of no live per-game standings
    /// until the run (or a cancel) finishes. Neither survives a domain reload (recompiling scripts
    /// mid-run cancels it); Parallel additionally blocks RunParallel's own call frame until the
    /// background task unwinds, so closing the window mid-run waits for that unwind rather than
    /// tearing down instantly the way Live's per-tick pump does.
    /// </summary>
    public sealed class AITournamentWindow : EditorWindow
    {
        private enum TournamentKind
        {
            HeadToHead,
            Quick,
            Full
        }

        private const int CustomProfileChoice = -1;

        [SerializeField] private TournamentKind _kind = TournamentKind.HeadToHead;
        [SerializeField] private int _runSeed = 20260713;
        [SerializeField] private int _positionCount = 4;
        [SerializeField] private int _plyCap = Tests.Utilities.MatchSimulator.DefaultPlyCap;

        [SerializeField] private int _subjectChoice;
        [SerializeField] private int _opponentChoice = 5;
        [SerializeField] private CustomProfileDraft _customSubject = CustomProfileDraft.Default("custom-a");
        [SerializeField] private CustomProfileDraft _customOpponent = CustomProfileDraft.Default("custom-b");

        private TournamentSession _session;
        private BenchmarkReport _report;
        private BenchmarkReport _baseline;
        private List<DriftFinding> _findings = new List<DriftFinding>();
        private Vector2 _scroll;

        /// <summary>Editable dial set for a hand-built profile. What actually gets played is this
        /// draft run through AIProfileGuardrails.Apply — the window can't construct a profile the
        /// resolution path would have rejected.</summary>
        [System.Serializable]
        private struct CustomProfileDraft
        {
            public string Id;
            public int MaxDepth;
            public int SoftTimeBudgetMs;
            public int HardTimeBudgetMs;
            public float BlunderRate;
            public int BlunderMarginCp;
            public float BetrayalAggression;
            public float AttackDefenseBias;
            public int TieBreakWindowCp;

            public static CustomProfileDraft Default(string id) => new CustomProfileDraft
            {
                Id = id,
                MaxDepth = 3,
                SoftTimeBudgetMs = 1000,
                HardTimeBudgetMs = 1500,
                BlunderRate = 0f,
                BlunderMarginCp = 0,
                BetrayalAggression = 0f,
                AttackDefenseBias = 1f,
                TieBreakWindowCp = 0,
            };

            // The simulator never consults the opening book — deliberately, so a tournament
            // measures raw search strength rather than book coverage (several curated starting
            // positions ARE reachable book lines, so a book probe would short-circuit real
            // searches). The flag is fixed off rather than shown as a dial that would do nothing.
            public AIProfile Build() => AIProfileGuardrails.Apply(new AIProfile(
                Id, MaxDepth, new AITimeBudget(SoftTimeBudgetMs, HardTimeBudgetMs), BlunderRate, BlunderMarginCp,
                BetrayalAggression, AttackDefenseBias, TieBreakWindowCp, useOpeningBook: false));
        }

        [MenuItem("Chess: The Betrayal/AI/Tournament Window...")]
        private static void Open()
        {
            var window = GetWindow<AITournamentWindow>("AI Tournament");
            window.minSize = new Vector2(520, 420);
        }

        private void OnEnable()
        {
            _baseline = BenchmarkBaselineIO.TryRead(BenchmarkBaselineIO.DefaultPath);
        }

        private void OnDisable() => StopRun();

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawSetup();
            EditorGUILayout.Space();
            DrawProgress();
            EditorGUILayout.Space();
            DrawResults();

            EditorGUILayout.EndScrollView();
        }

        // --- Setup ---

        private void DrawSetup()
        {
            bool running = _session != null && !_session.IsComplete;

            using (new EditorGUI.DisabledScope(running))
            {
                EditorGUILayout.LabelField("Setup", EditorStyles.boldLabel);
                _kind = (TournamentKind)EditorGUILayout.EnumPopup("Mode", _kind);
                _runSeed = EditorGUILayout.IntField(
                    new GUIContent("Run Seed", "Same seed reproduces the exact same tournament, bit for bit — including against a batch/CI run."),
                    _runSeed);
                _plyCap = Mathf.Max(2, EditorGUILayout.IntField(
                    new GUIContent("Ply Cap", "Games with no result by this many plies are adjudicated by evaluation margin."),
                    _plyCap));

                if (_kind == TournamentKind.HeadToHead)
                {
                    _positionCount = EditorGUILayout.IntSlider(
                        new GUIContent("Positions", "Each position is played twice, color-swapped, so games = positions x 2."),
                        _positionCount, 1, Tests.Utilities.CuratedPositionSuite.Count);

                    DrawProfilePicker("Side A", ref _subjectChoice, ref _customSubject);
                    DrawProfilePicker("Side B", ref _opponentChoice, ref _customOpponent);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        _kind == TournamentKind.Quick
                            ? "Quick: the six adjacent strength-chain pairings, 4 positions each — the routine check."
                            : "Full: all 15 pairings across every built-in tier and all 20 positions. Deep tiers make this take a long while.",
                        MessageType.Info);
                }

                EditorGUILayout.HelpBox(
                    "Each game runs a real synchronous search — the editor stalls for that game's duration. Deep tiers stall longest.",
                    MessageType.None);
            }

            EditorGUILayout.BeginHorizontal();
            if (!running)
            {
                if (GUILayout.Button("Run Tournament (Live)", GUILayout.Height(26)))
                    StartRun();

                if (_kind != TournamentKind.HeadToHead &&
                    GUILayout.Button("Run Tournament (Parallel, faster)", GUILayout.Height(26)))
                    RunParallel();
            }
            else
            {
                if (GUILayout.Button($"Cancel ({_session.GamesCompleted}/{_session.TotalGames})", GUILayout.Height(26)))
                    StopRun();
            }
            EditorGUILayout.EndHorizontal();

            if (!running)
            {
                EditorGUILayout.HelpBox(
                    "Live plays one game per editor tick and repaints as it goes, so you can watch standings fill " +
                    "in and cancel mid-run — best for a HeadToHead you're actively reading. Parallel plays every " +
                    "game across worker threads with a single cancelable progress bar instead — dramatically " +
                    "faster for Quick/Full, at the cost of not seeing per-game results until it finishes.",
                    MessageType.None);
            }
        }

        private static readonly string[] _builtInIdsCache = BuildChoiceLabels();

        private static string[] BuildChoiceLabels()
        {
            var labels = new List<string>();
            foreach (AIProfile profile in AIProfileTable.BuiltIn) labels.Add(profile.Id);
            labels.Add("custom...");
            return labels.ToArray();
        }

        private void DrawProfilePicker(string label, ref int choice, ref CustomProfileDraft draft)
        {
            int customIndex = _builtInIdsCache.Length - 1;
            int shown = choice == CustomProfileChoice ? customIndex : choice;
            int picked = EditorGUILayout.Popup(label, shown, _builtInIdsCache);
            choice = picked == customIndex ? CustomProfileChoice : picked;

            if (choice != CustomProfileChoice) return;

            EditorGUI.indentLevel++;
            draft.Id = EditorGUILayout.TextField("Id", string.IsNullOrWhiteSpace(draft.Id) ? "custom" : draft.Id);
            draft.MaxDepth = EditorGUILayout.IntSlider("Max Depth", draft.MaxDepth, 1, 12);
            draft.SoftTimeBudgetMs = Mathf.Max(1, EditorGUILayout.IntField("Soft Time Budget (ms)", draft.SoftTimeBudgetMs));
            draft.HardTimeBudgetMs = Mathf.Max(draft.SoftTimeBudgetMs, EditorGUILayout.IntField("Hard Time Budget (ms)", draft.HardTimeBudgetMs));
            draft.BlunderRate = EditorGUILayout.Slider("Blunder Rate", draft.BlunderRate, 0f, 1f);
            draft.BlunderMarginCp = Mathf.Max(0, EditorGUILayout.IntField("Blunder Margin (cp)", draft.BlunderMarginCp));
            draft.BetrayalAggression = EditorGUILayout.Slider("Betrayal Aggression", draft.BetrayalAggression, -1f, 1f);
            draft.AttackDefenseBias = EditorGUILayout.Slider("Attack/Defense Bias", draft.AttackDefenseBias, 0.5f, 2f);
            draft.TieBreakWindowCp = Mathf.Max(0, EditorGUILayout.IntField("Tie-Break Window (cp)", draft.TieBreakWindowCp));

            if (AIProfileGuardrails.RequiresClamp(draft.MaxDepth))
            {
                EditorGUILayout.HelpBox(
                    $"Depth {draft.MaxDepth} is shallow — Attack/Defense Bias and Betrayal Aggression will be clamped to " +
                    $"[{AIProfileGuardrails.MinClampedAttackDefenseBias}, {AIProfileGuardrails.MaxClampedAttackDefenseBias}] / " +
                    $"[{AIProfileGuardrails.MinClampedBetrayalAggression}, {AIProfileGuardrails.MaxClampedBetrayalAggression}] " +
                    "when the profile is built, the same way the runtime resolution path clamps them.",
                    MessageType.Warning);
            }
            EditorGUI.indentLevel--;
        }

        private AIProfile ResolveChoice(int choice, CustomProfileDraft draft) =>
            choice == CustomProfileChoice ? draft.Build() : AIProfileTable.BuiltIn[choice];

        // --- Run control ---

        private void StartRun()
        {
            _baseline = BenchmarkBaselineIO.TryRead(BenchmarkBaselineIO.DefaultPath);
            _findings.Clear();
            _report = null;

            switch (_kind)
            {
                case TournamentKind.HeadToHead:
                    AIProfile subject = ResolveChoice(_subjectChoice, _customSubject);
                    AIProfile opponent = ResolveChoice(_opponentChoice, _customOpponent);
                    if (subject.Id == opponent.Id && (_subjectChoice == CustomProfileChoice || _opponentChoice == CustomProfileChoice))
                    {
                        EditorUtility.DisplayDialog("AI Tournament",
                            "The two custom profiles need distinct ids so their results can be told apart.", "OK");
                        return;
                    }
                    _session = TournamentSession.CreateHeadToHead(_runSeed, subject, opponent, _positionCount, _plyCap);
                    break;
                case TournamentKind.Quick:
                    _session = TournamentSession.CreateQuick(_runSeed, AIProfileTable.BuiltIn, _plyCap);
                    break;
                default:
                    _session = TournamentSession.CreateFull(_runSeed, AIProfileTable.BuiltIn, _plyCap);
                    break;
            }

            _session.OnGameCompleted += HandleGameCompleted;
            EditorApplication.update += Pump;
        }

        /// <summary>
        /// Runs a Quick/Full tournament through ParallelTournamentExecutor instead of the per-tick
        /// live pump. The executor itself runs on a background thread (Parallel.For blocks its
        /// caller until every game finishes, and running it directly on the main thread would
        /// freeze the whole editor exactly like the old single-threaded path did — the entire
        /// reason for this second button is to avoid that) while this thread polls a thread-safe
        /// progress counter and drives a cancelable progress bar. Folding results into the session
        /// (ApplyCompletedGame, which raises OnGameCompleted) still happens on the executor's
        /// thread — fine here because nothing subscribes to OnGameCompleted during a parallel run,
        /// unlike the live path.
        /// </summary>
        private void RunParallel()
        {
            _baseline = BenchmarkBaselineIO.TryRead(BenchmarkBaselineIO.DefaultPath);
            _findings.Clear();
            _report = null;

            _session = _kind == TournamentKind.Quick
                ? TournamentSession.CreateQuick(_runSeed, AIProfileTable.BuiltIn, _plyCap)
                : TournamentSession.CreateFull(_runSeed, AIProfileTable.BuiltIn, _plyCap);

            TournamentSession session = _session;
            var cts = new System.Threading.CancellationTokenSource();
            var progress = new PolledProgress();

            int totalGames = session.TotalGames; // known immediately — don't wait on the first worker report to show a cancel option
            var workerTask = System.Threading.Tasks.Task.Run(() =>
                ParallelTournamentExecutor.RunRemainingGames(session, cancellationToken: cts.Token, progress: progress));

            while (!workerTask.IsCompleted)
            {
                progress.Snapshot(out int completed, out _);
                bool cancelled = EditorUtility.DisplayCancelableProgressBar(
                    "AI Tournament (Parallel)", $"{completed} / {totalGames} games",
                    totalGames == 0 ? 0f : (float)completed / totalGames);
                if (cancelled)
                {
                    cts.Cancel();
                    break;
                }
                System.Threading.Thread.Sleep(50);
            }

            workerTask.Wait(); // RunRemainingGames swallows OperationCanceledException itself; this never throws from cancellation.
            EditorUtility.ClearProgressBar();

            _report = session.BuildReport();
            _findings = BenchmarkDriftAnalyzer.Analyze(_report, _baseline);
            _session = null;
            Repaint();
        }

        /// <summary>Thread-safe last-known-progress holder — RunParallel's polling loop reads this
        /// from the main thread while ParallelTournamentExecutor's workers write it concurrently.</summary>
        private sealed class PolledProgress : ITournamentProgress
        {
            private int _completed;
            private int _total;

            public void ReportGameCompleted(int current, int total)
            {
                System.Threading.Interlocked.Exchange(ref _completed, current);
                System.Threading.Interlocked.Exchange(ref _total, total);
            }

            public void Snapshot(out int completed, out int total)
            {
                completed = System.Threading.Volatile.Read(ref _completed);
                total = System.Threading.Volatile.Read(ref _total);
            }
        }

        private void StopRun()
        {
            if (_session != null)
            {
                _session.OnGameCompleted -= HandleGameCompleted;
                _report = _session.BuildReport();
            }
            EditorApplication.update -= Pump;
            _session = null;
            Repaint();
        }

        /// <summary>One game per editor tick: the editor repaints and processes events between
        /// games, which is the entire reason this doesn't just call BenchmarkRunner.RunAll.</summary>
        private void Pump()
        {
            if (_session == null)
            {
                EditorApplication.update -= Pump;
                return;
            }

            if (!_session.RunNextGame())
                StopRun();
        }

        private void HandleGameCompleted(TournamentGameRecord record)
        {
            _report = _session.BuildReport();
            _findings = BenchmarkDriftAnalyzer.Analyze(_report, _baseline);
            Repaint();
        }

        // --- Display ---

        private void DrawProgress()
        {
            if (_session == null) return;

            Rect rect = EditorGUILayout.GetControlRect(false, 20);
            float fraction = _session.TotalGames == 0 ? 0f : (float)_session.GamesCompleted / _session.TotalGames;
            EditorGUI.ProgressBar(rect, fraction, $"{_session.GamesCompleted} / {_session.TotalGames} games");
        }

        private void DrawResults()
        {
            if (_report == null) return;

            EditorGUILayout.LabelField("Standings", EditorStyles.boldLabel);
            foreach (PairResult pair in _report.PairResults)
            {
                if (pair.Games == 0) continue;
                float margin = TournamentStatistics.WinRateMargin95(pair.Games);
                EditorGUILayout.LabelField(
                    $"{pair.Subject} vs {pair.Opponent}",
                    $"{pair.SubjectWinRate:P1} ±{margin:P0}  ({pair.SubjectWins}W {pair.OpponentWins}L {pair.Draws}D / {pair.Games})");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Per-Tier Search Cost", EditorStyles.boldLabel);
            foreach (TierPerformance tier in _report.TierPerformances)
            {
                EditorGUILayout.LabelField(
                    tier.ProfileId,
                    $"{tier.MeanMsPerMove:F0} ms/move, {tier.MeanNodesPerMove:F0} nodes/move, " +
                    $"depth mean {tier.MeanCompletedDepth:F1} (min {tier.ShallowestCompletedDepth}, max {tier.DeepestCompletedDepth}), " +
                    $"blunder-actuation {tier.ObservedBlunderActuationRate:P1} ({tier.MovesSampled} moves)");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Drift vs Baseline", EditorStyles.boldLabel);
            if (_baseline == null)
            {
                EditorGUILayout.HelpBox("No baseline on disk yet — nothing to diff against.", MessageType.None);
            }
            else if (_findings.Count == 0)
            {
                EditorGUILayout.HelpBox("No drift findings.", MessageType.Info);
            }
            else
            {
                foreach (DriftFinding finding in _findings)
                {
                    EditorGUILayout.HelpBox(finding.Message,
                        finding.Severity == DriftSeverity.Fail ? MessageType.Error : MessageType.Warning);
                }
            }

            if (_session != null) return; // buttons below only make sense on a finished/cancelled run

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy Summary"))
                EditorGUIUtility.systemCopyBuffer = FormatSummary(_report);

            if (GUILayout.Button("Export JSON..."))
            {
                string path = EditorUtility.SaveFilePanel("Export tournament report", "", "tournament-report", "json");
                if (!string.IsNullOrEmpty(path)) BenchmarkBaselineIO.Write(_report, path);
            }

            if (GUILayout.Button("Set as Baseline..."))
            {
                bool confirmed = EditorUtility.DisplayDialog("Update benchmark baseline",
                    "OVERWRITE Docs/Benchmarks/baseline.json with this run's report? Every future run diffs against it. " +
                    "A partial or head-to-head run makes a misleading baseline — prefer a completed Full run.",
                    "Overwrite", "Cancel");
                if (confirmed)
                {
                    BenchmarkBaselineIO.Write(_report, BenchmarkBaselineIO.DefaultPath);
                    _baseline = _report;
                    _findings.Clear();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private static string FormatSummary(BenchmarkReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Tournament — mode={report.Mode} seed={report.RunSeed}");
            foreach (PairResult pair in report.PairResults)
            {
                if (pair.Games == 0) continue;
                sb.AppendLine($"  {pair.Subject} vs {pair.Opponent}: {pair.SubjectWinRate:P1} ({pair.SubjectWins}W {pair.OpponentWins}L {pair.Draws}D / {pair.Games})");
            }
            foreach (TierPerformance tier in report.TierPerformances)
            {
                sb.AppendLine($"  [{tier.ProfileId}] {tier.MeanMsPerMove:F0}ms/move, {tier.MeanNodesPerMove:F0} nodes/move, " +
                    $"depth mean {tier.MeanCompletedDepth:F1} (min {tier.ShallowestCompletedDepth}, max {tier.DeepestCompletedDepth}), " +
                    $"blunder-actuation {tier.ObservedBlunderActuationRate:P1}");
            }
            return sb.ToString();
        }
    }
}
