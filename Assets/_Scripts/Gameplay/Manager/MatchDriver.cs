using System.Collections.Generic;
using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Logic;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Core.Diagnostics;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Gameplay.Manager
{
    /// <summary>
    /// Drives an in-progress match: applies moves through <see cref="IChessEngine"/>, translates
    /// the resulting <see cref="TurnAdvanceResult"/> into event-channel raises and clock calls,
    /// evaluates end-of-game conditions, and owns the <see cref="TurnPhase"/> state machine.
    ///
    /// This is the "after the first move" half of what GameManager used to do inline (see
    /// <see cref="GameSetup"/> for the "before the first move" half). MatchDriver never decides
    /// Betrayal phase transitions itself — it only reads them off TurnAdvanceResult and reacts.
    /// Nothing in here rolls dice, places pieces, or constructs a clock; if a move resolves
    /// incorrectly once the game is running, the bug is here.
    ///
    /// Constructed fresh per match by GameManager, which owns the Inspector-serialized event
    /// channels and passes them in — MatchDriver itself has no Unity serialization surface.
    /// </summary>
    public sealed class MatchDriver
    {
        private readonly IChessEngine _engine;
        private readonly BoardState _board;
        private readonly bool _logMoves;
        private readonly UnityDomainLogger _domainLogger;

        private readonly ChessTheBetrayal.Events.GameOverEventChannel _gameOverChannel;
        private readonly ChessTheBetrayal.Events.TurnChangedEventChannel _turnChangedChannel;
        private readonly ChessTheBetrayal.Events.MoveExecutedEventChannel _moveExecutedChannel;
        private readonly ChessTheBetrayal.Events.MoveRejectedEventChannel _moveRejectedChannel;
        private readonly ChessTheBetrayal.Events.GameEventChannel _checkDetectedChannel;
        private readonly ChessTheBetrayal.Events.BetrayalEventChannel _betrayalChannel;

        private GameModeConfig _selectedMode = GameModeConfig.Unlimited;
        private ChessClock _clock;
        private IClockSnapshotSource _clockSnapshotSource;
        private BetrayalBountyConfig _bountyConfig;

        // Captured once when an Act move starts a Betrayal sub-sequence, and reused for every
        // ply in that sequence (Retribution/DefensiveOverride/Defection). FullMoveNumber keeps
        // incrementing under the hood even though the turn hasn't flipped (RecordMove runs for
        // every ply, Betrayal or not) — without this, the log would show the move number jumping
        // mid-sequence instead of staying on the ply where the Betrayal actually started.
        private int _betrayalSequenceMoveNumber = -1;

        // Monotonic count of plies applied this match — see MoveExecutedPayload.PlyIndex doc
        // comment. Incremented once per applied ply (every branch that raises
        // _moveExecutedChannel), independent of TurnNumber which repeats across a Betrayal
        // sub-sequence. Reset alongside the turn accumulator at match start.
        private int _plyIndex;

        // Every MoveCommand applied since the last turn boundary — 1 entry for a plain move, 2+
        // for a Betrayal sub-sequence (Act, then whatever ends it). Flushed via OnTurnCompleted at
        // each of PlayMove's turn-ending branches, then cleared for the next turn.
        private readonly List<MoveCommand> _currentTurnMoves = new List<MoveCommand>(4);

        /// <summary>
        /// Fires once a turn is fully resolved (never on the intermediate Act ply), carrying every
        /// MoveCommand that turn applied in order. UndoService is the only intended subscriber —
        /// it records this so a later Undo can unmake exactly this turn's plies in reverse.
        /// </summary>
        public event System.Action<IReadOnlyList<MoveCommand>> OnTurnCompleted;

        /// <summary>
        /// Fires when the match enters a forced Betrayal sub-phase (RetributionPending or
        /// ForcedSave) in which the side to move owes a mandatory follow-up move WITHOUT the turn
        /// having flipped — carrying the team that owes that move. No TurnChangedEvent accompanies
        /// these transitions (Act/Defection don't flip the side to move, per the turn-flip
        /// invariant), so an autonomous player like the AI would otherwise never be prompted to
        /// continue its own forced sequence. A human is prompted by the UI reacting to the same
        /// phase change; this event is the domain-level equivalent for non-UI drivers. Fires only
        /// when a forced move is actually still owed — never when Defection already fully resolved
        /// the sequence (result.DidDefect with no ForcedSave), which ends the turn normally.
        /// </summary>
        public event System.Action<Team> OnBetrayalMoveRequired;

        /// <summary>Clears the in-progress turn buffer. Call alongside MoveLog.Clear() whenever a
        /// new match starts, so a stale partial turn from a previous game can never leak in.</summary>
        public void ResetTurnAccumulator()
        {
            _currentTurnMoves.Clear();
            _plyIndex = 0;
        }

        /// <summary>The current phase of the turn (Normal, Betrayal sub-phases, GameOver, etc.).</summary>
        public TurnPhase CurrentPhase { get; private set; } = TurnPhase.GameOver;

        /// <summary>
        /// Ordered, one-line-per-ply record of every move applied this match, including Betrayal
        /// sub-phase moves (Act/Retribution/DefensiveOverride/Defection). This is the ground truth
        /// to dump when a bug report needs the exact position recreated — see MatchMoveLog.DumpToString.
        /// Cleared by GameManager on new-match setup (GameSetup owns match lifecycle, not history).
        /// </summary>
        public MatchMoveLog MoveLog { get; } = new MatchMoveLog();

        public MatchDriver(
            IChessEngine engine,
            BoardState board,
            bool logMoves,
            UnityDomainLogger domainLogger,
            ChessTheBetrayal.Events.GameOverEventChannel gameOverChannel,
            ChessTheBetrayal.Events.TurnChangedEventChannel turnChangedChannel,
            ChessTheBetrayal.Events.MoveExecutedEventChannel moveExecutedChannel,
            ChessTheBetrayal.Events.MoveRejectedEventChannel moveRejectedChannel,
            ChessTheBetrayal.Events.GameEventChannel checkDetectedChannel,
            ChessTheBetrayal.Events.BetrayalEventChannel betrayalChannel)
        {
            _engine = engine;
            _board = board;
            _logMoves = logMoves;
            _domainLogger = domainLogger;

            _gameOverChannel = gameOverChannel;
            _turnChangedChannel = turnChangedChannel;
            _moveExecutedChannel = moveExecutedChannel;
            _moveRejectedChannel = moveRejectedChannel;
            _checkDetectedChannel = checkDetectedChannel;
            _betrayalChannel = betrayalChannel;
        }

        /// <summary>
        /// Attaches the clock and game mode for this match. Call after GameSetup.InitializeClock —
        /// MatchDriver only ever reads the clock (increments, bounties, snapshots), it never
        /// constructs one. Both may be null (Unlimited/AI mode), in which case clock-dependent
        /// behavior (increments, bounties, timeout checks) becomes a no-op throughout. Depending on
        /// IClockSnapshotSource rather than the concrete GameClockController keeps MatchDriver free
        /// of any MonoBehaviour-typed field, so it can run in a headless/non-Unity host.
        /// </summary>
        public void AttachClock(ChessClock clock, IClockSnapshotSource clockSnapshotSource, GameModeConfig selectedMode)
        {
            _clock = clock;
            _clockSnapshotSource = clockSnapshotSource;
            _selectedMode = selectedMode;
        }

        /// <summary>Returns a value-type snapshot of the clock state, or null if untimed/AI mode.</summary>
        public ClockState? GetCurrentClockSnapshot() => _clockSnapshotSource?.Current;

        /// <summary>
        /// Applies a validated move to the board and tells everyone who needs to know about it.
        /// Wraps execution in exception boundaries to safely catch domain invariant violations.
        /// NOT thread-safe: raises event channels that touch Unity objects. When an IAIAgent's
        /// OnMoveDecided fires from a background search thread, marshal the move back to the
        /// main thread (e.g. via a SynchronizationContext/dispatcher queue drained in Update())
        /// before it reaches this method.
        /// </summary>
        public void PlayMove(MoveCommand move)
        {
            if (_logMoves) Debug.Log($"[MatchDriver] Executing: {move}");

            int moveNumberBeforeApply = _board.FullMoveNumber;

            TurnAdvanceResult result;
            try
            {
                result = _engine.Advance(_board, move);
            }
            catch (BetrayalRuleViolationException ex)
            {
                // A Betrayal rule was violated — this is a bug at the call site.
                // Log it, reject the move visually, and recover gracefully.
                Debug.LogException(ex);
                _moveRejectedChannel?.Raise(new ChessTheBetrayal.Events.Payloads.MoveRejectedPayload(move.StartPosition, move.EndPosition));
                return;
            }
            catch (DomainException ex)
            {
                // Any other hard domain invariant violation.
                Debug.LogException(ex);
                return;
            }
            // Don't catch (Exception) here — genuine CLR crashes must surface.

            TransitionToPhase(result.NextPhase);
            _currentTurnMoves.Add(move);

            // The Act: the Betrayer has just captured a Victim.
            if (move.Stage == BetrayalStage.Act)
            {
                // Pin the move number for the whole sub-sequence — see field doc comment.
                _betrayalSequenceMoveNumber = moveNumberBeforeApply;

                // Recorded as Normal, not evaluated for Check/Checkmate: the turn hasn't resolved
                // yet (see EvaluateGameState's PendingBetrayerSquare guard) — a real result gets
                // logged once Retribution/Defection completes the turn, below.
                MoveLog.Record(move, _betrayalSequenceMoveNumber, GameState.Normal);

                // result.DidDefect is already known here (Advance resolved the whole sub-sequence
                // as far as it could before returning) — threading it through as WillDefect lets
                // BoardVisuals skip glowing a Betrayer that's about to be spun away with no
                // Retribution choice for the player to make, instead of flashing the glow on then
                // immediately off.
                _betrayalChannel?.Raise(new ChessTheBetrayal.Events.Payloads.BetrayalPayload(move.PieceTeam, move.EndPosition, ChessTheBetrayal.Events.Payloads.BetrayalPhase.Initiated, result.DidDefect));
                _betrayalChannel?.Raise(new ChessTheBetrayal.Events.Payloads.BetrayalPayload(move.PieceTeam, move.EndPosition, ChessTheBetrayal.Events.Payloads.BetrayalPhase.RetributionPending, result.DidDefect));

                // Fire the standard move event so visuals update, but pass isCheck=false
                // because Edge Case C dictates Discovered Checks on Opponent wait until the sequence resolves.
                _plyIndex++;
                _moveExecutedChannel?.Raise(new ChessTheBetrayal.Events.Payloads.MoveExecutedPayload(move, _board.FullMoveNumber, false, _plyIndex));

                if (result.DidDefect)
                {
                    _domainLogger?.LogWarning(new DomainLogEvent(DomainEventCode.Betrayal_RetributionPieceNone, message: "No legal Retribution move exists. Triggering Defection path."));
                    HandleDefectionOutcome(move.PieceTeam, result);
                }
                else
                {
                    // Retribution is still owed by the SAME side that just Acted — no turn flip, so
                    // no TurnChangedEvent. Announce that a forced follow-up move is required so an
                    // autonomous driver (the AI) continues its own sequence. _board.CurrentTurn is
                    // still the Betrayer's team here (Act doesn't flip the side to move).
                    OnBetrayalMoveRequired?.Invoke(_board.CurrentTurn);
                }

                // Early return. If Retribution is still pending, the turn does NOT end and the
                // clock does NOT get an increment yet.
                return;
            }

            // Retribution succeeded: an ally executed the Betrayer.
            if (move.Stage == BetrayalStage.Retribution)
            {
                _betrayalChannel?.Raise(new ChessTheBetrayal.Events.Payloads.BetrayalPayload(move.PieceTeam, move.EndPosition, ChessTheBetrayal.Events.Payloads.BetrayalPhase.Resolved));

                ApplyTimeBounty(move.PieceTeam);

                // Fire the move event so BoardVisuals plays the capture animation.
                bool isCheckAfterRetribution = _engine.IsKingInCheck(_board, _board.CurrentTurn);
                _plyIndex++;
                _moveExecutedChannel?.Raise(new ChessTheBetrayal.Events.Payloads.MoveExecutedPayload(move, _board.FullMoveNumber, isCheckAfterRetribution, _plyIndex));

                _clock?.OnMoveMade(move.PieceTeam); // Standard Fischer increment now applies
                CheckForGameEnd(move); // Discovered checks against the opponent evaluate here for the first time
                FlushCompletedTurn();
                return;
            }

            // Defensive Override succeeded: the initiator made their forced king-saving move.
            if (move.Stage == BetrayalStage.DefensiveOverride)
            {
                // Fire the move event so BoardVisuals plays the save animation.
                bool isCheckAfterSave = _engine.IsKingInCheck(_board, _board.CurrentTurn);
                _plyIndex++;
                _moveExecutedChannel?.Raise(new ChessTheBetrayal.Events.Payloads.MoveExecutedPayload(move, _board.FullMoveNumber, isCheckAfterSave, _plyIndex));

                _clock?.OnMoveMade(move.PieceTeam); // The Defensive Override move IS the final action of this turn — standard increment applies
                CheckForGameEnd(move); // Discovered checks against the opponent evaluate here
                FlushCompletedTurn();
                return;
            }

            _clock?.OnMoveMade(move.PieceTeam);

            // We need to calculate if this move resulted in a check so the UI can flash the HUD.
            bool isCheck = _engine.IsKingInCheck(_board, _board.CurrentTurn);

            _plyIndex++;
            _moveExecutedChannel?.Raise(new ChessTheBetrayal.Events.Payloads.MoveExecutedPayload(
                move,
                _board.FullMoveNumber,
                isCheck,
                _plyIndex
            ));

            CheckForGameEnd(move);
            FlushCompletedTurn();
        }

        /// <summary>Raises OnTurnCompleted with this turn's accumulated moves, then clears the
        /// buffer for the next turn. Called at every PlayMove/HandleDefectionOutcome branch that
        /// actually ends a turn — never after a bare Act, which leaves the turn still open.</summary>
        private void FlushCompletedTurn()
        {
            if (_currentTurnMoves.Count == 0) return;
            OnTurnCompleted?.Invoke(_currentTurnMoves);
            _currentTurnMoves.Clear();
        }

        /// <summary>
        /// Player is sitting in RetributionPending with at least one legal Executioner available,
        /// but chooses not to use it. Routes through the exact same resolution as a forced
        /// Defection (no legal Retribution existed) — including the Defensive Override self-check
        /// — the only difference is DefectionOutcome.Reason, which is descriptive-only.
        /// </summary>
        public void RequestRetributionSkip()
        {
            if (CurrentPhase != TurnPhase.RetributionPending)
            {
                if (_logMoves) Debug.Log($"[MatchDriver] Retribution skip rejected: not in RetributionPending (phase={CurrentPhase})");
                return;
            }

            Team initiatingTeam = _board.BetrayalInitiator ?? _board.CurrentTurn;

            TurnAdvanceResult result = _engine.ResolveVoluntaryDefection(_board);
            TransitionToPhase(result.NextPhase);

            _domainLogger?.LogInfo(new DomainLogEvent(DomainEventCode.Betrayal_RetributionSkipped, message: "Player voluntarily skipped Retribution. Triggering Defection path."));
            HandleDefectionOutcome(initiatingTeam, result);
        }

        /// <summary>
        /// Shared tail for both Defection triggers (forced failure inside PlayMove's Act branch,
        /// and the voluntary RequestRetributionSkip entry point): raises DefectionOccurred, then
        /// either ForcedSaveActive (rulebook 5B self-check) or evaluates game end. Never branches
        /// on why Defection happened — only on whether it now requires a forced Save.
        /// </summary>
        private void HandleDefectionOutcome(Team initiatingTeam, TurnAdvanceResult result)
        {
            _domainLogger?.LogInfo(new DomainLogEvent(DomainEventCode.Betrayal_DefectionResolved, auxInt: result.DefectedSquare.Value.y * _board.TileCountX + result.DefectedSquare.Value.x));

            _betrayalChannel?.Raise(new ChessTheBetrayal.Events.Payloads.BetrayalPayload(initiatingTeam, result.DefectedSquare.Value, ChessTheBetrayal.Events.Payloads.BetrayalPhase.DefectionOccurred));

            // Reached via two callers: PlayMove's Act branch (which already appended the Act move
            // to _currentTurnMoves before calling this) and RequestRetributionSkip (a voluntary
            // skip, which never goes through PlayMove at all — this is the first move that
            // sequence appends). Either way the DefectionMove itself is always new to this list —
            // its Stage.Defection can never collide with an already-appended Act/None entry.
            if (result.DefectionMove.HasValue)
            {
                _currentTurnMoves.Add(result.DefectionMove.Value);
            }

            if (result.RequiresForcedSave)
            {
                _domainLogger?.LogWarning(new DomainLogEvent(DomainEventCode.Betrayal_ForcedSaveRequired));
                _betrayalChannel?.Raise(new ChessTheBetrayal.Events.Payloads.BetrayalPayload(initiatingTeam, result.DefectedSquare.Value, ChessTheBetrayal.Events.Payloads.BetrayalPhase.ForcedSaveActive));
                // No NextTurn() yet — the pending Defensive Override move and final turn advancement
                // happen next. The side that owes that forced DefensiveOverride is whoever
                // _board.CurrentTurn now points at — the exact same key GetForcedSaveMoves and the
                // AI's search use — so announce that, not initiatingTeam (they coincide today, but
                // keying on CurrentTurn keeps this correct against the engine's own source of truth).
                OnBetrayalMoveRequired?.Invoke(_board.CurrentTurn);
            }
            else
            {
                // No time bounty — Defection alone grants nothing, per design doc.
                CheckForGameEnd(result.DefectionMove); // the opponent's newly-acquired piece may itself deliver check/checkmate — evaluated here for the first time.
                FlushCompletedTurn();
            }
        }

        /// <summary>
        /// Checks whether the game has ended (checkmate or stalemate) after a turn completes.
        /// Also fires OnCheck if the next player is in check but still has moves.
        /// </summary>
        /// <param name="justPlayed">The move that just completed a turn (any stage that reaches
        /// this point is turn-ending — Act never calls this). Pass null only when called from a
        /// context with no single completing move (there is none today; kept for future callers).
        /// Recorded into MoveLog with the GameState this call determines, so the log always
        /// reflects the actual checkmate/stalemate/check outcome, not a guess made earlier.</param>
        public void CheckForGameEnd(MoveCommand? justPlayed = null)
        {
            Team currentTeam = _board.CurrentTurn;
            GameState state = _engine.EvaluateGameState(_board, currentTeam, GetCurrentClockSnapshot());

            if (justPlayed.HasValue)
            {
                // Use the pinned sequence number if a Betrayal sub-sequence produced this move
                // (Retribution/DefensiveOverride/Defection), so the log doesn't show the move
                // number jumping mid-sequence — see _betrayalSequenceMoveNumber's doc comment.
                int loggedMoveNumber = _betrayalSequenceMoveNumber >= 0 ? _betrayalSequenceMoveNumber : _board.FullMoveNumber;
                MoveLog.Record(justPlayed.Value, loggedMoveNumber, state);
                _betrayalSequenceMoveNumber = -1;
            }

            switch (state)
            {
                case GameState.Checkmate:
                    // The winner is whoever just moved, not the team currently being evaluated.
                    Team winner = currentTeam == Team.White ? Team.Black : Team.White;
                    EndGame(winner);
                    break;

                case GameState.Stalemate:
                    EndGame(null); // Draw.
                    break;

                case GameState.Check:
                    _checkDetectedChannel?.Raise();
                    _turnChangedChannel?.Raise(new ChessTheBetrayal.Events.Payloads.TurnChangedPayload(
                        _board.CurrentTurn,
                        _board.FullMoveNumber,
                        ChessTheBetrayal.Events.Payloads.TurnSource.HumanLocal
                    ));
                    break;

                case GameState.Normal:
                    _turnChangedChannel?.Raise(new ChessTheBetrayal.Events.Payloads.TurnChangedPayload(
                        _board.CurrentTurn,
                        _board.FullMoveNumber,
                        ChessTheBetrayal.Events.Payloads.TurnSource.HumanLocal
                    ));
                    break;

                case GameState.Timeout:
                    // Primary resolution path is GameManager.OnClockTimeout() called directly via IClockEventHandler.
                    break;
            }
        }

        public void EndGame(Team? winner, bool byTimeout = false)
        {
            _board.IsGameOver = true;
            _board.Winner = winner;

            TransitionToPhase(TurnPhase.GameOver);

            var reason = winner.HasValue ? ChessTheBetrayal.Events.Payloads.GameEndReason.Checkmate : ChessTheBetrayal.Events.Payloads.GameEndReason.Stalemate;
            if (byTimeout) reason = ChessTheBetrayal.Events.Payloads.GameEndReason.Timeout;

            _gameOverChannel?.Raise(new ChessTheBetrayal.Events.Payloads.GameOverPayload(winner, reason, byTimeout));

            if (_logMoves)
            {
                Debug.Log($"[MatchDriver] Game Over. Winner: {(winner.HasValue ? winner.ToString() : "Draw")}. Timeout: {byTimeout}");
                Debug.Log($"[MatchDriver] Full move log ({MoveLog.Entries.Count} plies):\n{MoveLog.DumpToString()}");
            }
        }

        public void TransitionToPhase(TurnPhase nextPhase)
        {
            if (_logMoves && CurrentPhase != nextPhase)
            {
                Debug.Log($"[MatchDriver] Phase Transition: {CurrentPhase} -> {nextPhase}");
            }

            CurrentPhase = nextPhase;

            bool shouldPause = nextPhase == TurnPhase.Starting || nextPhase == TurnPhase.GameOver;

            if (shouldPause) _clock?.Pause();
            else _clock?.Resume();
        }

        /// <summary>
        /// Returns all legal moves for a piece at the given position, filtered to the current
        /// Betrayal sub-phase. Returns an empty list if it's not that team's turn or the game isn't active.
        /// </summary>
        public void GetLegalMovesAt(Vector2Int position, List<MoveCommand> results)
        {
            results.Clear();
            if (_board.IsGameOver) return;

            if (CurrentPhase == TurnPhase.Normal)
            {
                _engine.GetLegalMoves(_board, position, results);
            }
            else if (CurrentPhase == TurnPhase.RetributionPending && _board.PendingBetrayerSquare.HasValue)
            {
                _engine.GetRetributionMoves(_board, _board.CurrentTurn, _board.PendingBetrayerSquare.Value, results);
                RemoveMovesNotFrom(results, position);
            }
            else if (CurrentPhase == TurnPhase.ForcedSave)
            {
                _engine.GetForcedSaveMoves(_board, _board.CurrentTurn, results);
                RemoveMovesNotFrom(results, position);
            }
        }

        private static void RemoveMovesNotFrom(List<MoveCommand> moves, Vector2Int position)
        {
            for (int i = moves.Count - 1; i >= 0; i--)
            {
                if (moves[i].StartPosition != position)
                    moves.RemoveAt(i);
            }
        }

        public bool CanSelectPiece(Vector2Int position)
        {
            if ((CurrentPhase != TurnPhase.Normal && CurrentPhase != TurnPhase.RetributionPending && CurrentPhase != TurnPhase.ForcedSave) || _board.IsGameOver) return false;
            PieceData piece = _board.GetPiece(position);
            return !piece.IsEmpty && piece.Team == _board.CurrentTurn;
        }

        /// <summary>
        /// Evaluates if a team has sufficient material to force a checkmate.
        /// v1 implementation: a team can force mate if they have any piece beyond the King.
        /// Iterates only the pieces on the board rather than all 64 squares.
        /// </summary>
        public static bool CanForceMate(BoardState board, Team team)
        {
            var indices = board.GetPieceIndices(team);
            for (int i = 0; i < indices.Count; i++)
            {
                int idx = indices[i];
                PieceData p = board.GetPiece(idx % board.TileCountX, idx / board.TileCountX);

                // PieceData is a readonly struct — use !p.IsEmpty.
                if (!p.IsEmpty && p.Type != ChessPieceType.King)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Resolves a clock expiry into a game outcome. GameManager forwards its
        /// IClockEventHandler.OnClockTimeout callback here — MatchDriver decides the winner (or
        /// draw, if the opponent lacks mating material) and calls EndGame.
        /// </summary>
        public void HandleClockTimeout(Team timedOutTeam)
        {
            // If the opponent cannot force checkmate by any legal sequence,
            // the result is a draw even if the losing player's time reaches zero.
            Team opponent = timedOutTeam == Team.White ? Team.Black : Team.White;
            bool opponentCanMate = CanForceMate(_board, opponent);

            if (opponentCanMate)
            {
                EndGame(opponent, byTimeout: true);
            }
            else
            {
                EndGame(null, byTimeout: true); // Insufficient material draw
            }
        }

        private long GetBetrayalBountyMs(BetrayalBountyConfig bounty)
        {
            if (_selectedMode.IsUnlimited) return 0L;

            long baseMs = _selectedMode.BaseTimeMs;
            if (baseMs <= 60_000L) return bounty.BulletMs;         // Bullet 1|0
            if (baseMs <= 120_000L) return bounty.Bullet2Ms;       // Bullet 2|1
            if (baseMs <= 180_000L) return bounty.BlitzMs;         // Blitz 3|0
            if (baseMs <= 300_000L) return bounty.Blitz5Ms;        // Blitz 5|5
            if (baseMs <= 600_000L) return bounty.RapidMs;         // Rapid 10|0
            return bounty.Rapid15Ms;                                // Rapid 15|10+
        }

        /// <summary>Configures the time-bounty schedule read by ApplyTimeBounty. Set once at match setup.</summary>
        public void SetBountyConfig(BetrayalBountyConfig bounty) => _bountyConfig = bounty;

        private void ApplyTimeBounty(Team team)
        {
            // No bounty in Unlimited mode.
            if (_selectedMode.IsUnlimited) return;

            long bonus = GetBetrayalBountyMs(_bountyConfig);
            if (bonus <= 0L || _clock == null) return;
            _clock.ApplyBetrayalBounty(team, bonus);
        }
    }
}
