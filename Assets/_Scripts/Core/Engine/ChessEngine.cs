using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Movement;
using System.Runtime.CompilerServices;
using ChessTheBetrayal.Core.Diagnostics;
using MoveCommand = ChessTheBetrayal.Core.Engine.MoveCommand;

[assembly: InternalsVisibleTo("ChessTheBetrayal.Tests.EditMode")]

namespace ChessTheBetrayal.Core.Engine
{
    /// <summary>
    /// The rules referee. Handles move generation, check detection, and figuring out when the game is over.
    /// Nothing in here touches Unity — it's pure chess logic that can run on any thread.
    ///
    /// THREADING INVARIANT: a given BoardState instance is never touched by more than one thread
    /// concurrently. GetRetributionMoves briefly mutates the board in place — the "disguise trick"
    /// that flips the betrayer's team, probes each executioner's raw moves, then restores it (in a
    /// try/finally, so a throw mid-probe can't leave it stuck). That window is safe only because each
    /// thread owns its own BoardState: the main thread owns the live board, and a background search
    /// thread owns a private clone taken once up front via BoardState.CloneForSnapshot (see
    /// AsyncAIAgent). Never hand the same BoardState to two threads. (Act-target generation —
    /// GetBetrayalTargets — no longer mutates the board at all; it reads a per-piece attack map. Only
    /// the rarer pending-Retribution path retains the disguise trick.)
    /// </summary>
    public static class ChessEngine
    {
        private static IDomainLogger _logger = NullDomainLogger.Instance;

        public static void Initialize(IDomainLogger logger)
        {
            _logger = logger ?? NullDomainLogger.Instance;
        }

        // 218 is the highest number of legal moves known to be reachable in any chess position,
        // so a move buffer with this capacity never needs to grow.
        public const int MaxMovesPerPosition = 218;

        [System.ThreadStatic]
        private static List<MoveCommand> _moveGenBuffer;
        private static List<MoveCommand> MoveGenBuffer => _moveGenBuffer ??= new List<MoveCommand>(64);

        [System.ThreadStatic]
        private static List<MoveCommand> _betrayalLocalBuffer;
        private static List<MoveCommand> BetrayalLocalBuffer => _betrayalLocalBuffer ??= new List<MoveCommand>(32);

        // Attacked-square scratch for Betrayal Act-target generation (GetBetrayalTargets). Kept in its
        // own slot because GetBetrayalTargets calls DoesMoveLeaveKingInCheck → IsSquareUnderAttack,
        // which fills AttackCheckSquareBuffer; the two must not clobber each other mid-scan.
        [System.ThreadStatic]
        private static List<Vector2Int> _attackSquareBuffer;
        private static List<Vector2Int> AttackSquareBuffer => _attackSquareBuffer ??= new List<Vector2Int>(32);

        // Attacked-square scratch for check detection (IsSquareUnderAttack), distinct from the Act
        // path's buffer above for the reentrancy reason noted there.
        [System.ThreadStatic]
        private static List<Vector2Int> _attackCheckSquareBuffer;
        private static List<Vector2Int> AttackCheckSquareBuffer => _attackCheckSquareBuffer ??= new List<Vector2Int>(64);

        [System.ThreadStatic]
        private static int[] _indexScratchBuffer;

        [System.ThreadStatic]
        private static int[] _attackCheckIndexBuffer;

        [System.ThreadStatic]
        private static int[] _betrayalIndexBuffer;

        /// <summary>
        /// Copies a piece-index list into the given thread-static buffer (creating/growing it as needed)
        /// so callers that mutate the board mid-iteration (e.g. the betrayal disguise trick, or nested attack
        /// checks) can iterate a stable snapshot. One scratch buffer gets reused instead of building a fresh
        /// list on every call — move generation runs this constantly during search. Each call site must use
        /// its own dedicated buffer slot to avoid clobbering an outer snapshot that's still being iterated.
        /// </summary>
        private static int[] SnapshotIndices(List<int> source, ref int[] buffer, out int count)
        {
            if (buffer == null || buffer.Length < source.Count)
            {
                buffer = new int[System.Math.Max(64, source.Count)];
            }

            for (int i = 0; i < source.Count; i++)
            {
                buffer[i] = source[i];
            }

            count = source.Count;
            return buffer;
        }

        #region Legal Move Generation

        public static void GetLegalMoves(BoardState board, Vector2Int position, List<MoveCommand> output)
        {
            GetLegalMoves(board, position, output, board.CurrentTurn);
            GetBetrayalTargets(board, position, output);
        }

        private static void GetLegalMoves(BoardState board, Vector2Int position, List<MoveCommand> output, Team team)
        {
            output.Clear();
            PieceData piece = board.GetPiece(position);

            if (piece.IsEmpty || piece.Team != team)
            {
                return;
            }

            IPieceMovement strategy = MovementFactory.GetStrategy(piece.Type);
            if (strategy == null)
            {
                return;
            }

            strategy.GetRawMoves(board, piece, position, output);

            bool isKing = piece.Type == ChessPieceType.King;
            bool isCurrentlyInCheck = false;
            Team enemyTeam = piece.Team == Team.White ? Team.Black : Team.White;

            if (isKing)
            {
                isCurrentlyInCheck = IsSquareUnderAttack(board, position, enemyTeam);
            }

            int write = 0;
            for (int i = 0; i < output.Count; i++)
            {
                MoveCommand move = output[i];

                if (move.IsCastling)
                {
                    if (isCurrentlyInCheck) continue;

                    int direction = move.EndPosition.x > move.StartPosition.x ? 1 : -1;
                    Vector2Int passThroughSquare = new Vector2Int(move.StartPosition.x + direction, move.StartPosition.y);

                    if (IsSquareUnderAttack(board, passThroughSquare, enemyTeam)) continue;
                }

                if (!DoesMoveLeaveKingInCheck(board, move))
                {
                    output[write++] = move;
                }
            }

            if (write < output.Count)
            {
                output.RemoveRange(write, output.Count - write);
            }
        }

