using System.Collections.Generic;
using ChessTheMasterPiece.Data;
using ChessTheMasterPiece.Logic.Movement;

namespace ChessTheMasterPiece.Logic
{
    /// <summary>
    /// Pure, stateless chess rules engine with ZERO Unity dependencies.
    /// Handles legal move generation, check detection, checkmate/stalemate evaluation.
    /// Thread-safe and suitable for AI, networking, or headless simulation.
    /// </summary>
    public static class ChessEngine
    {
        #region Legal Move Generation

        /// <summary>
        /// Returns all fully legal moves for a piece at the given position.
        /// Filters out moves that would leave the player's own King in check.
        /// This is the primary method you'll call from your UI/Controller layer.
        /// </summary>
        /// <param name="board">Current board state</param>
        /// <param name="position">Position of the piece to move</param>
        /// <returns>List of legal move commands</returns>
        public static List<MoveCommand> GetLegalMoves(BoardState board, Vector2Int position)
        {
            List<MoveCommand> legalMoves = new List<MoveCommand>();
            PieceData piece = board.GetPiece(position);

            // Validate piece exists and belongs to the current turn
            if (piece == null || piece.Team != board.CurrentTurn)
            {
                return legalMoves;
            }

            // Get the movement strategy for this piece type
            IPieceMovement strategy = MovementFactory.GetStrategy(piece.Type);
            if (strategy == null)
            {
                return legalMoves;
            }

            // Step 1: Get all physically possible moves (ignoring check)
            List<MoveCommand> rawMoves = strategy.GetRawMovesWithHistory(board, piece, board.MoveHistory);

            bool isKing = piece.Type == ChessPieceType.King;
            bool isCurrentlyInCheck = false;
            Team enemyTeam = piece.Team == Team.White ? Team.Black : Team.White;

            if (isKing)
            {
                // Check if the King is in check BEFORE he makes a move
                isCurrentlyInCheck = IsSquareUnderAttack(board, position, enemyTeam);
            }

            // Step 2: Filter out moves that leave our own King in check
            foreach (var move in rawMoves)
            {
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
                    legalMoves.Add(move);
                }
            }

            return legalMoves;
        }

        /// <summary>
        /// Similar to GetLegalMoves but for all pieces of a given team.
        /// Useful for AI move generation or UI highlighting.
        /// </summary>
        public static Dictionary<Vector2Int, List<MoveCommand>> GetAllLegalMoves(BoardState board, Team team)
        {
            Dictionary<Vector2Int, List<MoveCommand>> allMoves = new Dictionary<Vector2Int, List<MoveCommand>>();
            List<PieceData> pieces = board.GetAllPieces(team);

            foreach (var piece in pieces)
            {
                Vector2Int pos = new Vector2Int(piece.CurrentX, piece.CurrentY);
                List<MoveCommand> moves = GetLegalMoves(board, pos);

                if (moves.Count > 0)
                {
                    allMoves[pos] = moves;
                }
            }

            return allMoves;
        }

        #endregion

        #region Check Detection

        /// <summary>
        /// Simulates a move on a cloned board to determine if it results in 
        /// the moving player's King being in check.
        /// This is the core of preventing illegal moves.
        /// </summary>
        private static bool DoesMoveLeaveKingInCheck(BoardState board, MoveCommand move)
        {
            // Clone the board to safely simulate without corrupting live state
            BoardState simulation = board.Clone();

            // Execute the move on the simulation
            ApplyMoveToBoard(simulation, move);

            // Find our King on the simulated board
            PieceData myKing = simulation.FindKing(move.PieceMoved.Team);
            if (myKing == null)
            {
                // King not found - this shouldn't happen in a valid game
                return true; // Assume illegal to be safe
            }

            Vector2Int kingPos = new Vector2Int(myKing.CurrentX, myKing.CurrentY);
            Team enemyTeam = move.PieceMoved.Team == Team.White ? Team.Black : Team.White;

            return IsSquareUnderAttack(simulation, kingPos, enemyTeam);
        }

        /// <summary>
        /// Checks if the specified square is under attack by any piece of the attacker team.
        /// Uses raw moves (not legal moves) to avoid infinite recursion.
        /// </summary>
        public static bool IsSquareUnderAttack(BoardState board, Vector2Int targetSquare, Team attackerTeam)
        {
            List<PieceData> attackers = board.GetAllPieces(attackerTeam);

            foreach (PieceData attacker in attackers)
            {
                IPieceMovement strategy = MovementFactory.GetStrategy(attacker.Type);
                if (strategy == null) continue;

                // CRITICAL: Use GetRawMoves here, NOT GetLegalMoves
                // This prevents infinite recursion and allows pinned pieces to still protect squares
                List<MoveCommand> attackMoves = strategy.GetRawMoves(board, attacker);

                foreach (var move in attackMoves)
                {
                    if (move.EndPosition == targetSquare)
                    {
                        return true; // Square is under attack
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
        /// If not, the game is either checkmate or stalemate.
        /// </summary>
        public static bool HasAnyLegalMoves(BoardState board, Team team)
        {
            List<PieceData> pieces = board.GetAllPieces(team);

            foreach (var piece in pieces)
            {
                Vector2Int pos = new Vector2Int(piece.CurrentX, piece.CurrentY);
                List<MoveCommand> legalMoves = GetLegalMoves(board, pos);

                if (legalMoves.Count > 0)
                {
                    return true; // Found at least one legal move
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
        /// Applies a move command to a board state.
        /// Handles standard moves, captures, and special moves.
        /// This modifies the board state - use on clones for simulation.
        /// </summary>
        public static void ApplyMoveToBoard(BoardState board, MoveCommand move)
        {
            // Handle special moves first
            if (move.IsCastling && move.RookStartPosition.HasValue && move.RookEndPosition.HasValue)
            {
                // Move the King
                board.MovePiece(move.StartPosition, move.EndPosition);

                // Move the Rook
                board.MovePiece(move.RookStartPosition.Value, move.RookEndPosition.Value);
                return;
            }

            if (move.IsEnPassant && move.EnPassantCapturePosition.HasValue)
            {
                // Move the pawn
                board.MovePiece(move.StartPosition, move.EndPosition);

                // Remove the captured pawn (at a different position than the landing square)
                board.RemovePiece(move.EnPassantCapturePosition.Value);
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
        }

        /// <summary>
        /// Validates and executes a move on the board if it's legal.
        /// Returns true if the move was successfully executed.
        /// </summary>
        public static bool TryExecuteMove(BoardState board, MoveCommand move)
        {
            // Verify it's the correct team's turn
            if (move.PieceMoved.Team != board.CurrentTurn)
            {
                return false;
            }

            // Get legal moves for this piece
            List<MoveCommand> legalMoves = GetLegalMoves(board, move.StartPosition);

            // Check if this move is in the legal moves list
            bool isLegal = false;
            foreach (var legalMove in legalMoves)
            {
                if (legalMove.EndPosition == move.EndPosition)
                {
                    isLegal = true;
                    break;
                }
            }

            if (!isLegal)
            {
                return false;
            }

            // Execute the move
            ApplyMoveToBoard(board, move);
            board.RecordMove(move.StartPosition, move.EndPosition);
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