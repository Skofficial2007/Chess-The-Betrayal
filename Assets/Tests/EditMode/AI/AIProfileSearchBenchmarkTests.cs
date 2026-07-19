using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Runs every built-in AIProfile tier (see AIProfileTable.BuiltIn) under the exact contract a
    /// live match gives it — a cancellation timer armed at the profile's hard time budget plus the
    /// search's own settle-early/panic-extend logic — on the same midgame position
    /// SearchBenchmarkTests uses. Each tier gets its own test so a slow profile fails on its own
    /// name instead of hiding inside one aggregate assertion.
    ///
    /// Two assertions per run, matching the two promises a tier actually makes to a player:
    /// the move arrives on time (wall clock never meaningfully exceeds the hard budget — the
    /// cancellation check runs per root move, so the overshoot allowance only covers finishing
    /// the move in flight plus timer scheduling jitter), and the search got meaningfully deep
    /// before the budget cut it off (a completed-depth floor per tier; iterative deepening keeps
    /// the last fully completed depth's answer, so this is what the player actually faces).
    /// Wall-clock-at-fixed-depth stopped being the gate once Betrayal Defections were valued
    /// honestly: a correct search tree on a Betrayal-live position is simply larger than the
    /// broken one that could be scored inside 3 seconds at depth 9, and the deep tiers' MaxDepth
    /// is a ceiling the budget may legitimately stop short of on any given machine — faster
    /// hardware reaches deeper, the budget promise stays fixed. The uncapped timings remain
    /// visible in each test's console telemetry for eyeballing, they just no longer gate.
    /// </summary>
    [TestFixture]
    public class AIProfileSearchBenchmarkTests
    {
        /// <summary>Grace on top of a profile's hard budget before the wall-clock assertion calls
        /// it late: covers cancellation-latency (the search only polls the token between root
        /// moves), timer scheduling jitter, and first-run JIT warmup on a cold test domain.</summary>
        private const double BudgetOvershootToleranceSeconds = 0.75;

        /// <summary>
        /// The shallowest fully-completed depth each tier must reach within its budget on the
        /// benchmark midgame, measured cold on the current desktop baseline with margin: easy and
        /// normal complete their entire configured depth in a fraction of their budgets, and every
        /// deeper tier completes depth 7 cold (8 warm) inside 3 seconds — so 6 leaves one full ply
        /// of slack for slower machines while still proving the deep tiers genuinely out-search
        /// normal's depth 5 rather than burning their budget on a broken tree.
        /// </summary>
        private static int MinCompletedDepth(AIProfile profile) => profile.Id switch
        {
            "easy" => 3,
            "normal" => 5,
            _ => 6,
        };

        private ChessEngineAdapter _engine;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
        }

        /// <summary>Same representative post-opening midgame position as SearchBenchmarkTests —
        /// both sides developed, no Betrayal state open, realistic piece density.</summary>
        private static BoardState MidgamePosition() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("g1", Team.White, ChessPieceType.King)
                .WithPiece("g8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("d8", Team.Black, ChessPieceType.Queen)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("f1", Team.White, ChessPieceType.Rook)
                .WithPiece("a8", Team.Black, ChessPieceType.Rook)
                .WithPiece("f8", Team.Black, ChessPieceType.Rook)
                .WithPiece("c3", Team.White, ChessPieceType.Bishop)
                .WithPiece("f3", Team.White, ChessPieceType.Knight)
                .WithPiece("c6", Team.Black, ChessPieceType.Bishop)
                .WithPiece("f6", Team.Black, ChessPieceType.Knight)
                .WithPiece("g2", Team.White, ChessPieceType.Bishop)
                .WithPiece("d2", Team.White, ChessPieceType.Knight)
                .WithPiece("g7", Team.Black, ChessPieceType.Bishop)
                .WithPiece("b8", Team.Black, ChessPieceType.Knight)
                .WithPiece("a2", Team.White, ChessPieceType.Pawn)
                .WithPiece("b2", Team.White, ChessPieceType.Pawn)
                .WithPiece("c2", Team.White, ChessPieceType.Pawn)
                .WithPiece("e4", Team.White, ChessPieceType.Pawn)
                .WithPiece("f2", Team.White, ChessPieceType.Pawn)
                .WithPiece("g3", Team.White, ChessPieceType.Pawn)
                .WithPiece("h2", Team.White, ChessPieceType.Pawn)
                .WithPiece("a7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("b7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("c7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("e5", Team.Black, ChessPieceType.Pawn)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("g6", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

        private static AISearchSettings SettingsFor(AIProfile profile) =>
            new AISearchSettings(profile.MaxDepth, profile.TimeBudget, BetrayalUsage.Full);

        /// <summary>Runs one search exactly the way the live agent does — hard-budget cancellation
        /// timer plus the settle-early logic — and returns the move with the wall clock and stats
        /// captured. The per-move token source allocation mirrors the live agent's own.</summary>
        private static MoveCommand FindMoveUnderProductionBudget(
            AlphaBetaSearch search, BoardState board, AISearchSettings settings, out double elapsedSeconds)
        {
            var stopwatch = Stopwatch.StartNew();
            MoveCommand best;
            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(settings.TimeBudget.HardMs);
                best = search.FindBestMove(board, settings, cts.Token, enableInstabilityTimeManagement: true);
            }
            stopwatch.Stop();
            elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
            return best;
        }

        private void AssertBudgetAndDepth(string profileId, AIProfile profile, double elapsedSeconds,
            int lastCompletedDepth, string context)
        {
            double budgetSeconds = profile.TimeBudget.HardMs / 1000.0;
            Assert.That(elapsedSeconds, Is.LessThan(budgetSeconds + BudgetOvershootToleranceSeconds),
                $"[{profileId}] {context} took {elapsedSeconds:F2}s against a {budgetSeconds:F1}s hard budget — " +
                "the cancellation timer or the search's own budget checks are not stopping it on time.");

            Assert.That(lastCompletedDepth, Is.GreaterThanOrEqualTo(MinCompletedDepth(profile)),
                $"[{profileId}] {context} only completed depth {lastCompletedDepth} within its budget — " +
                $"expected at least {MinCompletedDepth(profile)}; the search is spending its time without getting deep.");
        }

        private void AssertSingleMoveHonorsBudgetAndDepthFloor(string profileId)
        {
            AIProfile profile = FindProfile(profileId);
            BoardState board = MidgamePosition();
            var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(),
                transpositionTable: new TranspositionTable(log2Size: 20));
            AISearchSettings settings = SettingsFor(profile);

            MoveCommand best = FindMoveUnderProductionBudget(search, board, settings, out double elapsedSeconds);

            System.Console.WriteLine(
                $"[{profileId}] single-move (max depth {profile.MaxDepth}, hard budget {settings.TimeBudget.HardMs}ms): " +
                $"{elapsedSeconds:F2}s, best={best}, stats={search.Stats}");

            AssertBudgetAndDepth(profileId, profile, elapsedSeconds, search.Stats.LastCompletedDepth, "single-move search");
        }

        /// <summary>Hard backstop against a runaway Betrayal chain within one benchmarked ply. A
        /// real sequence (Act -> Retribution/Defection -> optional ForcedSave) is at most a handful
        /// of half-moves, so a genuine line never approaches this; it exists purely so a future
        /// edge case can't hang this test.</summary>
        private const int MaxBetrayalResolutionStepsPerPly = 8;

        private readonly ITurnResolver _turnResolver = new TurnResolver();

        /// <summary>
        /// Plays PLY_COUNT successive plies with ONE persistent search/TT instance — the exact
        /// escalation shape that used to blow up to 22s/157s/340s across turns before the
        /// pruning/quiescence work (see SearchBenchmarkTests' own doc comment) — so a profile that
        /// times fine on a single cold search but regresses on TT growth/successive-turn cost still
        /// gets caught here.
        ///
        /// If the search's best root move is a Betrayal Act, applying it alone leaves the board
        /// mid-sequence — the same side must immediately choose the Retribution follow-up, or if
        /// no legal Retribution exists, the Betrayer auto-defects. Applying the Act with a plain
        /// engine.ApplyMove (as this test used to) skips that resolution machinery entirely: when
        /// no Retribution move exists, FindBestMove correctly returns an empty move (there is
        /// nothing left to search), and re-applying that empty move as if it were real used to spin
        /// forever instead of ever resolving the Defection. Driving every move through
        /// ITurnResolver.Advance instead is what actually runs that machinery — the search only
        /// ever gets asked for another move while TurnAdvanceResult.NextPhase says one is still
        /// owed (RetributionPending or ForcedSave); a Defection Advance auto-resolves on its own and
        /// reports that in the very same call, no extra step needed.
        ///
        /// The search here is built with the SAME transposition-table size AsyncAIAgent uses in a
        /// real match (log2Size: 20), not the smaller default a single-search test would normally
        /// reach for — this test's whole point is measuring successive-turn cost against a
        /// persistent table exactly like a real game does, and an undersized table thrashes hard
        /// once enough plies accumulate (confirmed: the default 64K-entry table's verification-miss
        /// rate rose from ~7% of probes on the first ply to ~68% by the third on the deepest tier,
        /// which is what a table that's actually too small to hold the search's own working set
        /// looks like — not a real search regression, just this test not matching what a match
        /// actually gives the search to work with).
        /// </summary>
        private void AssertMultiMoveHonorsBudgetAndDepthFloor(string profileId, int plyCount = 4)
        {
            AIProfile profile = FindProfile(profileId);
            BoardState board = MidgamePosition();
            var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(), transpositionTable: new TranspositionTable(log2Size: 20));
            AISearchSettings settings = SettingsFor(profile);

            for (int ply = 0; ply < plyCount; ply++)
            {
                MoveCommand best = FindMoveUnderProductionBudget(search, board, settings, out double elapsedSeconds);

                System.Console.WriteLine(
                    $"[{profileId}] multi-move ply {ply + 1}/{plyCount}: {elapsedSeconds:F2}s, best={best}, stats={search.Stats}");

                AssertBudgetAndDepth(profileId, profile, elapsedSeconds, search.Stats.LastCompletedDepth,
                    $"ply {ply + 1}/{plyCount}");

                TurnAdvanceResult result = _turnResolver.Advance(board, best);

                int resolutionSteps = 0;
                while (result.NextPhase == TurnPhase.RetributionPending || result.NextPhase == TurnPhase.ForcedSave)
                {
                    Assert.That(resolutionSteps++, Is.LessThan(MaxBetrayalResolutionStepsPerPly),
                        $"[{profileId}] ply {ply + 1}/{plyCount} left a Betrayal sequence unresolved after " +
                        $"{MaxBetrayalResolutionStepsPerPly} resolution steps — a real sequence never runs this long.");

                    MoveCommand resolutionMove = search.FindBestMove(board, settings, CancellationToken.None);
                    System.Console.WriteLine(
                        $"[{profileId}] multi-move ply {ply + 1}/{plyCount} Betrayal resolution step {resolutionSteps}: best={resolutionMove}");
                    result = _turnResolver.Advance(board, resolutionMove);
                }
            }
        }

        private static AIProfile FindProfile(string id)
        {
            foreach (AIProfile profile in AIProfileTable.BuiltIn)
                if (profile.Id == id) return profile;

            Assert.Fail($"No built-in profile named '{id}' in AIProfileTable.BuiltIn.");
            return default;
        }

        [Test] public void Easy_SingleMove_HonorsBudgetAndDepthFloor() => AssertSingleMoveHonorsBudgetAndDepthFloor("easy");
        [Test] public void Normal_SingleMove_HonorsBudgetAndDepthFloor() => AssertSingleMoveHonorsBudgetAndDepthFloor("normal");
        [Test] public void Hard_SingleMove_HonorsBudgetAndDepthFloor() => AssertSingleMoveHonorsBudgetAndDepthFloor("hard");
        [Test] public void Aggressive_SingleMove_HonorsBudgetAndDepthFloor() => AssertSingleMoveHonorsBudgetAndDepthFloor("aggressive");
        [Test] public void Extreme_SingleMove_HonorsBudgetAndDepthFloor() => AssertSingleMoveHonorsBudgetAndDepthFloor("extreme");
        [Test] public void Impossible_SingleMove_HonorsBudgetAndDepthFloor() => AssertSingleMoveHonorsBudgetAndDepthFloor("impossible");

        [Test] public void Easy_MultiMove_EachPlyHonorsBudgetAndDepthFloor() => AssertMultiMoveHonorsBudgetAndDepthFloor("easy");
        [Test] public void Normal_MultiMove_EachPlyHonorsBudgetAndDepthFloor() => AssertMultiMoveHonorsBudgetAndDepthFloor("normal");
        [Test] public void Hard_MultiMove_EachPlyHonorsBudgetAndDepthFloor() => AssertMultiMoveHonorsBudgetAndDepthFloor("hard");
        [Test] public void Aggressive_MultiMove_EachPlyHonorsBudgetAndDepthFloor() => AssertMultiMoveHonorsBudgetAndDepthFloor("aggressive");
        [Test] public void Extreme_MultiMove_EachPlyHonorsBudgetAndDepthFloor() => AssertMultiMoveHonorsBudgetAndDepthFloor("extreme");
        [Test] public void Impossible_MultiMove_EachPlyHonorsBudgetAndDepthFloor() => AssertMultiMoveHonorsBudgetAndDepthFloor("impossible");
    }
}
