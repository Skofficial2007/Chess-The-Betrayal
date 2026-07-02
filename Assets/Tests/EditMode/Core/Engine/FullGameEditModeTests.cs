using NUnit.Framework;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine
{
    /// <summary>
    /// Proves a complete match — normal moves, a full Betrayal sequence, and termination —
    /// can be driven end-to-end through IChessEngine/ITurnResolver alone, with no GameManager,
    /// no MonoBehaviour, and no Unity scene. This is the contract a headless server or an AI
    /// search relies on: everything a player-facing client can do, these tests do the same way,
    /// through the same seam.
    /// </summary>
    [TestFixture]
    public class FullGameEditModeTests
    {
        private IChessEngine _engine;
        private List<MoveCommand> _moveBuffer;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
            _moveBuffer = new List<MoveCommand>();
        }

        /// <summary>
        /// Finds the single legal move from `from` to `to` for the side to move, the same way
        /// a move executor resolves a player's (or AI's) intent into a concrete MoveCommand.
        /// </summary>
        private MoveCommand FindMove(BoardState board, string from, string to)
        {
            Vector2Int fromPos = TestBoardSetupUtility.AlgebraicToVector(from);
            Vector2Int toPos = TestBoardSetupUtility.AlgebraicToVector(to);

            _moveBuffer.Clear();
            _engine.GetLegalMoves(board, fromPos, _moveBuffer);

            foreach (MoveCommand move in _moveBuffer)
            {
                if (move.EndPosition == toPos && !move.IsPromotion)
                {
                    return move;
                }
            }

            Assert.Fail($"No legal move found from {from} to {to}. Legal destinations: " +
                string.Join(", ", TestBoardSetupUtility.GetDestinations(_moveBuffer)));
            return default;
        }

        [Test]
        public void FoolsMate_DrivenEntirelyThroughIChessEngine_ReachesCheckmateWithConsistentHash()
        {
            // Fool's Mate: the fastest checkmate in chess. Drives four half-moves purely through
            // IChessEngine.Advance/GetLegalMoves/EvaluateGameState — no GameManager, no scene.
            BoardState board = TestBoardSetupUtility.CreateStandard();

            // 1. f3
            TurnAdvanceResult r1 = _engine.Advance(board, FindMove(board, "f2", "f3"));
            Assert.That(r1.NextPhase, Is.EqualTo(TurnPhase.Normal));
            Assert.That(r1.TurnPassedToOpponent, Is.True);
            Assert.That(board.CurrentTurn, Is.EqualTo(Team.Black));
            Assert.DoesNotThrow(() => board.AssertZobristConsistency());

            // 1... e5
            _engine.Advance(board, FindMove(board, "e7", "e5"));
            Assert.That(board.CurrentTurn, Is.EqualTo(Team.White));
            Assert.DoesNotThrow(() => board.AssertZobristConsistency());

            // 2. g4
            _engine.Advance(board, FindMove(board, "g2", "g4"));
            Assert.That(board.CurrentTurn, Is.EqualTo(Team.Black));
            Assert.DoesNotThrow(() => board.AssertZobristConsistency());

            // 2... Qh4# — delivers checkmate.
            TurnAdvanceResult finalResult = _engine.Advance(board, FindMove(board, "d8", "h4"));
            Assert.That(board.CurrentTurn, Is.EqualTo(Team.White), "Turn economy must still flip to White even though White has no reply.");
            Assert.DoesNotThrow(() => board.AssertZobristConsistency());

            GameState state = _engine.EvaluateGameState(board, board.CurrentTurn);
            Assert.That(state, Is.EqualTo(GameState.Checkmate), "White must be checkmated after 2...Qh4#.");
            Assert.That(_engine.IsKingInCheck(board, Team.White), Is.True);
            Assert.That(_engine.HasAnyLegalMoves(board, Team.White), Is.False);
        }

        [Test]
        public void BetrayalSequence_ActWithNoRetributionThenForcedSave_DrivenEntirelyThroughIChessEngine()
        {
            // Reproduces the ForcedSave integration scenario, but driven through IChessEngine.Advance
            // exactly as GameManager/a server would call it — no direct ChessEngine static calls,
            // no manual AdvanceBetrayalState/NextTurn bookkeeping in the test itself, and the Act
            // move is discovered via GetLegalMoves (genuine rook geometry) rather than hand-built.
            //
            // White Rook at e5 betrays the White Pawn at e3 (a legal same-file rook move).
            // No other White piece exists, so no Retribution executioner can possibly exist — the
            // engine must resolve Defection on its own. Once the Rook defects on e3, it directly
            // checks the King on e1 along the open e-file, so a Forced Save is required.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e5", Team.White, ChessPieceType.Rook)  // Betrayer
                .WithPiece("e3", Team.White, ChessPieceType.Pawn)  // Victim
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            // Phase 1: The Act. GetLegalMoves already folds in Betrayal targets via GetBetrayalTargets.
            MoveCommand actMove = FindMove(board, "e5", "e3");
            Assert.That(actMove.Stage, Is.EqualTo(BetrayalStage.Act));

            TurnAdvanceResult afterAct = _engine.Advance(board, actMove);

            // No legal Retribution exists (no other White piece can reach e3) — the engine resolves
            // Defection inline and lands directly on ForcedSave without waiting for further input.
            Assert.That(afterAct.NextPhase, Is.EqualTo(TurnPhase.ForcedSave));
            Assert.That(afterAct.DidDefect, Is.True);
            Assert.That(afterAct.RequiresForcedSave, Is.True);
            Assert.That(afterAct.TurnPassedToOpponent, Is.False, "Turn must not pass until the Forced Save move completes it.");
            Assert.That(afterAct.DefectionMove, Is.Not.Null, "AI/server undo stacks need this to unmake the Defection.");
            Assert.That(board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e3")).Team, Is.EqualTo(Team.Black), "Rook must have defected to Black.");
            Assert.That(board.CurrentTurn, Is.EqualTo(Team.White), "Still White's turn — the Forced Save has not been played yet.");
            Assert.DoesNotThrow(() => board.AssertZobristConsistency(), "Hash must remain consistent through Act + inline Defection.");

            // Phase 2: Forced Save. Pick any legal king escape via the same IChessEngine seam.
            _moveBuffer.Clear();
            _engine.GetForcedSaveMoves(board, board.CurrentTurn, _moveBuffer);
            Assert.That(_moveBuffer.Count, Is.GreaterThan(0), "King must have a legal escape from the Defection check.");
            MoveCommand saveMove = _moveBuffer[0];
            Assert.That(saveMove.Stage, Is.EqualTo(BetrayalStage.DefensiveSave));

            TurnAdvanceResult afterSave = _engine.Advance(board, saveMove);

            Assert.That(afterSave.NextPhase, Is.EqualTo(TurnPhase.Normal));
            Assert.That(afterSave.TurnPassedToOpponent, Is.True);
            Assert.That(board.CurrentTurn, Is.EqualTo(Team.Black), "Turn economy must pass to Black once the Forced Save completes the sequence.");
            Assert.That(board.PendingBetrayerSquare, Is.Null, "Betrayal sub-state must be fully closed out.");
            Assert.DoesNotThrow(() => board.AssertZobristConsistency(), "Hash must remain consistent after the Forced Save closes the sequence.");
        }

        [Test]
        public void BetrayalSequence_ActWithLegalRetribution_DrivenEntirelyThroughIChessEngine()
        {
            // Companion to the ForcedSave scenario above: this time an executioner is available,
            // so Advance should pause at RetributionPending rather than resolving inline.
            //
            // White Knight at b1 (Betrayer) betrays the White Pawn at a3.
            // White Rook at a1 can execute the Betrayer at a3 (Retribution succeeds).
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("a3", Team.White, ChessPieceType.Pawn)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            MoveCommand actMove = FindMove(board, "b1", "a3");
            TurnAdvanceResult afterAct = _engine.Advance(board, actMove);

            Assert.That(afterAct.NextPhase, Is.EqualTo(TurnPhase.RetributionPending));
            Assert.That(afterAct.DidDefect, Is.False);
            Assert.That(afterAct.TurnPassedToOpponent, Is.False);
            Assert.That(board.CurrentTurn, Is.EqualTo(Team.White), "Still White's turn during RetributionPending.");
            Assert.DoesNotThrow(() => board.AssertZobristConsistency());

            _moveBuffer.Clear();
            _engine.GetRetributionMoves(board, board.CurrentTurn, board.PendingBetrayerSquare.Value, _moveBuffer);
            Assert.That(_moveBuffer.Count, Is.EqualTo(1), "Rook at a1 should be the sole executioner.");
            MoveCommand retributionMove = _moveBuffer[0];

            TurnAdvanceResult afterRetribution = _engine.Advance(board, retributionMove);

            Assert.That(afterRetribution.NextPhase, Is.EqualTo(TurnPhase.Normal));
            Assert.That(afterRetribution.TurnPassedToOpponent, Is.True);
            Assert.That(board.CurrentTurn, Is.EqualTo(Team.Black));
            Assert.That(board.PendingBetrayerSquare, Is.Null);
            Assert.DoesNotThrow(() => board.AssertZobristConsistency());
        }
    }
}
