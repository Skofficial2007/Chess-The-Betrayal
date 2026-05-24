using System.Collections.Generic;
using ChessTheMasterPiece.Data;
using ChessTheMasterPiece.Logic.Movement;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ChessTheMasterPiece.Tests.EditMode")]

namespace ChessTheMasterPiece.Logic
{
    /// <summary>
    /// The rules referee. Handles move generation, check detection, and figuring out when the game is over.
    /// Nothing in here touches Unity — it's pure chess logic that can run on any thread.
    /// </summary>
    public static class ChessEngine
    {
        /// <summary>
        /// The theoretical maximum number of legal moves available to a single player in any valid chess position.
        /// </summary>
        public const int MaxMovesPerPosition = 218;

        // Each thread gets its own private buffers so parallel AI searches don't step on each other.
        [System.ThreadStatic]
        private static List<MoveCommand> _attackCheckBuffer;
        private static List<MoveCommand> AttackCheckBuffer => _attackCheckBuffer ??= new List<MoveCommand>(64);

        [System.ThreadStatic]
        private static List<MoveCommand> _moveGenBuffer;
        private static List<MoveCommand> MoveGenBuffer => _moveGenBuffer ??= new List<MoveCommand>(64);

        #region Legal Move Generation

        /// <summary>
        /// Fills <paramref name="output"/> with every legal move the piece at <paramref name="position"/> can make.
        /// Illegal moves (ones that leave your own King in check) are filtered out before returning.
        /// Uses board.CurrentTurn to determine which team's moves to generate.
        /// </summary>
        public static void GetLegalMoves(BoardState board, Vector2Int position, List<MoveCommand> output)
        {
            GetLegalMoves(board, position, output, board.CurrentTurn);
        }

        /// <summary>
        /// Fills <paramref name="output"/> with every legal move the piece at <paramref name="position"/> can make.
        /// Illegal moves (ones that leave your own King in check) are filtered out before returning.
        /// This overload allows specifying the team explicitly, which is essential for AI move generation
        /// where board.CurrentTurn may not match the team being evaluated.
        /// </summary>
        /// <param name="team">The team whose moves should be generated. Pieces of other teams are ignored.</param>
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

