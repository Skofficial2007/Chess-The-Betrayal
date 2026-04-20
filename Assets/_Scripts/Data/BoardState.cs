using System.Collections.Generic;

namespace ChessTheMasterPiece.Data
{
    /// <summary>
    /// Pure C# representation of the chess board state - no Unity dependencies.
    /// This is the single source of truth for the game's logical state.
    /// Can be serialized for network sync, game saves, or AI simulation.
    /// </summary>
    public class BoardState
    {
        // The mathematical grid - holds pure data, not GameObjects
        public PieceData[,] LogicalBoard { get; private set; }

        public Team CurrentTurn { get; set; }
        public int TileCountX { get; private set; }
        public int TileCountY { get; private set; }

        // Move history: stores position pairs for move tracking
        public List<Vector2Int> MoveHistory { get; private set; }

        /// <summary>
        /// Tracks captured pieces for each team.
        /// </summary>
        public List<PieceData> WhiteCaptured { get; private set; }
        public List<PieceData> BlackCaptured { get; private set; }

        /// <summary>
        /// Game state flags
        /// </summary>
        public bool IsGameOver { get; set; }
        public Team? Winner { get; set; } // null if stalemate

        public BoardState(int sizeX = 8, int sizeY = 8)
        {
            TileCountX = sizeX;
            TileCountY = sizeY;
            LogicalBoard = new PieceData[sizeX, sizeY];
            CurrentTurn = Team.White; // White starts
            MoveHistory = new List<Vector2Int>();
            WhiteCaptured = new List<PieceData>();
            BlackCaptured = new List<PieceData>();
            IsGameOver = false;
            Winner = null;
        }

        /// <summary>
        /// Places a piece on the board at the specified position.
        /// Updates the piece's coordinates automatically.
        /// </summary>
        public void SetPiece(PieceData piece, int x, int y)
        {
            if (IsValidIndex(x, y))
            {
                LogicalBoard[x, y] = piece;
                if (piece != null)
                {
                    piece.CurrentX = x;
                    piece.CurrentY = y;
                }
            }
        }

        /// <summary>
        /// Retrieves the piece at the specified position.
        /// Returns null if the square is empty or out of bounds.
        /// </summary>
        public PieceData GetPiece(int x, int y)
        {
            if (IsValidIndex(x, y))
            {
                return LogicalBoard[x, y];
            }
            return null;
        }

        /// <summary>
        /// Overload: Get piece using Vector2Int.
        /// </summary>
        public PieceData GetPiece(Vector2Int pos)
        {
            return GetPiece(pos.x, pos.y);
        }

        /// <summary>
        /// Moves a piece from one position to another.
        /// Does NOT validate if the move is legal - that's the rules engine's job.
        /// Returns the captured piece if any.
        /// </summary>
        public PieceData MovePiece(int fromX, int fromY, int toX, int toY)
        {
            if (!IsValidIndex(fromX, fromY) || !IsValidIndex(toX, toY))
            {
                return null;
            }

            PieceData movingPiece = LogicalBoard[fromX, fromY];
            if (movingPiece == null)
            {
                return null;
            }

            PieceData capturedPiece = LogicalBoard[toX, toY];

            // Update board
            LogicalBoard[fromX, fromY] = null;
            LogicalBoard[toX, toY] = movingPiece;

            // Update piece data
            movingPiece.MoveTo(toX, toY);

            // Track captures
            if (capturedPiece != null)
            {
                if (capturedPiece.IsWhite)
                {
                    WhiteCaptured.Add(capturedPiece);
                }
                else
                {
                    BlackCaptured.Add(capturedPiece);
                }
            }

            return capturedPiece;
        }

        /// <summary>
        /// Overload: Move piece using Vector2Int positions.
        /// </summary>
        public PieceData MovePiece(Vector2Int from, Vector2Int to)
        {
            return MovePiece(from.x, from.y, to.x, to.y);
        }

        /// <summary>
        /// Removes a piece from the board (for en passant, etc).
        /// </summary>
        public PieceData RemovePiece(int x, int y)
        {
            if (!IsValidIndex(x, y))
            {
                return null;
            }

            PieceData piece = LogicalBoard[x, y];
            LogicalBoard[x, y] = null;

            if (piece != null)
            {
                if (piece.IsWhite)
                {
                    WhiteCaptured.Add(piece);
                }
                else
                {
                    BlackCaptured.Add(piece);
                }
            }

            return piece;
        }

