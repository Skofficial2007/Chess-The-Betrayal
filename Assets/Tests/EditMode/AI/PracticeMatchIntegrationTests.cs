using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Gameplay.Manager;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Full scripted practice match, end to end: MatchDriver + UndoService + a real AsyncAIAgent
    /// wired exactly as GameManager wires them (OnTurnCompleted -> UndoService.RecordTurn,
    /// AI move -> MatchDriver.PlayMove), manually pumping Tick() the same way GameManager's
    /// Update() does. No MonoBehaviour/GameManager instance is constructed — EditMode can't host
    /// one — so this exercises every seam GameManager itself calls, at the layer immediately below
    /// the Unity wiring. Event channels are null throughout (MatchDriver only ever raises them via
    /// ?., same pattern as UndoServiceTests).
    /// </summary>
    [TestFixture]
    public class PracticeMatchIntegrationTests
    {
        private const int PollTimeoutMs = 10_000;
        private const int PollIntervalMs = 10;

        private ChessEngineAdapter _engine;
        private BoardState _board;
        private MatchDriver _matchDriver;
        private UndoService _undoService;
        private AsyncAIAgent _aiAgent;
        private MoveCommand? _lastAiMove;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
            _board = TestBoardSetupUtility.CreateStandard();
            _board.CurrentTurn = Team.White;

            _matchDriver = new MatchDriver(_engine, _board, logMoves: false, domainLogger: null,
                gameOverChannel: null, turnChangedChannel: null, moveExecutedChannel: null,
                moveRejectedChannel: null, checkDetectedChannel: null, betrayalChannel: null);
            _matchDriver.TransitionToPhase(TurnPhase.Normal);

            _undoService = new UndoService(_engine, _board, _matchDriver);
            _matchDriver.OnTurnCompleted += _undoService.RecordTurn;
        }

        [TearDown]
        public void TearDown()
        {
            _aiAgent?.Dispose();
        }

        private void ConstructAgent(BetrayalUsage betrayalUsage)
        {
            _aiAgent = new AsyncAIAgent(
                _engine,
                new BetrayalAwareEvaluator(),
                new AISearchSettings(maxDepth: 2, TestTimeBudgets.Generous, betrayalUsage));

            _aiAgent.OnMoveDecided += move =>
            {
                _lastAiMove = move;
                _matchDriver.PlayMove(move);
            };
        }

        private void RequestAiMoveAndWait(Team aiTeam)
        {
            _lastAiMove = null;
            _aiAgent.RequestBestMove(_board, aiTeam);
            PumpTickUntil(() => _lastAiMove.HasValue);
            Assert.That(_lastAiMove, Is.Not.Null, "AI search did not deliver a move within the poll timeout.");
        }

        private void PumpTickUntil(System.Func<bool> isDone)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!isDone() && stopwatch.ElapsedMilliseconds < PollTimeoutMs)
            {
                _aiAgent.Tick();
                Thread.Sleep(PollIntervalMs);
            }
        }

        [Test]
        public void FullMatch_HumanWhiteVsAiBlack_ExchangesThenGameEnd_ZobristConsistentThroughout()
        {
            // Human draws White; AI plays Black. This is the ordinary TurnChangedEvent-driven path
            // (GameManager.OnTurnChangedForAI) — the AI never moves until a human turn completes.
            ConstructAgent(BetrayalUsage.Full);

            for (int ply = 0; ply < 3; ply++)
            {
                Assert.That(_board.CurrentTurn, Is.EqualTo(Team.White));
                var legalMoves = new System.Collections.Generic.List<MoveCommand>();
                _engine.GetAllLegalMovesIncludingBetrayal(_board, Team.White, legalMoves);
                Assert.That(legalMoves, Is.Not.Empty, "Position must have at least one legal White move to continue the scripted match.");
                _matchDriver.PlayMove(legalMoves[0]);
                Assert.DoesNotThrow(() => _board.AssertZobristConsistency());

                if (_board.IsGameOver) break;

                Assert.That(_board.CurrentTurn, Is.EqualTo(Team.Black));
                RequestAiMoveAndWait(Team.Black);
                Assert.DoesNotThrow(() => _board.AssertZobristConsistency());
            }

            Assert.That(_matchDriver.MoveLog.Entries.Count, Is.GreaterThan(0));
        }

        [Test]
        public void FullMatch_HumanDrawsBlack_AiMovesFirstBeforeAnyHumanInput()
        {
            // Mirrors GameManager.BeginPlay's human-Black path: no preceding TurnChangedEvent
            // exists for the very first ply, so the caller must request the AI's move directly.
            _board.CurrentTurn = Team.White;
            ConstructAgent(BetrayalUsage.Full);

            RequestAiMoveAndWait(Team.White);

            Assert.That(_lastAiMove.Value.PieceTeam, Is.EqualTo(Team.White));
            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.Black), "AI's opening move must flip the turn to the human (Black).");
            Assert.DoesNotThrow(() => _board.AssertZobristConsistency());
        }

        [Test]
        public void DefendOnlyMatch_AiNeverInitiatesActButDefendsAgainstHumanAct()
        {
            // Constructed so White has a Betrayal Act available at the root, and after White's
            // reply Black (DefendOnly AI) still has its own Act visible one ply beyond root
            // (structural guarantee pinned by BetrayalModeTests) — this test instead exercises the
            // end-to-end wiring: MatchDriver->AsyncAIAgent->MatchDriver.PlayMove never produces an
            // Act as the AI's own root choice while DefendOnly is active.
            _board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("b8", Team.Black, ChessPieceType.Knight)
                .WithPiece("a6", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h1", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            _matchDriver = new MatchDriver(_engine, _board, logMoves: false, domainLogger: null,
                gameOverChannel: null, turnChangedChannel: null, moveExecutedChannel: null,
                moveRejectedChannel: null, checkDetectedChannel: null, betrayalChannel: null);
            _matchDriver.TransitionToPhase(TurnPhase.Normal);

            _undoService = new UndoService(_engine, _board, _matchDriver);
            _matchDriver.OnTurnCompleted += _undoService.RecordTurn;

            ConstructAgent(BetrayalUsage.DefendOnly);

            // White (human) makes an ordinary, non-Act move.
            _matchDriver.PlayMove(MoveCommand.CreateStandardMove(
                TestBoardSetupUtility.AlgebraicToVector("h1"),
                TestBoardSetupUtility.AlgebraicToVector("h5"),
                _board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("h1")),
                _board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("h5")),
                _board));

            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.Black));

            RequestAiMoveAndWait(Team.Black);

            Assert.That(_lastAiMove.Value.Stage, Is.Not.EqualTo(BetrayalStage.Act),
                "DefendOnly AI must never choose an Act move as its own root decision, end to end.");
            Assert.DoesNotThrow(() => _board.AssertZobristConsistency());
        }

        [Test]
        public void MidGameUndo_AiRepliedAlready_PopsBothPliesAndAiCanMoveAgainAfterUndo()
        {
            ConstructAgent(BetrayalUsage.Full);
            ulong hashBeforeExchange = _board.ZobristHash;

            var whiteMoves = new System.Collections.Generic.List<MoveCommand>();
            _engine.GetAllLegalMovesIncludingBetrayal(_board, Team.White, whiteMoves);
            _matchDriver.PlayMove(whiteMoves[0]);

            RequestAiMoveAndWait(Team.Black);
            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.White));

            // Search already finished and delivered (RequestAiMoveAndWait waited for it) —
            // this is the "AI search not in flight" Undo path: pop both plies.
            _undoService.RequestUndo(isAIMode: true, currentPhase: _matchDriver.CurrentPhase, aiSearchInFlight: false, aiMovesFirst: false);

            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.White));
            Assert.DoesNotThrow(() => _board.AssertZobristConsistency());
            Assert.That(_board.ZobristHash, Is.EqualTo(hashBeforeExchange));
            Assert.That(_matchDriver.MoveLog.Entries.Count, Is.EqualTo(0));

            // A subsequent AI request against the restored board must still work — regression
            // guard for the stale-result edge case: RequestBestMove's own CancelInFlight resets
            // the result slot before this new search starts, so a stray prior Tick() pump cannot
            // feed a move computed against the pre-Undo position into this post-Undo request.
            var whiteMovesAgain = new System.Collections.Generic.List<MoveCommand>();
            _engine.GetAllLegalMovesIncludingBetrayal(_board, Team.White, whiteMovesAgain);
            _matchDriver.PlayMove(whiteMovesAgain[0]);

            RequestAiMoveAndWait(Team.Black);
            Assert.That(_lastAiMove.Value.PieceTeam, Is.EqualTo(Team.Black));
            Assert.DoesNotThrow(() => _board.AssertZobristConsistency());
        }

        [Test]
        public void CancelledSearch_StaleResultSlot_NeverDeliveredToASubsequentTick()
        {
            // Regression case for the low-risk edge found while auditing AI-11: cancel an in-flight
            // search (as GameManager.RequestUndo does before popping the board), then pump Tick()
            // repeatedly with no new request in between. The cancelled search must never surface,
            // even if the worker was mid-flight when cancellation was observed.
            var slowAgent = new AsyncAIAgent(
                _engine,
                new BetrayalAwareEvaluator(),
                new AISearchSettings(maxDepth: 32, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full));

            try
            {
                bool delivered = false;
                slowAgent.OnMoveDecided += _ => delivered = true;

                slowAgent.RequestBestMove(_board, Team.White);
                Assert.That(slowAgent.IsSearching, Is.True);

                slowAgent.CancelSearch();

                var stopwatch = Stopwatch.StartNew();
                while (stopwatch.ElapsedMilliseconds < 1000)
                {
                    slowAgent.Tick();
                    Thread.Sleep(10);
                }

                Assert.That(delivered, Is.False,
                    "A cancelled search's result must never reach OnMoveDecided via any later Tick() pump.");
            }
            finally
            {
                slowAgent.Dispose();
            }
        }
    }
}
