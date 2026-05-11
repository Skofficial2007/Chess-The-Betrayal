using System;
using System.Collections.Generic;

namespace ChessTheMasterPiece.Data
{
    /// <summary>
    /// The complete snapshot of a chess game at any moment — the board, whose turn it is, captured pieces, and move history.
    /// No Unity code lives here, so it can be safely used in background threads, saved to disk, or sent over the network.
    /// </summary>
    public class BoardState
    {
        #region Zobrist Hashing
        // PieceKeys dimensions: Team x PieceType x Square. (2 x 7 x 64)
        private static readonly ulong[,,] PieceKeys;
        private static readonly ulong BlackToMoveKey;
        // 16 possible castling right combinations (4-bit mask)
        private static readonly ulong[] CastlingKeys;
        // 8 possible files for an En Passant target (X coordinates 0-7)
        private static readonly ulong[] EnPassantKeys;
        // Future Custom Mechanic Support
        private static readonly ulong BetrayalPhaseKey;

        /// <summary>
        /// Incrementally updated hash for transposition table lookups.
        /// </summary>
        public ulong ZobristHash { get; set; }

        /// <summary>
        /// 4-bit mask tracking available castling rights.
        /// Bit 0: White Kingside, Bit 1: White Queenside, Bit 2: Black Kingside, Bit 3: Black Queenside
        /// </summary>
        public int CastlingRights { get; set; }

        /// <summary>
        /// The file (X coordinate 0-7) where an en passant capture is currently legal.
        /// Null if no en passant is available this turn.
        /// </summary>
        public int? EnPassantFile { get; set; }

        // Castling mask bit constants for clarity
        public const int CastlingWhiteKingside = 1;   // Bit 0
        public const int CastlingWhiteQueenside = 2;  // Bit 1
        public const int CastlingBlackKingside = 4;   // Bit 2
        public const int CastlingBlackQueenside = 8;  // Bit 3
        public const int CastlingAllRights = 15;

        static BoardState()
        {
            // Fixed seed ensures reproducibility across network clients and AI threads
            var rng = new Random(42);

            ulong NextUlong()
            {
                byte[] buffer = new byte[8];
                rng.NextBytes(buffer);
                return BitConverter.ToUInt64(buffer, 0);
            }

            PieceKeys = new ulong[2, 7, 64];
            for (int t = 0; t < 2; t++)
            {
                for (int p = 0; p < 7; p++)
                {
                    for (int s = 0; s < 64; s++)
                    {
                        PieceKeys[t, p, s] = NextUlong();
                    }
                }
            }

            CastlingKeys = new ulong[16];
            for (int i = 0; i < 16; i++)
            {
                CastlingKeys[i] = NextUlong();
            }

            EnPassantKeys = new ulong[8];
            for (int i = 0; i < 8; i++)
            {
                EnPassantKeys[i] = NextUlong();
            }

            BlackToMoveKey = NextUlong();
            // TODO (Betrayal): Toggle this in the hash whenever the BetrayalRight is consumed for the match.
            BetrayalPhaseKey = NextUlong();
        }

        /// <summary>
        /// Flips a piece in or out of the hash at a given square. Calling it twice on the same piece cancels out, which is what makes move undo possible.
        /// </summary>
        public void TogglePieceHash(Team team, ChessPieceType type, int x, int y)
        {
            if (type == ChessPieceType.None) return;

            int squareIndex = (y * TileCountX) + x;
            ZobristHash ^= PieceKeys[(int)team, (int)type, squareIndex];
        }

        /// <summary>
        /// Flips whose turn it is in the hash.
        /// </summary>
        public void ToggleTurnHash()
        {
            ZobristHash ^= BlackToMoveKey;
        }

        /// <summary>
        /// XORs a castling rights mask into or out of the hash.
        /// </summary>
        public void ToggleCastlingHash(int castlingMask)
        {
            ZobristHash ^= CastlingKeys[castlingMask];
        }

        /// <summary>
        /// XORs an en passant file into or out of the hash.
        /// </summary>
        public void ToggleEnPassantHash(int xCoordinate)
        {
            if (xCoordinate >= 0 && xCoordinate < 8)
            {
                ZobristHash ^= EnPassantKeys[xCoordinate];
            }
        }

        /// <summary>
        /// XORs the Betrayal phase flag.
        /// </summary>
        public void ToggleBetrayalHash()
        {
            ZobristHash ^= BetrayalPhaseKey;
        }

        /// <summary>
        /// Builds the hash from scratch by reading every piece on the board. Call this once at game start, then keep it up to date incrementally as moves happen.
        /// </summary>
        public void ComputeFullZobristHash()
        {
            ZobristHash = 0;
            for (int x = 0; x < TileCountX; x++)
            {
                for (int y = 0; y < TileCountY; y++)
                {
                    PieceData piece = LogicalBoard[x, y];
                    if (!piece.IsEmpty)
                    {
                        TogglePieceHash(piece.Team, piece.Type, x, y);
                    }
                }
            }
            if (CurrentTurn == Team.Black)
            {
                ToggleTurnHash();
            }
            // Include castling rights in the hash
            ToggleCastlingHash(CastlingRights);
            // Include en passant file in the hash (if any)
            if (EnPassantFile.HasValue)
            {
                ToggleEnPassantHash(EnPassantFile.Value);
            }
        }

        #endregion

        // The mathematical grid - holds pure data, not GameObjects
        public PieceData[,] LogicalBoard { get; private set; }

        private readonly List<int> _whitePieceIndices = new List<int>(16);
        private readonly List<int> _blackPieceIndices = new List<int>(16);

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
            if (!IsValidIndex(x, y)) return;

