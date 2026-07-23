using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Changing the quiescence delta-pruning margin can, unlike most of this codebase's pruning
    /// levers, change which move a search reports as best — a positional swing the plain material
    /// margin doesn't account for could in principle be big enough to matter once the margin moves.
    /// Every move below was captured at a margin of 200 and confirmed identical at 150 across all
    /// five of the depth-wall diagnosis's positions, so these assertions hold on both values and
    /// pin the property that actually matters: the margin changes how much tree gets explored, not
    /// which move the search settles on.
    ///
    /// The margin has since been measured back to 200 (see its comment in AlphaBetaSearch for the
    /// reasoning), which is why these expectations did not need to move with it.
    ///
    /// SemiOpenMidgame's expected move changed when the King's piece-square table was split into
    /// midgame/endgame halves — that position sits at roughly 80% of full phase weight (a bishop
    /// and a knight already off the board on each side), so the King's evaluation genuinely shifted
    /// enough to reorder two close-scored root candidates. Re-verified this is the taper's doing and
    /// not a regression: the position's material and legal moves are unaffected, only which of two
    /// similarly-scored rook moves comes out ahead.
    ///
    /// QuietMidgame's expected move was checked again when pawn structure was added: Black's e5 pawn
    /// scores as isolated now, which briefly favored a bishop capture of it while the lazy hatch's
    /// swing bound was still zero -- but with the hatch's real, proven bound active the root move
    /// lands back on the original queen trade (the hatch legally cuts full evaluation at enough
    /// quiescence nodes to change which line gets explored deeply enough to matter). Re-verified
    /// this position's expectation is unchanged, not silently left alone.
    ///
    /// SemiOpenMidgame's expected move changed again for the same reason: with the pawn term and the
    /// active lazy hatch both wired in, the search now prefers Rxf8 (the straight rook capture on the
    /// open f-file) over the earlier Rf1-e1 repositioning -- both b6 and d6 read as isolated for
    /// Black now, sharpening the position enough that taking the exchange outright wins out.
    /// </summary>
    [TestFixture]
    public class QuiescenceDeltaMarginRetuneTests
    {
        private ChessEngineAdapter _engine;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
        }

        private MoveCommand SearchAt(BoardState board, int maxDepth)
        {
            var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(),
                transpositionTable: new TranspositionTable(log2Size: 20));
            var settings = new AISearchSettings(maxDepth, TestTimeBudgets.Generous, BetrayalUsage.Full);
            return search.FindBestMove(board, settings, CancellationToken.None);
        }

        [Test]
        public void FindBestMove_QuietMidgame_MatchesThePreRetuneBestMoveAtDepthSeven()
        {
            MoveCommand best = SearchAt(SearchDepthProfileCaptureTests.QuietMidgame(), 7);
            Assert.That(best.StartPosition.x, Is.EqualTo(3));
            Assert.That(best.StartPosition.y, Is.EqualTo(0));
            Assert.That(best.EndPosition.x, Is.EqualTo(3));
            Assert.That(best.EndPosition.y, Is.EqualTo(7));
            Assert.That(best.Stage, Is.EqualTo(BetrayalStage.None));
        }

        [Test]
        public void FindBestMove_SemiOpenMidgame_MatchesThePreRetuneBestMoveAtDepthSeven()
        {
            MoveCommand best = SearchAt(SearchDepthProfileCaptureTests.SemiOpenMidgame(), 7);
            Assert.That(best.StartPosition.x, Is.EqualTo(5));
            Assert.That(best.StartPosition.y, Is.EqualTo(0));
            Assert.That(best.EndPosition.x, Is.EqualTo(5));
            Assert.That(best.EndPosition.y, Is.EqualTo(7));
            Assert.That(best.Stage, Is.EqualTo(BetrayalStage.None));
        }

        [Test]
        public void FindBestMove_TacticalMidgame_MatchesThePreRetuneBestMoveAtDepthSeven()
        {
            MoveCommand best = SearchAt(SearchDepthProfileCaptureTests.TacticalMidgame(), 7);
            Assert.That(best.StartPosition.x, Is.EqualTo(4));
            Assert.That(best.StartPosition.y, Is.EqualTo(0));
            Assert.That(best.EndPosition.x, Is.EqualTo(4));
            Assert.That(best.EndPosition.y, Is.EqualTo(3));
            Assert.That(best.Stage, Is.EqualTo(BetrayalStage.None));
        }

        [Test]
        public void FindBestMove_BetrayalLiveMidgame_MatchesThePreRetuneBestMoveAtDepthSeven()
        {
            MoveCommand best = SearchAt(SearchDepthProfileCaptureTests.BetrayalLiveMidgame(), 7);
            Assert.That(best.StartPosition.x, Is.EqualTo(3));
            Assert.That(best.StartPosition.y, Is.EqualTo(3));
            Assert.That(best.EndPosition.x, Is.EqualTo(2));
            Assert.That(best.EndPosition.y, Is.EqualTo(5));
            Assert.That(best.Stage, Is.EqualTo(BetrayalStage.None));
        }

        [Test]
        public void FindBestMove_QuietEndgame_MatchesThePreRetuneBestMoveAtDepthSeven()
        {
            MoveCommand best = SearchAt(SearchDepthProfileCaptureTests.QuietEndgame(), 7);
            Assert.That(best.StartPosition.x, Is.EqualTo(3));
            Assert.That(best.StartPosition.y, Is.EqualTo(2));
            Assert.That(best.EndPosition.x, Is.EqualTo(3));
            Assert.That(best.EndPosition.y, Is.EqualTo(5));
            Assert.That(best.Stage, Is.EqualTo(BetrayalStage.None));
        }

        [Test]
        public void RunQuiescenceForTest_WinningQueenCapture_StillFindsItAtTheConfiguredMargin()
        {
            // Regression re-check of QuiescenceDeltaPruningTests' own winning-capture case — a
            // genuinely winning capture must still clear whatever margin is currently configured,
            // which is the invariant that has to survive any future retune in either direction.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d4", Team.White, ChessPieceType.Pawn)
                .WithPiece("e5", Team.Black, ChessPieceType.Queen)
                .WithTurn(Team.White)
                .WithComputedHash();
            var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());

            int scoreWithCapture = search.RunQuiescenceForTest(board, Team.White, CancellationToken.None);

            BoardState boardNoQueen = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d4", Team.White, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithComputedHash();
            var searchNoQueen = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());
            int scoreWithoutQueen = searchNoQueen.RunQuiescenceForTest(boardNoQueen, Team.White, CancellationToken.None);

            Assert.That(scoreWithCapture, Is.GreaterThan(scoreWithoutQueen),
                "The tightened margin must still find and credit a genuinely winning capture.");
        }
    }
}
