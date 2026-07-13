using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.AI.DeviceBenchmark
{
    /// <summary>
    /// Unity-free benchmark logic: times every built-in AIProfile tier (AIProfileTable.BuiltIn) on
    /// a fixed, materially balanced midgame position, once as a single cold search and once across
    /// several successive plies played by the search's own moves. Deliberately has no dependency on
    /// MonoBehaviour/UnityEngine.Debug/OnGUI — those are presentation concerns owned by
    /// DeviceSearchBenchmark, which drives this class from a coroutine so results can render as
    /// they complete. Keeping the two separate means this runner could be exercised from an
    /// EditMode test with no scene/Play-mode dependency if that's ever useful.
    ///
    /// TEMPORARY diagnostic tool, not shipped gameplay code — delete this whole folder once real
    /// device throughput has been measured across enough devices and a mobile-tier perf plan
    /// exists. Because of that, this class allocates freely (StringBuilder-free plain strings, a
    /// List&lt;MoveCommand&gt; per run) rather than following the zero-GC discipline the actual
    /// gameplay/search hot path (AlphaBetaSearch, TranspositionTable) holds itself to — a benchmark
    /// that runs twelve times total, not sixty times a second, is not that hot path.
    /// </summary>
    public sealed class MobileSearchBenchmarkRunner
    {
        private const double ThresholdSeconds = 6.0;
        private const int DefaultPlyCount = 4;

        /// <summary>One line of benchmark output, already formatted — the caller (a MonoBehaviour,
        /// a test, a console app) decides where it goes (Debug.Log, a scrolling label, stdout).</summary>
        public event Action<string> OnLine;

        /// <summary>
        /// Runs every built-in profile's single-move and multi-move benchmark in turn, then emits
        /// the device-info block and a final completion marker line. Synchronous/blocking — the
        /// caller is responsible for yielding between profiles if it wants incremental UI updates
        /// (see DeviceSearchBenchmark.RunAll's coroutine wrapper).
        /// </summary>
        public void RunProfile(AIProfile profile)
        {
            RunSingleMove(profile);
            RunMultiMove(profile);
        }

        public void EmitStartBanner() => Emit("Device search benchmark starting...");

        public void EmitCompletionBanner()
        {
            EmitDeviceInfo();

            // A single, unmistakable, greppable line — `adb logcat | grep BENCHMARK_RUN_COMPLETE`
            // (or eyeballing the on-screen log) tells you unambiguously that every profile ran to
            // completion, as opposed to the app having merely gone idle/frozen/crashed silently.
            Emit(CompletionMarker);
        }

        public const string CompletionMarker = "===BENCHMARK_RUN_COMPLETE===";

        /// <summary>
        /// DefendOnly, not Full: with Full, the search kept choosing a Betrayal Act as its root
        /// move on this position (a piece betraying its own side), which opens the Retribution
        /// sub-sequence — a game state this simple play-forward runner doesn't model, so the
        /// multi-move loop misread the position as ended one ply later on every profile and
        /// device. DefendOnly strips Act from the ROOT ONLY; the search tree underneath still
        /// explores Betrayal branches at full cost (see BetrayalUsage's own doc comment), so the
        /// measured search work stays representative while the played-out line stays ordinary
        /// chess this simple loop can follow.
        /// </summary>
        private static AISearchSettings SettingsFor(AIProfile profile) =>
            new AISearchSettings(profile.MaxDepth, profile.SoftTimeBudgetMs, BetrayalUsage.DefendOnly);

        /// <summary>
        /// Runs one search under the same wall-clock cap real gameplay uses: AsyncAIAgent arms
        /// CancelAfter(SoftTimeBudgetMs) on every request, and iterative deepening returns the
        /// best move from the last fully completed depth when that fires. Passing
        /// CancellationToken.None instead (an earlier version of this tool did) lets a deep tier
        /// like "impossible" run unbounded — timing a configuration that can never occur in a real
        /// match. Budget-capped timings are what players actually experience.
        /// </summary>
        private static MoveCommand TimedSearch(AlphaBetaSearch search, BoardState board,
            AISearchSettings settings, out SearchTiming timing)
        {
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(settings.SoftTimeBudgetMs);

            var stopwatch = Stopwatch.StartNew();
            MoveCommand best = search.FindBestMove(board, settings, cts.Token);
            stopwatch.Stop();

            int depthReached = 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            depthReached = search.Stats.LastCompletedDepth;
#endif
            timing = new SearchTiming(stopwatch.Elapsed.TotalSeconds, cts.IsCancellationRequested, depthReached);
            return best;
        }

        private void RunSingleMove(AIProfile profile)
        {
            var engine = new ChessEngineAdapter();
            BoardState board = MidgamePosition();
            var search = new AlphaBetaSearch(engine, new BetrayalAwareEvaluator());
            AISearchSettings settings = SettingsFor(profile);

            MoveCommand best = TimedSearch(search, board, settings, out SearchTiming timing);

            Emit($"[{profile.Id}] single-move depth {profile.MaxDepth}: {FormatTiming(timing)}, best={best} — {Verdict(timing.Seconds)}");
        }

        private void RunMultiMove(AIProfile profile, int plyCount = DefaultPlyCount)
        {
            var engine = new ChessEngineAdapter();
            BoardState board = MidgamePosition();
            var search = new AlphaBetaSearch(engine, new BetrayalAwareEvaluator());
            AISearchSettings settings = SettingsFor(profile);
            var legalMoves = new List<MoveCommand>();

            for (int ply = 0; ply < plyCount; ply++)
            {
                // A profile's own blunder rate or a forced sequence can walk this fixed midgame
                // position into checkmate/stalemate before plyCount is reached — FindBestMove
                // would then return an empty default MoveCommand near-instantly (no root moves to
                // search), which used to print a bogus "0.00s PASS" and then feed that no-op move
                // into ApplyMove on the next iteration. Stop cleanly instead of reporting fake data.
                legalMoves.Clear();
                engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, legalMoves);
                if (legalMoves.Count == 0)
                {
                    Emit($"[{profile.Id}] multi-move ply {ply + 1}/{plyCount}: game ended (checkmate/stalemate) — stopping early.");
                    break;
                }

                MoveCommand best = TimedSearch(search, board, settings, out SearchTiming timing);

                Emit($"[{profile.Id}] multi-move ply {ply + 1}/{plyCount}: {FormatTiming(timing)} — {Verdict(timing.Seconds)}");

                // DefendOnly means the search never hands us an Act at the root, so this simple
                // apply-and-flip loop can't wander into a Retribution sub-sequence it doesn't
                // model. If a staged move ever DOES appear here, that's a policy bug worth
                // surfacing loudly rather than silently corrupting the rest of the run.
                if (best.Stage != BetrayalStage.None)
                {
                    Emit($"[{profile.Id}] multi-move ply {ply + 1}/{plyCount}: UNEXPECTED staged move ({best.Stage}) under DefendOnly — aborting this profile's run.");
                    break;
                }

                Team mover = board.CurrentTurn;
                engine.ApplyMove(board, best);
                if (AlphaBetaSearch.StageFlipsTurn(best.Stage))
                    board.CurrentTurn = mover == Team.White ? Team.Black : Team.White;
            }
        }

        private static string FormatTiming(SearchTiming timing)
        {
            string cappedNote = timing.BudgetCapped ? " [budget-capped]" : "";
            string depthNote = timing.DepthReached > 0 ? $" (reached depth {timing.DepthReached})" : "";
            return $"{timing.Seconds:F2}s{cappedNote}{depthNote}";
        }

        private static string Verdict(double seconds) =>
            seconds < ThresholdSeconds ? "PASS (<6s)" : "FAIL (>=6s)";

        private void Emit(string line) => OnLine?.Invoke(line);

        /// <summary>
        /// Dumps every SystemInfo field useful for correlating a timing result with the actual
        /// hardware it ran on. Unity has no direct "chipset name" API — graphicsDeviceName is the
        /// closest honest proxy (a Mali GPU implies MediaTek/Exynos/Unisoc, an Adreno GPU implies
        /// Snapdragon), so it's included alongside deviceModel/processorType rather than guessed.
        /// Reads UnityEngine.SystemInfo/Screen directly (the one place this "Unity-free" class
        /// isn't) since there is no non-Unity way to learn this — kept isolated to this single
        /// method so the rest of the class stays trivially testable outside Play mode.
        /// </summary>
        private void EmitDeviceInfo()
        {
            Emit("--- Device info ---");
            Emit($"Device model: {UnityEngine.SystemInfo.deviceModel}");
            Emit($"Device name: {UnityEngine.SystemInfo.deviceName}");
            Emit($"Device unique ID: {UnityEngine.SystemInfo.deviceUniqueIdentifier}");
            Emit($"OS: {UnityEngine.SystemInfo.operatingSystem}");
            Emit($"CPU: {UnityEngine.SystemInfo.processorType} ({UnityEngine.SystemInfo.processorCount} cores, {UnityEngine.SystemInfo.processorFrequency}MHz)");
            Emit($"GPU (chipset proxy): {UnityEngine.SystemInfo.graphicsDeviceName} [{UnityEngine.SystemInfo.graphicsDeviceVendor}], API {UnityEngine.SystemInfo.graphicsDeviceType}");
            Emit($"RAM: {UnityEngine.SystemInfo.systemMemorySize}MB system, {UnityEngine.SystemInfo.graphicsMemorySize}MB graphics");
            Emit($"Battery: {UnityEngine.SystemInfo.batteryLevel * 100f:F0}% ({UnityEngine.SystemInfo.batteryStatus})");
            Emit($"Screen: {UnityEngine.Screen.width}x{UnityEngine.Screen.height} @ {UnityEngine.Screen.dpi}dpi");
        }

        /// <summary>
        /// A materially balanced, fully symmetric closed-ish midgame position — no hanging pieces,
        /// no immediate tactic for either side to grab. An earlier version of this position had an
        /// undefended pawn, which every profile immediately captured as its very first move,
        /// snowballing into forced mate by ply 3 of 4 on every single profile — barely exercising
        /// "multi-move" at all. This position is deliberately quieter so the search has to actually
        /// think its way through several successive plies instead of immediately cashing in a free
        /// piece and converting to a forced win.
        /// </summary>
        private static BoardState MidgamePosition()
        {
            var board = new BoardState(8, 8);
            board.Clear();
            board.CastlingRights = 0;
            board.EnPassantFile = null;

            void Place(string algebraic, Team team, ChessPieceType type)
            {
                int file = algebraic[0] - 'a';
                int rank = algebraic[1] - '1';
                int moveDir = team == Team.White ? 1 : -1;
                int startRow = type == ChessPieceType.Pawn ? (team == Team.White ? 1 : 6) : 0;
                board.SetPiece(new PieceData(team, type, moveDir, startRow), file, rank);
            }

            Place("g1", Team.White, ChessPieceType.King);
            Place("g8", Team.Black, ChessPieceType.King);
            Place("d1", Team.White, ChessPieceType.Queen);
            Place("d8", Team.Black, ChessPieceType.Queen);
            Place("a1", Team.White, ChessPieceType.Rook);
            Place("f1", Team.White, ChessPieceType.Rook);
            Place("a8", Team.Black, ChessPieceType.Rook);
            Place("f8", Team.Black, ChessPieceType.Rook);
            Place("c3", Team.White, ChessPieceType.Bishop);
            Place("d2", Team.White, ChessPieceType.Bishop);
            Place("c6", Team.Black, ChessPieceType.Bishop);
            Place("d7", Team.Black, ChessPieceType.Bishop);
            Place("f3", Team.White, ChessPieceType.Knight);
            Place("b1", Team.White, ChessPieceType.Knight);
            Place("f6", Team.Black, ChessPieceType.Knight);
            Place("b8", Team.Black, ChessPieceType.Knight);
            Place("a2", Team.White, ChessPieceType.Pawn);
            Place("b2", Team.White, ChessPieceType.Pawn);
            Place("c2", Team.White, ChessPieceType.Pawn);
            Place("d4", Team.White, ChessPieceType.Pawn);
            Place("e3", Team.White, ChessPieceType.Pawn);
            Place("f2", Team.White, ChessPieceType.Pawn);
            Place("g2", Team.White, ChessPieceType.Pawn);
            Place("h2", Team.White, ChessPieceType.Pawn);
            Place("a7", Team.Black, ChessPieceType.Pawn);
            Place("b7", Team.Black, ChessPieceType.Pawn);
            Place("c7", Team.Black, ChessPieceType.Pawn);
            Place("d5", Team.Black, ChessPieceType.Pawn);
            Place("e6", Team.Black, ChessPieceType.Pawn);
            Place("f7", Team.Black, ChessPieceType.Pawn);
            Place("g7", Team.Black, ChessPieceType.Pawn);
            Place("h7", Team.Black, ChessPieceType.Pawn);

            board.CurrentTurn = Team.White;
            board.BetrayalRightAvailable = true;
            board.ComputeFullZobristHash();
            return board;
        }

        /// <summary>One search's outcome: elapsed wall-clock time, whether the soft time budget cut
        /// it off before MaxDepth, and the deepest iterative-deepening depth it fully completed —
        /// the only field that still distinguishes two runs which both hit the same budget cap
        /// (their elapsed seconds are then identical by construction, but the depth reached is
        /// not).</summary>
        private readonly struct SearchTiming
        {
            public readonly double Seconds;
            public readonly bool BudgetCapped;
            public readonly int DepthReached;

            public SearchTiming(double seconds, bool budgetCapped, int depthReached)
            {
                Seconds = seconds;
                BudgetCapped = budgetCapped;
                DepthReached = depthReached;
            }
        }
    }
}
