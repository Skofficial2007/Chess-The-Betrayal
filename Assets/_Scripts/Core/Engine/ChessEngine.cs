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
        // Safe default: NullDomainLogger silently discards events, preserving testability.
        private static IDomainLogger _logger = NullDomainLogger.Instance;

        /// <summary>
        /// Binds the engine to a presentation-layer logger.
        /// Must be called by the game manager at startup.
        /// </summary>
        public static void Initialize(IDomainLogger logger)
        {
            _logger = logger ?? NullDomainLogger.Instance;
        }

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
        /// Also appends Betrayal targets if the global right is available.
        /// </summary>
        public static void GetLegalMoves(BoardState board, Vector2Int position, List<MoveCommand> output)
        {
            GetLegalMoves(board, position, output, board.CurrentTurn);

            // Append friendly-fire targets (Phase 1)
            // GetBetrayalTargets already internally checks board.BetrayalRightAvailable.
            GetBetrayalTargets(board, position, output);
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

            // If the board is mid-sequence (Retribution Phase), normal rules are suspended.
            // We deduce Phase 2 vs Phase 3 by checking if the betrayer has flipped teams yet.
            if (board.PendingBetrayerSquare.HasValue && board.BetrayalInitiator.HasValue)
            {
                PieceData betrayer = board.GetPiece(board.PendingBetrayerSquare.Value);

                // If the betrayer is still on the initiator's team, we are in Phase 2 (Retribution).
                // If it has changed teams, we are in Phase 3 (Forced Save) and MUST use normal move generation to find escapes.
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

        /// <summary>
        /// Generates legal "friendly-fire" moves for the Betrayal Act (Phase 1).
        /// Reuses standard movement geometry but targets friendly squares instead of enemy/empty squares.
        /// </summary>
        public static void GetBetrayalTargets(BoardState board, Vector2Int betrayerPos, List<MoveCommand> output)
        {
            PieceData piece = board.GetPiece(betrayerPos);

            if (piece.Type == ChessPieceType.King || !board.BetrayalRightAvailable || board.CurrentTurn != piece.Team) return;

            IPieceMovement strategy = MovementFactory.GetStrategy(piece.Type);
            if (strategy == null) return;

            Team enemyTeam = piece.Team == Team.White ? Team.Black : Team.White;
            int[] friendlyIndices = board.GetPieceIndices(piece.Team).ToArray();

            // Isolate buffer to prevent InvalidOperationException from nested buffer clearing in IsSquareUnderAttack
            List<MoveCommand> localBuffer = new List<MoveCommand>(32);

            for (int i = 0; i < friendlyIndices.Length; i++)
            {
                int idx = friendlyIndices[i];
                Vector2Int candidateTargetPos = new Vector2Int(idx % board.TileCountX, idx / board.TileCountX);
                PieceData candidateVictim = board.GetPiece(candidateTargetPos);

                if (candidateVictim.Type == ChessPieceType.King || candidateTargetPos == betrayerPos) continue;

                // --- DISGUISE TRICK ---
                // We MUST use SetPiece so the engine's internal occupancy arrays (_whiteOccupied, etc)
                // update. Otherwise, sliding pieces (Queens/Bishops) will think they are still blocked.
                board.SetPiece(candidateVictim.WithTeam(enemyTeam), candidateTargetPos.x, candidateTargetPos.y);

                localBuffer.Clear();
                strategy.GetRawMoves(board, piece, betrayerPos, localBuffer);

                board.SetPiece(candidateVictim, candidateTargetPos.x, candidateTargetPos.y);
                // ----------------------

                // Copy to array to completely decouple from any list enumeration exceptions
                MoveCommand[] rawMoves = localBuffer.ToArray();

                for (int j = 0; j < rawMoves.Length; j++)
                {
                    if (rawMoves[j].EndPosition == candidateTargetPos)
                    {
                        // DESIGN RULE: Promotion is suppressed for Betrayal Acts regardless of pawn position.
                        // A pawn that betrays a friendly piece on the back rank does not receive promotion —
                        // the piece becomes the Betrayer and the game enters RetributionPending immediately.
                        // Always use CreateStandardMove() here, never CreatePromotionMove().
                        MoveCommand actMove = MoveCommand.CreateStandardMove(betrayerPos, candidateTargetPos, piece, candidateVictim, board)
                                                         .WithStage(BetrayalStage.Act);

                        if (!DoesMoveLeaveKingInCheck(board, actMove))
                        {
                            output.Add(actMove);
                        }

                        // Prevent 4 duplicate candidate generation if rawMoves contained 4 promotion variants
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Generates legal moves for the Retribution sub-phase (Phase 2).
        /// Only moves that capture the Betrayer are permitted. 
        /// Because it reuses GetLegalMoves internally, the Pinned Executioner rule is enforced automatically.
        /// </summary>
        public static void GetRetributionMoves(BoardState board, Team executionerTeam, Vector2Int betrayerSquare, List<MoveCommand> output)
        {
            output.Clear();
            PieceData betrayer = board.GetPiece(betrayerSquare);
            if (betrayer.IsEmpty) return;

            Team enemyTeam = executionerTeam == Team.White ? Team.Black : Team.White;
            int[] friendlyIndices = board.GetPieceIndices(executionerTeam).ToArray();

            // Isolate buffer to prevent InvalidOperationException
            List<MoveCommand> localBuffer = new List<MoveCommand>(32);

            for (int i = 0; i < friendlyIndices.Length; i++)
            {
                int idx = friendlyIndices[i];
                Vector2Int pos = new Vector2Int(idx % board.TileCountX, idx / board.TileCountX);

                // CRITICAL FIX: A piece cannot execute itself! This prevents SetPiece from 
                // scrambling the indices array during the disguise trick.
                if (pos == betrayerSquare) continue;

                PieceData executioner = board.GetPiece(pos);

                IPieceMovement strategy = MovementFactory.GetStrategy(executioner.Type);
                if (strategy == null) continue;

                // --- DISGUISE TRICK ---
                board.SetPiece(betrayer.WithTeam(enemyTeam), betrayerSquare.x, betrayerSquare.y);

                localBuffer.Clear();
                strategy.GetRawMoves(board, executioner, pos, localBuffer);

                board.SetPiece(betrayer, betrayerSquare.x, betrayerSquare.y);
                // ----------------------

                MoveCommand[] rawMoves = localBuffer.ToArray();

                for (int j = 0; j < rawMoves.Length; j++)
                {
                    if (rawMoves[j].EndPosition == betrayerSquare)
                    {
                        // DESIGN RULE: Castling cannot be used to execute the Betrayer during Retribution.
                        // This is enforced automatically because castling always lands the King on g1/c1 (White)
                        // or g8/c8 (Black), never on the Betrayer's arbitrary square. The EndPosition == betrayerSquare
                        // filter below therefore naturally excludes all castling moves without any special-case code.
                        MoveCommand retMove = new MoveCommand(
                            pos, betrayerSquare, executioner, betrayer,
                            rawMoves[j].SpecialMoveType, rawMoves[j].PromotedTo,
                            null, null, null,
                            board.CastlingRights, board.EnPassantFile,
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

        /// <summary>
        /// Generates legal moves for a Forced Save phase (Betrayal Phase 3 Defensive Override).
        /// Reuses GetAllLegalMoves, which inherently filters out any move that does not resolve the check.
        /// </summary>
        public static void GetForcedSaveMoves(BoardState board, Team team, List<MoveCommand> output)
        {
            output.Clear();

            // GetAllLegalMoves on a board where 'team' is in check *only* returns 
            // moves that resolve that check (moving King, blocking, or capturing).
            GetAllLegalMoves(board, team, output);

            // Tag all resulting moves with DefensiveSave stage
            for (int i = 0; i < output.Count; i++)
            {
                output[i] = output[i].WithStage(BetrayalStage.DefensiveSave);
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
        /// Checks the board and tells you whether the given team is in checkmate, stalemate, check, timeout, or playing normally.
        /// </summary>
        public static GameState EvaluateGameState(BoardState board, Team team, ClockState? clock = null)
        {
            if (clock.HasValue && clock.Value.IsExpired && clock.Value.ActiveSide == team)
            {
                return GameState.Timeout;
            }

            // The game cannot end in standard checkmate/stalemate during Phase 2.
            // A lack of retribution moves means Defection, not Game Over. 
            if (board.PendingBetrayerSquare.HasValue && board.BetrayalInitiator.HasValue)
            {
                PieceData betrayer = board.GetPiece(board.PendingBetrayerSquare.Value);
                if (betrayer.Team == board.BetrayalInitiator.Value)
                {
                    return GameState.Normal;
                }
            }

            bool hasLegalMoves = HasAnyLegalMoves(board, team);

            if (hasLegalMoves) // This block runs if the game is still going
            {
                // IsKingInCheck is used to satisfy the GameState return type for the GameManager.
                return IsKingInCheck(board, team) ? GameState.Check : GameState.Normal;
            }

            // No legal moves: disambiguate checkmate vs stalemate
            return IsKingInCheck(board, team) ? GameState.Checkmate : GameState.Stalemate;
        }

        #endregion

        #region Move Execution

        /// <summary>
        /// Resolves a failed Retribution phase by defecting the Betrayer and evaluating Edge Case B (Self-Check).
        /// </summary>
        public static DefectionOutcome ResolveFailedRetribution(BoardState board)
        {
            Vector2Int betrayerSquare = board.PendingBetrayerSquare.Value;
            Team initiator = board.BetrayalInitiator.Value;

            board.DefectPiece(betrayerSquare);

            bool selfCheckAfterDefection = IsKingInCheck(board, initiator);
            return new DefectionOutcome(selfCheckAfterDefection, betrayerSquare);
        }

        /// <summary>
        /// Symmetrical inverse of DefectPiece, used exclusively by the AI search tree to Unmake a defection sequence.
        /// </summary>
        internal static void UndoDefection(BoardState board, Vector2Int square, Team originalTeam)
        {
            PieceData current = board.GetPiece(square);
            board.TogglePieceHash(current.Team, current.Type, square.x, square.y);

            PieceData restored = current.WithTeam(originalTeam);
            board.SetPiece(restored, square.x, square.y);

            board.TogglePieceHash(restored.Team, restored.Type, square.x, square.y);
        }

        /// <summary>
        /// Updates the Zobrist hash to reflect a move. Because XOR is self-cancelling, calling this
        /// on the same move twice perfectly undoes the change — which is how move undo works.
        /// </summary>
        private static void ApplyZobristMove(BoardState board, MoveCommand move, int previousCastlingMask, int? previousEnPassantFile)
        {
            // 1. Toggle whose turn it is.
            // Only toggle the turn hash if this move actually hands over the turn.
            // The Act phase is a mid-turn sequence, so we bypass the turn toggle.
            if (move.Stage != BetrayalStage.Act)
            {
                board.ToggleTurnHash();
            }

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
                    _logger.LogError(new DomainLogEvent(
                        DomainEventCode.Engine_PromotionPieceNotFound,
                        auxInt: move.EndPosition.y * board.TileCountX + move.EndPosition.x));
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

            if (move.HasCapture)
            {
                // We MUST maintain the visual graveyard arrays for the UI (so BoardVisuals can accurately count captured pieces),
                // but we no longer pop from them to recreate the piece. We use the immutable snapshot.
                List<PieceData> graveyard = move.CapturedTeam == Team.White ? board.WhiteCaptured : board.BlackCaptured;
                if (graveyard.Count > 0)
                {
                    graveyard.RemoveAt(graveyard.Count - 1);
                }

                // Resurrect the exact piece using the immutable snapshot captured at the moment of the move.
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

    /// <summary>
    /// What state is the game in right now, from a specific team's point of view?
    /// </summary>
    public enum GameState
    {
        Normal,     // Nothing special — game continues.
        Check,      // King is in check but has at least one escape.
        Checkmate,  // King is in check with no legal moves — game over.
        Stalemate,  // No legal moves, but not in check — draw.
        Timeout     // A player's clock reached zero.
    }
}