        /// <summary>
        /// Which of the three states a pending Betrayal sub-sequence can leave the board in, as far
        /// as move generation is concerned. Both GetAllLegalMoves and GetAllLegalMovesIncludingBetrayal
        /// need to route to a different generator for each — sharing this one check keeps them from
        /// drifting out of agreement the way they did before ForcedSave was added here.
        /// </summary>
        private enum BetrayalMoveGenState
        {
            None,
            RetributionPending,
            ForcedSavePending
        }

        private static BetrayalMoveGenState GetBetrayalMoveGenState(BoardState board)
        {
            if (!board.PendingBetrayerSquare.HasValue || !board.BetrayalInitiator.HasValue)
                return BetrayalMoveGenState.None;

            PieceData betrayer = board.GetPiece(board.PendingBetrayerSquare.Value);

            // Still on the initiator's team: the Betrayer hasn't defected yet, so an executioner
            // owes Retribution. Already flipped: the Defection already happened and left the
            // initiator's own king in check, so the SAME side now owes a mandatory king-save
            // instead (ForcedSave) — see ChessEngine.ResolveDefection's doc comment for why the
            // pending fields deliberately stay set across that transition.
            return betrayer.Team == board.BetrayalInitiator.Value
                ? BetrayalMoveGenState.RetributionPending
                : BetrayalMoveGenState.ForcedSavePending;
        }

        public static void GetAllLegalMoves(BoardState board, Team team, List<MoveCommand> masterBuffer)
        {
            masterBuffer.Clear();

            switch (GetBetrayalMoveGenState(board))
            {
                case BetrayalMoveGenState.RetributionPending:
                    GetRetributionMoves(board, team, board.PendingBetrayerSquare.Value, masterBuffer);
                    return;
                case BetrayalMoveGenState.ForcedSavePending:
                    GetForcedSaveMoves(board, team, masterBuffer);
                    return;
            }

            GenerateRawLegalMoves(board, team, masterBuffer);
        }

        /// <summary>
        /// The plain "every piece's legal moves, no Betrayal special-casing" loop GetAllLegalMoves
        /// falls through to once it's ruled out both pending-Betrayal states — also what
        /// GetForcedSaveMoves itself needs once IT has confirmed ForcedSave applies, which is why
        /// this is split out rather than inlined: GetForcedSaveMoves calling back into
        /// GetAllLegalMoves would just re-enter its own ForcedSavePending branch forever.
        /// </summary>
        private static void GenerateRawLegalMoves(BoardState board, Team team, List<MoveCommand> masterBuffer)
        {
            if (masterBuffer.Capacity < MaxMovesPerPosition)
            {
                masterBuffer.Capacity = MaxMovesPerPosition;
            }

            int[] indicesSnapshot = SnapshotIndices(board.GetPieceIndices(team), ref _indexScratchBuffer, out int indexCount);

            for (int i = 0; i < indexCount; i++)
            {
                int idx = indicesSnapshot[i];
                Vector2Int pos = new Vector2Int(idx % board.TileCountX, idx / board.TileCountX);

                GetLegalMoves(board, pos, MoveGenBuffer, team);
                masterBuffer.AddRange(MoveGenBuffer);
            }
        }

        /// <summary>
        /// Same shape as <see cref="GetAllLegalMoves"/>, but calls the public per-position
        /// GetLegalMoves(board, position, output) overload instead of the private one — the only
        /// difference being that overload also appends GetBetrayalTargets. See IChessEngine's doc
        /// comment for why GetAllLegalMoves itself must stay Act-free (GetForcedSaveMoves and
        /// HasAnyLegalMoves both depend on that) while the AI search needs Act moves visible.
        /// </summary>
        public static void GetAllLegalMovesIncludingBetrayal(BoardState board, Team team, List<MoveCommand> masterBuffer)
        {
            masterBuffer.Clear();

            switch (GetBetrayalMoveGenState(board))
            {
                case BetrayalMoveGenState.RetributionPending:
                    GetRetributionMoves(board, team, board.PendingBetrayerSquare.Value, masterBuffer);
                    return;
                case BetrayalMoveGenState.ForcedSavePending:
                    GetForcedSaveMoves(board, team, masterBuffer);
                    return;
            }

            if (masterBuffer.Capacity < MaxMovesPerPosition)
            {
                masterBuffer.Capacity = MaxMovesPerPosition;
            }

            int[] indicesSnapshot = SnapshotIndices(board.GetPieceIndices(team), ref _indexScratchBuffer, out int indexCount);

            for (int i = 0; i < indexCount; i++)
            {
                int idx = indicesSnapshot[i];
                Vector2Int pos = new Vector2Int(idx % board.TileCountX, idx / board.TileCountX);

                GetLegalMoves(board, pos, MoveGenBuffer);
                masterBuffer.AddRange(MoveGenBuffer);
            }
        }

