using System.Collections.Generic;
using ChessTheMasterPiece.Data;
using ChessTheMasterPiece.Logic.Movement;

namespace ChessTheMasterPiece.Logic
{
    /// <summary>
    /// Pure, stateless chess rules engine with ZERO Unity dependencies.
    /// Handles legal move generation, check detection, checkmate/stalemate evaluation.
    /// Thread-safe and suitable for AI, networking, or headless simulation.
    /// GC-optimized with buffer-passing pattern to eliminate allocation spikes.
    /// </summary>
    public static class ChessEngine
    {
        // Pre-allocated scratch buffers to eliminate GC spikes
        private static readonly List<MoveCommand> _attackScratch = new List<MoveCommand>(64);
        private static readonly List<MoveCommand> _engineScratch = new List<MoveCommand>(64);

        #region Legal Move Generation

        /// <summary>
        /// Populates the provided output list with legal moves.
        /// Zero allocations - reuses the output buffer.
        /// </summary>
        /// <param name="board">Current board state</param>
        /// <param name="position">Position of the piece to move</param>
        /// <param name="output">Pre-allocated list to populate with legal moves</param>
        public static void GetLegalMoves(BoardState board, Vector2Int position, List<MoveCommand> output)
        {
            output.Clear();
            PieceData piece = board.GetPiece(position);

            // Validate piece exists and belongs to the current turn
            if (piece == null || piece.Team != board.CurrentTurn)
            {
                return;
            }

            // Get the movement strategy for this piece type
            IPieceMovement strategy = MovementFactory.GetStrategy(piece.Type);
            if (strategy == null)
            {
                return;
            }

            // Step 1: Get all physically possible moves (ignoring check)
            // Populate the provided output buffer with raw moves first (zero-alloc).
            strategy.GetRawMoves(board, piece, output);

            bool isKing = piece.Type == ChessPieceType.King;
            bool isCurrentlyInCheck = false;
            Team enemyTeam = piece.Team == Team.White ? Team.Black : Team.White;

            if (isKing)
            {
                // Check if the King is in check BEFORE he makes a move
                isCurrentlyInCheck = IsSquareUnderAttack(board, position, enemyTeam);
            }

            // Step 2: Filter out moves that leave our own King in check
            // We filter in-place to avoid allocations: compact valid moves to the start of the list.
            int write = 0;
            for (int i = 0; i < output.Count; i++)
            {
                MoveCommand move = output[i];

                // Enforce strict Castling rules
                if (move.IsCastling)
                {
                    // Rule 1: Cannot castle out of check
                    if (isCurrentlyInCheck) continue;

                    // Rule 2: Cannot pass through an attacked square
                    // The King moves 2 steps. The intermediate square is 1 step in that direction.
                    int direction = move.EndPosition.x > move.StartPosition.x ? 1 : -1;
                    Vector2Int passThroughSquare = new Vector2Int(move.StartPosition.x + direction, move.StartPosition.y);

                    if (IsSquareUnderAttack(board, passThroughSquare, enemyTeam)) continue;
                }

                // For all moves, ensure the final resting place is safe
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
        /// High-performance AI move generator. 
        /// Populates a single master buffer with ALL legal moves for a specific team.
        /// 100% Zero Allocation.
        /// </summary>
        public static void GetAllLegalMoves(BoardState board, Team team, List<MoveCommand> masterBuffer)
        {
            masterBuffer.Clear();

            // Loop directly over the array to avoid GetAllPieces() list allocations
            for (int x = 0; x < board.TileCountX; x++)
            {
                for (int y = 0; y < board.TileCountY; y++)
                {
                    PieceData piece = board.GetPiece(x, y);
                    if (piece != null && piece.Team == team)
                    {
                        Vector2Int pos = new Vector2Int(piece.CurrentX, piece.CurrentY);
                        GetLegalMoves(board, pos, _engineScratch);
                        masterBuffer.AddRange(_engineScratch);
                    }
                }
            }
        }

        #endregion

        #region Check Detection

        /// <summary>
        /// Determines if a move would leave the moving player's King in check.
        /// Uses Make/Unmake pattern to eliminate GC pressure from deep move trees.
        /// Zero allocations - mutates and restores the live board state.
        /// </summary>
        private static bool DoesMoveLeaveKingInCheck(BoardState board, MoveCommand move)
        {
            // 1. MAKE: Apply the move directly to the live board
            ApplyMoveToBoard(board, move);

            // 2. EVALUATE: Find our King and check if he is under attack
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
                // King not found - this shouldn't happen in a valid game
                inCheck = true; // Assume illegal to be safe
            }

            // 3. UNMAKE: Reverse time to restore the live board perfectly
            UndoMoveOnBoard(board, move);

            return inCheck;
        }

        /// <summary>
        /// Checks if the specified square is under attack by any piece of the attacker team.
        /// Uses raw moves (not legal moves) to avoid infinite recursion.
        /// GC-optimized with scratch buffer and 2D array traversal.
        /// </summary>
        public static bool IsSquareUnderAttack(BoardState board, Vector2Int targetSquare, Team attackerTeam)
        {
            // Zero-allocation loop over the board
            for (int x = 0; x < board.TileCountX; x++)
            {
                for (int y = 0; y < board.TileCountY; y++)
                {
                    PieceData attacker = board.GetPiece(x, y);
                    
                    if (attacker != null && attacker.Team == attackerTeam)
                    {
                        IPieceMovement strategy = MovementFactory.GetStrategy(attacker.Type);
                        if (strategy == null) continue;

                        // Zero-allocation raw move generation
                        _attackScratch.Clear();
                        strategy.GetRawMoves(board, attacker, _attackScratch);

                        for (int i = 0; i < _attackScratch.Count; i++)
                        {
                            if (_attackScratch[i].EndPosition == targetSquare)
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
        /// Determines if the specified team's King is currently in check.
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
        /// Evaluates if the specified team has any legal moves remaining.
        /// GC-optimized with scratch buffer and 2D array traversal.
        /// </summary>
        public static bool HasAnyLegalMoves(BoardState board, Team team)
        {
            // Zero-allocation loop over the board
            for (int x = 0; x < board.TileCountX; x++)
            {
                for (int y = 0; y < board.TileCountY; y++)
                {
                    PieceData piece = board.GetPiece(x, y);
                    
                    if (piece != null && piece.Team == team)
                    {
                        Vector2Int pos = new Vector2Int(piece.CurrentX, piece.CurrentY);
                        GetLegalMoves(board, pos, _engineScratch);

                        if (_engineScratch.Count > 0)
                        {
                            return true; // Found at least one legal move
                        }
                    }
                }
            }
            return false; // No legal moves available
        }

        /// <summary>
        /// Evaluates the current game state for the specified team.
        /// Returns: Checkmate, Stalemate, Check, or Normal.
        /// </summary>
        public static GameState EvaluateGameState(BoardState board, Team team)
        {
            bool isInCheck = IsKingInCheck(board, team);
            bool hasLegalMoves = HasAnyLegalMoves(board, team);

            if (!hasLegalMoves)
            {
                return isInCheck ? GameState.Checkmate : GameState.Stalemate;
            }

            return isInCheck ? GameState.Check : GameState.Normal;
        }

        #endregion

        #region Move Execution

        /// <summary>
        /// XORs all moving parts of a command into or out of the board's Zobrist Hash.
        /// Calling this once applies the move to the hash. Calling it again perfectly reverses it.
        /// Zero allocations - pure bitwise XOR operations.
        /// </summary>
        private static void ApplyZobristMove(BoardState board, MoveCommand move, int previousCastlingMask, int? previousEnPassantFile)
        {
            // 1. Toggle Turn
            board.ToggleTurnHash();

            // 2. Toggle Primary Piece (Remove from start, Add to end)
            board.TogglePieceHash(move.PieceTeam, move.PieceType, move.StartPosition.x, move.StartPosition.y);
            board.TogglePieceHash(move.PieceTeam, move.PieceType, move.EndPosition.x, move.EndPosition.y);

            // 3. Toggle Capture (Remove captured piece from its square)
            if (move.HasCapture)
            {
                Vector2Int capPos = move.IsEnPassant && move.EnPassantCapturePosition.HasValue
                    ? move.EnPassantCapturePosition.Value
                    : move.EndPosition;

                board.TogglePieceHash(move.CapturedTeam, move.CapturedType, capPos.x, capPos.y);
            }

            // 4. Toggle Promotion (Remove the pawn that arrived, Add the promoted piece)
            if (move.IsPromotion)
            {
                board.TogglePieceHash(move.PieceTeam, ChessPieceType.Pawn, move.EndPosition.x, move.EndPosition.y);
                board.TogglePieceHash(move.PieceTeam, move.PromotedTo, move.EndPosition.x, move.EndPosition.y);
            }

            // 5. Toggle Castling (Move the Rook)
            if (move.IsCastling && move.RookStartPosition.HasValue && move.RookEndPosition.HasValue)
            {
                board.TogglePieceHash(move.PieceTeam, ChessPieceType.Rook, move.RookStartPosition.Value.x, move.RookStartPosition.Value.y);
                board.TogglePieceHash(move.PieceTeam, ChessPieceType.Rook, move.RookEndPosition.Value.x, move.RookEndPosition.Value.y);
            }

            // 6. Toggle Castling Rights State Change
            // XOR out the old castling rights
            board.ToggleCastlingHash(previousCastlingMask);
            // XOR in the new castling rights
            board.ToggleCastlingHash(board.CurrentCastlingMask);

            // 7. Toggle En Passant State Change
            if (previousEnPassantFile.HasValue)
            {
                board.ToggleEnPassantHash(previousEnPassantFile.Value);
            }
            if (board.CurrentEnPassantFile.HasValue)
            {
                board.ToggleEnPassantHash(board.CurrentEnPassantFile.Value);
            }
        }

        /// <summary>
        /// Computes the new castling mask after a move.
        /// Removes rights when King/Rook moves or is captured.
        /// </summary>
        private static int ComputeNewCastlingMask(BoardState board, MoveCommand move)
        {
            int mask = board.CurrentCastlingMask;

            // If King moves, lose both castling rights for that team
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

            // If Rook moves from corner, lose that side's castling right
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

            // If Rook is captured on corner, lose that side's castling right
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
        /// Computes the new en passant file after a move.
        /// Only set when a pawn moves 2 squares.
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
        /// Applies a move command to a board state.
        /// Handles standard moves, captures, and special moves.
        /// This modifies the board state - use on clones for simulation.
        /// </summary>
        public static void ApplyMoveToBoard(BoardState board, MoveCommand move)
        {
            // Snapshot the previous state for Zobrist updates
            int previousCastlingMask = board.CurrentCastlingMask;
            int? previousEnPassantFile = board.CurrentEnPassantFile;

            // Push this move to the history stack for En Passant detection during simulation
            board.RecordMove(move.StartPosition, move.EndPosition);

            // Handle special moves first
            if (move.IsCastling && move.RookStartPosition.HasValue && move.RookEndPosition.HasValue)
            {
                // Move the King
                board.MovePiece(move.StartPosition, move.EndPosition);

                // Move the Rook
                board.MovePiece(move.RookStartPosition.Value, move.RookEndPosition.Value);

                // Update castling mask and en passant state
                board.CurrentCastlingMask = ComputeNewCastlingMask(board, move);
                board.CurrentEnPassantFile = ComputeNewEnPassantFile(move);

                // Update Zobrist hash after board modification
                ApplyZobristMove(board, move, previousCastlingMask, previousEnPassantFile);
                return;
            }

            if (move.IsEnPassant && move.EnPassantCapturePosition.HasValue)
            {
                // Move the pawn
                board.MovePiece(move.StartPosition, move.EndPosition);

                // Remove the captured pawn (at a different position than the landing square)
                board.RemovePiece(move.EnPassantCapturePosition.Value);

                // Update castling mask and en passant state
                board.CurrentCastlingMask = ComputeNewCastlingMask(board, move);
                board.CurrentEnPassantFile = ComputeNewEnPassantFile(move);

                // Update Zobrist hash after board modification
                ApplyZobristMove(board, move, previousCastlingMask, previousEnPassantFile);
                return;
            }

            // Standard move or capture
            board.MovePiece(move.StartPosition, move.EndPosition);

            // Handle promotion - CORRECTED: Get the piece from its NEW position
            if (move.IsPromotion)
            {
                // Fetch the piece that is NOW at the destination (the moved pawn)
                PieceData pieceOnBoard = board.GetPiece(move.EndPosition);
                
                if (pieceOnBoard != null)
                {
                    // Use the dedicated PromoteTo method for proper state management
                    pieceOnBoard.PromoteTo(move.PromotedTo);
                }
                else
                {
                    // This should never happen in a valid game
                    UnityEngine.Debug.LogError($"[ChessEngine] Promotion failed: No piece found at {move.EndPosition}");
                }
            }

            // Update castling mask and en passant state
            board.CurrentCastlingMask = ComputeNewCastlingMask(board, move);
            board.CurrentEnPassantFile = ComputeNewEnPassantFile(move);

            // Update Zobrist hash after board modification
            ApplyZobristMove(board, move, previousCastlingMask, previousEnPassantFile);
        }

        /// <summary>
        /// Perfectly reverses a MoveCommand, restoring the board to its exact previous state.
        /// This includes restoring captured pieces from the graveyard and resetting 'HasMoved' flags.
        /// Zero allocations - essential for high-performance AI move tree evaluation.
        /// </summary>
        private static void UndoMoveOnBoard(BoardState board, MoveCommand move)
        {
            // Snapshot current state for Zobrist reversal
            int currentCastlingMask = board.CurrentCastlingMask;
            int? currentEnPassantFile = board.CurrentEnPassantFile;

            // Restore the previous castling and en passant state from the move snapshot
            board.CurrentCastlingMask = move.PreviousCastlingMask;
            board.CurrentEnPassantFile = move.PreviousEnPassantFile;

            // Reverse the Zobrist hash exactly (XOR is self-inverse)
            ApplyZobristMove(board, move, currentCastlingMask, currentEnPassantFile);

            // Pop this move from the history stack (O(1) operation)
            // Each move adds 2 entries (start, end), so we remove both
            if (board.MoveHistory.Count >= 2)
            {
                board.MoveHistory.RemoveAt(board.MoveHistory.Count - 1);
                board.MoveHistory.RemoveAt(board.MoveHistory.Count - 1);
            }

            // 1. Fetch the primary piece from its current destination
            PieceData primaryPiece = board.GetPiece(move.EndPosition);

            // If it was a promotion, demote it back to a pawn
            if (move.IsPromotion && primaryPiece != null)
            {
                primaryPiece.Type = ChessPieceType.Pawn;
            }

            // 2. Move the primary piece BACK using SetPiece 
            // (We use SetPiece instead of MovePiece to avoid triggering capture logic)
            board.SetPiece(null, move.EndPosition.x, move.EndPosition.y); // Clear destination
            board.SetPiece(primaryPiece, move.StartPosition.x, move.StartPosition.y); // Put back at start

            // Restore the piece's historical 'HasMoved' state
            if (primaryPiece != null)
            {
                primaryPiece.HasMoved = move.PieceHadMoved;
            }

            // 3. Restore Captured Piece (if any)
            if (move.HasCapture)
            {
                // Determine which graveyard the piece was sent to
                List<PieceData> graveyard = move.CapturedTeam == Team.White ? board.WhiteCaptured : board.BlackCaptured;
                PieceData resurrectedPiece = null;

                // Pluck it out of the graveyard to prevent memory leaks during simulation
                if (graveyard.Count > 0)
                {
                    resurrectedPiece = graveyard[graveyard.Count - 1];
                    graveyard.RemoveAt(graveyard.Count - 1);
                }
                else
                {
                    // Failsafe: Recreate the piece if the graveyard is empty (shouldn't happen)
                    int dir = move.CapturedTeam == Team.White ? 1 : -1;
                    resurrectedPiece = new PieceData(move.CapturedTeam, move.CapturedType, 0, 0, dir);
                }

                // Restore its historical 'HasMoved' state
                resurrectedPiece.HasMoved = move.CapturedHadMoved;

                // Determine where it died (En Passant captures happen on a different square)
                Vector2Int capturePos = move.IsEnPassant && move.EnPassantCapturePosition.HasValue 
                    ? move.EnPassantCapturePosition.Value 
                    : move.EndPosition;

                // Put it back on the board
                board.SetPiece(resurrectedPiece, capturePos.x, capturePos.y);
            }

            // 4. Reverse Castling (Move the Rook back)
            if (move.IsCastling && move.RookStartPosition.HasValue && move.RookEndPosition.HasValue)
            {
                PieceData rook = board.GetPiece(move.RookEndPosition.Value);
                
                // Move Rook back
                board.SetPiece(null, move.RookEndPosition.Value.x, move.RookEndPosition.Value.y);
                board.SetPiece(rook, move.RookStartPosition.Value.x, move.RookStartPosition.Value.y);
                
                // If it was castling, the Rook definitely hadn't moved prior to this turn
                if (rook != null)
                {
                    rook.HasMoved = false;
                }
            }
        }

        /// <summary>
        /// Validates and executes a move on the board if it's legal.
        /// Returns true if the move was successfully executed.
        /// </summary>
        public static bool TryExecuteMove(BoardState board, MoveCommand move)
        {
            // Verify it's the correct team's turn
            if (move.PieceTeam != board.CurrentTurn)
            {
                return false;
            }

            // Get legal moves for this piece using scratch buffer
            _engineScratch.Clear();
            GetLegalMoves(board, move.StartPosition, _engineScratch);

            // Check if this move is in the legal moves list
            bool isLegal = false;
            for (int i = 0; i < _engineScratch.Count; i++)
            {
                if (_engineScratch[i].EndPosition == move.EndPosition)
                {
                    isLegal = true;
                    break;
                }
            }

            if (!isLegal)
            {
                return false;
            }

            // Execute the move (ApplyMoveToBoard now handles RecordMove internally)
            ApplyMoveToBoard(board, move);
            board.NextTurn();

            return true;
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Calculates the material value difference between teams.
        /// Positive means White is ahead, negative means Black is ahead.
        /// Useful for AI evaluation functions.
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
        /// Returns the standard material value of a piece type.
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
                ChessPieceType.King => 0, // King has no material value (priceless!)
                _ => 0
            };
        }

        #endregion
    }

    /// <summary>
    /// Represents the current state of the game from a specific team's perspective.
    /// </summary>
    public enum GameState
    {
        Normal,      // Game continues normally
        Check,       // King is in check but has escape moves
        Checkmate,   // King is in check with no legal moves (game over)
        Stalemate    // Not in check but no legal moves (draw)
    }
}