using NUnit.Framework;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine.Betrayal
{
    /// <summary>
    /// Regression suite for a real production bug: TurnResolver.ResultFromDefectionOutcome left
    /// board.PendingBetrayerSquare/BetrayalInitiator set after a Defection that did NOT require a
    /// ForcedSave. EvaluateGameState's Betrayal guard reads those two fields to detect "still
    /// mid-sequence" — but PieceData.Empty.Team defaults to Team.White (enum value 0), so once the
    /// pending square emptied out (the defected piece later moved or was captured), the guard's
    /// `betrayer.Team == board.BetrayalInitiator.Value` check could spuriously read true again and
    /// silently force GameState.Normal for the rest of the game, no matter how the position actually
    /// stood — including a genuine checkmate.
    ///
    /// Every test here drives the full sequence through IChessEngine/ITurnResolver exactly as
    /// GameManager/a server would (see FullGameEditModeTests for the same convention), then checks
    /// that EvaluateGameState still correctly reports Checkmate/Check/Normal afterward. This is the
    /// class of test that would have caught the bug before it shipped.
    /// </summary>
    [TestFixture]
    public class CheckmateAfterBetrayalTests
    {
        private IChessEngine _engine;
        private List<MoveCommand> _moveBuffer;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
            _moveBuffer = new List<MoveCommand>();
        }

        private MoveCommand FindMove(BoardState board, string from, string to)
        {
            Vector2Int fromPos = TestBoardSetupUtility.AlgebraicToVector(from);
            Vector2Int toPos = TestBoardSetupUtility.AlgebraicToVector(to);

            _moveBuffer.Clear();
            _engine.GetLegalMoves(board, fromPos, _moveBuffer);

            foreach (MoveCommand move in _moveBuffer)
            {
                if (move.EndPosition == toPos)
                {
                    return move;
                }
            }

            Assert.Fail($"No legal move found from {from} to {to}. Legal destinations: " +
                string.Join(", ", TestBoardSetupUtility.GetDestinations(_moveBuffer)));
            return default;
        }

        /// <summary>
        /// Ladder-mate shape reused by every test below: a lone Black King on h8 is boxed by two
        /// White Rooks (one cutting off rank 7, one delivering check along rank 8) — the exact
        /// "trapped king, no escape square" shape the original bug report described, just with
        /// rooks instead of queens. Placed once here so every scenario mates the same, proven way.
        /// </summary>
        private static BoardState WithLadderMateSetup(BoardState board)
        {
            return board
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithPiece("a7", Team.White, ChessPieceType.Rook)  // Cuts off g7/h7
                .WithPiece("a8", Team.White, ChessPieceType.Rook); // Checks along rank 8, covers g8
        }

        [Test]
        public void NoBetrayal_PlainCheckmate_IsDetected()
        {
            // Baseline: the ladder mate alone, no Betrayal mechanic touched at all. If this ever
            // fails, the regression is elsewhere in EvaluateGameState/HasAnyLegalMoves, not in the
            // Betrayal guard these other tests target.
            BoardState board = WithLadderMateSetup(TestBoardSetupUtility.CreateEmpty())
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithTurn(Team.Black);

            GameState state = _engine.EvaluateGameState(board, Team.Black);

            Assert.That(state, Is.EqualTo(GameState.Checkmate));
            Assert.That(board.PendingBetrayerSquare, Is.Null, "No Betrayal ever occurred; this must never be set.");
        }

        [Test]
        public void AfterSuccessfulRetribution_CheckmateElsewhereOnTheBoard_IsStillDetected()
        {
            // A Betrayal Act + successful Retribution completes far from the mating attack — proves
            // a clean Resolution A sequence never poisons subsequent game-state evaluation.
            BoardState board = WithLadderMateSetup(TestBoardSetupUtility.CreateEmpty())
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("c1", Team.White, ChessPieceType.Bishop) // Betrayer
                .WithPiece("d2", Team.White, ChessPieceType.Pawn)   // Victim
                .WithPiece("d1", Team.White, ChessPieceType.Queen)  // Executioner
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            MoveCommand actMove = FindMove(board, "c1", "d2");
            Assert.That(actMove.Stage, Is.EqualTo(BetrayalStage.Act));

            TurnAdvanceResult afterAct = _engine.Advance(board, actMove);
            Assert.That(afterAct.NextPhase, Is.EqualTo(TurnPhase.RetributionPending), "Queen on d1 is a legal executioner.");

            _moveBuffer.Clear();
            _engine.GetRetributionMoves(board, board.CurrentTurn, board.PendingBetrayerSquare.Value, _moveBuffer);
            Assert.That(_moveBuffer.Count, Is.EqualTo(1), "Queen should be the sole executioner.");

            TurnAdvanceResult afterRetribution = _engine.Advance(board, _moveBuffer[0]);
            Assert.That(afterRetribution.TurnPassedToOpponent, Is.True);
            Assert.That(board.PendingBetrayerSquare, Is.Null, "Retribution must close the sequence out immediately.");
            Assert.That(board.CurrentTurn, Is.EqualTo(Team.Black));

            GameState state = _engine.EvaluateGameState(board, Team.Black);
            Assert.That(state, Is.EqualTo(GameState.Checkmate),
                "The ladder mate on h8 was never touched by the Betrayal sequence and must still be detected.");
        }

        [Test]
        public void AfterDefectionWithoutForcedSave_CheckmateElsewhereOnTheBoard_IsStillDetected()
        {
            // THE BUG: a Defection that resolves cleanly (no self-check, no ForcedSave) is the one
            // path that used to leave PendingBetrayerSquare/BetrayalInitiator set forever, silently
            // forcing GameState.Normal on every future call to EvaluateGameState for the rest of the
            // game. This reproduces exactly that shape: Knight betrays a pawn no other White piece
            // can reach, so Retribution has zero candidates and the engine auto-resolves Defection.
            BoardState board = WithLadderMateSetup(TestBoardSetupUtility.CreateEmpty())
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("g1", Team.White, ChessPieceType.Knight) // Betrayer
                .WithPiece("f3", Team.White, ChessPieceType.Pawn)   // Victim; unreachable by any other White piece
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            MoveCommand actMove = FindMove(board, "g1", "f3");
            Assert.That(actMove.Stage, Is.EqualTo(BetrayalStage.Act));

            TurnAdvanceResult afterAct = _engine.Advance(board, actMove);

            Assert.That(afterAct.DidDefect, Is.True, "No executioner exists for f3; Defection must trigger inline.");
            Assert.That(afterAct.RequiresForcedSave, Is.False, "Defected Knight on f3 is nowhere near the White King on a1.");
            Assert.That(afterAct.TurnPassedToOpponent, Is.True, "A Defection with no ForcedSave completes the turn immediately.");
            Assert.That(board.CurrentTurn, Is.EqualTo(Team.Black));

            Assert.That(board.PendingBetrayerSquare, Is.Null,
                "Regression guard: a Defection that doesn't require ForcedSave must close the Betrayal " +
                "sub-sequence immediately, exactly like a successful Retribution does.");
            Assert.That(board.BetrayalInitiator, Is.Null);

            GameState state = _engine.EvaluateGameState(board, Team.Black);
            Assert.That(state, Is.EqualTo(GameState.Checkmate),
                "The ladder mate on h8 must still be detected after a completed Defection. " +
                "Before the fix, this incorrectly returned GameState.Normal forever.");
        }

        [Test]
        public void AfterVoluntarySkip_NoForcedSaveRequired_CheckmateElsewhereOnTheBoard_IsStillDetected()
        {
            // Same bug, reached via the Skip button's entry point instead of a forced "no legal
            // Retribution" — RequestRetributionSkip routes through ResolveVoluntaryDefection, which
            // shares ResultFromDefectionOutcome with the forced path and must clear the same state.
            BoardState board = WithLadderMateSetup(TestBoardSetupUtility.CreateEmpty())
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("g1", Team.White, ChessPieceType.Knight) // Betrayer
                .WithPiece("f3", Team.White, ChessPieceType.Pawn)   // Victim
                .WithPiece("h1", Team.White, ChessPieceType.Bishop) // Legal executioner (h1-f3 diagonal) the player chooses to ignore
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            MoveCommand actMove = FindMove(board, "g1", "f3");
            TurnAdvanceResult afterAct = _engine.Advance(board, actMove);
            Assert.That(afterAct.NextPhase, Is.EqualTo(TurnPhase.RetributionPending),
                "Bishop on h1 can reach f3 along the diagonal, so a legal Retribution exists.");

            TurnAdvanceResult afterSkip = _engine.ResolveVoluntaryDefection(board);

            Assert.That(afterSkip.RequiresForcedSave, Is.False);
            Assert.That(afterSkip.TurnPassedToOpponent, Is.True);
            Assert.That(board.PendingBetrayerSquare, Is.Null,
                "A voluntary Skip that doesn't require ForcedSave must close the sequence immediately, same as the forced path.");
            Assert.That(board.BetrayalInitiator, Is.Null);

            GameState state = _engine.EvaluateGameState(board, Team.Black);
            Assert.That(state, Is.EqualTo(GameState.Checkmate),
                "Checkmate detection must survive a voluntarily-skipped Retribution exactly like the forced-Defection path.");
        }

        [Test]
        public void AfterDefectionWithForcedSave_SequenceCloses_AndUnrelatedCheckmateIsStillDetected()
        {
            // Companion to the "no ForcedSave" case above: here the Defection DOES require a Forced
            // Save (the defected piece checks its former King), which already correctly clears
            // PendingBetrayerSquare/BetrayalInitiator via AdvanceBetrayalState's Retribution/
            // DefensiveOverride branch. This test exists so a future change can't silently break
            // *that* path while fixing the other one — both must end in the same clean state.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Rook)   // Executioner candidate, pinned
                .WithPiece("e8", Team.Black, ChessPieceType.Rook)   // Pins the Rook to the King
                .WithPiece("h8", Team.Black, ChessPieceType.King)   // Without this, TryFindKing fails and
                                                                     // DoesMoveLeaveKingInCheck's no-king
                                                                     // fallback (inCheck = true) would falsely
                                                                     // reject every Black move as illegal.
                .WithPiece("f4", Team.White, ChessPieceType.Knight) // Betrayer (f4-d3 is a real knight move)
                .WithPiece("d3", Team.White, ChessPieceType.Pawn)   // Victim; from d3 a Black Knight attacks e1
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            MoveCommand actMove = FindMove(board, "f4", "d3");
            TurnAdvanceResult afterAct = _engine.Advance(board, actMove);

            Assert.That(afterAct.DidDefect, Is.True, "Rook e4 is pinned and cannot execute Retribution.");
            Assert.That(afterAct.RequiresForcedSave, Is.True, "Defected Knight on d3 checks the King on e1.");
            Assert.That(afterAct.TurnPassedToOpponent, Is.False, "Turn must not pass until the Forced Save completes it.");
            Assert.That(board.PendingBetrayerSquare, Is.Not.Null, "Still mid-sequence — ForcedSave hasn't resolved yet.");

            _moveBuffer.Clear();
            _engine.GetForcedSaveMoves(board, board.CurrentTurn, _moveBuffer);
            Assert.That(_moveBuffer.Count, Is.GreaterThan(0), "King must have a legal escape.");

            TurnAdvanceResult afterSave = _engine.Advance(board, _moveBuffer[0]);

            Assert.That(afterSave.TurnPassedToOpponent, Is.True);
            Assert.That(board.PendingBetrayerSquare, Is.Null, "ForcedSave's DefensiveOverride move must close the sequence.");
            Assert.That(board.BetrayalInitiator, Is.Null);

            // The position just after the King's escape is not itself mate (that's the point of a
            // successful Save) — assert game-state evaluation is genuinely live again (Normal, not
            // stuck), then rearrange into a fresh, independent checkmate to prove EvaluateGameState
            // isn't permanently poisoned by having gone through a ForcedSave sequence.
            Assert.That(_engine.EvaluateGameState(board, board.CurrentTurn), Is.EqualTo(GameState.Normal),
                "Black must have a full, ordinary move set immediately after White's Forced Save.");

            BoardState freshMate = WithLadderMateSetup(TestBoardSetupUtility.CreateEmpty())
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithTurn(Team.Black);

            Assert.That(_engine.EvaluateGameState(freshMate, Team.Black), Is.EqualTo(GameState.Checkmate),
                "A brand-new checkmate must still be detected correctly after a Defection+ForcedSave sequence elsewhere.");
        }

        [Test]
        public void AfterDefectionWithoutForcedSave_StalemateElsewhereOnTheBoard_IsStillDetected()
        {
            // The same guard can just as easily hide a Stalemate as a Checkmate — both branches of
            // EvaluateGameState's "no legal moves" path sit behind the same short-circuit. Stalemate
            // shape mirrors ChessEngineTests_Check.EvaluateGameState_KingNotInCheckWithNoLegalMoves_
            // ReturnsStalemate, team-flipped so Black is the side boxed in after White's Defection.
            //
            // The Betrayer must be a Pawn, not a Knight: DefectPiece flips whichever piece physically
            // lands on the victim's square (the Betrayer, not the victim) to the opposing team, so a
            // defected Knight would hand Black a genuinely mobile piece and turn this into Normal
            // instead of Stalemate. A pawn boxed in (blocked push, no diagonal targets) has none.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.Black, ChessPieceType.King)  // Cornered — a2/b1/b2 all covered by the Queen, not a1 itself
                .WithPiece("b3", Team.White, ChessPieceType.Queen)
                .WithPiece("c2", Team.White, ChessPieceType.King) // Protects the Queen; doubles as White's own King
                .WithPiece("f5", Team.White, ChessPieceType.Pawn) // Betrayer (f5-g6 is a legal diagonal capture shape)
                .WithPiece("g6", Team.White, ChessPieceType.Pawn) // Victim
                .WithPiece("g5", Team.White, ChessPieceType.Pawn) // Blocks the defected Black pawn's only forward push
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            MoveCommand actMove = FindMove(board, "f5", "g6");
            TurnAdvanceResult afterAct = _engine.Advance(board, actMove);

            Assert.That(afterAct.DidDefect, Is.True, "No executioner can reach g6; Defection must trigger inline.");
            Assert.That(afterAct.RequiresForcedSave, Is.False, "Defected Pawn on g6 is nowhere near the White King on c2.");
            Assert.That(board.PendingBetrayerSquare, Is.Null, "Defection without ForcedSave must close the sequence immediately.");

            GameState state = _engine.EvaluateGameState(board, Team.Black);
            Assert.That(state, Is.EqualTo(GameState.Stalemate),
                "Black's king has no legal moves and is not in check — must report Stalemate, not a Betrayal-guard-forced Normal.");
        }
    }
}