        /// <summary>
        /// Same result set as filtering <see cref="GetAllLegalMovesIncludingBetrayal"/> down to
        /// captures and Acts, but cheaper: quiet pseudo-legal moves are dropped before the
        /// expensive <see cref="DoesMoveLeaveKingInCheck"/> check runs on them instead of after,
        /// since that check is what actually costs anything per move. Built for quiescence search,
        /// which only ever wants captures and Acts and was previously paying full legality-checked
        /// movegen on every node just to throw most of it away.
        /// </summary>
        public static void GetCapturesAndActsOnly(BoardState board, Team team, List<MoveCommand> masterBuffer)
        {
            masterBuffer.Clear();

            if (board.PendingBetrayerSquare.HasValue && board.BetrayalInitiator.HasValue)
            {
                PieceData betrayer = board.GetPiece(board.PendingBetrayerSquare.Value);

                if (betrayer.Team == board.BetrayalInitiator.Value)
                {
                    GetRetributionMoves(board, team, board.PendingBetrayerSquare.Value, masterBuffer);
                    return;
                }
            }

            int[] indicesSnapshot = SnapshotIndices(board.GetPieceIndices(team), ref _indexScratchBuffer, out int indexCount);

            for (int i = 0; i < indexCount; i++)
            {
                int idx = indicesSnapshot[i];
                Vector2Int pos = new Vector2Int(idx % board.TileCountX, idx / board.TileCountX);

                GetCaptureMoves(board, pos, MoveGenBuffer, team);
                masterBuffer.AddRange(MoveGenBuffer);

                GetBetrayalTargets(board, pos, masterBuffer);
            }
        }

        /// <summary>
        /// Pseudo-legal-then-filter mirror of the private <see cref="GetLegalMoves(BoardState,Vector2Int,List{MoveCommand},Team)"/>
        /// overload, except the capture/promotion filter runs BEFORE <see cref="DoesMoveLeaveKingInCheck"/>
        /// instead of after — that check is the expensive part of move generation, so skipping it for
        /// quiet moves is the entire point of this method existing.
        /// </summary>
        private static void GetCaptureMoves(BoardState board, Vector2Int position, List<MoveCommand> output, Team team)
        {
            output.Clear();
            PieceData piece = board.GetPiece(position);

            if (piece.IsEmpty || piece.Team != team)
            {
                return;
            }

            IPieceMovement strategy = MovementFactory.GetStrategy(piece.Type);
            if (strategy == null)
            {
                return;
            }

            List<MoveCommand> raw = BetrayalLocalBuffer;
            raw.Clear();
            strategy.GetRawMoves(board, piece, position, raw);

            bool isKing = piece.Type == ChessPieceType.King;
            bool isCurrentlyInCheck = false;
            Team enemyTeam = piece.Team == Team.White ? Team.Black : Team.White;

            if (isKing)
            {
                isCurrentlyInCheck = IsSquareUnderAttack(board, position, enemyTeam);
            }

            for (int i = 0; i < raw.Count; i++)
            {
                MoveCommand move = raw[i];

                // A quiet, non-promoting move can never be a quiescence target — drop it before
                // paying for the legality check at all. Castling is always quiet, so it's dropped
                // here too (quiescence has no use for it).
                if (!move.HasCapture && !move.IsPromotion) continue;

                if (!DoesMoveLeaveKingInCheck(board, move))
                {
                    output.Add(move);
                }
            }
        }

        /// <summary>
        /// Appends every legal Betrayal <see cref="BetrayalStage.Act"/> move for the piece at
        /// <paramref name="betrayerPos"/>: the betrayer "captures" one of its own pieces as if that
        /// friendly piece were an enemy.
        ///
        /// A friendly piece is a valid victim exactly when the betrayer <em>attacks</em> its square —
        /// i.e. could capture an enemy sitting there — which is a pure function of the betrayer's
        /// movement geometry and the board's occupancy, and does NOT depend on the victim's team. So
        /// this generates the betrayer's attack map ONCE (see <see cref="IPieceMovement.GetAttackedSquares"/>)
        /// and keeps the attacked squares that hold a friendly non-King piece. That replaces the old
        /// O(friendly-pieces²)-per-node "disguise trick" — which, for each of the O(pieces) friendly
        /// candidates, flipped that candidate to the enemy team, re-ran the betrayer's full raw-move
        /// generation, then restored the board — with a single O(pieces) geometry scan and no board
        /// mutation at all. That per-node cost multiplier was what stalled depth-7 search while the
        /// once-per-match Betrayal right was still available (i.e. most of every real game).
        /// </summary>
        public static void GetBetrayalTargets(BoardState board, Vector2Int betrayerPos, List<MoveCommand> output)
        {
            PieceData piece = board.GetPiece(betrayerPos);

            if (piece.Type == ChessPieceType.King || !board.BetrayalRightAvailable || board.CurrentTurn != piece.Team) return;

            IPieceMovement strategy = MovementFactory.GetStrategy(piece.Type);
            if (strategy == null) return;

            List<Vector2Int> attackedSquares = AttackSquareBuffer;
            attackedSquares.Clear();
            strategy.GetAttackedSquares(board, piece, betrayerPos, attackedSquares);

            for (int i = 0; i < attackedSquares.Count; i++)
            {
                Vector2Int candidateTargetPos = attackedSquares[i];
                PieceData candidateVictim = board.GetPiece(candidateTargetPos);

                // Only a friendly, non-King piece can be betrayed. Empty attacked squares and enemy
                // pieces on the attack map are ordinary moves/captures, not Acts, and the King is
                // immune from being targeted as a Victim.
                if (candidateVictim.IsEmpty ||
                    candidateVictim.Team != piece.Team ||
                    candidateVictim.Type == ChessPieceType.King)
                {
                    continue;
                }

                MoveCommand actMove = MoveCommand.CreateStandardMove(betrayerPos, candidateTargetPos, piece, candidateVictim, board)
                                                 .WithStage(BetrayalStage.Act);

                if (!DoesMoveLeaveKingInCheck(board, actMove))
                {
                    output.Add(actMove);
                }
            }
        }

