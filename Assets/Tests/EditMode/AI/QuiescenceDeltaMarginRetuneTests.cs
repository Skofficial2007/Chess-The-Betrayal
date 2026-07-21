using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Tightening the quiescence delta-pruning margin can, unlike most of this codebase's pruning
    /// levers, change which move a search reports as best — a positional swing the plain material
    /// margin doesn't account for could in principle be big enough to matter once the margin is cut.
    /// Every move below was captured with the margin temporarily reverted to its pre-retune value of
    /// 200 and confirmed identical on the tightened value of 150 across all five of the depth-wall
    /// diagnosis's positions before being locked in here — proving the smaller margin changed how
    /// much tree gets explored, not which move the search settles on.
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
        public void RunQuiescenceForTest_WinningQueenCapture_StillFindsItAtTheTighterMargin()
        {
            // Regression re-check of QuiescenceDeltaPruningTests' own winning-capture case at the
            // NEW margin value — a genuinely winning capture must still clear whatever margin is
            // currently configured.
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
