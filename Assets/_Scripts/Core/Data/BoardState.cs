using System;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Diagnostics;

namespace ChessTheBetrayal.Core.Data
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
        // Betrayal sub-state: which square holds the pending Betrayer, and which side initiated.
        // Without these, two mid-sequence positions that are identical in piece placement but
        // differ in which piece is the pending Betrayer would hash identically — transposition-table
        // poisoning during exactly the high-branching Act/Retribution sub-phase.
        private static readonly ulong[] PendingBetrayerSquareKeys; // [64]
        private static readonly ulong BetrayalInitiatorIsBlackKey;

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
            BetrayalPhaseKey = NextUlong();

            PendingBetrayerSquareKeys = new ulong[64];
            for (int i = 0; i < 64; i++)
            {
                PendingBetrayerSquareKeys[i] = NextUlong();
            }
            BetrayalInitiatorIsBlackKey = NextUlong();
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
        /// XORs the pending-Betrayer square and initiator side into or out of the hash.
        /// Call once when a Retribution sequence opens (Act) and again with the same
        /// square/initiator when it closes (Retribution/DefensiveSave) — Defection deliberately
        /// leaves the sub-state (and this hash contribution) untouched, since PendingBetrayerSquare
        /// and BetrayalInitiator stay set until the terminal move.
        /// </summary>
        public void ToggleBetrayalSubStateHash(Vector2Int square, Team initiator)
        {
            int squareIndex = (square.y * TileCountX) + square.x;
            ZobristHash ^= PendingBetrayerSquareKeys[squareIndex];

            if (initiator == Team.Black)
            {
                ZobristHash ^= BetrayalInitiatorIsBlackKey;
            }
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

            // Include Betrayal phase flag
            if (!BetrayalRightAvailable)
            {
                ToggleBetrayalHash();
            }

            // Include the pending Betrayer's square + initiator, if a Retribution sequence is open
            // (covers Act through Defection — cleared only once Retribution/DefensiveSave resolves it).
            if (PendingBetrayerSquare.HasValue && BetrayalInitiator.HasValue)
            {
                ToggleBetrayalSubStateHash(PendingBetrayerSquare.Value, BetrayalInitiator.Value);
            }
        }

        /// <summary>
        /// DEBUG ONLY: Validates that the incremental hash matches a full recomputation.
        /// Call this after every move during testing to catch hash drift bugs immediately.
        /// Throws a DomainException if the hashes don't match, indicating a Make/Unmake bug.
        /// </summary>
        public void AssertZobristConsistency()
        {
            ulong savedHash = ZobristHash;
            ComputeFullZobristHash();
            ulong recomputedHash = ZobristHash;
            
            // RESTORE — non-negotiable invariant. The hash must be perfectly reverted
            // after the assertion check to prevent silent transposition table corruption.
            ZobristHash = savedHash;

            if (savedHash != recomputedHash)
            {
                throw new DomainException(
                    DomainEventCode.Board_ZobristDesync,
                    $"Incremental=0x{savedHash:X16} Full=0x{recomputedHash:X16}. " +
                    "A Make/Unmake operation failed to XOR the correct hash key.");
            }
        }

        #endregion

        // The mathematical grid - holds pure data, not GameObjects
        public PieceData[,] LogicalBoard { get; private set; }

        // For faster piece lookups, we maintain separate lists of occupied squares for each team.
        // The boolean arrays track occupancy for quick checks,
        // while the index lists allow iteration over pieces without scanning the whole board.
        // Indices are maintained incrementally in SetPiece for optimal AI search performance.
        private readonly bool[] _whiteOccupied = new bool[64];
        private readonly bool[] _blackOccupied = new bool[64];
        private readonly List<int> _whitePieceIndices = new List<int>(16);
        private readonly List<int> _blackPieceIndices = new List<int>(16);

        // -1 indicates king not yet placed or has been captured (illegal state).
        // Maintained incrementally in SetPiece alongside piece indices.
        private int _whiteKingSquare = -1;
        private int _blackKingSquare = -1;

        public Team CurrentTurn { get; set; }
        public int TileCountX { get; private set; }
        public int TileCountY { get; private set; }

        // Move history: stores position pairs for move tracking
        public List<Vector2Int> MoveHistory { get; private set; }

        /// <summary>
        /// The full move number (e.g. Turn 1 covers both White's and Black's 1st moves).
        /// Single source of truth for turn-number derivation — used by move-executed events,
        /// PGN-style logging, and network replay.
        /// </summary>
        public int FullMoveNumber => MoveHistory.Count / 2;

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

        /// <summary>
        /// Global, shared, once-per-match resource for the Betrayal mechanic.
        /// Defaults to true and is permanently consumed the instant Phase 1 succeeds.
        /// </summary>
        public bool BetrayalRightAvailable { get; set; } = true;

        /// <summary>
        /// Tracks the square of the piece that initiated Betrayal, serving as the mandatory target for Phase 2.
        /// </summary>
        public Vector2Int? PendingBetrayerSquare { get; set; }

        /// <summary>
        /// Tracks which team initiated the Betrayal sequence, since CurrentTurn does not change mid-sequence.
        /// </summary>
        public Team? BetrayalInitiator { get; set; }

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
        /// Maintains piece indices incrementally for optimal AI search performance.
        /// </summary>
        public void SetPiece(PieceData piece, int x, int y)
        {
            if (!IsValidIndex(x, y)) return;

            PieceData existing = LogicalBoard[x, y];
            int sq = y * TileCountX + x;

            // Remove old piece from indices
            if (!existing.IsEmpty)
            {
                if (existing.Team == Team.White)
                {
                    _whiteOccupied[sq] = false;
                    _whitePieceIndices.Remove(sq); // O(N) where N ≤ 16
                }
                else
                {
                    _blackOccupied[sq] = false;
                    _blackPieceIndices.Remove(sq);
                }
            }

            LogicalBoard[x, y] = piece;

            // Clear king cache if king was removed from this square
            if (!existing.IsEmpty && existing.Type == ChessPieceType.King)
            {
                if (existing.Team == Team.White)
                {
                    _whiteKingSquare = -1;
                }
                else
                {
                    _blackKingSquare = -1;
                }
            }

            // Add new piece to indices
            if (!piece.IsEmpty)
            {
                if (piece.Team == Team.White)
                {
                    _whiteOccupied[sq] = true;
                    _whitePieceIndices.Add(sq); // O(1) amortized
                    // Update king cache
                    if (piece.Type == ChessPieceType.King)
                    {
                        _whiteKingSquare = sq;
                    }
                }
                else
                {
                    _blackOccupied[sq] = true;
                    _blackPieceIndices.Add(sq);
                    // Update king cache
                    if (piece.Type == ChessPieceType.King)
                    {
                        _blackKingSquare = sq;
                    }
                }
            }
        }

        /// <summary>
        /// Retrieves the piece at the specified position.
        /// Returns PieceData.Empty if the square is empty or out of bounds.
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
        /// Atomically flips a piece to the opposing team (Resolution B).
        /// Removes it from the old team's hash/indices and adds it to the new team's.
        /// </summary>
        public void DefectPiece(Vector2Int square)
        {
            PieceData piece = GetPiece(square);
            if (piece.IsEmpty) return;

            // Remove from old team's hash & indices, flip team, re-add to new team's hash & indices.
            TogglePieceHash(piece.Team, piece.Type, square.x, square.y);

            PieceData defected = piece.WithTeam(piece.Team == Team.White ? Team.Black : Team.White);
            SetPiece(defected, square.x, square.y);

            TogglePieceHash(defected.Team, defected.Type, square.x, square.y);
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

            for (int i = 0; i < 64; i++)
            {
                _whiteOccupied[i] = false;
                _blackOccupied[i] = false;
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

            // Clear king cache
            _whiteKingSquare = -1;
            _blackKingSquare = -1;

            // Clear Betrayal states
            BetrayalRightAvailable = true;
            PendingBetrayerSquare = null;
            BetrayalInitiator = null;
        }

        /// <summary>
        /// Fallback scan included for safety, though it should never trigger in production.
        /// </summary>  
        public bool TryFindKing(Team team, out Vector2Int kingPos)
        {
            int cachedSquare = team == Team.White ? _whiteKingSquare : _blackKingSquare;
            
            // Fast path: Use cached position
            if (cachedSquare >= 0)
            {
                int x = cachedSquare % TileCountX;
                int y = cachedSquare / TileCountX;
                PieceData piece = LogicalBoard[x, y];
                
                // Verify cache coherence (should always be true)
                if (piece.Type == ChessPieceType.King && piece.Team == team)
                {
                    kingPos = new Vector2Int(x, y);
                    return true;
                }
            }
            
            // Fallback: Full scan (cache miss or corruption - should never happen)
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
                    // Repair cache on fallback
                    if (team == Team.White)
                    {
                        _whiteKingSquare = idx;
                    }
                    else
                    {
                        _blackKingSquare = idx;
                    }
                    return true;
                }
            }
            
            kingPos = Vector2Int.Invalid;
            return false;
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
        /// Creates a full deep copy of this board state for save/load, UI, and network snapshots ONLY.
        /// NEVER call this from an AI search tree or move simulation — use the Make/Unmake architecture in ChessEngine instead to prevent catastrophic heap allocation.
        /// </summary>
        public BoardState CloneForSnapshot()
        {
            BoardState clone = new BoardState(TileCountX, TileCountY);
            clone.CurrentTurn = this.CurrentTurn;
            clone.IsGameOver = this.IsGameOver;
            clone.Winner = this.Winner;
            clone.ZobristHash = this.ZobristHash;
            clone.CastlingRights = this.CastlingRights;
            clone.EnPassantFile = this.EnPassantFile;

            // Betrayal States
            clone.BetrayalRightAvailable = this.BetrayalRightAvailable;
            clone.PendingBetrayerSquare = this.PendingBetrayerSquare;
            clone.BetrayalInitiator = this.BetrayalInitiator;

            Array.Copy(this.LogicalBoard, clone.LogicalBoard, this.LogicalBoard.Length);

            // Copy the occupancy arrays and piece index lists
            Array.Copy(this._whiteOccupied, clone._whiteOccupied, 64);
            Array.Copy(this._blackOccupied, clone._blackOccupied, 64);
            clone._whitePieceIndices.AddRange(this._whitePieceIndices);
            clone._blackPieceIndices.AddRange(this._blackPieceIndices);
            // PERF-002: Copy king cache
            clone._whiteKingSquare = this._whiteKingSquare;
            clone._blackKingSquare = this._blackKingSquare;

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

        /// <summary>
        /// Returns the list of occupied square indices for the specified team.
        /// Indices are maintained incrementally, so this is always O(1).
        /// </summary>
        public List<int> GetPieceIndices(Team team)
        {
            return team == Team.White ? _whitePieceIndices : _blackPieceIndices;
        }
    }
}