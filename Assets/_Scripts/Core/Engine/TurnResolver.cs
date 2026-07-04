using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Core.Engine
{
    /// <summary>
    /// The outcome of applying one half-move via ITurnResolver.Advance. Presentation reads this to
    /// fire event channels and drive the clock; a server broadcasts it; an AI search ignores it and
    /// just recurses on the board using the MoveCommand(s) that were applied.
    /// </summary>
    public readonly struct TurnAdvanceResult
    {
        public readonly TurnPhase NextPhase;
        public readonly bool DidDefect;
        public readonly bool RequiresForcedSave;

        /// <summary>True if CurrentTurn actually flipped to the opponent as part of this Advance call.</summary>
        public readonly bool TurnPassedToOpponent;

        public readonly Vector2Int? DefectedSquare;

        /// <summary>
        /// Set only when DidDefect is true. The Defection MoveCommand that was applied — callers that
        /// maintain their own undo stack (AI search, network replay) push this alongside the initiating
        /// Act move so the whole Betrayal sequence can be unmade move-by-move.
        /// </summary>
        public readonly MoveCommand? DefectionMove;

        public TurnAdvanceResult(
            TurnPhase nextPhase,
            bool didDefect,
            bool requiresForcedSave,
            bool turnPassedToOpponent,
            Vector2Int? defectedSquare,
            MoveCommand? defectionMove)
        {
            NextPhase = nextPhase;
            DidDefect = didDefect;
            RequiresForcedSave = requiresForcedSave;
            TurnPassedToOpponent = turnPassedToOpponent;
            DefectedSquare = defectedSquare;
            DefectionMove = defectionMove;
        }
    }

    /// <summary>
    /// Applies a single move and drives the Betrayal sub-machine to its next resting phase.
    /// Pure domain logic: no events, no MonoBehaviour, no clock. GameManager (client), a future
    /// server, and the AI all drive a Betrayal turn through this same seam.
    /// </summary>
    public interface ITurnResolver
    {
        /// <summary>
        /// Applies <paramref name="move"/> to <paramref name="board"/> and, if it's the Act stage of a
        /// Betrayal, resolves the Retribution/Defection sub-sequence as far as it can go without
        /// waiting on further input. Returns what happened; does not raise events or touch clocks.
        /// </summary>
        TurnAdvanceResult Advance(BoardState board, MoveCommand move);

        /// <summary>
        /// Resolves Defection while already resting in RetributionPending, without a MoveCommand to
        /// Advance — the player had a legal Executioner available and chose not to use it. Runs the
        /// identical resolution path as the "no legal Retribution" branch of Advance, including the
        /// Defensive Override self-check; it is a second trigger into the same resolution, not a
        /// different outcome. Callers must have already confirmed board.PendingBetrayerSquare and
        /// board.BetrayalInitiator are set (i.e. CurrentPhase == TurnPhase.RetributionPending).
        /// </summary>
        TurnAdvanceResult ResolveVoluntaryDefection(BoardState board);
    }

    /// <remarks>
    /// CROSS-REFERENCE: this class decides when the turn flips (Act and Defection don't;
    /// everything else does) by calling board.NextTurn()/ToggleTurnHash() explicitly. The AI search
    /// (AlphaBetaSearch.ScoreChild in the AI assembly) bypasses this class entirely for per-ply
    /// control over the Betrayal sub-phase, and re-derives the identical flip rule from
    /// move.Stage. If you ever change WHEN Act/Defection flip the turn, update both places.
    /// </remarks>
    public sealed class TurnResolver : ITurnResolver
    {
        [System.ThreadStatic]
        private static List<MoveCommand> _retributionBuffer;
        private static List<MoveCommand> RetributionBuffer => _retributionBuffer ??= new List<MoveCommand>(32);

        public TurnAdvanceResult Advance(BoardState board, MoveCommand move)
        {
            ChessEngine.ApplyMoveToBoard(board, move);

            if (move.Stage == BetrayalStage.Act)
            {
                return AdvanceAfterAct(board, move);
            }

            board.NextTurn();
            return new TurnAdvanceResult(TurnPhase.Normal, false, false, true, null, null);
        }

        private static TurnAdvanceResult AdvanceAfterAct(BoardState board, MoveCommand actMove)
        {
            List<MoveCommand> retributionMoves = RetributionBuffer;
            ChessEngine.GetRetributionMoves(board, actMove.PieceTeam, actMove.EndPosition, retributionMoves);

            if (retributionMoves.Count > 0)
            {
                // Waiting on the opponent (or AI child search) to pick a Retribution/DefensiveOverride move.
                return new TurnAdvanceResult(TurnPhase.RetributionPending, false, false, false, null, null);
            }

            DefectionOutcome outcome = ChessEngine.ResolveDefection(board, DefectionReason.NoLegalCapture);
            return ResultFromDefectionOutcome(board, outcome);
        }

        public TurnAdvanceResult ResolveVoluntaryDefection(BoardState board)
        {
            DefectionOutcome outcome = ChessEngine.ResolveDefection(board, DefectionReason.VoluntarySkip);
            return ResultFromDefectionOutcome(board, outcome);
        }

        /// <summary>
        /// Shared tail of both Defection triggers (forced failure and voluntary skip): branch into
        /// the Defensive Override if the defected piece checks the initiator's own King (rulebook
        /// 5B), otherwise pass the turn. Identical regardless of DefectionOutcome.Reason.
        /// </summary>
        private static TurnAdvanceResult ResultFromDefectionOutcome(BoardState board, DefectionOutcome outcome)
        {
            if (outcome.RequiresForcedSave)
            {
                // Turn does not pass yet — BETRAYER-07 handles the forced Save move and final turn advancement.
                return new TurnAdvanceResult(
                    TurnPhase.ForcedSave, true, true, false, outcome.DefectedSquare, outcome.DefectionMove);
            }

            // The Defection move itself never toggles the turn-hash bit (like Act, it's not a
            // real "turn" on its own — see ApplyZobristMove's Defection branch). Whether the turn
            // actually passes is only known now, so the toggle has to happen here to stay in sync
            // with CurrentTurn flipping via NextTurn().
            board.ToggleTurnHash();
            board.NextTurn();
            return new TurnAdvanceResult(
                TurnPhase.Normal, true, false, true, outcome.DefectedSquare, outcome.DefectionMove);
        }
    }
}
