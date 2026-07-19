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
    /// Times every built-in AIProfile tier (see AIProfileTable.BuiltIn) independently, at its own
    /// MaxDepth/TimeBudget, on the same midgame position SearchBenchmarkTests uses. Each
    /// tier gets its own test so a slow profile fails on its own name instead of hiding inside one
    /// aggregate assertion.
    ///
    /// Threshold is 3.0s per profile — the real per-move target for every difficulty tier (see
    /// AIProfileTable's own comment), tightened from a temporary 6.0s once the search-performance
    /// pass landed enough levers (history/killers, forward pruning, SEE, Betrayal extension, IIR,
    /// instability time management) that every tier measures well under 1s uncapped — see the
    /// benchmark-baseline tracking memory for the numbers this threshold was verified against.
    /// </summary>
    [TestFixture]
    public class AIProfileSearchBenchmarkTests
    {
        private const double ThresholdSeconds = 3.0;

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

        private void AssertSingleMoveUnderThreshold(string profileId)
        {
            AIProfile profile = FindProfile(profileId);
            BoardState board = MidgamePosition();
            var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());
            AISearchSettings settings = SettingsFor(profile);

            var stopwatch = Stopwatch.StartNew();
            MoveCommand best = search.FindBestMove(board, settings, CancellationToken.None);
            stopwatch.Stop();

            System.Console.WriteLine(
                $"[{profileId}] single-move depth {profile.MaxDepth}: {stopwatch.Elapsed.TotalSeconds:F2}s, " +
                $"best={best}, stats={search.Stats}");

            Assert.That(stopwatch.Elapsed.TotalSeconds, Is.LessThan(ThresholdSeconds),
                $"[{profileId}] single-move search took {stopwatch.Elapsed.TotalSeconds:F2}s at depth {profile.MaxDepth} — " +
                $"expected well under {ThresholdSeconds}s.");
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
        private void AssertMultiMoveUnderThreshold(string profileId, int plyCount = 4)
        {
            AIProfile profile = FindProfile(profileId);
            BoardState board = MidgamePosition();
            var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(), transpositionTable: new TranspositionTable(log2Size: 20));
            AISearchSettings settings = SettingsFor(profile);

            for (int ply = 0; ply < plyCount; ply++)
            {
                var stopwatch = Stopwatch.StartNew();
                MoveCommand best = search.FindBestMove(board, settings, CancellationToken.None);
                stopwatch.Stop();

                System.Console.WriteLine(
                    $"[{profileId}] multi-move ply {ply + 1}/{plyCount}: {stopwatch.Elapsed.TotalSeconds:F2}s, best={best}, stats={search.Stats}");

                Assert.That(stopwatch.Elapsed.TotalSeconds, Is.LessThan(ThresholdSeconds),
                    $"[{profileId}] ply {ply + 1}/{plyCount} took {stopwatch.Elapsed.TotalSeconds:F2}s — " +
                    $"expected well under {ThresholdSeconds}s even as the TT fills across successive turns.");

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

        [Test] public void Easy_SingleMove_CompletesUnderThreshold() => AssertSingleMoveUnderThreshold("easy");
        [Test] public void Normal_SingleMove_CompletesUnderThreshold() => AssertSingleMoveUnderThreshold("normal");
        [Test] public void Hard_SingleMove_CompletesUnderThreshold() => AssertSingleMoveUnderThreshold("hard");
        [Test] public void Aggressive_SingleMove_CompletesUnderThreshold() => AssertSingleMoveUnderThreshold("aggressive");
        [Test] public void Extreme_SingleMove_CompletesUnderThreshold() => AssertSingleMoveUnderThreshold("extreme");
        [Test] public void Impossible_SingleMove_CompletesUnderThreshold() => AssertSingleMoveUnderThreshold("impossible");

        [Test] public void Easy_MultiMove_EachPlyCompletesUnderThreshold() => AssertMultiMoveUnderThreshold("easy");
        [Test] public void Normal_MultiMove_EachPlyCompletesUnderThreshold() => AssertMultiMoveUnderThreshold("normal");
        [Test] public void Hard_MultiMove_EachPlyCompletesUnderThreshold() => AssertMultiMoveUnderThreshold("hard");
        [Test] public void Aggressive_MultiMove_EachPlyCompletesUnderThreshold() => AssertMultiMoveUnderThreshold("aggressive");
        [Test] public void Extreme_MultiMove_EachPlyCompletesUnderThreshold() => AssertMultiMoveUnderThreshold("extreme");
        [Test] public void Impossible_MultiMove_EachPlyCompletesUnderThreshold() => AssertMultiMoveUnderThreshold("impossible");
    }
}
