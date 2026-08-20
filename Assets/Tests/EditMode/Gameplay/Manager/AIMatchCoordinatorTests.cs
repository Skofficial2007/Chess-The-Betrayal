using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI.Search;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.AI.MatchTelemetry;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Diagnostics;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Gameplay.Manager;
using ChessTheBetrayal.Tooling;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Manager
{
    /// <summary>
    /// AIMatchCoordinator owns GameManager's AI-coordinator slice: turn
    /// triggering, search lifecycle, and Undo's cancel-before-pop ordering. Constructed with only
    /// IChessEngine/BoardState/a playMove delegate — no MatchDriver or GameManager reference — so
    /// these tests exercise it exactly the way GameManager composes it, minus the MonoBehaviour.
    /// </summary>
    [TestFixture]
    public class AIMatchCoordinatorTests
    {
        private const int PollTimeoutMs = 5000;
        private const int PollIntervalMs = 10;

        // Shallow/fast settings for delivery/cancellation tests — mirrors AsyncAgentTests' own
        // depth-1 override, so these tests don't have to wait out a full deep search.
        private static AISearchSettings ShallowSettings(BetrayalUsage usage, AIProfile profile) =>
            new AISearchSettings(maxDepth: 1, TestTimeBudgets.Generous, usage);

        // Deep/slow settings, used only where a test needs a wide cancellation window.
        private static AISearchSettings SlowSettings(BetrayalUsage usage, AIProfile profile) =>
            new AISearchSettings(maxDepth: 32, TestTimeBudgets.Generous, usage);

        private static readonly IAIProfileProvider ProfileProvider = new AIProfileTableProvider();

        private ChessEngineAdapter _engine;
        private BoardState _board;
        private AIMatchCoordinator _coordinator;
        private MoveCommand? _lastPlayedMove;
        private int _pliesLanded;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
            _board = TestBoardSetupUtility.CreateStandard();
            _lastPlayedMove = null;
            _pliesLanded = 0;

            // Stands in for the production seam, which applies the move and then announces the ply
            // it landed on. The stub never touches the board, so it counts the plies itself — what
            // matters here is that a move reaching its destination is what completes a record.
            _coordinator = new AIMatchCoordinator(
                _engine, _board,
                move =>
                {
                    _lastPlayedMove = move;
                    _coordinator.NotePlyApplied(move, ++_pliesLanded);
                },
                ShallowSettings, ProfileProvider);
        }

        [TearDown]
        public void TearDown()
        {
            _coordinator.Dispose();
        }

        private void PumpTickUntil(System.Func<bool> isDone)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!isDone() && stopwatch.ElapsedMilliseconds < PollTimeoutMs)
            {
                _coordinator.Tick();
                Thread.Sleep(PollIntervalMs);
            }
        }

        [Test]
        public void IsAiMode_FalseUntilSetAIModeCalled()
        {
            Assert.That(_coordinator.IsAiMode, Is.False);

            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            Assert.That(_coordinator.IsAiMode, Is.True);
        }

        [Test]
        public void TryRequestMove_NotAiTeamsTurn_NeverPlaysAMove()
        {
            _board.CurrentTurn = Team.White;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            _coordinator.TryRequestMove(isGameActive: true);
            Thread.Sleep(200);
            _coordinator.Tick();

            Assert.That(_lastPlayedMove, Is.Null, "AI must not move when it isn't its team's turn.");
        }

        [Test]
        public void TryRequestMove_GameNotActive_NeverPlaysAMove()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            _coordinator.TryRequestMove(isGameActive: false);
            Thread.Sleep(200);
            _coordinator.Tick();

            Assert.That(_lastPlayedMove, Is.Null, "AI must not move once the game is no longer active.");
        }

        [Test]
        public void TryRequestMove_AiTeamsTurnAndGameActive_EventuallyPlaysAMoveThroughTheDelegate()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            _coordinator.TryRequestMove(isGameActive: true);
            PumpTickUntil(() => _lastPlayedMove.HasValue);

            Assert.That(_lastPlayedMove, Is.Not.Null, "AI must deliver a move through the playMove delegate once the search completes.");
            Assert.That(_lastPlayedMove.Value.PieceTeam, Is.EqualTo(Team.Black));
        }

        [Test]
        public void IsSearchInFlight_TrueWhileSearching_FalseAfterDelivery()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            _coordinator.TryRequestMove(isGameActive: true);
            Assert.That(_coordinator.IsSearchInFlight, Is.True);

            PumpTickUntil(() => _lastPlayedMove.HasValue);

            Assert.That(_coordinator.IsSearchInFlight, Is.False);
        }

        [Test]
        public void CancelInFlightSearch_PreventsTheInFlightSearchFromEverPlayingAMove()
        {
            // Deep/slow search settings so cancellation has a wide window to land inside.
            var slowCoordinator = new AIMatchCoordinator(_engine, _board, move => _lastPlayedMove = move, SlowSettings, ProfileProvider);
            _board.CurrentTurn = Team.Black;

            try
            {
                slowCoordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
                slowCoordinator.TryRequestMove(isGameActive: true);
                slowCoordinator.CancelInFlightSearch();

                var stopwatch = Stopwatch.StartNew();
                while (stopwatch.ElapsedMilliseconds < 1000)
                {
                    slowCoordinator.Tick();
                    Thread.Sleep(10);
                }

                Assert.That(_lastPlayedMove, Is.Null, "A cancelled search must never reach the playMove delegate.");
            }
            finally
            {
                slowCoordinator.Dispose();
            }
        }

        [Test]
        public void SetAIMode_CalledAgain_TearsDownThePreviousAgentFirst()
        {
            // Reconfiguring for AI play (e.g. a new match) must not leak the previous agent's
            // OnMoveDecided subscription or leave two agents racing to deliver a move.
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
            _coordinator.TryRequestMove(isGameActive: true);

            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            Assert.That(_coordinator.IsSearchInFlight, Is.False,
                "Reconfiguring via SetAIMode must cancel/replace the prior agent, not run both.");
        }

        [Test]
        public void ClearAIMode_TearsDownTheAgentAndClearsTelemetry()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
            Assert.That(_coordinator.Telemetry, Is.Not.Null, "Sanity check.");
            Assert.That(_coordinator.IsAiMode, Is.True, "Sanity check.");

            _coordinator.ClearAIMode();

            Assert.That(_coordinator.Telemetry, Is.Null,
                "A coordinator configured for no AI at all must not still be holding a previous " +
                "match's recorded moves — that's what let a plain match offer to share the wrong report.");
            Assert.That(_coordinator.IsAiMode, Is.False);
        }

        [Test]
        public void ClearAIMode_OnACoordinatorThatNeverConfiguredAI_IsASafeNoOp()
        {
            Assert.DoesNotThrow(() => _coordinator.ClearAIMode());
            Assert.That(_coordinator.Telemetry, Is.Null);
        }

        [Test]
        public void SetAIMode_AlwaysConstructsAFreshTelemetry_SoAReplayNeverBlendsWithThePriorMatch()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
            AiMatchTelemetry first = _coordinator.Telemetry;

            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
            AiMatchTelemetry second = _coordinator.Telemetry;

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(second, Is.Not.SameAs(first),
                "A second match (e.g. Replay) must get its own telemetry object, not keep recording into the first one's.");
        }

        [Test]
        public void RecordTelemetry_OffByDefault_NeverRecordsAMove_EvenAfterDelivery()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
            // RecordTelemetry is left at its default (false) — this is the composition root's
            // opt-in feature flag, off unless something explicitly turns it on.

            _coordinator.TryRequestMove(isGameActive: true);
            PumpTickUntil(() => _lastPlayedMove.HasValue);

            Assert.That(_coordinator.Telemetry.MoveCount, Is.Zero,
                "Nothing must be recorded unless RecordTelemetry was explicitly turned on.");
        }

        [Test]
        public void RecordTelemetry_On_RecordsTheSearchedMovesDepthAndElapsed()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
            _coordinator.RecordTelemetry = true;

            _coordinator.TryRequestMove(isGameActive: true);
            PumpTickUntil(() => _lastPlayedMove.HasValue);

            Assert.That(_coordinator.Telemetry.MoveCount, Is.EqualTo(1));

            string report = _coordinator.Telemetry.Render();
            Assert.That(report, Does.Contain("1 plies recorded (0 from the opening book, 1 searched, 0 by Defection)"));
            Assert.That(report, Does.Contain("depth 1,"));
            Assert.That(report, Does.Not.Contain("(book)"));
        }

        [Test]
        public void SetAIMode_WithDeviceFactsSupplied_StampsThemOnThisMatchsTelemetry()
        {
            var coordinator = new AIMatchCoordinator(
                _engine, _board, move => _lastPlayedMove = move, ShallowSettings, ProfileProvider,
                logger: null, deviceFacts: () => new[] { "Device model: TestPhone" });

            try
            {
                coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

                // Without this a shared match report is timings attributable to no hardware, which
                // is most of what makes one worth sending back.
                Assert.That(coordinator.Telemetry.Render(), Does.Contain("Device model: TestPhone"));
            }
            finally
            {
                coordinator.Dispose();
            }
        }

        [Test]
        public void SetAIMode_NamesTheTierAndWhatItWasAllowed()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "aggressive");

            // Every elapsed and depth figure in the report is only judgeable against these. A worst
            // elapsed of 3005ms is a tier sitting on its budget or one well inside it depending
            // entirely on which budget it had, and nothing else in the file says which.
            string report = _coordinator.Telemetry.Render();
            Assert.That(report, Does.Contain("AI tier: aggressive"));
            Assert.That(report, Does.Contain("budget"));
        }

        [Test]
        public void SetAIMode_StampsTheFactsAgainForASecondMatch_NotOnlyTheFirst()
        {
            int reads = 0;
            var coordinator = new AIMatchCoordinator(
                _engine, _board, move => _lastPlayedMove = move, ShallowSettings, ProfileProvider,
                logger: null, deviceFacts: () => { reads++; return new[] { "Device model: TestPhone" }; });

            try
            {
                coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
                coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

                Assert.That(reads, Is.EqualTo(2));
                Assert.That(coordinator.Telemetry.Render(), Does.Contain("Device model: TestPhone"),
                    "A Replay builds fresh telemetry, and a header only stamped on the first match " +
                    "would leave the second one unattributable.");
            }
            finally
            {
                coordinator.Dispose();
            }
        }

        [Test]
        public void RecordDefection_RecordsThePieceAsBelongingToWhoeverGainedIt()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
            _coordinator.RecordTelemetry = true;

            // A White queen that has just changed sides. The move carries the Betrayer as it was
            // beforehand, so what the log must name is the other army — the one now holding it.
            var betrayer = new PieceData(Team.White, ChessPieceType.Queen, moveDirection: 1, startRow: 0);
            _coordinator.RecordDefection(
                MoveCommand.CreateDefectionMove(new Vector2Int(0, 0), betrayer), plyNumber: 7);

            string report = _coordinator.Telemetry.Render();
            Assert.That(report, Does.Contain("ply 7:"),
                "The number the driver announced is the one the report shows.");
            Assert.That(report, Does.Contain("Qa1 defects"));
            Assert.That(report, Does.Contain("now Black's"),
                "A White Betrayer that defects ends up in Black's army, and that is what makes " +
                "the piece's later moves explicable.");
            Assert.That(report, Does.Contain("1 by Defection"));
        }

        [Test]
        public void RecordDefection_WithTelemetryOff_RecordsNothing()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            var betrayer = new PieceData(Team.White, ChessPieceType.Queen, moveDirection: 1, startRow: 0);
            _coordinator.RecordDefection(
                MoveCommand.CreateDefectionMove(new Vector2Int(0, 0), betrayer), plyNumber: 7);

            Assert.That(_coordinator.Telemetry.MoveCount, Is.Zero,
                "The same opt-in flag gates this as gates every other recorded ply.");
        }

        [Test]
        public void RecordDefection_BeforeAnyMatchHasStarted_DoesNotThrow()
        {
            // The composition root wires this once and leaves it wired, so it stays subscribed
            // through a plain human-vs-human match where there is no telemetry object at all.
            var betrayer = new PieceData(Team.White, ChessPieceType.Queen, moveDirection: 1, startRow: 0);
            _coordinator.RecordTelemetry = true;

            Assert.DoesNotThrow(() => _coordinator.RecordDefection(
                MoveCommand.CreateDefectionMove(new Vector2Int(0, 0), betrayer), plyNumber: 1));
        }

        [Test]
        public void RecordTelemetry_On_TakesThePlyNumberFromTheMomentTheMoveReachedTheBoard()
        {
            // The shared fixture's playMove stub above only records the move for assertions and
            // never touches the board, so BoardState.PliesPlayed would never move off zero there.
            // This one applies the move the way MatchDriver does and announces the ply that
            // resulted, so the recorded number reflects something real.
            var engine = new ChessEngineAdapter();
            var board = TestBoardSetupUtility.CreateStandard();
            board.CurrentTurn = Team.Black;
            AIMatchCoordinator coordinator = null;
            coordinator = new AIMatchCoordinator(
                engine, board,
                move =>
                {
                    new TurnResolver().Advance(board, move);
                    coordinator.NotePlyApplied(move, board.PliesPlayed);
                },
                ShallowSettings, ProfileProvider);

            try
            {
                coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
                coordinator.RecordTelemetry = true;
                coordinator.TryRequestMove(isGameActive: true);

                var stopwatch = Stopwatch.StartNew();
                while (coordinator.Telemetry.MoveCount == 0 && stopwatch.ElapsedMilliseconds < PollTimeoutMs)
                {
                    coordinator.Tick();
                    Thread.Sleep(PollIntervalMs);
                }

                Assert.That(coordinator.Telemetry.MoveCount, Is.EqualTo(1),
                    "The move landed, so its record must have been completed.");
                Assert.That(coordinator.Telemetry.Render(), Does.Contain("ply 1:"));
            }
            finally
            {
                coordinator.Dispose();
            }
        }

        /// <summary>
        /// CancelInFlightSearch covers the reply that had not landed yet; this covers the plies that
        /// had. Both halves are needed: one real match took two turns back and its report still
        /// listed the moves from them, so its ply numbers ran 47, 49, 50 and then 47 again.
        /// </summary>
        [Test]
        public void NotePliesUnmade_RemovesTheRecordsForPliesATakebackTookOffTheBoard()
        {
            var engine = new ChessEngineAdapter();
            var board = TestBoardSetupUtility.CreateStandard();
            board.CurrentTurn = Team.Black;
            AIMatchCoordinator coordinator = null;
            coordinator = new AIMatchCoordinator(
                engine, board,
                move =>
                {
                    new TurnResolver().Advance(board, move);
                    coordinator.NotePlyApplied(move, board.PliesPlayed);
                },
                ShallowSettings, ProfileProvider);

            try
            {
                coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
                coordinator.RecordTelemetry = true;
                coordinator.TryRequestMove(isGameActive: true);

                var stopwatch = Stopwatch.StartNew();
                while (coordinator.Telemetry.MoveCount == 0 && stopwatch.ElapsedMilliseconds < PollTimeoutMs)
                {
                    coordinator.Tick();
                    Thread.Sleep(PollIntervalMs);
                }

                var betrayer = new PieceData(Team.White, ChessPieceType.Queen, moveDirection: 1, startRow: 0);
                coordinator.RecordDefection(
                    MoveCommand.CreateDefectionMove(new Vector2Int(0, 0), betrayer), plyNumber: 2);

                Assert.That(coordinator.Telemetry.MoveCount, Is.EqualTo(2),
                    "A searched ply and a Defection on top of it — the state before the takeback.");

                // Undo has rewound the board to where it stood after the first ply.
                coordinator.NotePliesUnmade(lastSurvivingPlyNumber: 1);

                Assert.That(coordinator.Telemetry.MoveCount, Is.EqualTo(1));

                string report = coordinator.Telemetry.Render();
                Assert.That(report, Does.Contain("ply 1:"), "The ply that survived the takeback stays.");
                Assert.That(report, Does.Not.Contain("ply 2:"));
                Assert.That(report, Does.Contain("0 by Defection"),
                    "A Defection that was taken back must stop being counted, or the report claims a "
                    + "Betrayal right was spent twice in one match.");
            }
            finally
            {
                coordinator.Dispose();
            }
        }

        [Test]
        public void NotePliesUnmade_BeforeAnyMatchHasStarted_DoesNotThrow()
        {
            // Wired once by the composition root and left wired, so it stays subscribed through a
            // plain human-vs-human match where there is no telemetry object at all.
            Assert.DoesNotThrow(() => _coordinator.NotePliesUnmade(0));
        }

        [Test]
        public void RecordTelemetry_AMoveHeldBackBehindAnAnimation_StillGetsThePlyItLandedOn()
        {
            // The real playMove seam is a pacing queue: it applies a move straight away when idle
            // and holds it behind whatever is still animating otherwise. A move decided fast enough
            // -- an instant book reply, or a mate found in a few dozen milliseconds -- arrives while
            // the opponent's move is still playing out and so reaches the board later than it was
            // decided. Reading the board at decision time recorded those a ply short, and they are
            // the only ones a real match ever got wrong.
            var engine = new ChessEngineAdapter();
            var board = TestBoardSetupUtility.CreateStandard();
            board.CurrentTurn = Team.Black;

            MoveCommand? held = null;
            AIMatchCoordinator coordinator = null;
            coordinator = new AIMatchCoordinator(
                engine, board, move => held = move, ShallowSettings, ProfileProvider);

            try
            {
                coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
                coordinator.RecordTelemetry = true;
                coordinator.TryRequestMove(isGameActive: true);

                var stopwatch = Stopwatch.StartNew();
                while (held == null && stopwatch.ElapsedMilliseconds < PollTimeoutMs)
                {
                    coordinator.Tick();
                    Thread.Sleep(PollIntervalMs);
                }

                Assert.That(held.HasValue, Is.True, "The search never delivered a move.");
                Assert.That(coordinator.Telemetry.MoveCount, Is.Zero,
                    "Nothing may be recorded while the move is still waiting to reach the board — " +
                    "its ply number does not exist yet.");

                // Two opponent plies land first, exactly as they would while this one waits its turn.
                board.RecordMove(new Vector2Int(0, 1), new Vector2Int(0, 2));
                board.RecordMove(new Vector2Int(0, 6), new Vector2Int(0, 5));

                new TurnResolver().Advance(board, held.Value);
                coordinator.NotePlyApplied(held.Value, board.PliesPlayed);

                Assert.That(coordinator.Telemetry.Render(), Does.Contain("ply 3:"),
                    "Two plies went ahead of it, so it landed on the third — not the first, which is " +
                    "what the board said at the moment it was decided.");
            }
            finally
            {
                coordinator.Dispose();
            }
        }

        [Test]
        public void NotePlyApplied_UsesTheNumberItIsHanded_NotWhateverTheBoardSaysWhenItRuns()
        {
            // The point of the whole hand-off is that the coordinator never derives this number
            // itself. Every other test here happens to apply the move to a board whose count then
            // agrees with the announced number, so none of them can tell the two apart — this one
            // makes them disagree on purpose.
            var engine = new ChessEngineAdapter();
            var board = TestBoardSetupUtility.CreateStandard();
            board.CurrentTurn = Team.Black;

            MoveCommand? held = null;
            var coordinator = new AIMatchCoordinator(
                engine, board, move => held = move, ShallowSettings, ProfileProvider);

            try
            {
                coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
                coordinator.RecordTelemetry = true;
                coordinator.TryRequestMove(isGameActive: true);

                var stopwatch = Stopwatch.StartNew();
                while (held == null && stopwatch.ElapsedMilliseconds < PollTimeoutMs)
                {
                    coordinator.Tick();
                    Thread.Sleep(PollIntervalMs);
                }

                Assert.That(board.PliesPlayed, Is.Zero, "The stub never touched the board.");
                coordinator.NotePlyApplied(held.Value, plyNumber: 7);

                Assert.That(coordinator.Telemetry.Render(), Does.Contain("ply 7:"));
            }
            finally
            {
                coordinator.Dispose();
            }
        }

        [Test]
        public void NotePlyApplied_ForSomeoneElsesMove_LeavesTheHeldRecordAlone()
        {
            var engine = new ChessEngineAdapter();
            var board = TestBoardSetupUtility.CreateStandard();
            board.CurrentTurn = Team.Black;

            MoveCommand? held = null;
            var coordinator = new AIMatchCoordinator(
                engine, board, move => held = move, ShallowSettings, ProfileProvider);

            try
            {
                coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
                coordinator.RecordTelemetry = true;
                coordinator.TryRequestMove(isGameActive: true);

                var stopwatch = Stopwatch.StartNew();
                while (held == null && stopwatch.ElapsedMilliseconds < PollTimeoutMs)
                {
                    coordinator.Tick();
                    Thread.Sleep(PollIntervalMs);
                }

                // An opponent move already queued ahead of the AI's reply reaches the board first.
                // Taking its ply number would hand the AI's record a number belonging to somebody
                // else's move, which is worse than the defect this replaced.
                MoveCommand someoneElse = MoveCommand.CreateStandardMove(
                    new Vector2Int(0, 1), new Vector2Int(0, 2),
                    board.GetPiece(new Vector2Int(0, 1)), PieceData.Empty, board);
                coordinator.NotePlyApplied(someoneElse, plyNumber: 99);

                Assert.That(coordinator.Telemetry.MoveCount, Is.Zero,
                    "Only the move this record was built for may complete it.");
            }
            finally
            {
                coordinator.Dispose();
            }
        }

        [Test]
        public void CancelInFlightSearch_DropsAMoveThatWasStillWaitingToReachTheBoard()
        {
            var engine = new ChessEngineAdapter();
            var board = TestBoardSetupUtility.CreateStandard();
            board.CurrentTurn = Team.Black;

            MoveCommand? held = null;
            var coordinator = new AIMatchCoordinator(
                engine, board, move => held = move, ShallowSettings, ProfileProvider);

            try
            {
                coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
                coordinator.RecordTelemetry = true;
                coordinator.TryRequestMove(isGameActive: true);

                var stopwatch = Stopwatch.StartNew();
                while (held == null && stopwatch.ElapsedMilliseconds < PollTimeoutMs)
                {
                    coordinator.Tick();
                    Thread.Sleep(PollIntervalMs);
                }

                // Undo empties the pacing queue too, so this move will never reach the board.
                coordinator.CancelInFlightSearch();
                coordinator.NotePlyApplied(held.Value, plyNumber: 7);

                Assert.That(coordinator.Telemetry.MoveCount, Is.Zero,
                    "A move an Undo threw away must not appear in the log as though it were played.");
            }
            finally
            {
                coordinator.Dispose();
            }
        }

        [Test]
        public void TryRequestMove_ThenDelivery_EmitsSearchRequestedAndMoveDecided()
        {
            var logger = new CapturingLogger();
            var coordinator = new AIMatchCoordinator(_engine, _board, move => _lastPlayedMove = move, ShallowSettings, ProfileProvider, logger);
            _board.CurrentTurn = Team.Black;

            try
            {
                coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
                coordinator.TryRequestMove(isGameActive: true);

                Assert.That(logger.Codes, Contains.Item(DomainEventCode.AI_SearchRequested),
                    "Requesting a move must log AI_SearchRequested so the human can see the AI start thinking.");

                var stopwatch = Stopwatch.StartNew();
                while (!_lastPlayedMove.HasValue && stopwatch.ElapsedMilliseconds < PollTimeoutMs)
                {
                    coordinator.Tick();
                    Thread.Sleep(PollIntervalMs);
                }

                Assert.That(_lastPlayedMove, Is.Not.Null);
                Assert.That(logger.Codes, Contains.Item(DomainEventCode.AI_MoveDecided),
                    "Delivering a move must log AI_MoveDecided (with elapsed ms) so the search cost is visible.");
            }
            finally
            {
                coordinator.Dispose();
            }
        }

        [Test]
        public void TryRequestMove_ThenDelivery_MoveDecidedMessageNamesTheDepthAndStopReason()
        {
            var logger = new CapturingLogger();
            var coordinator = new AIMatchCoordinator(_engine, _board, move => _lastPlayedMove = move, ShallowSettings, ProfileProvider, logger);
            _board.CurrentTurn = Team.Black;

            try
            {
                coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
                coordinator.TryRequestMove(isGameActive: true);

                var stopwatch = Stopwatch.StartNew();
                while (!_lastPlayedMove.HasValue && stopwatch.ElapsedMilliseconds < PollTimeoutMs)
                {
                    coordinator.Tick();
                    Thread.Sleep(PollIntervalMs);
                }

                Assert.That(_lastPlayedMove, Is.Not.Null);
                DomainLogEvent moveDecided = logger.Events.First(e => e.Code == DomainEventCode.AI_MoveDecided);

                // ShallowSettings caps MaxDepth at 1, so a search that completed normally must
                // report exactly that depth back through the log line.
                Assert.That(moveDecided.Message, Does.Contain("depth 1,"),
                    "AI_MoveDecided must name the depth the search actually completed, not just that a move was played.");
                Assert.That(moveDecided.Message, Does.Not.Contain("Unset"),
                    "A real search always sets a concrete stop reason; Unset would mean the depth/reason wiring never ran.");
            }
            finally
            {
                coordinator.Dispose();
            }
        }

        [Test]
        public void CancelInFlightSearch_WhileSearching_EmitsSearchCancelled()
        {
            var logger = new CapturingLogger();
            // Slow settings so the search is genuinely still in flight when we cancel it.
            var coordinator = new AIMatchCoordinator(_engine, _board, move => _lastPlayedMove = move, SlowSettings, ProfileProvider, logger);
            _board.CurrentTurn = Team.Black;

            try
            {
                coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
                coordinator.TryRequestMove(isGameActive: true);
                coordinator.CancelInFlightSearch();

                Assert.That(logger.Codes, Contains.Item(DomainEventCode.AI_SearchCancelled),
                    "Cancelling an in-flight search (the Undo path) must log AI_SearchCancelled.");
            }
            finally
            {
                coordinator.Dispose();
            }
        }

        [Test]
        public void CancelInFlightSearch_WithNoSearchRunning_LogsNothing()
        {
            var logger = new CapturingLogger();
            var coordinator = new AIMatchCoordinator(_engine, _board, move => _lastPlayedMove = move, ShallowSettings, ProfileProvider, logger);

            try
            {
                coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
                coordinator.CancelInFlightSearch(); // nothing is searching

                Assert.That(logger.Codes, Has.No.Member(DomainEventCode.AI_SearchCancelled),
                    "Cancelling when no search is in flight must be a silent no-op, not a spurious cancel log.");
            }
            finally
            {
                coordinator.Dispose();
            }
        }

        [Test]
        public void Dispose_StopsFurtherMoveDelivery()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
            _coordinator.TryRequestMove(isGameActive: true);

            _coordinator.Dispose();

            Thread.Sleep(200);
            _coordinator.Tick();

            Assert.That(_lastPlayedMove, Is.Null, "A disposed coordinator must never deliver a move.");
            Assert.That(_coordinator.IsAiMode, Is.False);
        }

        /// <summary>
        /// Verbose-on IDomainLogger that records every event code it's handed, so lifecycle-logging
        /// assertions can check what the coordinator emitted. IsVerbose is true because the
        /// coordinator gates its LogInfo calls behind it. Locked because a search delivered via
        /// Tick() and a cancel from the test thread can both call in.
        /// </summary>
        private sealed class CapturingLogger : IDomainLogger
        {
            private readonly object _lock = new object();
            private readonly List<DomainLogEvent> _events = new List<DomainLogEvent>();

            public bool IsVerbose => true;

            public IReadOnlyList<DomainEventCode> Codes
            {
                get { lock (_lock) { return _events.Select(e => e.Code).ToList(); } }
            }

            /// <summary>The full events, message and AuxInt included — Codes above only carries
            /// enough to assert an event fired at all, not what it said.</summary>
            public IReadOnlyList<DomainLogEvent> Events
            {
                get { lock (_lock) { return new List<DomainLogEvent>(_events); } }
            }

            private void Add(DomainLogEvent evt) { lock (_lock) { _events.Add(evt); } }

            public void LogInfo(DomainLogEvent evt) => Add(evt);
            public void LogWarning(DomainLogEvent evt) => Add(evt);
            public void LogError(DomainLogEvent evt) => Add(evt);
        }
    }
}
