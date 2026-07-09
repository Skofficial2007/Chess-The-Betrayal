using System.Collections.Generic;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Match;

namespace ChessTheBetrayal.Gameplay.Manager
{
    /// <summary>
    /// Practice-mode (AI-vs-human) Undo. Plain class, no MonoBehaviour — GameManager owns the
    /// instance and calls RequestUndo() from a future HUD button.
    ///
    /// One Undo always lands back at TurnPhase.Normal with board.CurrentTurn == the human's team:
    /// if the AI already replied, that means unwinding the AI's whole turn PLUS the player's whole
    /// turn; if the AI's search is still in flight, only the player's turn needs unwinding (and the
    /// in-flight search must be cancelled first — see RequestUndo's ordering).
    ///
    /// "One turn" is not a fixed ply count: a plain move is one MoveLogEntry, but a Betrayal that
    /// resolves via Retribution or Defection is two (Act, then the turn-ending ply) — see
    /// PopOneTurn's walk-back-to-the-last-turn-flip logic. Never AI-only: gated on isAIMode so it's
    /// unreachable under a (future) NetworkMoveExecutor, per this feature's multiplayer exclusion.
    ///
    /// When the AI drew the first-mover seat, its forced opening move is PROTECTED from Undo
    /// (chess.com parity): there's no human move beneath it to take back, so the last Undo lands on
    /// the human's first turn and leaves the opening in place, rather than rewinding onto the AI's
    /// turn to move. The aiMovesFirst flag threaded through CanUndo/RequestUndo is what enforces this.
    /// </summary>
    public sealed class UndoService
    {
        private const int MaxStackDepth = 256;

        private readonly IChessEngine _engine;
        private readonly BoardState _board;
        private readonly MatchDriver _matchDriver;

        // One entry per completed turn, each holding that turn's MoveCommands in application
        // order (1 entry for a plain move, 2+ for a Betrayal sub-sequence). Bounded so a very long
        // game can't grow this without limit; cleared on new game by GameManager.
        private readonly List<List<MoveCommand>> _turnStack = new List<List<MoveCommand>>(MaxStackDepth);

        public UndoService(IChessEngine engine, BoardState board, MatchDriver matchDriver)
        {
            _engine = engine;
            _board = board;
            _matchDriver = matchDriver;
        }

        /// <summary>
        /// Records one completed turn's moves so it can be popped later. Call once per turn
        /// boundary (i.e. whenever MatchDriver actually completes a turn — mirror PlayMove's own
        /// turn-ending branches), passing every MoveCommand that turn applied, in application order.
        /// </summary>
        public void RecordTurn(IReadOnlyList<MoveCommand> turnMoves)
        {
            if (turnMoves.Count == 0) return;

            if (_turnStack.Count >= MaxStackDepth)
            {
                _turnStack.RemoveAt(0); // drop the oldest turn — no unbounded growth over a long game
            }

            _turnStack.Add(new List<MoveCommand>(turnMoves));
        }

        public void Clear() => _turnStack.Clear();

        /// <summary>Number of full turns currently on the undo stack — how many times Undo can still
        /// be pressed (2 recorded turns = 1 player+AI pair). Read only for debug logging.</summary>
        public int TurnCount => _turnStack.Count;

        /// <summary>
        /// True once there's at least one full player turn to undo back to.
        ///
        /// aiMovesFirst protects the AI's forced opening move (chess.com parity): when the AI drew
        /// the first-mover seat, the bottom stack entry is that opening and has no human move beneath
        /// it to take back — so it's never undoable, and Undo needs strictly MORE than that one
        /// protected turn to be available. When the human moves first, every recorded turn pairs
        /// down cleanly to the opening position, so any non-empty stack is undoable.
        /// </summary>
        public bool CanUndo(bool isAIMode, TurnPhase currentPhase, bool aiMovesFirst) =>
            isAIMode
            && (currentPhase == TurnPhase.Normal || currentPhase == TurnPhase.GameOver)
            && _turnStack.Count > (aiMovesFirst ? 1 : 0);