        public static void GetRetributionMoves(BoardState board, Team executionerTeam, Vector2Int betrayerSquare, List<MoveCommand> output)
        {
            output.Clear();
            PieceData betrayer = board.GetPiece(betrayerSquare);
            if (betrayer.IsEmpty) return;

            Team enemyTeam = executionerTeam == Team.White ? Team.Black : Team.White;
            int[] friendlyIndices = SnapshotIndices(board.GetPieceIndices(executionerTeam), ref _betrayalIndexBuffer, out int friendlyCount);

            List<MoveCommand> localBuffer = BetrayalLocalBuffer;

            for (int i = 0; i < friendlyCount; i++)
            {
                int idx = friendlyIndices[i];
                Vector2Int pos = new Vector2Int(idx % board.TileCountX, idx / board.TileCountX);

                if (pos == betrayerSquare) continue;

                PieceData executioner = board.GetPiece(pos);

                IPieceMovement strategy = MovementFactory.GetStrategy(executioner.Type);
                if (strategy == null) continue;

                board.SetPiece(betrayer.WithTeam(enemyTeam), betrayerSquare.x, betrayerSquare.y);
                try
                {
                    localBuffer.Clear();
                    strategy.GetRawMoves(board, executioner, pos, localBuffer);
                }
                finally
                {
                    board.SetPiece(betrayer, betrayerSquare.x, betrayerSquare.y);
                }

                for (int j = 0; j < localBuffer.Count; j++)
                {
                    if (localBuffer[j].EndPosition == betrayerSquare)
                    {
                        MoveCommand retMove = new MoveCommand(
                            pos, betrayerSquare, executioner, betrayer,
                            localBuffer[j].SpecialMoveType, localBuffer[j].PromotedTo,
                            null, null, null,
                            board.CastlingRights, board.EnPassantFile,
                            board.BetrayalRightAvailable, board.PendingBetrayerSquare, board.BetrayalInitiator,
                            long.MaxValue, long.MaxValue,
                            BetrayalStage.Retribution
                        );

                        if (!DoesMoveLeaveKingInCheck(board, retMove))
                        {
                            output.Add(retMove);
                        }
                    }
                }
            }
        }

        public static void GetForcedSaveMoves(BoardState board, Team team, List<MoveCommand> output)
        {
            // ForcedSave only makes sense once the Betrayer has already defected (changed teams) —
            // assert the invariant explicitly so a stale PendingBetrayerSquare/BetrayalInitiator can
            // never silently produce ForcedSave moves for a Retribution that hasn't happened yet.
            if (GetBetrayalMoveGenState(board) == BetrayalMoveGenState.RetributionPending)
            {
                throw new DomainException(
                    DomainEventCode.Betrayal_ForcedSaveInvariantViolated,
                    "GetForcedSaveMoves was called while the Betrayer still belongs to the initiator's team. " +
                    "ForcedSave requires the Betrayer to have already defected.");
            }

            output.Clear();
            // Raw generation, not GetAllLegalMoves — that would recognize this same ForcedSave state
            // and route straight back here, looping forever.
            GenerateRawLegalMoves(board, team, output);

            for (int i = 0; i < output.Count; i++)
            {
                output[i] = output[i].WithStage(BetrayalStage.DefensiveOverride);
            }
        }

        #endregion

        #region Check Detection

        private static bool DoesMoveLeaveKingInCheck(BoardState board, MoveCommand move)
        {
            ApplyMoveToBoard(board, move, recordHistory: false);

            bool inCheck;
            if (board.TryFindKing(move.PieceTeam, out Vector2Int kingPos))
            {
                Team enemyTeam = move.PieceTeam == Team.White ? Team.Black : Team.White;
                inCheck = IsSquareUnderAttack(board, kingPos, enemyTeam);
            }
            else
            {
                inCheck = true;
            }

            UndoMoveOnBoard(board, move, recordHistory: false);

            return inCheck;
        }

        public static bool IsSquareUnderAttack(BoardState board, Vector2Int targetSquare, Team attackerTeam)
        {
            int[] indicesSnapshot = SnapshotIndices(board.GetPieceIndices(attackerTeam), ref _attackCheckIndexBuffer, out int indexCount);

            List<Vector2Int> attackedSquares = AttackCheckSquareBuffer;

            for (int i = 0; i < indexCount; i++)
            {
                int idx = indicesSnapshot[i];
                int ax = idx % board.TileCountX;
                int ay = idx / board.TileCountX;
                PieceData attacker = board.GetPiece(ax, ay);
                Vector2Int attackerPos = new Vector2Int(ax, ay);

                IPieceMovement strategy = MovementFactory.GetStrategy(attacker.Type);
                if (strategy == null) continue;

                // Attack squares — not raw moves. A raw-move scan only reports a pawn's diagonal
                // when an enemy already occupies it, so it would miss a pawn guarding an EMPTY square
                // (e.g. a King's would-be escape or a castling pass-through). The attack map reports
                // the diagonal unconditionally, which is the correct definition of "under attack".
                attackedSquares.Clear();
                strategy.GetAttackedSquares(board, attacker, attackerPos, attackedSquares);

                for (int j = 0; j < attackedSquares.Count; j++)
                {
                    if (attackedSquares[j] == targetSquare)
                        return true;
                }
            }

            return false;
        }

        public static bool IsKingInCheck(BoardState board, Team team)
        {
            if (!board.TryFindKing(team, out Vector2Int kingPos))
                return false;

            Team enemyTeam = team == Team.White ? Team.Black : Team.White;

            return IsSquareUnderAttack(board, kingPos, enemyTeam);
        }

        #endregion

        #region Game State Evaluation