        /// <summary>
        /// Overload: Remove piece using Vector2Int.
        /// </summary>
        public PieceData RemovePiece(Vector2Int pos)
        {
            return RemovePiece(pos.x, pos.y);
        }

        /// <summary>
        /// Clears all pieces from the board.
        /// </summary>
        public void Clear()
        {
            for (int x = 0; x < TileCountX; x++)
            {
                for (int y = 0; y < TileCountY; y++)
                {
                    LogicalBoard[x, y] = null;
                }
            }
            WhiteCaptured.Clear();
            BlackCaptured.Clear();
            MoveHistory.Clear();
            IsGameOver = false;
            Winner = null;
            CurrentTurn = Team.White;
        }

        /// <summary>
        /// Finds the king of the specified team.
        /// Returns null if not found (shouldn't happen in a valid game).
        /// </summary>
        public PieceData FindKing(Team team)
        {
            for (int x = 0; x < TileCountX; x++)
            {
                for (int y = 0; y < TileCountY; y++)
                {
                    PieceData piece = LogicalBoard[x, y];
                    if (piece != null && piece.Type == ChessPieceType.King && piece.Team == team)
                    {
                        return piece;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Gets all pieces belonging to the specified team.
        /// </summary>
        public List<PieceData> GetAllPieces(Team team)
        {
            List<PieceData> pieces = new List<PieceData>();
            for (int x = 0; x < TileCountX; x++)
            {
                for (int y = 0; y < TileCountY; y++)
                {
                    PieceData piece = LogicalBoard[x, y];
                    if (piece != null && piece.Team == team)
                    {
                        pieces.Add(piece);
                    }
                }
            }
            return pieces;
        }

        /// <summary>
        /// Validates if the given coordinates are within board bounds.
        /// </summary>
        public bool IsValidIndex(int x, int y)
        {
            return x >= 0 && x < TileCountX && y >= 0 && y < TileCountY;
        }

        /// <summary>
        /// Overload: Validate Vector2Int position.
        /// </summary>
        public bool IsValidIndex(Vector2Int pos)
        {
            return IsValidIndex(pos.x, pos.y);
        }

        /// <summary>
        /// Switches to the next player's turn.
        /// </summary>
        public void NextTurn()
        {
            CurrentTurn = CurrentTurn == Team.White ? Team.Black : Team.White;
        }

        /// <summary>
        /// Records a move to history (used for en passant detection, etc).
        /// </summary>
        public void RecordMove(Vector2Int from, Vector2Int to)
        {
            MoveHistory.Add(from);
            MoveHistory.Add(to);
        }

        /// <summary>
        /// Gets the last move made (returns start and end positions).
        /// Returns null if no moves have been made.
        /// </summary>
        public (Vector2Int start, Vector2Int end)? GetLastMove()
        {
            if (MoveHistory.Count < 2)
            {
                return null;
            }

            int lastIndex = MoveHistory.Count - 1;
            return (MoveHistory[lastIndex - 1], MoveHistory[lastIndex]);
        }

        /// <summary>
        /// Creates a completely independent deep copy of the entire board state.
        /// Critical for AI simulation and "what-if" move validation (check prevention).
        /// This is expensive - use sparingly and only when needed.
        /// </summary>
        public BoardState Clone()
        {
            BoardState clone = new BoardState(TileCountX, TileCountY);
            clone.CurrentTurn = this.CurrentTurn;
            clone.IsGameOver = this.IsGameOver;
            clone.Winner = this.Winner;

            // Clone the board
            for (int x = 0; x < TileCountX; x++)
            {
                for (int y = 0; y < TileCountY; y++)
                {
                    if (this.LogicalBoard[x, y] != null)
                    {
                        clone.LogicalBoard[x, y] = this.LogicalBoard[x, y].Clone();
                    }
                }
            }

            // Clone captured pieces
            foreach (var piece in WhiteCaptured)
            {
                clone.WhiteCaptured.Add(piece.Clone());
            }
            foreach (var piece in BlackCaptured)
            {
                clone.BlackCaptured.Add(piece.Clone());
            }

            // Clone move history
            foreach (var move in MoveHistory)
            {
                clone.MoveHistory.Add(move);
            }

            return clone;
        }
    }
}