using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Gameplay.Manager;
using ChessTheBetrayal.Tests.Utilities;

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
        public void RequestUndo_AiSearchNotInFlight_PopsBothPlayerAndAiTurn()
        {
            // Player: a2-a3. AI (Black): a7-a6. Undo (search finished) must restore both pawns
            // and hand the turn back to White, exactly as it stood before either move.
            ulong hashBefore = _board.ZobristHash;

            _matchDriver.PlayMove(StandardMove(_board, "a2", "a3"));
            _matchDriver.PlayMove(StandardMove(_board, "a7", "a6"));

            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.White));

            _undoService.RequestUndo(isAIMode: true, currentPhase: _matchDriver.CurrentPhase, aiSearchInFlight: false, aiMovesFirst: false);

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
            _undoService.RequestUndo(isAIMode: true, currentPhase: _matchDriver.CurrentPhase, aiSearchInFlight: false, aiMovesFirst: false);
            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.White));
            Assert.That(_board.ZobristHash, Is.EqualTo(hashAfterTurn1));
            Assert.That(_matchDriver.MoveLog.Entries.Count, Is.EqualTo(2));
            Assert.That(_undoService.CanUndo(isAIMode: true, _matchDriver.CurrentPhase, aiMovesFirst: false), Is.True,
                "Still one turn-pair on the stack — Undo must stay available.");

            // Second press: pop turn-pair 1, back to the opening position.
            _undoService.RequestUndo(isAIMode: true, currentPhase: _matchDriver.CurrentPhase, aiSearchInFlight: false, aiMovesFirst: false);
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
            _undoService.RequestUndo(isAIMode: true, currentPhase: _matchDriver.CurrentPhase, aiSearchInFlight: false, aiMovesFirst: true);

            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.Black), "Undo must land on the human's turn, not the AI's.");
            Assert.That(_board.ZobristHash, Is.EqualTo(hashAfterAiOpening), "Board is back to just after the AI's opening.");
            Assert.That(_board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("a3")).Type, Is.EqualTo(ChessPieceType.Pawn),
                "The AI's opening pawn must still be on a3 — its opening move is protected from Undo.");

            // The protected opening is all that's left — Undo must now be unavailable, and a further
            // request must be a hard no-op (never rewinding into the AI's opening / onto White).
            Assert.That(_undoService.CanUndo(isAIMode: true, _matchDriver.CurrentPhase, aiMovesFirst: true), Is.False,
                "Only the AI's protected opening remains — Undo must be unavailable (the 'stuck on first move' bug).");

            _undoService.RequestUndo(isAIMode: true, currentPhase: _matchDriver.CurrentPhase, aiSearchInFlight: false, aiMovesFirst: true);
            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.Black), "A no-op Undo must not flip the board onto the AI's turn.");
            Assert.That(_board.ZobristHash, Is.EqualTo(hashAfterAiOpening), "A no-op Undo must not change the board.");
        }

        [Test]
        public void RequestUndo_AiSearchInFlight_PopsOnlyPlayerTurn()
        {
            // AI's reply never arrived (search still running when Undo was pressed), so only the
            // player's own last turn needs unwinding — CurrentTurn must land back on White.
            _matchDriver.PlayMove(StandardMove(_board, "a2", "a3"));
            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.Black));

            _undoService.RequestUndo(isAIMode: true, currentPhase: _matchDriver.CurrentPhase, aiSearchInFlight: true, aiMovesFirst: false);

            Assert.That(_board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("a2")).Type, Is.EqualTo(ChessPieceType.Pawn));
            Assert.That(_board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("a3")).IsEmpty, Is.True);
            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.White));
            Assert.That(_matchDriver.CurrentPhase, Is.EqualTo(TurnPhase.Normal));
            Assert.That(_matchDriver.MoveLog.Entries.Count, Is.EqualTo(0));
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

            _undoService.RequestUndo(isAIMode: true, currentPhase: _matchDriver.CurrentPhase, aiSearchInFlight: true, aiMovesFirst: false);

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

            _undoService.RequestUndo(isAIMode: true, currentPhase: _matchDriver.CurrentPhase, aiSearchInFlight: true, aiMovesFirst: false);

            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.White),
                "Undo must restore White to move — the pre-fix bug left CurrentTurn on Black because " +
                "the Stage-only rule never recognized this Defection as turn-flipping.");
            Assert.That(_board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("h8")).Team, Is.EqualTo(Team.White),
                "Knight must be restored to White before it defected.");
            Assert.DoesNotThrow(() => _board.AssertZobristConsistency());
            Assert.That(_board.ZobristHash, Is.EqualTo(hashBefore));
            Assert.That(_matchDriver.MoveLog.Entries.Count, Is.EqualTo(0));
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
