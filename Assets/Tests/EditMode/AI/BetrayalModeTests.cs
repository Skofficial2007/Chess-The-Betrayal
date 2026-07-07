using NUnit.Framework;
using System.Collections.Generic;
using System.Threading;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Verifies the three player-facing Betrayal configurations from the AI's point of view:
    /// Normal (board-level, zero AI code — BetrayalRightAvailable is simply false), DefendOnly
    /// (the agent never Acts itself but must still see and fear the opponent's Act), and Skip mode
    /// (the search never invokes the human-only voluntary-skip path).
    /// </summary>
    [TestFixture]
    public class BetrayalModeTests
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
        public void NormalMode_BetrayalRightUnavailable_RootMovesNeverContainAct()
        {
            // Normal mode is board-level: BetrayalRightAvailable = false at match init. No AI
            // policy is involved at all — this just confirms the search's own Act-inclusive move
            // generation (GetAllLegalMovesIncludingBetrayal) never surfaces an Act move when the
            // right isn't available, since GetBetrayalTargets itself early-returns on that flag.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("a3", Team.White, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithBetrayalRight(false)
                .WithComputedHash();

            var settings = new AISearchSettings(maxDepth: 2, softTimeBudgetMs: 5000, BetrayalUsage.Full);
            MoveCommand best = _search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(best.Stage, Is.Not.EqualTo(BetrayalStage.Act),
                "With BetrayalRightAvailable false, no Act move can exist to choose from.");

            List<MoveCommand> rootMoves = new List<MoveCommand>();
            _engine.GetAllLegalMovesIncludingBetrayal(board, Team.White, rootMoves);
            Assert.That(rootMoves, Has.None.Matches<MoveCommand>(m => m.Stage == BetrayalStage.Act),
                "Root move list must never contain an Act move when the board-level right is unavailable.");
        }

        [Test]
        public void DefendOnlyMode_NeverChoosesActAsItsOwnRootMove()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("a3", Team.White, ChessPieceType.Pawn)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            var settings = new AISearchSettings(maxDepth: 2, softTimeBudgetMs: 5000, BetrayalUsage.DefendOnly);
            MoveCommand best = _search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(best.Stage, Is.Not.EqualTo(BetrayalStage.Act),
                "DefendOnly must never let the agent choose an Act move as its own root decision.");
        }

        [Test]
        public void DefendOnlyMode_OpponentActStillLegalOneplyBeyondRoot()
        {
            // AlphaBetaSearch.BuildRootMoves is the ONLY place the DefendOnly filter runs, and it
            // only ever touches _rootMoves — the recursive Search/Quiescence methods call
            // IChessEngine.GetAllLegalMovesIncludingBetrayal directly with no filtering at all.
            // This test pins that structural guarantee from the outside: after White (DefendOnly)
            // makes any move, the exact same move-gen call the search's recursion uses must still
            // return Black's Act move untouched. If DefendOnly's filter ever leaked into the
            // recursive move-gen call, this Act move would silently disappear from what the search
            // can see one ply in.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("h1", Team.White, ChessPieceType.Rook)
                .WithPiece("b8", Team.Black, ChessPieceType.Knight)
                .WithPiece("a6", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            var settings = new AISearchSettings(maxDepth: 1, softTimeBudgetMs: 5000, BetrayalUsage.DefendOnly);
            MoveCommand whiteMove = _search.FindBestMove(board, settings, CancellationToken.None);

            _engine.ApplyMove(board, whiteMove);
            board.CurrentTurn = Team.Black;

            List<MoveCommand> blackMoves = new List<MoveCommand>();
            _engine.GetAllLegalMovesIncludingBetrayal(board, Team.Black, blackMoves);

            Assert.That(blackMoves, Has.Some.Matches<MoveCommand>(m => m.Stage == BetrayalStage.Act),
                "Black's Act move must still be legal and visible one ply beyond White's DefendOnly root — " +
                "the filter must never reach the recursive move generation the search itself calls.");

            board.CurrentTurn = Team.White;
            _engine.UndoMove(board, whiteMove);
        }

        [Test]
        public void SkipMode_LegalRetributionAlwaysChosenOverForcedDefection()
        {
            // The search never calls RequestRetributionSkip/ResolveVoluntaryDefection — that path
            // is human-only. This means whenever a legal Retribution capture exists, the search's
            // own move generation (which returns ONLY Retribution moves while a Betrayer is
            // pending) leaves it no alternative but to choose one, never to "pass" on it.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("a3", Team.White, ChessPieceType.Pawn)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            List<MoveCommand> actMoves = new List<MoveCommand>();
            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("b1"), actMoves);
            MoveCommand actMove = actMoves[0];
            _engine.ApplyMove(board, actMove);

            var settings = new AISearchSettings(maxDepth: 2, softTimeBudgetMs: 5000, BetrayalUsage.Full);
            MoveCommand chosen = _search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(chosen.Stage, Is.EqualTo(BetrayalStage.Retribution),
                "With a legal executioner available, the search must choose a Retribution move — " +
                "it has no voluntary-skip alternative to consider.");
            Assert.That(chosen.EndPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("a3")),
                "The only legal executioner (Rook a1) must capture the Betrayer at a3.");

            _engine.UndoMove(board, actMove);
        }
    }
}
