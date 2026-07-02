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
    /// </summary>
    public static class ChessEngine
    {
        private static IDomainLogger _logger = NullDomainLogger.Instance;

        public static void Initialize(IDomainLogger logger)
        {
            _logger = logger ?? NullDomainLogger.Instance;
        }

        public const int MaxMovesPerPosition = 218;

        [System.ThreadStatic]
        private static List<MoveCommand> _attackCheckBuffer;
        private static List<MoveCommand> AttackCheckBuffer => _attackCheckBuffer ??= new List<MoveCommand>(64);

        [System.ThreadStatic]
        private static List<MoveCommand> _moveGenBuffer;
        private static List<MoveCommand> MoveGenBuffer => _moveGenBuffer ??= new List<MoveCommand>(64);

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

            int[] indicesSnapshot = board.GetPieceIndices(team).ToArray();

            for (int i = 0; i < indicesSnapshot.Length; i++)
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
            int[] friendlyIndices = board.GetPieceIndices(piece.Team).ToArray();

            List<MoveCommand> localBuffer = new List<MoveCommand>(32);

            for (int i = 0; i < friendlyIndices.Length; i++)
            {
                int idx = friendlyIndices[i];
                Vector2Int candidateTargetPos = new Vector2Int(idx % board.TileCountX, idx / board.TileCountX);
                PieceData candidateVictim = board.GetPiece(candidateTargetPos);

                if (candidateVictim.Type == ChessPieceType.King || candidateTargetPos == betrayerPos) continue;

                board.SetPiece(candidateVictim.WithTeam(enemyTeam), candidateTargetPos.x, candidateTargetPos.y);

                localBuffer.Clear();
                strategy.GetRawMoves(board, piece, betrayerPos, localBuffer);

                board.SetPiece(candidateVictim, candidateTargetPos.x, candidateTargetPos.y);

                MoveCommand[] rawMoves = localBuffer.ToArray();

                for (int j = 0; j < rawMoves.Length; j++)
                {
                    if (rawMoves[j].EndPosition == candidateTargetPos)
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
            int[] friendlyIndices = board.GetPieceIndices(executionerTeam).ToArray();

            List<MoveCommand> localBuffer = new List<MoveCommand>(32);

            for (int i = 0; i < friendlyIndices.Length; i++)
            {
                int idx = friendlyIndices[i];
                Vector2Int pos = new Vector2Int(idx % board.TileCountX, idx / board.TileCountX);

                if (pos == betrayerSquare) continue;

                PieceData executioner = board.GetPiece(pos);

                IPieceMovement strategy = MovementFactory.GetStrategy(executioner.Type);
                if (strategy == null) continue;

                board.SetPiece(betrayer.WithTeam(enemyTeam), betrayerSquare.x, betrayerSquare.y);

                localBuffer.Clear();
                strategy.GetRawMoves(board, executioner, pos, localBuffer);

                board.SetPiece(betrayer, betrayerSquare.x, betrayerSquare.y);

                MoveCommand[] rawMoves = localBuffer.ToArray();

                for (int j = 0; j < rawMoves.Length; j++)
                {
                    if (rawMoves[j].EndPosition == betrayerSquare)
                    {
                        MoveCommand retMove = new MoveCommand(
                            pos, betrayerSquare, executioner, betrayer,
                            rawMoves[j].SpecialMoveType, rawMoves[j].PromotedTo,
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
            output.Clear();
            GetAllLegalMoves(board, team, output);

            for (int i = 0; i < output.Count; i++)
            {
                output[i] = output[i].WithStage(BetrayalStage.DefensiveSave);
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
            int[] indicesSnapshot = board.GetPieceIndices(attackerTeam).ToArray();

            for (int i = 0; i < indicesSnapshot.Length; i++)
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
            int[] indicesSnapshot = board.GetPieceIndices(team).ToArray();

            for (int i = 0; i < indicesSnapshot.Length; i++)
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

        public static DefectionOutcome ResolveFailedRetribution(BoardState board)
        {
            Vector2Int betrayerSquare = board.PendingBetrayerSquare.Value;
            Team initiator = board.BetrayalInitiator.Value;

            board.DefectPiece(betrayerSquare);

            bool selfCheckAfterDefection = IsKingInCheck(board, initiator);
            return new DefectionOutcome(selfCheckAfterDefection, betrayerSquare);
        }

        internal static void UndoDefection(BoardState board, Vector2Int square, Team originalTeam)
        {
            PieceData current = board.GetPiece(square);
            board.TogglePieceHash(current.Team, current.Type, square.x, square.y);

            PieceData restored = current.WithTeam(originalTeam);
            board.SetPiece(restored, square.x, square.y);

            board.TogglePieceHash(restored.Team, restored.Type, square.x, square.y);
        }

        private static void ApplyZobristMove(BoardState board, MoveCommand move, int previousCastlingMask, int? previousEnPassantFile)
        {
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
            else if (move.Stage == BetrayalStage.Retribution || move.Stage == BetrayalStage.DefensiveSave)
            {
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
        /// This is how the AI can explore thousands of move sequences without ever copying the board.
        /// </summary>
        /// <remarks>
        /// Exposed via [InternalsVisibleTo] — remains encapsulated from external production assemblies.
        /// </remarks>
        internal static void UndoMoveOnBoard(BoardState board, MoveCommand move, bool recordHistory = true)
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
        /// Optimized to use O(N) piece lists instead of O(64) board scans.
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
                ChessPieceType.Pawn   => 1,
                ChessPieceType.Knight => 3,
                ChessPieceType.Bishop => 3,
                ChessPieceType.Rook   => 5,
                ChessPieceType.Queen  => 9,
                ChessPieceType.King   => 0,
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

        public DefectionOutcome(bool requiresForcedSave, Vector2Int defectedSquare)
        {
            RequiresForcedSave = requiresForcedSave;
            DefectedSquare = defectedSquare;
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