        public static bool HasAnyLegalMoves(BoardState board, Team team)
        {
            int[] indicesSnapshot = SnapshotIndices(board.GetPieceIndices(team), ref _indexScratchBuffer, out int indexCount);

            for (int i = 0; i < indexCount; i++)
            {
                int idx = indicesSnapshot[i];
                Vector2Int pos = new Vector2Int(idx % board.TileCountX, idx / board.TileCountX);

                GetLegalMoves(board, pos, MoveGenBuffer, team);
                if (MoveGenBuffer.Count > 0)
                {
                    return true;
                }
            }
            return false;
        }

        public static GameState EvaluateGameState(BoardState board, Team team, ClockState? clock = null)
        {
            if (clock.HasValue && clock.Value.IsExpired && clock.Value.ActiveSide == team)
            {
                return GameState.Timeout;
            }

            // Mid-Betrayal-sequence (either the pre-defection Retribution wait or the post-defection
            // ForcedSave wait): the turn hasn't resolved yet, so this isn't a real checkmate/stalemate
            // check point — report Normal and let the sub-sequence play out. Ordinary HasAnyLegalMoves
            // below only ever looks at plain moves, which is meaningless while either sub-phase is open.
            if (GetBetrayalMoveGenState(board) != BetrayalMoveGenState.None)
            {
                return GameState.Normal;
            }

            bool hasLegalMoves = HasAnyLegalMoves(board, team);

            if (hasLegalMoves)
            {
                return IsKingInCheck(board, team) ? GameState.Check : GameState.Normal;
            }

            return IsKingInCheck(board, team) ? GameState.Checkmate : GameState.Stalemate;
        }

        #endregion

        #region Move Execution

        /// <summary>
        /// Builds and applies the Defection move for a Retribution that has no legal executioner.
        /// Routed through ApplyMoveToBoard, so the DefectionOutcome.DefectionMove it returns can be
        /// unmade with a plain <c>UndoMoveOnBoard(outcome.DefectionMove)</c> — the same seam every
        /// other move type uses. An Alpha-Beta search exploring a failed Betrayal unmakes it exactly
        /// like any other move; there is no separate "undo defection" API.
        /// </summary>
        public static DefectionOutcome ResolveFailedRetribution(BoardState board) =>
            ResolveDefection(board, DefectionReason.NoLegalCapture);

        /// <summary>
        /// Resolves Defection (Resolution B) regardless of why it was triggered — a forced failure
        /// (no legal Executioner) and a voluntary skip of a legal Retribution both run through this
        /// exact same code, including the self-check test for the Defensive Override (rulebook 5B).
        /// <paramref name="reason"/> is descriptive only (logging/analytics/AI move-eval) and must
        /// never change the outcome.
        /// </summary>
        public static DefectionOutcome ResolveDefection(BoardState board, DefectionReason reason)
        {
            Vector2Int betrayerSquare = board.PendingBetrayerSquare.Value;
            Team initiator = board.BetrayalInitiator.Value;
            PieceData betrayer = board.GetPiece(betrayerSquare);

            MoveCommand defectionMove = MoveCommand.CreateDefectionMove(betrayerSquare, betrayer, board);
            ApplyMoveToBoard(board, defectionMove);

            bool selfCheckAfterDefection = IsKingInCheck(board, initiator);

            // A Defection that does NOT require a ForcedSave fully closes the Betrayal sub-sequence
            // right here — no further Retribution/DefensiveOverride move is coming to close it out
            // (that only happens via AdvanceBetrayalState's Retribution/DefensiveOverride branch,
            // which never runs for a Defection) — AND it passes the turn, which a plain Defection
            // never does on its own (see ApplyZobristMove's Defection branch and the turn-flip
            // invariant). Both hash effects of "closing the sequence" — the turn-hash toggle AND the
            // pending-Betrayer sub-state toggle — MUST happen as one atomic step with clearing the
            // fields: BoardState.ComputeFullZobristHash's sub-state term is keyed on
            // PendingBetrayerSquare.HasValue, so toggling either hash bit while the fields still
            // disagree (or vice versa) desyncs incremental vs. full-recompute for as long as that gap
            // exists. Doing all three together here — before any caller can observe or undo an
            // intermediate half-closed state — is what keeps them in lockstep.
            //
            // Stamping the move lets UndoMoveOnBoard replaying it later perform the SAME two toggles
            // again via ApplyZobristMove's Defection branch (self-cancelling), while UndoMoveOnBoard's
            // own Previous* restore puts PendingBetrayerSquare/BetrayalInitiator back — so the toggles
            // and the fields all reverse together on undo, exactly mirroring what happens here.
            // CurrentTurn itself is NOT flipped here — that stays the caller's job (TurnResolver /
            // AlphaBetaSearch each own their own turn/perspective bookkeeping) — only the hash bit that
            // must travel symmetrically with this move is handled here.
            if (!selfCheckAfterDefection)
            {
                board.ToggleTurnHash();
                board.ToggleBetrayalSubStateHash(betrayerSquare, initiator);
                board.PendingBetrayerSquare = null;
                board.BetrayalInitiator = null;
                defectionMove = defectionMove.WithClosesBetrayalSequence(true);
            }

            return new DefectionOutcome(selfCheckAfterDefection, betrayerSquare, defectionMove, reason);
        }

