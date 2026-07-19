using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Proves the experimental aspiration-window lever is correct when opted into (a narrow-window
    /// fail is always caught and re-searched with the full window, never trusted as a real score,
    /// so the reported best move never changes versus the full-window baseline) and inert when not
    /// (every existing caller that omits enableAspirationWindows is byte-identical to before this
    /// lever existed). This lever ships flag-default-off regardless of what these tests find — see
    /// AlphaBetaSearch.FindBestMove's own doc comment for why.
    /// </summary>
    [TestFixture]
    public class AspirationWindowExperimentTests
    {
        private ChessEngineAdapter _engine;

        [SetUp]
        public void Setup() => _engine = new ChessEngineAdapter();

        /// <summary>A quiet, fully-developed, materially balanced position — deep enough search
        /// (maxDepth 6) that the score has room to drift a little between depths without any single
        /// depth being trivial, a reasonable stand-in for "an ordinary position in a real game."</summary>
        private static BoardState QuietPosition() =>
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

        /// <summary>A position engineered so the true score only becomes visible one ply of
        /// lookahead deeper than a shallow search would suggest — White's queen looks like a safe
        /// capture on b7 at a shallow glance, but is itself trapped by the bishop/knight pair one
        /// ply behind it, so successive iterative-deepening depths land on genuinely different
        /// scores rather than converging smoothly. This is the same shape InstabilityTimeManagement
        /// Tests already uses for its own "unstable root" case, reused here because it's the
        /// existing, proven way in this codebase to force a real depth-to-depth score swing large
        /// enough to blow past a 50cp aspiration window.</summary>
        private static BoardState VolatilePosition() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("g1", Team.White, ChessPieceType.King)
                .WithPiece("g8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("b7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("a8", Team.Black, ChessPieceType.Rook)
                .WithPiece("c8", Team.Black, ChessPieceType.Bishop)
                .WithPiece("b5", Team.Black, ChessPieceType.Bishop)
                .WithPiece("d5", Team.Black, ChessPieceType.Knight)
                .WithPiece("a2", Team.White, ChessPieceType.Pawn)
                .WithPiece("b2", Team.White, ChessPieceType.Pawn)
                .WithPiece("f2", Team.White, ChessPieceType.Pawn)
                .WithPiece("g2", Team.White, ChessPieceType.Pawn)
                .WithPiece("h2", Team.White, ChessPieceType.Pawn)
                .WithPiece("a7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("g7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithComputedHash();

        [Test]
        public void FindBestMove_QuietPosition_AspirationWindowsPickSameMoveAsFullWindowBaseline()
        {
            var settings = new AISearchSettings(maxDepth: 6, TestTimeBudgets.Generous, BetrayalUsage.Full);

            var baseline = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());
            MoveCommand baselineBest = baseline.FindBestMove(QuietPosition(), settings, CancellationToken.None);

            var withAspiration = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());
            MoveCommand aspirationBest = withAspiration.FindBestMove(QuietPosition(), settings, CancellationToken.None,
                enableAspirationWindows: true);

            Assert.That(aspirationBest.StartPosition, Is.EqualTo(baselineBest.StartPosition));
            Assert.That(aspirationBest.EndPosition, Is.EqualTo(baselineBest.EndPosition));
        }

        [Test]
        public void FindBestMove_VolatilePosition_AspirationWindowsPickSameMoveAsFullWindowBaseline()
        {
            // The whole point of this case: a narrow-window fail MUST trigger a same-depth
            // re-search rather than ever being trusted as the real score — if that guarantee ever
            // broke, this is the position where a wrong, window-clamped score would most likely
            // change which root move looks best.
            var settings = new AISearchSettings(maxDepth: 6, TestTimeBudgets.Generous, BetrayalUsage.Full);

            var baseline = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());
            MoveCommand baselineBest = baseline.FindBestMove(VolatilePosition(), settings, CancellationToken.None);

            var withAspiration = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());
            MoveCommand aspirationBest = withAspiration.FindBestMove(VolatilePosition(), settings, CancellationToken.None,
                enableAspirationWindows: true);

            Assert.That(aspirationBest.StartPosition, Is.EqualTo(baselineBest.StartPosition));
            Assert.That(aspirationBest.EndPosition, Is.EqualTo(baselineBest.EndPosition));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Assert.That(withAspiration.Stats.AspirationWindowAttempts, Is.GreaterThan(0),
                "This position should give aspiration windows at least one narrow-window attempt to make, " +
                "otherwise this test isn't actually exercising the lever it claims to.");
            Assert.That(withAspiration.Stats.AspirationWindowReSearches, Is.GreaterThan(0),
                "This position should also force at least one narrow-window guess to fail and trigger " +
                "the full-window re-search - otherwise the retry path (the actual safety-critical part " +
                "of this whole lever) is never proven correct by this test, only reachable-without-crashing.");
#endif
        }

        [Test]
        public void FindBestMove_FlagOff_NeverRecordsAspirationTelemetry()
        {
            var settings = new AISearchSettings(maxDepth: 6, TestTimeBudgets.Generous, BetrayalUsage.Full);
            var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());

            search.FindBestMove(VolatilePosition(), settings, CancellationToken.None);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Assert.That(search.Stats.AspirationWindowAttempts, Is.EqualTo(0));
            Assert.That(search.Stats.AspirationWindowReSearches, Is.EqualTo(0));
#endif
        }

        [Test]
        public void FindBestMove_ReSearchCount_NeverExceedsAttemptCount()
        {
            // A re-search is only ever counted immediately after a narrow-window attempt that
            // failed — it can never outnumber the attempts that could have produced it.
            var settings = new AISearchSettings(maxDepth: 6, TestTimeBudgets.Generous, BetrayalUsage.Full);
            var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());

            search.FindBestMove(VolatilePosition(), settings, CancellationToken.None,
                enableAspirationWindows: true);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Assert.That(search.Stats.AspirationWindowReSearches, Is.LessThanOrEqualTo(search.Stats.AspirationWindowAttempts));
#endif
        }
    }
}
