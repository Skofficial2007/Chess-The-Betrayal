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

        /// <summary>True once there's at least one full player turn to undo back to.</summary>
        public bool CanUndo(bool isAIMode, TurnPhase currentPhase) =>
            isAIMode
            && (currentPhase == TurnPhase.Normal || currentPhase == TurnPhase.GameOver)
            && _turnStack.Count > 0;

        /// <summary>
        /// Pops back to the last Normal position where it was the human's turn to move.
        ///
        /// Ordering is load-bearing: if aiSearchInFlight is true, the caller MUST have already
        /// cancelled that search and discarded its result slot before calling this — searching a
        /// board that's about to be popped out from under the worker thread is a race, not merely
        /// wasted work. Callers cancel via AsyncAIAgent.CancelSearch(), never Dispose().
        /// </summary>
        public void RequestUndo(bool isAIMode, TurnPhase currentPhase, bool aiSearchInFlight)
        {
            if (!CanUndo(isAIMode, currentPhase)) return;

            // AI's search was still running when Undo was pressed: it never got to reply, so only
            // the player's own last turn needs unwinding (pop 1 turn's worth of plies).
            // AI already replied: unwind the AI's turn, then the player's turn underneath it
            // (pop 2 turns' worth of plies) so the player always lands back to their own turn.
            int turnsToPop = aiSearchInFlight ? 1 : 2;

            for (int i = 0; i < turnsToPop && _turnStack.Count > 0; i++)
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
        /// </summary>
        private void PopOneTurn()
        {
            List<MoveCommand> turnMoves = _turnStack[_turnStack.Count - 1];
            _turnStack.RemoveAt(_turnStack.Count - 1);

            for (int i = turnMoves.Count - 1; i >= 0; i--)
            {
                MoveCommand move = turnMoves[i];

                if (BetrayalStageRules.FlipsTurn(move.Stage))
                {
                    _board.NextTurn();
                }

                _engine.UndoMove(_board, move);
            }

            _matchDriver.MoveLog.RemoveLast(turnMoves.Count);
        }
    }
}