        // TURN-FLIP INVARIANT: Act/Defection do NOT flip side-to-move; Retribution/DefensiveOverride
        // (and ordinary None moves) DO. Mirrored in AlphaBetaSearch.StageFlipsTurn
        // (ChessTheBetrayal.AI) — change BOTH or SearchTurnFlipAgreementTests fails.
        private static void ApplyZobristMove(BoardState board, MoveCommand move, int previousCastlingMask, int? previousEnPassantFile)
        {
            if (move.Stage == BetrayalStage.Defection)
            {
                // BoardState.DefectPiece already toggles the piece out under its old team and back in
                // under its new one (called by ApplyMoveToBoard just before this). A PLAIN Defection
                // never flips whose turn it is, so ordinarily no turn-hash toggle either — UNLESS this
                // is the one Defection that closed its Betrayal sub-sequence without a ForcedSave
                // (ClosesBetrayalSequence), in which case ChessEngine.ResolveDefection already toggled
                // the turn-hash AND the pending-Betrayer sub-state hash directly on the live board, in
                // the same atomic step that determined no ForcedSave applied and cleared the pending
                // fields (see its doc comment for why that has to happen together).
                //
                // This branch never fires that pair of toggles on the FORWARD apply — ResolveDefection
                // calls ApplyMoveToBoard with the still-UNSTAMPED move (the self-check outcome, and so
                // ClosesBetrayalSequence, isn't known until after that call returns). It fires only
                // when UndoMoveOnBoard later replays the STAMPED move that ResolveDefection returned —
                // reversing both toggles symmetrically, keyed off the move itself rather than live
                // board state, exactly once for exactly one prior manual toggle. Start==End for a
                // Defection, so EndPosition/PieceTeam already identify the Betrayer square/initiator;
                // no Previous* needed.
                if (move.ClosesBetrayalSequence)
                {
                    board.ToggleTurnHash();
                    board.ToggleBetrayalSubStateHash(move.EndPosition, move.PieceTeam);
                }
                return;
            }

            if (move.Stage != BetrayalStage.Act)
            {
                board.ToggleTurnHash();
            }

            board.TogglePieceHash(move.PieceTeam, move.PieceType, move.StartPosition.x, move.StartPosition.y);
            board.TogglePieceHash(move.PieceTeam, move.PieceType, move.EndPosition.x, move.EndPosition.y);

            if (move.HasCapture)
            {
                Vector2Int capPos = move.IsEnPassant && move.EnPassantCapturePosition.HasValue
                    ? move.EnPassantCapturePosition.Value
                    : move.EndPosition;

                board.TogglePieceHash(move.CapturedTeam, move.CapturedType, capPos.x, capPos.y);
            }

            if (move.IsPromotion)
            {
                board.TogglePieceHash(move.PieceTeam, ChessPieceType.Pawn, move.EndPosition.x, move.EndPosition.y);
                board.TogglePieceHash(move.PieceTeam, move.PromotedTo, move.EndPosition.x, move.EndPosition.y);
            }

            if (move.IsCastling && move.RookStartPosition.HasValue && move.RookEndPosition.HasValue)
            {
                board.TogglePieceHash(move.PieceTeam, ChessPieceType.Rook, move.RookStartPosition.Value.x, move.RookStartPosition.Value.y);
                board.TogglePieceHash(move.PieceTeam, ChessPieceType.Rook, move.RookEndPosition.Value.x, move.RookEndPosition.Value.y);
            }

            board.ToggleCastlingHash(previousCastlingMask);
            board.ToggleCastlingHash(board.CastlingRights);

            if (previousEnPassantFile.HasValue)
            {
                board.ToggleEnPassantHash(previousEnPassantFile.Value);
            }
            if (board.EnPassantFile.HasValue)
            {
                board.ToggleEnPassantHash(board.EnPassantFile.Value);
            }

            if (move.Stage == BetrayalStage.Act)
            {
                board.ToggleBetrayalHash();
                board.ToggleBetrayalSubStateHash(move.EndPosition, move.PieceTeam);
            }
            else if (move.Stage == BetrayalStage.Retribution || move.Stage == BetrayalStage.DefensiveOverride)
            {
                // Closes out the sub-sequence opened by Act — toggle out using the square/initiator
                // that were pending going into this move (Defection deliberately left them untouched).
                if (move.PreviousPendingBetrayerSquare.HasValue && move.PreviousBetrayalInitiator.HasValue)
                {
                    board.ToggleBetrayalSubStateHash(move.PreviousPendingBetrayerSquare.Value, move.PreviousBetrayalInitiator.Value);
                }
            }
        }

        private static int ComputeNewCastlingMask(BoardState board, MoveCommand move)
        {
            int mask = board.CastlingRights;

            if (move.PieceType == ChessPieceType.King)
            {
                if (move.PieceTeam == Team.White)
                {
                    mask &= ~(BoardState.CastlingWhiteKingside | BoardState.CastlingWhiteQueenside);
                }
                else
                {
                    mask &= ~(BoardState.CastlingBlackKingside | BoardState.CastlingBlackQueenside);
                }
            }

            if (move.PieceType == ChessPieceType.Rook && !move.PieceHadMoved)
            {
                if (move.PieceTeam == Team.White)
                {
                    if (move.StartPosition.x == 0 && move.StartPosition.y == 0)
                        mask &= ~BoardState.CastlingWhiteQueenside;
                    else if (move.StartPosition.x == 7 && move.StartPosition.y == 0)
                        mask &= ~BoardState.CastlingWhiteKingside;
                }
                else
                {
                    if (move.StartPosition.x == 0 && move.StartPosition.y == 7)
                        mask &= ~BoardState.CastlingBlackQueenside;
                    else if (move.StartPosition.x == 7 && move.StartPosition.y == 7)
                        mask &= ~BoardState.CastlingBlackKingside;
                }
            }

            if (move.HasCapture && move.CapturedType == ChessPieceType.Rook)
            {
                Vector2Int capPos = move.IsEnPassant && move.EnPassantCapturePosition.HasValue
                    ? move.EnPassantCapturePosition.Value
                    : move.EndPosition;

                if (capPos.x == 0 && capPos.y == 0)
                    mask &= ~BoardState.CastlingWhiteQueenside;
                else if (capPos.x == 7 && capPos.y == 0)
                    mask &= ~BoardState.CastlingWhiteKingside;
                else if (capPos.x == 0 && capPos.y == 7)
                    mask &= ~BoardState.CastlingBlackQueenside;
                else if (capPos.x == 7 && capPos.y == 7)
                    mask &= ~BoardState.CastlingBlackKingside;
            }

            return mask;
        }