            // Step 1: Ask the piece for every square it could physically reach, ignoring check rules.
            strategy.GetRawMoves(board, piece, position, output);

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
        /// This method works correctly regardless of board.CurrentTurn, making it safe for AI evaluation.
        /// </summary>
        public static void GetAllLegalMoves(BoardState board, Team team, List<MoveCommand> masterBuffer)
        {
            masterBuffer.Clear();

            if (masterBuffer.Capacity < MaxMovesPerPosition)
            {
                masterBuffer.Capacity = MaxMovesPerPosition;
            }

            // Take a snapshot to avoid collection-modified-during-iteration.
            // GetLegalMoves internally calls DoesMoveLeaveKingInCheck, which modifies indices via SetPiece.
            int[] indicesSnapshot = board.GetPieceIndices(team).ToArray();

            for (int i = 0; i < indicesSnapshot.Length; i++)
            {
                int idx = indicesSnapshot[i];
                Vector2Int pos = new Vector2Int(idx % board.TileCountX, idx / board.TileCountX);

                GetLegalMoves(board, pos, MoveGenBuffer, team);
                masterBuffer.AddRange(MoveGenBuffer);
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

        /// <summary>
        /// Returns true if any piece on <paramref name="attackerTeam"/> can reach <paramref name="targetSquare"/>.
        /// Uses raw moves (not legal moves) to avoid infinite recursion with check detection.
        /// </summary>
        /// <remarks>
        /// PERF-003: Known O(N×M) hotspot. Current implementation generates full move lists for each attacker
        /// and scans for a matching destination. A Rook might generate 14 moves to check one square.
        /// 
        /// OPTIMIZATION PATH (pre-AI sprint):
        /// Replace with "attack from target" pattern (super-piece detection):
        /// 1. Place imaginary sliding piece at targetSquare
        /// 2. Cast rays in 8 directions until hitting piece or edge
        /// 3. Check if piece found matches the ray type (Rook for orthogonal, Bishop for diagonal, Queen for both)
        /// 4. Check knight offsets from target for Knights
        /// 5. Check pawn capture diagonals from target for Pawns
        /// 6. Check king radius from target for Kings
        /// 
        /// This reduces from O(N pieces × M moves/piece) to O(8 rays + 8 knight checks + 2 pawn checks) = O(1) relative to piece count.
        /// Critical for alpha-beta AI where this is called millions of times per search.
        /// </remarks>
        public static bool IsSquareUnderAttack(BoardState board, Vector2Int targetSquare, Team attackerTeam)
        {
            // Note: GetRawMoves doesn't trigger make/unmake, so no snapshot needed here.
            // But taking snapshot anyway for consistency and future-proofing.
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
                // PERF-003 HOTSPOT: Generates all moves for this piece even though we only check one destination.
                // This is the O(M) factor in O(N×M). Replace with targeted ray-casting for 10-100× speedup.
                strategy.GetRawMoves(board, attacker, attackerPos, AttackCheckBuffer);

                for (int j = 0; j < AttackCheckBuffer.Count; j++)
                {
                    if (AttackCheckBuffer[j].EndPosition == targetSquare)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true if the given team's King is currently in check.
        /// </summary>
        public static bool IsKingInCheck(BoardState board, Team team)
        {
            if (!board.TryFindKing(team, out Vector2Int kingPos))
                return false;

            Team enemyTeam = team == Team.White ? Team.Black : Team.White;

            return IsSquareUnderAttack(board, kingPos, enemyTeam);
        }

        #endregion

        #region Game State Evaluation

        /// <summary>
        /// Returns true if the given team has at least one legal move available.
        /// Optimized to use O(N) piece list instead of O(64) board scan.
        /// This method works correctly regardless of board.CurrentTurn, making it safe for AI evaluation.
        /// </summary>
        public static bool HasAnyLegalMoves(BoardState board, Team team)
        {
            // Take a snapshot to avoid collection-modified-during-iteration.
            // GetLegalMoves internally calls DoesMoveLeaveKingInCheck, which modifies indices via SetPiece.
            int[] indicesSnapshot = board.GetPieceIndices(team).ToArray();
            
            for (int i = 0; i < indicesSnapshot.Length; i++)
            {
                int idx = indicesSnapshot[i];
                Vector2Int pos = new Vector2Int(idx % board.TileCountX, idx / board.TileCountX);

                // We only need to find ONE legal move to prove the game isn't over.
                GetLegalMoves(board, pos, MoveGenBuffer, team);
                if (MoveGenBuffer.Count > 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Checks the board and tells you whether the given team is in checkmate, stalemate, check, or playing normally.
        /// </summary>
        public static GameState EvaluateGameState(BoardState board, Team team)
        {
            bool hasLegalMoves = HasAnyLegalMoves(board, team);

            if (hasLegalMoves) // Removed the '!' - this block runs if the game is still going
            {
                // IsKingInCheck is used to satisfy the GameState return type for the GameManager.
                return IsKingInCheck(board, team) ? GameState.Check : GameState.Normal;
            }

            // TODO (Betrayal): Add GameState.BetrayalInitiated and GameState.RetributionFailed here when
            // the custom mechanic is implemented. GameManager's CheckForGameEnd() will handle the transitions.

            // No legal moves: disambiguate checkmate vs stalemate
            return IsKingInCheck(board, team) ? GameState.Checkmate : GameState.Stalemate;
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
                PieceData pieceOnBoard = board.GetPiece(move.EndPosition);

                if (!pieceOnBoard.IsEmpty)
                {
                    board.SetPiece(pieceOnBoard.WithType(move.PromotedTo), move.EndPosition.x, move.EndPosition.y);
                }
                else
                {
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
        /// <remarks>
        /// Exposed via [InternalsVisibleTo] — remains encapsulated from external production assemblies.
        /// </remarks>
        internal static void UndoMoveOnBoard(BoardState board, MoveCommand move, bool recordHistory = true)
        {
            int currentCastlingMask = board.CastlingRights;
            int? currentEnPassantFile = board.EnPassantFile;

            // Restore the castling and en passant state that was recorded on the move itself.
            board.CastlingRights = move.PreviousCastlingMask;
            board.EnPassantFile = move.PreviousEnPassantFile;

            // Reverse the hash — XOR is self-inverse, so calling this again undoes it perfectly.
            ApplyZobristMove(board, move, currentCastlingMask, currentEnPassantFile);

            if (recordHistory)
            {
                // Each move records two history entries (start and end), so we remove both.
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

            // TODO: Graveyard-as-stack pattern is fragile for non-make/unmake paths.
            // Current assumption: WhiteCaptured/BlackCaptured operates as a strict LIFO stack where every
            // ApplyMoveToBoard push has a corresponding UndoMoveOnBoard pop. This works for paired make/unmake
            // in AI search but breaks if any code path calls RemovePiece or SetPiece(Empty) outside of
            // ApplyMoveToBoard (e.g., BoardState.Clear, future Betrayal mechanic "piece changes sides").
            //
            // The fallback creates a synthetic PieceData with StartRow=0, HasMoved=false,
            // which is incorrect for promoted pieces or pieces that had already moved.
            //
            // BEFORE Betrayal implementation we need to: 
            // Replace graveyard pattern with explicit captured-piece storage in MoveCommand itself.
            // Add optional `PieceData? CapturedPieceFullState` field to preserve exact
            // piece state (including StartRow, HasMoved) without relying on graveyard stack coherence.
            // This enables Betrayal's "convert captured piece to ally" mechanic to access full piece history.
            if (move.HasCapture)
            {
                List<PieceData> graveyard = move.CapturedTeam == Team.White ? board.WhiteCaptured : board.BlackCaptured;
                PieceData resurrectedPiece;
                if (graveyard.Count > 0)
                {
                    resurrectedPiece = graveyard[graveyard.Count - 1];
                    graveyard.RemoveAt(graveyard.Count - 1);
                }
                else
                {
                    // DESIGN SMELL-002: Synthetic fallback — incorrect for pieces with non-default state
                    int dir = move.CapturedTeam == Team.White ? 1 : -1;
                    resurrectedPiece = new PieceData(move.CapturedTeam, move.CapturedType, dir, 0, false);
                }
                resurrectedPiece = resurrectedPiece.WithHasMoved(move.CapturedHadMoved);

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

            // Calculate White Material
            List<int> whiteIndices = board.GetPieceIndices(Team.White);

            for (int i = 0; i < whiteIndices.Count; i++)
            {
                int idx = whiteIndices[i];
                PieceData piece = board.GetPiece(idx % board.TileCountX, idx / board.TileCountX);
                whiteValue += GetPieceValue(piece.Type);
            }

            // Calculate Black Material
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