        /// <summary>
        /// Pops back to the last Normal position where it was the human's turn to move.
        ///
        /// Ordering is load-bearing: if aiSearchInFlight is true, the caller MUST have already
        /// cancelled that search and discarded its result slot before calling this — searching a
        /// board that's about to be popped out from under the worker thread is a race, not merely
        /// wasted work. Callers cancel via AsyncAIAgent.CancelSearch(), never Dispose().
        /// </summary>
        public void RequestUndo(bool isAIMode, TurnPhase currentPhase, bool aiSearchInFlight, bool aiMovesFirst)
        {
            if (!CanUndo(isAIMode, currentPhase, aiMovesFirst)) return;

            // AI's search was still running when Undo was pressed: it never got to reply, so only
            // the player's own last turn needs unwinding (pop 1 turn's worth of plies).
            // AI already replied: unwind the AI's turn, then the player's turn underneath it
            // (pop 2 turns' worth of plies) so the player always lands back to their own turn.
            int turnsToPop = aiSearchInFlight ? 1 : 2;

            // Never pop the AI's protected opening move (see CanUndo): when the AI moved first, the
            // bottom entry stays put, so the loop stops at a floor of 1 instead of emptying to 0.
            // This is what makes the last Undo land on the human's first turn rather than rewinding
            // into the AI's forced opening (which would leave the board on the AI's turn to move).
            int floor = aiMovesFirst ? 1 : 0;

            for (int i = 0; i < turnsToPop && _turnStack.Count > floor; i++)
            {
                PopOneTurn();
            }

            _matchDriver.TransitionToPhase(TurnPhase.Normal);
        }

        /// <summary>
        /// Unmakes every MoveCommand in the most recent recorded turn, in reverse application
        /// order, restoring board.CurrentTurn by re-flipping it wherever the original move had
        /// flipped it forward (IChessEngine.UndoMove itself never touches CurrentTurn — see
        /// AlphaBetaSearch.ApplyMoveAndTurn/UndoMoveAndTurn for the identical pattern in search).
        ///
        /// BetrayalStageRules.FlipsTurn is a per-Stage rule and is NOT enough on its own for
        /// Defection: a Defection's Stage is always Defection whether or not it required a
        /// ForcedSave, but TurnResolver.ResultFromDefectionOutcome only actually passes the turn
        /// when there is NO ForcedSave — i.e. when the Defection is the last move recorded for
        /// this turn. When a ForcedSave followed, that DefensiveOverride move is what really
        /// passes the turn (and BetrayalStageRules.FlipsTurn already says so for that Stage). See
        /// IsTurnFlippingMove for the combined rule.
        /// </summary>
        private void PopOneTurn()
        {
            List<MoveCommand> turnMoves = _turnStack[_turnStack.Count - 1];
            _turnStack.RemoveAt(_turnStack.Count - 1);

            for (int i = turnMoves.Count - 1; i >= 0; i--)
            {
                MoveCommand move = turnMoves[i];

                if (IsTurnFlippingMove(move, isLastMoveOfTurn: i == turnMoves.Count - 1))
                {
                    _board.NextTurn();
                }

                _engine.UndoMove(_board, move);
            }

            _matchDriver.MoveLog.RemoveLast(turnMoves.Count);
        }

        /// <summary>
        /// True when <paramref name="move"/> actually passed the turn to the opponent at the time it
        /// was originally applied. Mirrors BetrayalStageRules.FlipsTurn for every stage except
        /// Defection, whose turn-flip behavior depends on whether a ForcedSave followed it (see
        /// TurnResolver.ResultFromDefectionOutcome) rather than on the Stage alone — a Defection only
        /// flips the turn when nothing else in the turn's recorded moves comes after it.
        /// </summary>
        private static bool IsTurnFlippingMove(MoveCommand move, bool isLastMoveOfTurn)
        {
            if (move.Stage == BetrayalStage.Defection)
                return isLastMoveOfTurn;

            return BetrayalStageRules.FlipsTurn(move.Stage);
        }
    }
}