        private static int? ComputeNewEnPassantFile(MoveCommand move)
        {
            if (move.PieceType == ChessPieceType.Pawn)
            {
                int distance = System.Math.Abs(move.EndPosition.y - move.StartPosition.y);
                if (distance == 2)
                {
                    return move.EndPosition.x;
                }
            }
            return null;
        }

        // TURN-FLIP INVARIANT: Act/Defection do NOT flip side-to-move; Retribution/DefensiveOverride
        // (and ordinary None moves) DO. Mirrored in AlphaBetaSearch.StageFlipsTurn
        // (ChessTheBetrayal.AI) — change BOTH or SearchTurnFlipAgreementTests fails.
        private static void AdvanceBetrayalState(BoardState board, MoveCommand move)
        {
            if (move.Stage == BetrayalStage.Act)
            {
                board.BetrayalRightAvailable = false;
                board.PendingBetrayerSquare = move.EndPosition;
                board.BetrayalInitiator = move.PieceTeam;
            }
            else if (move.Stage == BetrayalStage.Retribution || move.Stage == BetrayalStage.DefensiveOverride)
            {
                // This is the terminal move of a Betrayal sequence that went through ForcedSave —
                // GetForcedSaveMoves (and the ForcedSave UI/AI path) needed PendingBetrayerSquare/
                // BetrayalInitiator to still identify the defected piece up until now, so Defection
                // itself doesn't clear them (see TurnResolver.ResultFromDefectionOutcome for the
                // sibling case: a Defection that does NOT require ForcedSave closes the sequence —
                // and clears this same state — immediately, since no Retribution/DefensiveOverride
                // move is coming to do it here).
                board.PendingBetrayerSquare = null;
                board.BetrayalInitiator = null;
            }
        }

        public static void ApplyMoveToBoard(BoardState board, MoveCommand move, bool recordHistory = true)
        {
            int previousCastlingMask = board.CastlingRights;
            int? previousEnPassantFile = board.EnPassantFile;

            if (recordHistory)
            {
                board.RecordMove(move.StartPosition, move.EndPosition);
            }

            if (move.Stage == BetrayalStage.Defection)
            {
                board.DefectPiece(move.StartPosition);
                AdvanceBetrayalState(board, move);
                ApplyZobristMove(board, move, previousCastlingMask, previousEnPassantFile);
                return;
            }

            if (move.IsCastling && move.RookStartPosition.HasValue && move.RookEndPosition.HasValue)
            {
                board.MovePiece(move.StartPosition, move.EndPosition);
                board.MovePiece(move.RookStartPosition.Value, move.RookEndPosition.Value);
                board.CastlingRights = ComputeNewCastlingMask(board, move);
                board.EnPassantFile = ComputeNewEnPassantFile(move);
                AdvanceBetrayalState(board, move);
                ApplyZobristMove(board, move, previousCastlingMask, previousEnPassantFile);
                return;
            }

            if (move.IsEnPassant && move.EnPassantCapturePosition.HasValue)
            {
                board.MovePiece(move.StartPosition, move.EndPosition);
                board.RemovePiece(move.EnPassantCapturePosition.Value);
                board.CastlingRights = ComputeNewCastlingMask(board, move);
                board.EnPassantFile = ComputeNewEnPassantFile(move);
                AdvanceBetrayalState(board, move);
                ApplyZobristMove(board, move, previousCastlingMask, previousEnPassantFile);
                return;
            }

            board.MovePiece(move.StartPosition, move.EndPosition);

            if (move.IsPromotion)
            {
                PieceData pieceOnBoard = board.GetPiece(move.EndPosition);

                if (!pieceOnBoard.IsEmpty)
                {
                    board.SetPiece(pieceOnBoard.WithType(move.PromotedTo), move.EndPosition.x, move.EndPosition.y);
                }
                else
                {
                    _logger.LogError(new DomainLogEvent(
                        DomainEventCode.Engine_PromotionPieceNotFound,
                        auxInt: move.EndPosition.y * board.TileCountX + move.EndPosition.x));
                }
            }

            board.CastlingRights = ComputeNewCastlingMask(board, move);
            board.EnPassantFile = ComputeNewEnPassantFile(move);
            AdvanceBetrayalState(board, move);
            ApplyZobristMove(board, move, previousCastlingMask, previousEnPassantFile);
        }

