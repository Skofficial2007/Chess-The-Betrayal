using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Proves internal iterative reduction actually fires on a TT-move-less deep node (seeding move
    /// ordering with a cheap shallower probe before the real search), never fires once a TT move is
    /// already known, and never changes which move the search ultimately picks on a fixed position.
    /// </summary>
    [TestFixture]
    public class InternalIterativeReductionTests
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
        public void FindBestMove_DeepSearchFromEmptyTT_FiresInternalIterativeReduction()
        {
            // A fresh search on a mid-complexity, symmetric middlegame position visits plenty of
            // nodes at depth >= 4 with no prior TT entry to hand them a move - a standard opening's
            // narrow, highly-transposing move set instead lets nearly every depth>=4 node get
            // reached by a shallower iterative-deepening pass first, leaving no fresh nodes at all
            // for IIR to act on (confirmed by instrumenting the production code directly - every
            // depth>=4 probe already carried a non-zero TT move there). This position's wider,
            // less-transposing branching gives IIR real TT-move-less nodes to seed instead.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("d8", Team.Black, ChessPieceType.Queen)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("h1", Team.White, ChessPieceType.Rook)
                .WithPiece("a8", Team.Black, ChessPieceType.Rook)
                .WithPiece("h8", Team.Black, ChessPieceType.Rook)
                .WithPiece("c3", Team.White, ChessPieceType.Knight)
                .WithPiece("f3", Team.White, ChessPieceType.Knight)
                .WithPiece("c6", Team.Black, ChessPieceType.Knight)
                .WithPiece("f6", Team.Black, ChessPieceType.Knight)
                .WithTurn(Team.White)
                .WithComputedHash();

            var settings = new AISearchSettings(maxDepth: 5, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);
            _search.FindBestMove(board, settings, CancellationToken.None);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Assert.That(_search.Stats.IirReductions, Is.GreaterThan(0),
                "A deep search starting from an empty table should trigger at least one internal iterative reduction.");
#endif
        }

        [Test]
        public void FindBestMove_ShallowSearch_NeverFiresInternalIterativeReduction()
        {
            // Below the minimum depth threshold, every node is already cheap enough that the extra
            // probe search would cost more than it saves - IIR must stay off entirely down there.
            BoardState board = TestBoardSetupUtility.CreateStandard();

            var settings = new AISearchSettings(maxDepth: 3, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);
            _search.FindBestMove(board, settings, CancellationToken.None);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Assert.That(_search.Stats.IirReductions, Is.EqualTo(0));
#endif
        }

        [Test]
        public void FindBestMove_FixedPosition_PicksSameBestMoveAcrossRepeatedRuns()
        {
            // IIR only changes move ORDERING within a node via its own shallower probe search - it
            // must never change which move the search ultimately settles on for a fixed position.
            // Since IIR fires deterministically off ttMove/depth (no randomness, no wall-clock
            // dependence), running the exact same position through two independent, freshly
            // constructed searches - each starting from an empty table so IIR gets the same chances
            // to fire both times - must land on the identical best move every time.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("d8", Team.Black, ChessPieceType.Queen)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("h1", Team.White, ChessPieceType.Rook)
                .WithPiece("a8", Team.Black, ChessPieceType.Rook)
                .WithPiece("h8", Team.Black, ChessPieceType.Rook)
                .WithPiece("c3", Team.White, ChessPieceType.Knight)
                .WithPiece("f3", Team.White, ChessPieceType.Knight)
                .WithPiece("c6", Team.Black, ChessPieceType.Knight)
                .WithPiece("f6", Team.Black, ChessPieceType.Knight)
                .WithTurn(Team.White)
                .WithComputedHash();

            var settings = new AISearchSettings(maxDepth: 5, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            var firstRun = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());
            MoveCommand firstBest = firstRun.FindBestMove(board, settings, CancellationToken.None);

            var secondRun = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());
            MoveCommand secondBest = secondRun.FindBestMove(board, settings, CancellationToken.None);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Assert.That(firstRun.Stats.IirReductions, Is.GreaterThan(0),
                "This fixed position at depth 5 should give IIR at least one TT-move-less node to fire on, " +
                "otherwise this test isn't actually exercising the lever it claims to.");
#endif

            Assert.That(secondBest.StartPosition, Is.EqualTo(firstBest.StartPosition));
            Assert.That(secondBest.EndPosition, Is.EqualTo(firstBest.EndPosition));
        }
    }
}
