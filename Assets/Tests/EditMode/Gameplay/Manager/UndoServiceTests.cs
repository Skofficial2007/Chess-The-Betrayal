using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Gameplay.Manager;
using ChessTheBetrayal.Tooling;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Manager
{
    /// <summary>
    /// UndoService is exercised through the real MatchDriver.PlayMove -> OnTurnCompleted pipeline
    /// (not by hand-constructing turn lists) so these tests pin the actual integration, not an
    /// idealized version of it. All event channels are passed null — MatchDriver only ever raises
    /// them via null-conditional (?.), so domain behavior is fully exercised without any Unity
    /// ScriptableObject instances.
    /// </summary>
    [TestFixture]
    public class UndoServiceTests
    {
        private ChessEngineAdapter _engine;
        private BoardState _board;
        private MatchDriver _matchDriver;
        private UndoService _undoService;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
            _board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a2", Team.White, ChessPieceType.Pawn)
                .WithPiece("a7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithComputedHash();

            _matchDriver = new MatchDriver(_engine, _board, logMoves: false, domainLogger: null,
                gameOverChannel: null, turnChangedChannel: null, moveExecutedChannel: null,
                moveRejectedChannel: null, checkDetectedChannel: null, betrayalChannel: null);
            _matchDriver.TransitionToPhase(TurnPhase.Normal);

            _undoService = new UndoService(_engine, _board, _matchDriver);
            _matchDriver.OnTurnCompleted += _undoService.RecordTurn;
        }

        private static MoveCommand StandardMove(BoardState board, string from, string to)
        {
            Vector2Int start = TestBoardSetupUtility.AlgebraicToVector(from);
            Vector2Int end = TestBoardSetupUtility.AlgebraicToVector(to);
            return MoveCommand.CreateStandardMove(start, end, board.GetPiece(start), board.GetPiece(end), board);
        }

        [Test]
        public void RequestUndo_AfterAiHasReplied_PopsBothPlayerAndAiTurn()
        {
            // Player: a2-a3. AI (Black): a7-a6. Undo must restore both pawns and hand the turn back
            // to White, exactly as it stood before either move.
            ulong hashBefore = _board.ZobristHash;

            _matchDriver.PlayMove(StandardMove(_board, "a2", "a3"));
            _matchDriver.PlayMove(StandardMove(_board, "a7", "a6"));

            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.White));

            _undoService.RequestUndo(isAIMode: true, currentPhase: _matchDriver.CurrentPhase, humanTeam: Team.White, aiMovesFirst: false);

            Assert.That(_board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("a2")).Type, Is.EqualTo(ChessPieceType.Pawn));
            Assert.That(_board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("a7")).Type, Is.EqualTo(ChessPieceType.Pawn));
            Assert.That(_board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("a3")).IsEmpty, Is.True);
            Assert.That(_board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("a6")).IsEmpty, Is.True);
            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.White));
            Assert.That(_matchDriver.CurrentPhase, Is.EqualTo(TurnPhase.Normal));
            Assert.DoesNotThrow(() => _board.AssertZobristConsistency());
            Assert.That(_board.ZobristHash, Is.EqualTo(hashBefore));
            Assert.That(_matchDriver.MoveLog.Entries.Count, Is.EqualTo(0));
        }

        [Test]
        public void RequestUndo_PressedRepeatedly_WalksBackTurnByTurnAlwaysLandingOnWhite()
        {
            // The user-facing contract: after several full turns, each Undo press pops one
            // player+AI turn-pair and lands back on White (the human), with the exact board and
            // hash it had before that pair — repeatable all the way to the opening position.
            ulong hashAtStart = _board.ZobristHash;

            _matchDriver.PlayMove(StandardMove(_board, "a2", "a3")); // White turn 1
            _matchDriver.PlayMove(StandardMove(_board, "a7", "a6")); // Black turn 1
            ulong hashAfterTurn1 = _board.ZobristHash;

            _matchDriver.PlayMove(StandardMove(_board, "a3", "a4")); // White turn 2
            _matchDriver.PlayMove(StandardMove(_board, "a6", "a5")); // Black turn 2

            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.White));
            Assert.That(_matchDriver.MoveLog.Entries.Count, Is.EqualTo(4));

            // First press: pop turn-pair 2, back to the position after turn 1.
            _undoService.RequestUndo(isAIMode: true, currentPhase: _matchDriver.CurrentPhase, humanTeam: Team.White, aiMovesFirst: false);
            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.White));
            Assert.That(_board.ZobristHash, Is.EqualTo(hashAfterTurn1));
            Assert.That(_matchDriver.MoveLog.Entries.Count, Is.EqualTo(2));
            Assert.That(_undoService.CanUndo(isAIMode: true, _matchDriver.CurrentPhase, aiMovesFirst: false), Is.True,
                "Still one turn-pair on the stack — Undo must stay available.");

            // Second press: pop turn-pair 1, back to the opening position.
            _undoService.RequestUndo(isAIMode: true, currentPhase: _matchDriver.CurrentPhase, humanTeam: Team.White, aiMovesFirst: false);
            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.White));
            Assert.That(_board.ZobristHash, Is.EqualTo(hashAtStart));
            Assert.That(_matchDriver.MoveLog.Entries.Count, Is.EqualTo(0));
            Assert.DoesNotThrow(() => _board.AssertZobristConsistency());
            Assert.That(_undoService.CanUndo(isAIMode: true, _matchDriver.CurrentPhase, aiMovesFirst: false), Is.False,
                "Stack is empty at the opening position — Undo must go unavailable.");
        }

        [Test]
        public void RequestUndo_AiMovedFirst_NeverUndoesTheAiOpening_LastUndoLandsOnHuman()
        {
            // Human drew Black; the AI (White) played the forced opening. Stack fills bottom->top as
            // [White opening, Black human, White reply]. Like chess.com, the AI's opening is NOT
            // undoable: the last Undo must land on the human's (Black's) first turn, leaving the
            // White opening in place — never rewinding onto White's (the AI's) turn to move.
            _matchDriver.PlayMove(StandardMove(_board, "a2", "a3")); // White = AI opening (protected)
            ulong hashAfterAiOpening = _board.ZobristHash;

            _matchDriver.PlayMove(StandardMove(_board, "a7", "a6")); // Black = human turn 1
            _matchDriver.PlayMove(StandardMove(_board, "a3", "a4")); // White = AI reply

            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.Black), "Human (Black) is on the move after the AI reply.");
            Assert.That(_undoService.CanUndo(isAIMode: true, _matchDriver.CurrentPhase, aiMovesFirst: true), Is.True);

            // One press pops the AI reply + the human's turn under it, landing back on Black with only
            // the protected White opening left on the stack.
            _undoService.RequestUndo(isAIMode: true, currentPhase: _matchDriver.CurrentPhase, humanTeam: Team.Black, aiMovesFirst: true);

            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.Black), "Undo must land on the human's turn, not the AI's.");
            Assert.That(_board.ZobristHash, Is.EqualTo(hashAfterAiOpening), "Board is back to just after the AI's opening.");
            Assert.That(_board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("a3")).Type, Is.EqualTo(ChessPieceType.Pawn),
                "The AI's opening pawn must still be on a3 — its opening move is protected from Undo.");

            // The protected opening is all that's left — Undo must now be unavailable, and a further
            // request must be a hard no-op (never rewinding into the AI's opening / onto White).
            Assert.That(_undoService.CanUndo(isAIMode: true, _matchDriver.CurrentPhase, aiMovesFirst: true), Is.False,
                "Only the AI's protected opening remains — Undo must be unavailable (the 'stuck on first move' bug).");

            _undoService.RequestUndo(isAIMode: true, currentPhase: _matchDriver.CurrentPhase, humanTeam: Team.Black, aiMovesFirst: true);
            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.Black), "A no-op Undo must not flip the board onto the AI's turn.");
            Assert.That(_board.ZobristHash, Is.EqualTo(hashAfterAiOpening), "A no-op Undo must not change the board.");
        }

        [Test]
        public void RequestUndo_AiHasNotRepliedYet_PopsOnlyPlayerTurn()
        {
            // The AI's reply never reached the board, so the player's own turn is still on top and
            // only it needs unwinding — CurrentTurn must land back on White. Nothing tells the
            // service this; it reads it off the board, which is already sitting on the AI's turn.
            _matchDriver.PlayMove(StandardMove(_board, "a2", "a3"));
            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.Black));

            _undoService.RequestUndo(isAIMode: true, currentPhase: _matchDriver.CurrentPhase, humanTeam: Team.White, aiMovesFirst: false);

            Assert.That(_board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("a2")).Type, Is.EqualTo(ChessPieceType.Pawn));
            Assert.That(_board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("a3")).IsEmpty, Is.True);
            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.White));
            Assert.That(_matchDriver.CurrentPhase, Is.EqualTo(TurnPhase.Normal));
            Assert.That(_matchDriver.MoveLog.Entries.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// The AI's reply hasn't landed, but there is plenty of history underneath — so nothing but
        /// the turn count itself can stop the walk-back going one turn too far. Popping the pair
        /// here would rewind past a move the player never asked to take back AND leave the board on
        /// the AI's turn, which is the state an Undo is supposed to be incapable of producing.
        ///
        /// This is the shape a queued-but-unplayed AI reply produces, and it is why the pop count is
        /// read off the board rather than taken as an argument.
        /// </summary>
        [Test]
        public void RequestUndo_AiHasNotRepliedYet_WithHistoryBeneath_StillPopsOnlyOneTurn()
        {
            _matchDriver.PlayMove(StandardMove(_board, "a2", "a3")); // White (human) turn 1
            _matchDriver.PlayMove(StandardMove(_board, "a7", "a6")); // Black (AI) turn 1
            ulong hashAfterAiReply = _board.ZobristHash;

            _matchDriver.PlayMove(StandardMove(_board, "a3", "a4")); // White (human) turn 2

            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.Black), "Guard: the AI is on the move and has not replied.");
            Assert.That(_undoService.TurnCount, Is.EqualTo(3), "Guard: there is history beneath, so only the turn rule can stop the walk-back.");

            _undoService.RequestUndo(isAIMode: true, currentPhase: _matchDriver.CurrentPhase, humanTeam: Team.White, aiMovesFirst: false);

            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.White));
            Assert.That(_undoService.TurnCount, Is.EqualTo(2), "Only the player's own unanswered turn comes off.");
            Assert.That(_board.ZobristHash, Is.EqualTo(hashAfterAiReply),
                "The board must land exactly where the AI's last reply left it, not a turn earlier.");
            Assert.That(_board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("a6")).Type, Is.EqualTo(ChessPieceType.Pawn),
                "The AI's own earlier reply must survive — it was never part of this takeback.");
        }

        [Test]
        public void RequestUndo_BetrayalTurnWithRetribution_PopsAllPliesOfThatTurn()
        {
            // White's whole turn is Act(b1->a3) + Retribution(a1->a3) — two MoveCommands, one
            // MatchDriver.PlayMove call each, but ONE turn. Undo (search in flight, pop 1 turn)
            // must unmake both plies in one call and land back on White to move.
            // Clears Setup()'s a2 pawn first — it would block the Rook's a1->a3 Retribution path.
            Vector2Int a2 = TestBoardSetupUtility.AlgebraicToVector("a2");
            _board.SetPiece(PieceData.Empty, a2.x, a2.y);
            _board.WithPiece("b1", Team.White, ChessPieceType.Knight);
            _board.WithPiece("a1", Team.White, ChessPieceType.Rook);
            _board.WithPiece("a3", Team.White, ChessPieceType.Pawn); // Betrayal victim
            _board.WithBetrayalRight(true);
            _board.ComputeFullZobristHash();
            ulong hashBefore = _board.ZobristHash;

            var actMoves = new System.Collections.Generic.List<MoveCommand>();
            ChessEngine.GetBetrayalTargets(_board, TestBoardSetupUtility.AlgebraicToVector("b1"), actMoves);
            MoveCommand actMove = actMoves[0];
            _matchDriver.PlayMove(actMove);

            Assert.That(_matchDriver.CurrentPhase, Is.EqualTo(TurnPhase.RetributionPending));

            var retMoves = new System.Collections.Generic.List<MoveCommand>();
            _engine.GetRetributionMoves(_board, Team.White, _board.PendingBetrayerSquare.Value, retMoves);
            MoveCommand retMove = retMoves[0];
            _matchDriver.PlayMove(retMove);

            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.Black));
            Assert.That(_matchDriver.CurrentPhase, Is.EqualTo(TurnPhase.Normal));

            _undoService.RequestUndo(isAIMode: true, currentPhase: _matchDriver.CurrentPhase, humanTeam: Team.White, aiMovesFirst: false);

            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.White));
            Assert.That(_board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("b1")).Type, Is.EqualTo(ChessPieceType.Knight));
            Assert.That(_board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("a1")).Type, Is.EqualTo(ChessPieceType.Rook));
            Assert.That(_board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("a3")).Type, Is.EqualTo(ChessPieceType.Pawn));
            Assert.That(_board.BetrayalRightAvailable, Is.True);
            Assert.DoesNotThrow(() => _board.AssertZobristConsistency());
            Assert.That(_board.ZobristHash, Is.EqualTo(hashBefore));
            Assert.That(_matchDriver.MoveLog.Entries.Count, Is.EqualTo(0));
        }

        [Test]
        public void RequestUndo_BetrayalTurnWithForcedDefectionNoSelfCheck_PopsAllPliesAndRestoresTurn()
        {
            // Regression: a forced Defection with NO ForcedSave still passes the turn (per
            // TurnResolver.ResultFromDefectionOutcome) even though BetrayalStage.Defection's own
            // Stage-based rule (BetrayalStageRules.FlipsTurn) always says "no flip". PopOneTurn must
            // recognize this specific Defection as the turn's real turn-flipping ply (it's the LAST
            // move recorded for the turn) or CurrentTurn desyncs from the board after Undo.
            //
            // White Knight at h8 (Betrayer) Acts onto the Pawn at f7 (a knight-move away, and far from
            // White's King at e1). No White piece can reach f7 to execute Retribution, and the
            // defected Knight doesn't check e1 from f7, so no ForcedSave -> the turn passes.
            _board.WithPiece("h8", Team.White, ChessPieceType.Knight);
            _board.WithPiece("f7", Team.White, ChessPieceType.Pawn); // Victim
            _board.WithBetrayalRight(true);
            _board.ComputeFullZobristHash();
            ulong hashBefore = _board.ZobristHash;

            var actMoves = new System.Collections.Generic.List<MoveCommand>();
            ChessEngine.GetBetrayalTargets(_board, TestBoardSetupUtility.AlgebraicToVector("h8"), actMoves);
            MoveCommand actMove = actMoves[0];
            _matchDriver.PlayMove(actMove);

            Assert.That(_matchDriver.CurrentPhase, Is.EqualTo(TurnPhase.Normal),
                "No legal Retribution and no self-check must fully resolve the sequence in one PlayMove call.");
            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.Black), "Defection with no ForcedSave must pass the turn.");
            Assert.That(_board.PendingBetrayerSquare, Is.Null);

            _undoService.RequestUndo(isAIMode: true, currentPhase: _matchDriver.CurrentPhase, humanTeam: Team.White, aiMovesFirst: false);

            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.White),
                "Undo must restore White to move — the pre-fix bug left CurrentTurn on Black because " +
                "the Stage-only rule never recognized this Defection as turn-flipping.");
            Assert.That(_board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("h8")).Team, Is.EqualTo(Team.White),
                "Knight must be restored to White before it defected.");
            Assert.DoesNotThrow(() => _board.AssertZobristConsistency());
            Assert.That(_board.ZobristHash, Is.EqualTo(hashBefore));
            Assert.That(_matchDriver.MoveLog.Entries.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// CanUndo deliberately allows TurnPhase.GameOver, so taking back a checkmate is a supported
        /// press. Unmaking the moves is not enough on its own: BoardState.IsGameOver is set by
        /// MatchDriver.EndGame and no engine unmake ever clears it, and while it is true both
        /// MatchDriver.CanSelectPiece and the coordinator's RequestMove refuse everything — so the
        /// board came back to a legal position that the player could not touch.
        /// </summary>
        [Test]
        public void RequestUndo_AfterCheckmate_LeavesTheBoardPlayableAgain()
        {
            // Black king boxed in on h8 by its own pawns; White mates down the open a-file onto the
            // back rank. Betrayal is switched off, or the king escapes by capturing its own g7 pawn
            // and the position is no longer mate at all.
            ClearSquares("a2", "a7", "e8");
            _board.WithPiece("h8", Team.Black, ChessPieceType.King);
            _board.WithPiece("g7", Team.Black, ChessPieceType.Pawn);
            _board.WithPiece("h7", Team.Black, ChessPieceType.Pawn);
            _board.WithPiece("a1", Team.White, ChessPieceType.Rook);
            _board.WithBetrayalRight(false);
            _board.ComputeFullZobristHash();

            _matchDriver.PlayMove(StandardMove(_board, "a1", "a8"));

            Assert.That(_board.IsGameOver, Is.True, "Guard: the position must actually be checkmate for this test to mean anything.");
            Assert.That(_matchDriver.CurrentPhase, Is.EqualTo(TurnPhase.GameOver));

            _undoService.RequestUndo(isAIMode: true, currentPhase: _matchDriver.CurrentPhase, humanTeam: Team.White, aiMovesFirst: false);

            Assert.That(_board.IsGameOver, Is.False);
            Assert.That(_board.Winner, Is.Null);
            Assert.That(_matchDriver.CurrentPhase, Is.EqualTo(TurnPhase.Normal));
            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.White));
            Assert.That(_matchDriver.CanSelectPiece(TestBoardSetupUtility.AlgebraicToVector("a1")), Is.True,
                "The rook is back on a1 and it is White's turn — the player must be able to pick it up again.");
        }

        /// <summary>
        /// Anything replaying a takeback has to walk history backwards, because a captured piece
        /// only goes back where it came from while the plies come off newest-first. The order is
        /// reported rather than left to be worked out, so this pins it.
        /// </summary>
        [Test]
        public void RequestUndo_ReportsEveryUnmadePly_NewestFirst()
        {
            _matchDriver.PlayMove(StandardMove(_board, "a2", "a3")); // White (human)
            _matchDriver.PlayMove(StandardMove(_board, "a7", "a6")); // Black (AI)

            var unmade = new System.Collections.Generic.List<MoveCommand>();
            _undoService.RequestUndo(isAIMode: true, currentPhase: _matchDriver.CurrentPhase,
                humanTeam: Team.White, aiMovesFirst: false, unmadeMoves: unmade);

            Assert.That(unmade, Has.Count.EqualTo(2));
            Assert.That(unmade[0].StartPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("a7")),
                "The AI's reply came off first, so it must be reported first.");
            Assert.That(unmade[1].StartPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("a2")),
                "The player's own move came off underneath it.");
        }

        /// <summary>
        /// A Betrayal turn is more than one ply, and all of them belong to the single turn a press
        /// takes back — so every one has to be reported, still newest-first, or a replay would show
        /// part of a turn undoing itself.
        /// </summary>
        [Test]
        public void RequestUndo_BetrayalTurn_ReportsEveryPlyOfThatTurn()
        {
            ClearSquares("a2");
            _board.WithPiece("b1", Team.White, ChessPieceType.Knight);
            _board.WithPiece("a1", Team.White, ChessPieceType.Rook);
            _board.WithPiece("a3", Team.White, ChessPieceType.Pawn); // Betrayal victim
            _board.WithBetrayalRight(true);
            _board.ComputeFullZobristHash();

            var actMoves = new System.Collections.Generic.List<MoveCommand>();
            ChessEngine.GetBetrayalTargets(_board, TestBoardSetupUtility.AlgebraicToVector("b1"), actMoves);
            _matchDriver.PlayMove(actMoves[0]);

            var retMoves = new System.Collections.Generic.List<MoveCommand>();
            _engine.GetRetributionMoves(_board, Team.White, _board.PendingBetrayerSquare.Value, retMoves);
            _matchDriver.PlayMove(retMoves[0]);

            var unmade = new System.Collections.Generic.List<MoveCommand>();
            _undoService.RequestUndo(isAIMode: true, currentPhase: _matchDriver.CurrentPhase,
                humanTeam: Team.White, aiMovesFirst: false, unmadeMoves: unmade);

            Assert.That(unmade, Has.Count.EqualTo(2), "Act and Retribution are one turn, but two plies to replay.");
            Assert.That(unmade[0].Stage, Is.EqualTo(BetrayalStage.Retribution), "The turn's last ply comes off first.");
            Assert.That(unmade[1].Stage, Is.EqualTo(BetrayalStage.Act));
        }

        [Test]
        public void RequestUndo_NothingToUndo_ReportsNoPlies()
        {
            var unmade = new System.Collections.Generic.List<MoveCommand> { StandardMove(_board, "a2", "a3") };

            _undoService.RequestUndo(isAIMode: true, currentPhase: TurnPhase.Normal,
                humanTeam: Team.White, aiMovesFirst: false, unmadeMoves: unmade);

            Assert.That(unmade, Is.Empty, "A press with nothing to take back must not leave stale plies for a replay to act on.");
        }

        private void ClearSquares(params string[] squares)
        {
            for (int i = 0; i < squares.Length; i++)
            {
                Vector2Int square = TestBoardSetupUtility.AlgebraicToVector(squares[i]);
                _board.SetPiece(PieceData.Empty, square.x, square.y);
            }
        }

        [Test]
        public void CanUndo_MidBetrayalRetributionPending_ReturnsFalse()
        {
            // Clears Setup()'s a2 pawn first — it would block the Rook's a1->a3 Retribution path,
            // which would resolve the sequence immediately (Defection) instead of landing in
            // RetributionPending as this test needs.
            Vector2Int a2 = TestBoardSetupUtility.AlgebraicToVector("a2");
            _board.SetPiece(PieceData.Empty, a2.x, a2.y);
            _board.WithPiece("b1", Team.White, ChessPieceType.Knight);
            _board.WithPiece("a1", Team.White, ChessPieceType.Rook);
            _board.WithPiece("a3", Team.White, ChessPieceType.Pawn); // Betrayal victim
            _board.WithBetrayalRight(true);
            _board.ComputeFullZobristHash();

            var actMoves = new System.Collections.Generic.List<MoveCommand>();
            ChessEngine.GetBetrayalTargets(_board, TestBoardSetupUtility.AlgebraicToVector("b1"), actMoves);
            _matchDriver.PlayMove(actMoves[0]);

            Assert.That(_matchDriver.CurrentPhase, Is.EqualTo(TurnPhase.RetributionPending));
            Assert.That(_undoService.CanUndo(isAIMode: true, _matchDriver.CurrentPhase, aiMovesFirst: false), Is.False,
                "Undo must be disallowed while mid-Betrayal (RetributionPending).");
        }

        [Test]
        public void CanUndo_NotAIMode_ReturnsFalse()
        {
            _matchDriver.PlayMove(StandardMove(_board, "a2", "a3"));
            _matchDriver.TransitionToPhase(TurnPhase.Normal);

            Assert.That(_undoService.CanUndo(isAIMode: false, _matchDriver.CurrentPhase, aiMovesFirst: false), Is.False,
                "Undo must be unreachable outside AI practice mode (human-vs-human / future network play).");
        }

        [Test]
        public void CanUndo_NoTurnsRecordedYet_ReturnsFalse()
        {
            Assert.That(_undoService.CanUndo(isAIMode: true, TurnPhase.Normal, aiMovesFirst: false), Is.False);
        }

        [Test]
        public void Clear_RemovesAllRecordedTurns()
        {
            _matchDriver.PlayMove(StandardMove(_board, "a2", "a3"));
            _matchDriver.TransitionToPhase(TurnPhase.Normal);
            Assert.That(_undoService.CanUndo(isAIMode: true, TurnPhase.Normal, aiMovesFirst: false), Is.True);

            _undoService.Clear();

            Assert.That(_undoService.CanUndo(isAIMode: true, TurnPhase.Normal, aiMovesFirst: false), Is.False);
        }
    }
}