            PieceData existing = LogicalBoard[x, y];

            if (!existing.IsEmpty)
            {
                if (existing.Team == Team.White) 
                {
                    _whitePieceIndices.Remove(y * TileCountX + x);
                }
                else
                {
                    _blackPieceIndices.Remove(y * TileCountX + x);
                }
            }

            LogicalBoard[x, y] = piece;

            if (!piece.IsEmpty)
            {
                if (piece.Team == Team.White)
                {
                    _whitePieceIndices.Add(y * TileCountX + x);
                }
                else
                {
                    _blackPieceIndices.Add(y * TileCountX + x);
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
            return PieceData.Empty;
        }

        /// <summary>
        /// Overload: Get piece using Vector2Int.
        /// </summary>
        public PieceData GetPiece(Vector2Int pos)
        {
            return GetPiece(pos.x, pos.y);
        }

        /// <summary>
        /// Moves a piece and returns whatever was captured, if anything.
        /// </summary>
        public PieceData MovePiece(int fromX, int fromY, int toX, int toY)
        {
            if (!IsValidIndex(fromX, fromY) || !IsValidIndex(toX, toY))
            {
                return PieceData.Empty;
            }

            PieceData movingPiece = LogicalBoard[fromX, fromY];
            if (movingPiece.IsEmpty)
            {
                return PieceData.Empty;
            }

            PieceData capturedPiece = LogicalBoard[toX, toY];

            SetPiece(PieceData.Empty, fromX, fromY);
            SetPiece(movingPiece.WithMoved(), toX, toY);

            if (!capturedPiece.IsEmpty)
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
        /// Removes a piece from the board (used for en passant captures, where the taken pawn isn't on the landing square).
        /// </summary>
        public PieceData RemovePiece(int x, int y)
        {
            if (!IsValidIndex(x, y))
            {
                return PieceData.Empty;
            }

            PieceData piece = LogicalBoard[x, y];
            SetPiece(PieceData.Empty, x, y);

            if (!piece.IsEmpty)
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
                    LogicalBoard[x, y] = PieceData.Empty;
                }
            }
            WhiteCaptured.Clear();
            BlackCaptured.Clear();
            MoveHistory.Clear();
            _whitePieceIndices.Clear();
            _blackPieceIndices.Clear();
            IsGameOver = false;
            Winner = null;
            CurrentTurn = Team.White;
            ZobristHash = 0;
            CastlingRights = CastlingAllRights; // Reset to all castling available
            EnPassantFile = null;
        }

        public bool TryFindKing(Team team, out Vector2Int kingPos)
        {
            List<int> indices = GetPieceIndices(team);
            for (int i = 0; i < indices.Count; i++)
            {
                int idx = indices[i];
                int x = idx % TileCountX;
                int y = idx / TileCountX;
                PieceData piece = LogicalBoard[x, y];
                if (piece.Type == ChessPieceType.King)
                {
                    kingPos = new Vector2Int(x, y);
                    return true;
                }
            }
            kingPos = Vector2Int.Invalid;
            return false;
        }

        public PieceData FindKing(Team team)
        {
            List<int> indices = GetPieceIndices(team);
            for (int i = 0; i < indices.Count; i++)
            {
                int idx = indices[i];
                int x = idx % TileCountX;
                int y = idx / TileCountX;
                PieceData piece = LogicalBoard[x, y];
                if (piece.Type == ChessPieceType.King)
                {
                    return piece;
                }
            }
            return PieceData.Empty;
        }

        public Vector2Int FindKingPosition(Team team)
        {
            List<int> indices = GetPieceIndices(team);
            for (int i = 0; i < indices.Count; i++)
            {
                int idx = indices[i];
                int x = idx % TileCountX;
                int y = idx / TileCountX;
                PieceData piece = LogicalBoard[x, y];
                if (piece.Type == ChessPieceType.King)
                {
                    return new Vector2Int(x, y);
                }
            }
            return Vector2Int.Invalid;
        }

        /// <summary>
        /// Populates the provided list with all pieces belonging to the specified team.
        /// </summary>
        public void GetAllPieces(Team team, List<PieceData> output)
        {
            output.Clear();
            for (int x = 0; x < TileCountX; x++)
            {
                for (int y = 0; y < TileCountY; y++)
                {
                    PieceData piece = LogicalBoard[x, y];
                    if (!piece.IsEmpty && piece.Team == team)
                    {
                        output.Add(piece);
                    }
                }
            }
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
        /// Creates a full copy of this board state. Mainly used for saving/loading and network sync — the AI uses make/unmake instead, which is much faster.
        /// </summary>
        public BoardState Clone()
        {
            BoardState clone = new BoardState(TileCountX, TileCountY);
            clone.CurrentTurn = this.CurrentTurn;
            clone.IsGameOver = this.IsGameOver;
            clone.Winner = this.Winner;
            clone.ZobristHash = this.ZobristHash;
            clone.CastlingRights = this.CastlingRights;
            clone.EnPassantFile = this.EnPassantFile;

            Array.Copy(this.LogicalBoard, clone.LogicalBoard, this.LogicalBoard.Length);

            foreach (var piece in WhiteCaptured)
            {
                clone.WhiteCaptured.Add(piece);
            }
            foreach (var piece in BlackCaptured)
            {
                clone.BlackCaptured.Add(piece);
            }

            foreach (var move in MoveHistory)
            {
                clone.MoveHistory.Add(move);
            }

            return clone;
        }

        public List<int> GetPieceIndices(Team team) =>
            team == Team.White ? _whitePieceIndices : _blackPieceIndices;
    }
}