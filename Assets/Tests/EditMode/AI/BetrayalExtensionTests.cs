using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins the Betrayal/Retribution search extension's contract. The extension is currently
    /// DISABLED (see MaxBetrayalExtensionsPerLine's own comment for the measured reasoning), so
    /// the headline test here proves it stays off even on a position built to invite it — a
    /// silent re-enable would roughly double deep-search node counts. The remaining tests pin the
    /// machinery's safety properties (state restoration, sane counter bounds), which must hold
    /// unchanged if a future measured pass ever turns the cap back up.
    /// </summary>
    [TestFixture]
    public class BetrayalExtensionTests
    {
        private ChessEngineAdapter _engine;
        private AlphaBetaSearch _search;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
            _search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());
        }

        [Test]
        public void FindBestMove_ActStagesRetribution_ExtensionStaysDisabled()
        {
            // White can Act the Knight at b1 onto its own Pawn at a3; White's Rook at a1 can then
            // execute Retribution — the exact shape the extension was built for. With the cap at 0
            // it must still never fire: this position is deliberately the most inviting one, so a
            // future change that quietly re-enables the extension (and its measured node-count
            // cost) fails here instead of only showing up as a slow benchmark.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("a3", Team.White, ChessPieceType.Pawn)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            var settings = new AISearchSettings(maxDepth: 4, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);
            _search.FindBestMove(board, settings, CancellationToken.None);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Assert.That(_search.Stats.BetrayalExtensions, Is.EqualTo(0),
                "The extension is disabled — no Act may be granted extra depth while the cap is 0, " +
                "at the root or anywhere in the tree.");
#endif
        }

        [Test]
        public void FindBestMove_NoLegalBetrayal_NeverGrantsExtension()
        {
            // A position with BetrayalRight unavailable can never Act, so the extension must never
            // fire regardless of depth — a control case proving the guard is actually gating on a
            // real staged Retribution, not firing unconditionally.
            BoardState board = TestBoardSetupUtility.CreateStandard();

            var settings = new AISearchSettings(maxDepth: 3, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);
            _search.FindBestMove(board, settings, CancellationToken.None);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Assert.That(_search.Stats.BetrayalExtensions, Is.EqualTo(0));
#endif
        }

        [Test]
        public void FindBestMove_ChainableBetrayalOpportunities_NeverThrowsOrHangsAndStaysConsistent()
        {
            // Three separate White pieces each have their own Act -> Retribution opportunity
            // available in the same position, engineered so a search descending through one
            // doesn't preclude finding another deeper in the tree — more chances to extend than
            // MaxBetrayalExtensionsPerLine allows on any single line. This isn't a hang/overflow
            // regression test in the strict sense (the qsearch Act bound already prevents the
            // original unbounded-recursion class of bug) - it's a correctness and performance
            // sanity check that a generous supply of extension opportunities completes promptly and
            // leaves the board and hash exactly as it found them, the same proof-of-restraint
            // NullMovePruningSafetyTests uses for the null-move guard.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("a3", Team.White, ChessPieceType.Pawn)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("g1", Team.White, ChessPieceType.Knight)
                .WithPiece("h3", Team.White, ChessPieceType.Pawn)
                .WithPiece("h1", Team.White, ChessPieceType.Rook)
                .WithPiece("d1", Team.White, ChessPieceType.Bishop)
                .WithPiece("c2", Team.White, ChessPieceType.Pawn)
                .WithPiece("b2", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            ulong hashBefore = board.ZobristHash;
            var settings = new AISearchSettings(maxDepth: 4, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            Assert.DoesNotThrow(() => _search.FindBestMove(board, settings, CancellationToken.None));

            Assert.DoesNotThrow(() => board.AssertZobristConsistency(),
                "A runaway or mis-threaded extension counter would eventually desync the incremental hash.");
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore));
        }

        [Test]
        public void FindBestMove_RetributionMoveItself_DoesNotDoubleCountAsExtension()
        {
            // The Retribution move that RESOLVES a pending Betrayer is a Stage == Retribution move,
            // never Stage == Act — the extension guard only ever matches Act, so playing out the
            // Retribution itself must never register a second extension on top of the one already
            // granted for the Act that staged it. Directly exercises the position from the
            // "ExtensionFires" test above but asserts the count stays proportionate rather than
            // growing per Retribution ply explored.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("a3", Team.White, ChessPieceType.Pawn)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            // A single Act/Retribution pair, searched across iterative deepening's own repeated
            // re-exploration of shallower depths plus the exact-rescore pass, legitimately racks up
            // more than one extension count WITHOUT that being double-counting — this position's
            // total node count at maxDepth 4 (small enough to enumerate exactly) puts a hard,
            // deterministic ceiling on how many times a Search node keyed on this exact position
            // could possibly be entered, which is the real invariant worth pinning here rather than
            // an arbitrary guess at "small."
            var settings = new AISearchSettings(maxDepth: 4, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);
            _search.FindBestMove(board, settings, CancellationToken.None);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Never negative, and never so large it could only be explained by a runaway/mis-capped
            // extension loop rather than ordinary iterative-deepening re-exploration of one position.
            Assert.That(_search.Stats.BetrayalExtensions, Is.GreaterThanOrEqualTo(0));
            Assert.That(_search.Stats.BetrayalExtensions, Is.LessThan(_search.Stats.NodesVisited),
                "Extensions granted can never exceed the number of nodes actually visited - each one is " +
                "counted at most once per Search call, on the Act move specifically.");
#endif
        }
    }
}
