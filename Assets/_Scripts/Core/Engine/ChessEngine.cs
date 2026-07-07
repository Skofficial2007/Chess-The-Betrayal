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
    /// concurrently. Move generation (GetBetrayalTargets, GetRetributionMoves) briefly mutates the
    /// board in place — the "disguise trick" that flips a candidate victim's team, probes raw moves,
    /// then restores it (now in a try/finally, so a throw mid-probe can't leave it stuck). That
    /// window is safe only because each thread owns its own BoardState: the main thread owns the
    /// live board, and a background search thread owns a private clone taken once up front via
    /// BoardState.CloneForSnapshot (see AsyncAIAgent). Never hand the same BoardState to two threads.
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
        private static List<MoveCommand> _attackCheckBuffer;
        private static List<MoveCommand> AttackCheckBuffer => _attackCheckBuffer ??= new List<MoveCommand>(64);

        [System.ThreadStatic]
        private static List<MoveCommand> _moveGenBuffer;
        private static List<MoveCommand> MoveGenBuffer => _moveGenBuffer ??= new List<MoveCommand>(64);

        [System.ThreadStatic]
        private static List<MoveCommand> _betrayalLocalBuffer;
        private static List<MoveCommand> BetrayalLocalBuffer => _betrayalLocalBuffer ??= new List<MoveCommand>(32);

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

        public static void GetAllLegalMoves(BoardState board, Team team, List<MoveCommand> masterBuffer)
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

        public static void GetBetrayalTargets(BoardState board, Vector2Int betrayerPos, List<MoveCommand> output)
        {
            PieceData piece = board.GetPiece(betrayerPos);

            if (piece.Type == ChessPieceType.King || !board.BetrayalRightAvailable || board.CurrentTurn != piece.Team) return;

            IPieceMovement strategy = MovementFactory.GetStrategy(piece.Type);
            if (strategy == null) return;

            Team enemyTeam = piece.Team == Team.White ? Team.Black : Team.White;
            int[] friendlyIndices = SnapshotIndices(board.GetPieceIndices(piece.Team), ref _betrayalIndexBuffer, out int friendlyCount);

            List<MoveCommand> localBuffer = BetrayalLocalBuffer;

            for (int i = 0; i < friendlyCount; i++)
            {
                int idx = friendlyIndices[i];
                Vector2Int candidateTargetPos = new Vector2Int(idx % board.TileCountX, idx / board.TileCountX);
                PieceData candidateVictim = board.GetPiece(candidateTargetPos);

                if (candidateVictim.Type == ChessPieceType.King || candidateTargetPos == betrayerPos) continue;

                // Disguise-restore MUST be exception-safe: GetRawMoves runs arbitrary piece-strategy
                // code, and an early return/throw mid-disguise would leave the board permanently
                // wrong for every caller after this one (including a concurrent search thread's own
                // clone, if move-gen is ever invoked reentrantly on it).
                board.SetPiece(candidateVictim.WithTeam(enemyTeam), candidateTargetPos.x, candidateTargetPos.y);
                try
                {
                    localBuffer.Clear();
                    strategy.GetRawMoves(board, piece, betrayerPos, localBuffer);
                }
                finally
                {
                    board.SetPiece(candidateVictim, candidateTargetPos.x, candidateTargetPos.y);
                }

                for (int j = 0; j < localBuffer.Count; j++)
                {
                    if (localBuffer[j].EndPosition == candidateTargetPos)
                    {
                        MoveCommand actMove = MoveCommand.CreateStandardMove(betrayerPos, candidateTargetPos, piece, candidateVictim, board)
                                                         .WithStage(BetrayalStage.Act);

                        if (!DoesMoveLeaveKingInCheck(board, actMove))
                        {
                            output.Add(actMove);
                        }

                        break;
                    }
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
            // ForcedSave only makes sense once the Betrayer has already defected (changed teams).
            // GetAllLegalMoves() falls through to plain legal-move generation for exactly this
            // reason, but that fallthrough is an implicit side effect of the team-flip check —
            // assert the invariant explicitly here so a stale PendingBetrayerSquare/BetrayalInitiator
            // can never silently reroute this call into GetRetributionMoves instead.
            if (board.PendingBetrayerSquare.HasValue && board.BetrayalInitiator.HasValue)
            {
                PieceData betrayer = board.GetPiece(board.PendingBetrayerSquare.Value);
                if (betrayer.Team == board.BetrayalInitiator.Value)
                {
                    throw new DomainException(
                        DomainEventCode.Betrayal_ForcedSaveInvariantViolated,
                        "GetForcedSaveMoves was called while the Betrayer still belongs to the initiator's team. " +
                        "ForcedSave requires the Betrayer to have already defected.");
                }
            }

            output.Clear();
            GetAllLegalMoves(board, team, output);

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

            for (int i = 0; i < indexCount; i++)
            {
                int idx = indicesSnapshot[i];
                int ax = idx % board.TileCountX;
                int ay = idx / board.TileCountX;
                PieceData attacker = board.GetPiece(ax, ay);
                Vector2Int attackerPos = new Vector2Int(ax, ay);

                IPieceMovement strategy = MovementFactory.GetStrategy(attacker.Type);
                if (strategy == null) continue;

                AttackCheckBuffer.Clear();
                strategy.GetRawMoves(board, attacker, attackerPos, AttackCheckBuffer);

                for (int j = 0; j < AttackCheckBuffer.Count; j++)
                {
                    if (AttackCheckBuffer[j].EndPosition == targetSquare)
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

            if (board.PendingBetrayerSquare.HasValue && board.BetrayalInitiator.HasValue)
            {
                PieceData betrayer = board.GetPiece(board.PendingBetrayerSquare.Value);
                if (betrayer.Team == board.BetrayalInitiator.Value)
                {
                    return GameState.Normal;
                }
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
            return new DefectionOutcome(selfCheckAfterDefection, betrayerSquare, defectionMove, reason);
        }

        private static void ApplyZobristMove(BoardState board, MoveCommand move, int previousCastlingMask, int? previousEnPassantFile)
        {
            if (move.Stage == BetrayalStage.Defection)
            {
                // BoardState.DefectPiece already toggles the piece out under its old team and back in
                // under its new one (called by ApplyMoveToBoard just before this). Nothing left to do
                // here — Defection never flips whose turn it is, so no turn-hash toggle either.
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