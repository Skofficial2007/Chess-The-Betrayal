using System.Collections.Generic;
using System.Threading;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.AI.Evaluation;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Tests.Utilities
{
    public enum ConversionVerdict
    {
        /// <summary>The goal was reached (mate delivered, or a queen appeared for the attacking
        /// side) within the ply budget.</summary>
        Converted,

        /// <summary>The ply budget ran out without the goal being reached — the attacker never
        /// finished the win, whether or not it was still making progress.</summary>
        Stalled,

        /// <summary>The position resolved to a draw (stalemate) before the goal was reached — the
        /// worst possible outcome for a position that started strictly won.</summary>
        Drawn,
    }

    /// <summary>One conversion position's full played-out result — enough to see not just pass/fail
    /// but whether the attacker was making real progress or shuffling in place.</summary>
    public sealed class ConversionResult
    {
        public readonly EndgameConversionPosition Position;
        public readonly ConversionVerdict Verdict;
        public readonly int PliesPlayed;
        public readonly IReadOnlyList<int> ProgressTrace;

        public ConversionResult(EndgameConversionPosition position, ConversionVerdict verdict, int pliesPlayed,
            IReadOnlyList<int> progressTrace)
        {
            Position = position;
            Verdict = verdict;
            PliesPlayed = pliesPlayed;
            ProgressTrace = progressTrace;
        }

        public string DescribeFailure() =>
            $"[{Position.Name}] verdict {Verdict} after {PliesPlayed} plies. " +
            $"Progress trace (lower is closer to the goal): {string.Join(",", ProgressTrace)}. {Position.Note}";
    }

    /// <summary>
    /// Plays a conversion position out move by move using the REAL search on both sides — the
    /// attacker at a named profile (impossible by default, matching every prior A/B's subject),
    /// the defender at a fast, weak profile (easy) so the lone king still resists sensibly without
    /// the probe spending its budget on the losing side. Uses IChessEngine.Advance (never the raw
    /// ApplyMove/UndoMove make-half) so CurrentTurn genuinely flips between calls — the same trap
    /// YardstickRunner's own callers had to learn.
    /// </summary>
    public static class EndgameConversionRunner
    {
        private const int DefaultPlyBudget = 80;

        public static ConversionResult Run(EndgameConversionPosition position, AIProfile attackerProfile,
            AIProfile defenderProfile, int plyBudget = DefaultPlyBudget)
        {
            BoardState board = position.BuildBoard();
            var engine = new ChessEngineAdapter();

            var progressTrace = new List<int>();
            int startingProgress = MeasureProgress(board, position);
            progressTrace.Add(startingProgress);

            for (int ply = 0; ply < plyBudget; ply++)
            {
                GameState state = engine.EvaluateGameState(board, board.CurrentTurn);
                if (state == GameState.Checkmate)
                {
                    // The side to move is mated — that's a win for whoever just moved, i.e. the
                    // side NOT to move now.
                    bool attackerWon = board.CurrentTurn != position.AttackingTeam;
                    return new ConversionResult(position,
                        attackerWon ? ConversionVerdict.Converted : ConversionVerdict.Drawn,
                        ply, progressTrace);
                }
                if (state == GameState.Stalemate)
                {
                    return new ConversionResult(position, ConversionVerdict.Drawn, ply, progressTrace);
                }

                if (position.Goal == ConversionGoal.PromoteThePawn && HasPromotedQueen(board, position.AttackingTeam))
                {
                    return new ConversionResult(position, ConversionVerdict.Converted, ply, progressTrace);
                }

                AIProfile toMoveProfile = board.CurrentTurn == position.AttackingTeam ? attackerProfile : defenderProfile;
                var search = new AlphaBetaSearch(engine, new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(toMoveProfile)));
                var settings = AISearchSettings.FromProfile(BetrayalUsage.Full, toMoveProfile);

                using var cts = new CancellationTokenSource();
                cts.CancelAfter(settings.TimeBudget.HardMs);
                MoveCommand chosen = search.FindBestMove(board, settings, cts.Token, enableInstabilityTimeManagement: true);

                engine.Advance(board, chosen);
                progressTrace.Add(MeasureProgress(board, position));
            }

            return new ConversionResult(position, ConversionVerdict.Stalled, plyBudget, progressTrace);
        }

        private static bool HasPromotedQueen(BoardState board, Team attackingTeam)
        {
            var indices = board.GetPieceIndices(attackingTeam);
            for (int i = 0; i < indices.Count; i++)
            {
                int idx = indices[i];
                PieceData piece = board.GetPiece(idx % board.TileCountX, idx / board.TileCountX);
                if (!piece.IsEmpty && piece.Type == ChessPieceType.Queen) return true;
            }
            return false;
        }

        /// <summary>Lower is closer to the goal — a plain distance metric, not a score, so a
        /// monotonic decrease is a directly readable "is the attacker actually closing in" signal
        /// without needing the evaluator's own centipawn scale.</summary>
        private static int MeasureProgress(BoardState board, EndgameConversionPosition position)
        {
            if (position.Goal == ConversionGoal.DriveLoneKingToMate)
            {
                Team loneKingTeam = position.AttackingTeam == Team.White ? Team.Black : Team.White;
                if (!board.TryFindKing(loneKingTeam, out Vector2Int loneKing)) return 0;
                return DistanceToNearestEdge(board, loneKing);
            }

            // PromoteThePawn: the attacking pawn's remaining distance to its promotion rank. Reads
            // the attacker's pawn indices live each call rather than tracking one square, since the
            // pawn's file/rank can shift on a capture-free push and this metric must not go stale.
            var indices = board.GetPieceIndices(position.AttackingTeam);
            int bestRemaining = board.TileCountY;
            for (int i = 0; i < indices.Count; i++)
            {
                int idx = indices[i];
                int x = idx % board.TileCountX;
                int y = idx / board.TileCountX;
                PieceData piece = board.GetPiece(x, y);
                if (piece.IsEmpty || piece.Type != ChessPieceType.Pawn) continue;

                int remaining = position.AttackingTeam == Team.White
                    ? (board.TileCountY - 1) - y
                    : y;
                if (remaining < bestRemaining) bestRemaining = remaining;
            }
            return bestRemaining;
        }

        private static int DistanceToNearestEdge(BoardState board, Vector2Int square)
        {
            int distX = System.Math.Min(square.x, (board.TileCountX - 1) - square.x);
            int distY = System.Math.Min(square.y, (board.TileCountY - 1) - square.y);
            return System.Math.Min(distX, distY);
        }
    }
}