        /// <summary>
        /// Rolls back a move completely, restoring the board to exactly how it was before.
        /// This is how a search can explore thousands of move sequences without ever copying the board.
        /// </summary>
        public static void UndoMoveOnBoard(BoardState board, MoveCommand move, bool recordHistory = true)
        {
            int currentCastlingMask = board.CastlingRights;
            int? currentEnPassantFile = board.EnPassantFile;

            board.CastlingRights = move.PreviousCastlingMask;
            board.EnPassantFile = move.PreviousEnPassantFile;
            board.BetrayalRightAvailable = move.PreviousBetrayalRightAvailable;
            board.PendingBetrayerSquare = move.PreviousPendingBetrayerSquare;
            board.BetrayalInitiator = move.PreviousBetrayalInitiator;

            ApplyZobristMove(board, move, currentCastlingMask, currentEnPassantFile);

            if (recordHistory)
            {
                if (board.MoveHistory.Count >= 2)
                {
                    board.MoveHistory.RemoveAt(board.MoveHistory.Count - 1);
                    board.MoveHistory.RemoveAt(board.MoveHistory.Count - 1);
                }
            }

            if (move.Stage == BetrayalStage.Defection)
            {
                // Inverse of DefectPiece: toggle the defected piece out and the restored piece back in.
                // ApplyZobristMove is a no-op for Defection, so this is the only hash update here.
                PieceData defected = board.GetPiece(move.StartPosition);
                board.TogglePieceHash(defected.Team, defected.Type, move.StartPosition.x, move.StartPosition.y);

                PieceData restored = defected.WithTeam(move.PieceTeam);
                board.SetPiece(restored, move.StartPosition.x, move.StartPosition.y);

                board.TogglePieceHash(restored.Team, restored.Type, move.StartPosition.x, move.StartPosition.y);
                return;
            }

            PieceData primaryPiece = board.GetPiece(move.EndPosition);

            if (move.IsPromotion && !primaryPiece.IsEmpty)
            {
                primaryPiece = primaryPiece.WithType(ChessPieceType.Pawn);
            }

            board.SetPiece(PieceData.Empty, move.EndPosition.x, move.EndPosition.y);
            board.SetPiece(primaryPiece.WithHasMoved(move.PieceHadMoved), move.StartPosition.x, move.StartPosition.y);

            if (move.HasCapture)
            {
                List<PieceData> graveyard = move.CapturedTeam == Team.White ? board.WhiteCaptured : board.BlackCaptured;
                if (graveyard.Count > 0)
                {
                    graveyard.RemoveAt(graveyard.Count - 1);
                }

                PieceData resurrectedPiece = move.CapturedPieceFullState;

                Vector2Int capturePos = move.IsEnPassant && move.EnPassantCapturePosition.HasValue
                    ? move.EnPassantCapturePosition.Value
                    : move.EndPosition;

                board.SetPiece(resurrectedPiece, capturePos.x, capturePos.y);
            }

            if (move.IsCastling && move.RookStartPosition.HasValue && move.RookEndPosition.HasValue)
            {
                PieceData rook = board.GetPiece(move.RookEndPosition.Value);

                board.SetPiece(PieceData.Empty, move.RookEndPosition.Value.x, move.RookEndPosition.Value.y);
                board.SetPiece(rook.WithHasMoved(false), move.RookStartPosition.Value.x, move.RookStartPosition.Value.y);
            }
        }

        /// <summary>
        /// Checks if a move is legal and, if so, applies it and advances the turn.
        /// Returns true on success, false if the move was illegal.
        /// </summary>
        public static bool TryExecuteMove(BoardState board, MoveCommand move)
        {
            if (move.PieceTeam != board.CurrentTurn)
            {
                return false;
            }

            MoveGenBuffer.Clear();
            GetLegalMoves(board, move.StartPosition, MoveGenBuffer);
            bool isLegal = false;
            for (int i = 0; i < MoveGenBuffer.Count; i++)
            {
                if (MoveGenBuffer[i].EndPosition == move.EndPosition)
                {
                    isLegal = true;
                    break;
                }
            }

            if (!isLegal)
            {
                return false;
            }
            ApplyMoveToBoard(board, move);
            board.NextTurn();

            return true;
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Returns the material difference between teams, from White's perspective.
        /// Positive means White is ahead; negative means Black is ahead.
        /// </summary>
        public static int GetMaterialAdvantage(BoardState board)
        {
            int whiteValue = 0;
            int blackValue = 0;

            List<int> whiteIndices = board.GetPieceIndices(Team.White);

            for (int i = 0; i < whiteIndices.Count; i++)
            {
                int idx = whiteIndices[i];
                PieceData piece = board.GetPiece(idx % board.TileCountX, idx / board.TileCountX);
                whiteValue += GetPieceValue(piece.Type);
            }

            List<int> blackIndices = board.GetPieceIndices(Team.Black);

            for (int i = 0; i < blackIndices.Count; i++)
            {
                int idx = blackIndices[i];
                PieceData piece = board.GetPiece(idx % board.TileCountX, idx / board.TileCountX);
                blackValue += GetPieceValue(piece.Type);
            }

            return whiteValue - blackValue;
        }

        /// <summary>
        /// Returns the standard point value for a piece type.
        /// </summary>
        private static int GetPieceValue(ChessPieceType type)
        {
            return type switch
            {
                ChessPieceType.Pawn => 1,
                ChessPieceType.Knight => 3,
                ChessPieceType.Bishop => 3,
                ChessPieceType.Rook => 5,
                ChessPieceType.Queen => 9,
                ChessPieceType.King => 0,
                _ => 0
            };
        }

        #endregion
    }

    /// <summary>
    /// Describes the board state immediately following a Defection (Resolution B).
    /// </summary>
    public readonly struct DefectionOutcome
    {
        public readonly bool RequiresForcedSave;
        public readonly Vector2Int DefectedSquare;

        /// <summary>
        /// The applied Defection MoveCommand. Push this onto an undo stack to reverse
        /// the defection via UndoMoveOnBoard, exactly like any other move.
        /// </summary>
        public readonly MoveCommand DefectionMove;

        /// <summary>Descriptive only — logging/analytics/AI move-eval. Never branches resolution.</summary>
        public readonly DefectionReason Reason;

        public DefectionOutcome(bool requiresForcedSave, Vector2Int defectedSquare, MoveCommand defectionMove, DefectionReason reason)
        {
            RequiresForcedSave = requiresForcedSave;
            DefectedSquare = defectedSquare;
            DefectionMove = defectionMove;
            Reason = reason;
        }
    }

    public enum GameState
    {
        Normal,
        Check,
        Checkmate,
        Stalemate,
        Timeout
    }
}