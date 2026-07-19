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
    /// Threshold is a temporary 6.0s per profile while search performance work is still in
    /// progress — the real target for every tier is under 3s (see AIProfileTable's own comment).
    /// "hard" and above are expected to be the tightest against this ceiling until that work lands;
    /// a tier failing here is a real signal, not a flaky threshold, since 6s is already double the
    /// eventual target.
    /// </summary>
    [TestFixture]
    public class AIProfileSearchBenchmarkTests
    {
        private const double ThresholdSeconds = 6.0;

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

        /// <summary>
        /// Plays PLY_COUNT successive plies with ONE persistent search/TT instance — the exact
        /// escalation shape that used to blow up to 22s/157s/340s across turns before the
        /// pruning/quiescence work (see SearchBenchmarkTests' own doc comment) — so a profile that
        /// times fine on a single cold search but regresses on TT growth/successive-turn cost still
        /// gets caught here.
        /// </summary>
        private void AssertMultiMoveUnderThreshold(string profileId, int plyCount = 4)
        {
            AIProfile profile = FindProfile(profileId);
            BoardState board = MidgamePosition();
            var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());
            AISearchSettings settings = SettingsFor(profile);

            for (int ply = 0; ply < plyCount; ply++)
            {
                var stopwatch = Stopwatch.StartNew();
                MoveCommand best = search.FindBestMove(board, settings, CancellationToken.None);
                stopwatch.Stop();

                System.Console.WriteLine(
                    $"[{profileId}] multi-move ply {ply + 1}/{plyCount}: {stopwatch.Elapsed.TotalSeconds:F2}s, best={best}");

                Assert.That(stopwatch.Elapsed.TotalSeconds, Is.LessThan(ThresholdSeconds),
                    $"[{profileId}] ply {ply + 1}/{plyCount} took {stopwatch.Elapsed.TotalSeconds:F2}s — " +
                    $"expected well under {ThresholdSeconds}s even as the TT fills across successive turns.");

                Team mover = board.CurrentTurn;
                _engine.ApplyMove(board, best);
                if (AlphaBetaSearch.StageFlipsTurn(best.Stage))
                    board.CurrentTurn = mover == Team.White ? Team.Black : Team.White;
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
