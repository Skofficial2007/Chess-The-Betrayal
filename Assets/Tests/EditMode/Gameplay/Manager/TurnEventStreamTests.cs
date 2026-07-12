using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Core.Utils;
using ChessTheBetrayal.Events.Payloads;
using ChessTheBetrayal.Gameplay.Manager;
using ChessTheBetrayal.Tests.Utilities;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Manager
{
    /// <summary>
    /// Pins the per-ply event-stream contract the AI-26 match-lifecycle work introduces:
    /// MoveExecutedPayload.PlyIndex stays monotonic independent of TurnNumber (which repeats
    /// across a Betrayal sub-sequence), and MatchFlowCoordinator.ConfigureMatch is a genuinely
    /// pure domain seam that raises zero view-facing callbacks — the exact gap a future
    /// server-authoritative caller needs closed (see MatchFlowCoordinator's doc comments).
    /// </summary>
    [TestFixture]
    public class TurnEventStreamTests
    {
        [Test]
        public void PlyIndex_PlainMoves_IncrementsOncePerPly()
        {
            var engine = new ChessEngineAdapter();
            var board = TestBoardSetupUtility.CreateStandard();

            var seenPlyIndexes = new List<int>();
            var moveExecutedChannel = ScriptableObject.CreateInstance<ChessTheBetrayal.Events.MoveExecutedEventChannel>();
            moveExecutedChannel.Register(payload => seenPlyIndexes.Add(payload.PlyIndex));

            var matchDriver = new MatchDriver(engine, board, logMoves: false, domainLogger: null,
                gameOverChannel: null, turnChangedChannel: null, moveExecutedChannel: moveExecutedChannel,
                moveRejectedChannel: null, checkDetectedChannel: null, betrayalChannel: null);
            matchDriver.TransitionToPhase(TurnPhase.Normal);

            MoveCommand whitePush = MoveCommand.CreateStandardMove(
                TestBoardSetupUtility.AlgebraicToVector("e2"), TestBoardSetupUtility.AlgebraicToVector("e4"),
                board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e2")), PieceData.Empty, board);
            matchDriver.PlayMove(whitePush);

            MoveCommand blackPush = MoveCommand.CreateStandardMove(
                TestBoardSetupUtility.AlgebraicToVector("e7"), TestBoardSetupUtility.AlgebraicToVector("e5"),
                board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e7")), PieceData.Empty, board);
            matchDriver.PlayMove(blackPush);

            Assert.That(seenPlyIndexes, Is.EqualTo(new[] { 1, 2 }),
                "Each applied ply must raise MoveExecutedPayload with a monotonically incrementing PlyIndex starting at 1.");

            Object.DestroyImmediate(moveExecutedChannel);
        }

        [Test]
        public void PlyIndex_BetrayalActThenRetribution_KeepsIncrementingAcrossTheSubSequence()
        {
            // PlyIndex must increment on EVERY applied ply, including the Act/Retribution pair of
            // a Betrayal sub-sequence — unlike TurnNumber, which stays pinned to the same value for
            // both plies since the turn hasn't flipped yet (see _betrayalSequenceMoveNumber).
            var engine = new ChessEngineAdapter();
            var board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("a3", Team.White, ChessPieceType.Pawn)
                .WithBetrayalRight(true)
                .WithTurn(Team.White);
            board.ComputeFullZobristHash();

            var seenPlyIndexes = new List<int>();
            var moveExecutedChannel = ScriptableObject.CreateInstance<ChessTheBetrayal.Events.MoveExecutedEventChannel>();
            moveExecutedChannel.Register(payload => seenPlyIndexes.Add(payload.PlyIndex));

            var matchDriver = new MatchDriver(engine, board, logMoves: false, domainLogger: null,
                gameOverChannel: null, turnChangedChannel: null, moveExecutedChannel: moveExecutedChannel,
                moveRejectedChannel: null, checkDetectedChannel: null, betrayalChannel: null);
            matchDriver.TransitionToPhase(TurnPhase.Normal);

            var actMoves = new List<MoveCommand>();
            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("b1"), actMoves);
            matchDriver.PlayMove(actMoves[0]);

            var retMoves = new List<MoveCommand>();
            engine.GetRetributionMoves(board, Team.White, board.PendingBetrayerSquare.Value, retMoves);
            matchDriver.PlayMove(retMoves[0]);

            Assert.That(seenPlyIndexes, Is.EqualTo(new[] { 1, 2 }),
                "Act and Retribution are two distinct plies — PlyIndex must count both even though TurnNumber doesn't advance between them.");

            Object.DestroyImmediate(moveExecutedChannel);
        }

        [Test]
        public void PlyIndex_ResetsToZeroOnConfigureMatch()
        {
            MatchFlowFixture fixture = MatchFlowFixture.Build();

            fixture.MatchFlow.HandleTeamRollRequested();
            fixture.MatchFlow.HandleTeamAnimationComplete();
            fixture.MatchFlow.BeginPlay();

            var from = new Vector2Int(0, 1);
            var to = new Vector2Int(0, 3);
            fixture.MatchFlow.RequestMove(from, to);

            // A second match (e.g. Replay) must not inherit the previous match's ply count.
            fixture.MatchFlow.HandleTeamRollRequested();
            fixture.MatchFlow.HandleTeamAnimationComplete();
            fixture.MatchFlow.BeginPlay();

            var seenPlyIndexes = new List<int>();
            fixture.MoveExecutedChannel.Register(payload => seenPlyIndexes.Add(payload.PlyIndex));

            fixture.MatchFlow.RequestMove(from, to);

            Assert.That(seenPlyIndexes, Is.EqualTo(new[] { 1 }),
                "PlyIndex must restart at 1 for the new match, not continue counting from the previous one.");

            fixture.Dispose();
        }

        [Test]
        public void ConfigureMatch_RaisesZeroViewFacingCallbacks()
        {
            MatchFlowFixture fixture = MatchFlowFixture.Build();

            fixture.MatchFlow.HandleTeamRollRequested();
            fixture.MatchFlow.ConfigureMatch(settings: null);

            Assert.That(fixture.RaisedGameModeConfiguredCount, Is.Zero,
                "ConfigureMatch is the pure domain half — raising GameModeConfigured is HandleTeamAnimationComplete's job.");
            Assert.That(fixture.RaisedGameStartedCount, Is.Zero,
                "ConfigureMatch is the pure domain half — raising GameStarted is HandleTeamAnimationComplete's job.");
            Assert.That(fixture.SetSharedBoardStateCount, Is.Zero,
                "ConfigureMatch must not touch the shared-state bridge either — that's presentation wiring.");
            Assert.That(fixture.MatchFlow.CurrentPhase, Is.EqualTo(TurnPhase.Starting),
                "The domain state machine must still boot into Starting even with zero view callbacks raised.");

            fixture.Dispose();
        }

        [Test]
        public void HandleTeamAnimationComplete_StillRaisesBothViewCallbacksExactlyOnce()
        {
            // Regression guard for the AI-26 extraction: the local-play flow's observable
            // behavior must not change even though the raises moved out of ConfigureMatch.
            MatchFlowFixture fixture = MatchFlowFixture.Build();

            fixture.MatchFlow.HandleTeamRollRequested();
            fixture.MatchFlow.HandleTeamAnimationComplete();

            Assert.That(fixture.RaisedGameModeConfiguredCount, Is.EqualTo(1));
            Assert.That(fixture.RaisedGameStartedCount, Is.EqualTo(1));
            Assert.That(fixture.SetSharedBoardStateCount, Is.EqualTo(1));

            fixture.Dispose();
        }

        [Test]
        public void RequestUndo_RaisesBoardResyncRequired_NotGameStarted()
        {
            // AI-26 replaces the undo path's reuse of raiseGameStarted with a distinct
            // BoardResyncRequired signal — "game started" must stay a true lifecycle fact a
            // future network reconnect can rely on, not be overloaded to also mean "resync."
            MatchFlowFixture fixture = MatchFlowFixture.Build();

            var settings = new PracticeMatchSettings(
                betrayalEnabled: true, aiDefendOnly: true, retributionSkipAllowed: true, aiProfileId: "normal");
            fixture.MatchFlow.SetPracticeMatchSettings(settings);

            fixture.MatchFlow.HandleTeamRollRequested();
            Assert.That(fixture.MatchFlow.PlayerTeam, Is.EqualTo(Team.White), "Fixture pins the human to White.");
            fixture.MatchFlow.HandleTeamAnimationComplete();
            fixture.MatchFlow.BeginPlay();

            fixture.MatchFlow.RequestMove(new Vector2Int(0, 1), new Vector2Int(0, 3));
            Assert.That(fixture.MatchFlow.CanUndo, Is.True);

            int gameStartedCountBeforeUndo = fixture.RaisedGameStartedCount;

            fixture.MatchFlow.RequestUndo();

            Assert.That(fixture.RaisedBoardResyncRequiredCount, Is.EqualTo(1),
                "Undo must raise BoardResyncRequired so BoardVisuals rebuilds.");
            Assert.That(fixture.RaisedGameStartedCount, Is.EqualTo(gameStartedCountBeforeUndo),
                "Undo must NOT re-raise GameStarted — that would make it fire for reasons other than a genuinely new match.");

            fixture.Dispose();
        }

        // Deterministic stand-in for SystemRandomSource — pins RollTeams so the human always draws
        // White, matching the pattern MatchFlowCoordinatorTests.CanUndo_TrueOnlyAfterAPracticeMatchTurnCompletes
        // uses for the same reason (a hardcoded White-pawn move below is illegal if the roll gives
        // the AI White instead).
        private sealed class FixedRandomSource : IRandomSource
        {
            private readonly bool _nextBool;
            public FixedRandomSource(bool nextBool) { _nextBool = nextBool; }
            public bool NextBool() => _nextBool;
            public int NextInt(int maxExclusive) => 0;
            public float NextFloat() => 0f;
        }

        /// <summary>Minimal MatchFlowCoordinator wiring shared by this fixture's tests, mirroring MatchFlowCoordinatorTests' Setup.</summary>
        private sealed class MatchFlowFixture
        {
            public MatchFlowCoordinator MatchFlow;
            public ChessTheBetrayal.Events.MoveExecutedEventChannel MoveExecutedChannel;
            public int RaisedGameModeConfiguredCount;
            public int RaisedGameStartedCount;
            public int RaisedBoardResyncRequiredCount;
            public int SetSharedBoardStateCount;

            private GameObject _host;
            private AIMatchCoordinator _aiCoordinator;

            public static MatchFlowFixture Build()
            {
                var fixture = new MatchFlowFixture();
                fixture._host = new GameObject("TurnEventStreamTests.Host");

                var engine = new ChessEngineAdapter();
                var board = new BoardState(8, 8);
                fixture.MoveExecutedChannel = ScriptableObject.CreateInstance<ChessTheBetrayal.Events.MoveExecutedEventChannel>();

                var matchDriver = new MatchDriver(engine, board, logMoves: false, domainLogger: null,
                    gameOverChannel: null, turnChangedChannel: null, moveExecutedChannel: fixture.MoveExecutedChannel,
                    moveRejectedChannel: null, checkDetectedChannel: null, betrayalChannel: null);

                var undoService = new UndoService(engine, board, matchDriver);
                matchDriver.OnTurnCompleted += undoService.RecordTurn;

                fixture._aiCoordinator = new AIMatchCoordinator(engine, board, matchDriver.PlayMove);
                var clockCoordinator = new ClockCoordinator(new GameSetup(logMoves: false), _ => { }, (_, __) => { });

                var deterministicSetup = new GameSetup(logMoves: false, new FixedRandomSource(nextBool: true), new RandomFirstMoverPolicy());

                fixture.MatchFlow = new MatchFlowCoordinator(
                    board, deterministicSetup, matchDriver, matchDriver.PlayMove, engine, undoService, fixture._aiCoordinator, clockCoordinator,
                    fixture._host, boardSizeX: 8, boardSizeY: 8, logMoves: false,
                    triggerTeamRoulette: _ => { },
                    showTeamSelection: () => { },
                    showGameModeSelection: () => { },
                    showAIMatchSettings: () => { },
                    onExecutorMoveRejected: (_, __) => { },
                    onExecutorPromotionRequired: (_, __, ___) => { },
                    raiseGameModeConfigured: _ => fixture.RaisedGameModeConfiguredCount++,
                    raiseGameStarted: () => fixture.RaisedGameStartedCount++,
                    raiseBoardResyncRequired: () => fixture.RaisedBoardResyncRequiredCount++,
                    setSharedBoardState: _ => fixture.SetSharedBoardStateCount++,
                    clearSharedBoardState: () => { },
                    raiseGameReset: () => { });

                return fixture;
            }

            public void Dispose()
            {
                _aiCoordinator.Dispose();
                Object.DestroyImmediate(MoveExecutedChannel);
                if (_host != null) Object.DestroyImmediate(_host);
            }
        }
    }
}
