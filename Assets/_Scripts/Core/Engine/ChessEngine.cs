using System.Collections.Generic;
using ChessTheMasterPiece.Data;
using ChessTheMasterPiece.Logic.Movement;

namespace ChessTheMasterPiece.Logic
{
    /// <summary>
    /// The rules referee. Handles move generation, check detection, and figuring out when the game is over.
    /// Nothing in here touches Unity — it's pure chess logic that can run on any thread.
    /// </summary>
    public static class ChessEngine
    {
        // Each thread gets its own private buffers so parallel AI searches don't step on each other.
        [System.ThreadStatic]
        private static List<MoveCommand> _attackCheckBuffer;
        private static List<MoveCommand> AttackCheckBuffer => _attackCheckBuffer ??= new List<MoveCommand>(64);

        [System.ThreadStatic]
        private static List<MoveCommand> _moveGenBuffer;
        private static List<MoveCommand> MoveGenBuffer => _moveGenBuffer ??= new List<MoveCommand>(64);

        [System.ThreadStatic]
        private static List<PieceData> _pieceListBuffer;
        private static List<PieceData> PieceListBuffer => _pieceListBuffer ??= new List<PieceData>(16);

        #region Legal Move Generation

        /// <summary>
        /// Fills <paramref name="output"/> with every legal move the piece at <paramref name="position"/> can make.
        /// Illegal moves (ones that leave your own King in check) are filtered out before returning.
        /// </summary>
        public static void GetLegalMoves(BoardState board, Vector2Int position, List<MoveCommand> output)
        {
            output.Clear();
            PieceData piece = board.GetPiece(position);

            if (piece == null || piece.Team != board.CurrentTurn)
            {
                return;
            }

            IPieceMovement strategy = MovementFactory.GetStrategy(piece.Type);
            if (strategy == null)
            {
                return;
            }

            // Step 1: Ask the piece for every square it could physically reach, ignoring check rules.
            strategy.GetRawMoves(board, piece, output);

            bool isKing = piece.Type == ChessPieceType.King;
            bool isCurrentlyInCheck = false;
            Team enemyTeam = piece.Team == Team.White ? Team.Black : Team.White;

            if (isKing)
            {
                isCurrentlyInCheck = IsSquareUnderAttack(board, position, enemyTeam);
            }

            // Step 2: Walk through the raw moves and throw out any that leave our King in danger.
            int write = 0;
            for (int i = 0; i < output.Count; i++)
            {
                MoveCommand move = output[i];

                // Castling has extra rules beyond just "does it leave the King in check?"
                if (move.IsCastling)
                {
                    // Rule 1: Cannot castle out of check.
                    if (isCurrentlyInCheck) continue;

                    // Rule 2: Cannot pass through an attacked square.
                    // The King moves 2 steps; the square in between must be safe too.
                    int direction = move.EndPosition.x > move.StartPosition.x ? 1 : -1;
                    Vector2Int passThroughSquare = new Vector2Int(move.StartPosition.x + direction, move.StartPosition.y);

                    if (IsSquareUnderAttack(board, passThroughSquare, enemyTeam)) continue;
                }

                // For all moves, make sure the King isn't sitting in check after it resolves.
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
        /// Fills <paramref name="masterBuffer"/> with every legal move available to a team.
        /// </summary>
        public static void GetAllLegalMoves(BoardState board, Team team, List<MoveCommand> masterBuffer)
        {
            masterBuffer.Clear();
            
            
            for (int x = 0; x < board.TileCountX; x++)
            {
                for (int y = 0; y < board.TileCountY; y++)
                {
                    PieceData piece = board.GetPiece(x, y);
                    if (piece != null && piece.Team == team)
                    {
                        Vector2Int pos = new Vector2Int(piece.CurrentX, piece.CurrentY);
                            GetLegalMoves(board, pos, MoveGenBuffer);
                            masterBuffer.AddRange(MoveGenBuffer);
                    }
                }
            }
        }

        #endregion

        #region Check Detection

        /// <summary>
        /// Returns true if making this move would leave the moving player's King in check.
        /// We apply the move, check the result, then undo it — the board ends up exactly as it started.
        /// </summary>
        private static bool DoesMoveLeaveKingInCheck(BoardState board, MoveCommand move)
        {
            // 1. MAKE: Apply the move to the live board.
            ApplyMoveToBoard(board, move);

            // 2. EVALUATE: Find our King and see if he's under attack.
            PieceData myKing = board.FindKing(move.PieceTeam);
            bool inCheck = false;
            
            if (myKing != null)
            {
                Vector2Int kingPos = new Vector2Int(myKing.CurrentX, myKing.CurrentY);
                Team enemyTeam = move.PieceTeam == Team.White ? Team.Black : Team.White;
                inCheck = IsSquareUnderAttack(board, kingPos, enemyTeam);
            }
            else
            {
                // King not found — this shouldn't happen in a valid game, but assume illegal to be safe.
                inCheck = true;
            }

            // 3. UNMAKE: Restore the board to exactly how it was before.
            UndoMoveOnBoard(board, move);

            return inCheck;
        }

        /// <summary>
        /// Returns true if any piece on <paramref name="attackerTeam"/> can reach <paramref name="targetSquare"/>.
        /// Uses raw moves (not legal moves) to avoid infinite recursion with check detection.
        /// </summary>
        public static bool IsSquareUnderAttack(BoardState board, Vector2Int targetSquare, Team attackerTeam)
        {
            for (int x = 0; x < board.TileCountX; x++)
            {
                for (int y = 0; y < board.TileCountY; y++)
                {
                    PieceData attacker = board.GetPiece(x, y);
                    
                    if (attacker != null && attacker.Team == attackerTeam)
                    {
                        IPieceMovement strategy = MovementFactory.GetStrategy(attacker.Type);
                        if (strategy == null) continue;

                        AttackCheckBuffer.Clear();
                        strategy.GetRawMoves(board, attacker, AttackCheckBuffer);

                        for (int i = 0; i < AttackCheckBuffer.Count; i++)
                        {
                            if (AttackCheckBuffer[i].EndPosition == targetSquare)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Returns true if the given team's King is currently in check.
        /// </summary>
        public static bool IsKingInCheck(BoardState board, Team team)
        {
            PieceData king = board.FindKing(team);
            if (king == null) return false;

            Vector2Int kingPos = new Vector2Int(king.CurrentX, king.CurrentY);
            Team enemyTeam = team == Team.White ? Team.Black : Team.White;

            return IsSquareUnderAttack(board, kingPos, enemyTeam);
        }

        #endregion

        #region Game State Evaluation

        /// <summary>
        /// Returns true if the given team has at least one legal move available.
        /// </summary>
        public static bool HasAnyLegalMoves(BoardState board, Team team)
        {
            for (int x = 0; x < board.TileCountX; x++)
            {
                for (int y = 0; y < board.TileCountY; y++)
                {
                    PieceData piece = board.GetPiece(x, y);
                    
                    if (piece != null && piece.Team == team)
                    {
                        Vector2Int pos = new Vector2Int(piece.CurrentX, piece.CurrentY);
                        GetLegalMoves(board, pos, MoveGenBuffer);

                        if (MoveGenBuffer.Count > 0)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Checks the board and tells you whether the given team is in checkmate, stalemate, check, or playing normally.
        /// </summary>
        public static GameState EvaluateGameState(BoardState board, Team team)
        {
            bool isInCheck = IsKingInCheck(board, team);
            bool hasLegalMoves = HasAnyLegalMoves(board, team);

            if (!hasLegalMoves)
            {
                return isInCheck ? GameState.Checkmate : GameState.Stalemate;
            }

            // TODO (Betrayal): Add GameState.BetrayalInitiated and GameState.RetributionFailed here when
            // the custom mechanic is implemented. GameManager's CheckForGameEnd() will handle the transitions.
            return isInCheck ? GameState.Check : GameState.Normal;
        }

        #endregion

        #region Move Execution

        /// <summary>
        /// Updates the Zobrist hash to reflect a move. Because XOR is self-cancelling, calling this
        /// on the same move twice perfectly undoes the change — which is how move undo works.
        /// </summary>
        private static void ApplyZobristMove(BoardState board, MoveCommand move, int previousCastlingMask, int? previousEnPassantFile)
        {
            // 1. Toggle whose turn it is.
            board.ToggleTurnHash();

            // 2. Move the primary piece: remove it from the start square, add it to the end square.
            board.TogglePieceHash(move.PieceTeam, move.PieceType, move.StartPosition.x, move.StartPosition.y);
            board.TogglePieceHash(move.PieceTeam, move.PieceType, move.EndPosition.x, move.EndPosition.y);

            // 3. Remove the captured piece from its square.
            if (move.HasCapture)
            {
                Vector2Int capPos = move.IsEnPassant && move.EnPassantCapturePosition.HasValue
                    ? move.EnPassantCapturePosition.Value
                    : move.EndPosition;

                board.TogglePieceHash(move.CapturedTeam, move.CapturedType, capPos.x, capPos.y);
            }

            // 4. For promotions: remove the pawn that arrived, add the promoted piece.
            if (move.IsPromotion)
            {
                board.TogglePieceHash(move.PieceTeam, ChessPieceType.Pawn, move.EndPosition.x, move.EndPosition.y);
                board.TogglePieceHash(move.PieceTeam, move.PromotedTo, move.EndPosition.x, move.EndPosition.y);
            }

            // 5. For castling: move the Rook.
            if (move.IsCastling && move.RookStartPosition.HasValue && move.RookEndPosition.HasValue)
            {
                board.TogglePieceHash(move.PieceTeam, ChessPieceType.Rook, move.RookStartPosition.Value.x, move.RookStartPosition.Value.y);
                board.TogglePieceHash(move.PieceTeam, ChessPieceType.Rook, move.RookEndPosition.Value.x, move.RookEndPosition.Value.y);
            }

            // 6. Swap out the old castling rights for the new ones.
            board.ToggleCastlingHash(previousCastlingMask);
            board.ToggleCastlingHash(board.CastlingRights);

            // 7. Swap out the old en passant file for the new one (if either exists).
            if (previousEnPassantFile.HasValue)
            {
                board.ToggleEnPassantHash(previousEnPassantFile.Value);
            }
            if (board.EnPassantFile.HasValue)
            {
                board.ToggleEnPassantHash(board.EnPassantFile.Value);
            }
        }

        /// <summary>
        /// Works out which castling rights should still be available after a move.
        /// A King moving, a Rook moving, or a Rook being captured all cancel the relevant right.
        /// </summary>
        private static int ComputeNewCastlingMask(BoardState board, MoveCommand move)
        {
            int mask = board.CastlingRights;

            // If the King moves, both castling options for that team are gone.
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

            // If a Rook leaves its starting corner, that side's castling right is gone.
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

            // If an enemy Rook is captured on its starting corner, that side's castling right is also gone.
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

        /// <summary>
        /// Returns the en passant file that should be active after this move, or null if none.
        /// En passant is only possible in the turn immediately after a pawn moves two squares.
        /// </summary>
        private static int? ComputeNewEnPassantFile(MoveCommand move)
        {
            // En passant is only possible after a pawn moves 2 squares
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

        /// <summary>
        /// Applies a move to the board, including all the side effects: captures, castling, promotion,
        /// en passant, updated castling rights, and the Zobrist hash.
        /// </summary>
        public static void ApplyMoveToBoard(BoardState board, MoveCommand move)
        {
            int previousCastlingMask = board.CastlingRights;
            int? previousEnPassantFile = board.EnPassantFile;

            board.RecordMove(move.StartPosition, move.EndPosition);

            if (move.IsCastling && move.RookStartPosition.HasValue && move.RookEndPosition.HasValue)
            {
                board.MovePiece(move.StartPosition, move.EndPosition);
                board.MovePiece(move.RookStartPosition.Value, move.RookEndPosition.Value);
                board.CastlingRights = ComputeNewCastlingMask(board, move);
                board.EnPassantFile = ComputeNewEnPassantFile(move);

                ApplyZobristMove(board, move, previousCastlingMask, previousEnPassantFile);
                return;
            }

            if (move.IsEnPassant && move.EnPassantCapturePosition.HasValue)
            {
                board.MovePiece(move.StartPosition, move.EndPosition);

                // The captured pawn sits on a different square than where we landed.
                board.RemovePiece(move.EnPassantCapturePosition.Value);
                board.CastlingRights = ComputeNewCastlingMask(board, move);
                board.EnPassantFile = ComputeNewEnPassantFile(move);

                ApplyZobristMove(board, move, previousCastlingMask, previousEnPassantFile);
                return;
            }

            board.MovePiece(move.StartPosition, move.EndPosition);

            if (move.IsPromotion)
            {
                // The pawn has already moved to the end square — grab it there and change its type.
                PieceData pieceOnBoard = board.GetPiece(move.EndPosition);

                if (pieceOnBoard != null)
                {
                    pieceOnBoard.PromoteTo(move.PromotedTo);
                }
                else
                {
                    // This should never happen in a valid game.
                    // TODO: Replace UnityEngine.Debug.LogError with a C# exception or a delegate callback so this class stays Unity-free.
                    UnityEngine.Debug.LogError($"[ChessEngine] Promotion failed: No piece found at {move.EndPosition}");
                }
            }

            board.CastlingRights = ComputeNewCastlingMask(board, move);
            board.EnPassantFile = ComputeNewEnPassantFile(move);
            ApplyZobristMove(board, move, previousCastlingMask, previousEnPassantFile);
        }

        /// <summary>
        /// Rolls back a move completely, restoring the board to exactly how it was before.
        /// This is how the AI can explore thousands of move sequences without ever copying the board.
        /// </summary>
        private static void UndoMoveOnBoard(BoardState board, MoveCommand move)
        {
            int currentCastlingMask = board.CastlingRights;
            int? currentEnPassantFile = board.EnPassantFile;

            // Restore the castling and en passant state that was recorded on the move itself.
            board.CastlingRights = move.PreviousCastlingMask;
            board.EnPassantFile = move.PreviousEnPassantFile;

            // Reverse the hash — XOR is self-inverse, so calling this again undoes it perfectly.
            ApplyZobristMove(board, move, currentCastlingMask, currentEnPassantFile);

            // Each move records two history entries (start and end), so we remove both.
            if (board.MoveHistory.Count >= 2)
            {
                board.MoveHistory.RemoveAt(board.MoveHistory.Count - 1);
                board.MoveHistory.RemoveAt(board.MoveHistory.Count - 1);
            }

            // 1. Grab the primary piece from its destination.
            PieceData primaryPiece = board.GetPiece(move.EndPosition);

            // If it was promoted, turn it back into a pawn.
            if (move.IsPromotion && primaryPiece != null)
            {
                primaryPiece.Type = ChessPieceType.Pawn;
            }

            // 2. Move it back to where it came from.
            // We use SetPiece instead of MovePiece here to avoid triggering capture logic.
            board.SetPiece(null, move.EndPosition.x, move.EndPosition.y);
            board.SetPiece(primaryPiece, move.StartPosition.x, move.StartPosition.y);
            if (primaryPiece != null)
            {
                primaryPiece.HasMoved = move.PieceHadMoved;
            }

            // 3. Bring back any piece that was captured.
            if (move.HasCapture)
            {
                List<PieceData> graveyard = move.CapturedTeam == Team.White ? board.WhiteCaptured : board.BlackCaptured;
                PieceData resurrectedPiece = null;
                if (graveyard.Count > 0)
                {
                    resurrectedPiece = graveyard[graveyard.Count - 1];
                    graveyard.RemoveAt(graveyard.Count - 1);
                }
                else
                {
                    // Failsafe: recreate the piece if the graveyard is somehow empty (shouldn't happen).
                    int dir = move.CapturedTeam == Team.White ? 1 : -1;
                    resurrectedPiece = new PieceData(move.CapturedTeam, move.CapturedType, 0, 0, dir);
                }
                resurrectedPiece.HasMoved = move.CapturedHadMoved;

                // En passant captures happen on a different square than the landing square.
                Vector2Int capturePos = move.IsEnPassant && move.EnPassantCapturePosition.HasValue
                    ? move.EnPassantCapturePosition.Value
                    : move.EndPosition;
                board.SetPiece(resurrectedPiece, capturePos.x, capturePos.y);
            }

            // 4. Move the Rook back if this was a castling move.
            if (move.IsCastling && move.RookStartPosition.HasValue && move.RookEndPosition.HasValue)
            {
                PieceData rook = board.GetPiece(move.RookEndPosition.Value);

                board.SetPiece(null, move.RookEndPosition.Value.x, move.RookEndPosition.Value.y);
                board.SetPiece(rook, move.RookStartPosition.Value.x, move.RookStartPosition.Value.y);

                // The Rook definitely hadn't moved before castling, so restore that state.
                if (rook != null)
                {
                    rook.HasMoved = false;
                }
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
        /// Handy for AI evaluation.
        /// </summary>
        public static int GetMaterialAdvantage(BoardState board)
        {
            int whiteValue = 0;
            int blackValue = 0;

            for (int x = 0; x < board.TileCountX; x++)
            {
                for (int y = 0; y < board.TileCountY; y++)
                {
                    PieceData piece = board.GetPiece(x, y);
                    if (piece == null) continue;

                    int value = GetPieceValue(piece.Type);

                    if (piece.Team == Team.White)
                        whiteValue += value;
                    else
                        blackValue += value;
                }
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
                ChessPieceType.King   => 0, // Priceless.
                _ => 0
            };
        }

        #endregion
    }

    /// <summary>
    /// What state is the game in right now, from a specific team's point of view?
    /// </summary>
    public enum GameState
    {
        Normal,     // Nothing special — game continues.
        Check,      // King is in check but has at least one escape.
        Checkmate,  // King is in check with no legal moves — game over.
        Stalemate   // No legal moves, but not in check — draw.
    }
}