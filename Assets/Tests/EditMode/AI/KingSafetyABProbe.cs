using System;
using System.IO;
using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// One-off A/B strength measurement for the king-safety term: PreAI53Evaluator (a hand-copied
    /// clone of BetrayalAwareEvaluator as it was before king safety existed -- material + PST + king
    /// shelter + Betrayal option + pawn structure, nothing else) against the real, current
    /// BetrayalAwareEvaluator, both under the "impossible" profile's weights (BlunderRate=0,
    /// TieBreakWindowCp=0, so MoveSelectionPolicy never randomizes and the search's own best move is
    /// always what's played -- the least noisy possible read of an eval change). Color-swapped over
    /// all 20 curated positions (N=40), a compressed per-move clock so the run finishes in minutes.
    ///
    /// [Explicit] because this is a measurement, not a correctness pin -- it produces a result to read
    /// and record, not a pass/fail gate to run on every commit. Runs entirely off ITurnResolver (never
    /// MatchDriver), same reasoning as PawnStructureABProbe's own doc comment.
    /// </summary>
    [TestFixture]
    [Explicit("One-off A/B measurement, not a per-commit gate. Run manually via -testFilter.")]
    public class KingSafetyABProbe
    {
        private const int MoveBudgetCapMs = 400;
        private const int PlyCap = 60;

        /// <summary>
        /// Reuses the real, unchanged PawnStructure class directly (AI-53 did not touch it) rather
        /// than hand-copying its logic a second time -- only what actually predates AI-53 (king
        /// safety) is missing from this clone.
        /// </summary>
        private sealed class PreAI53Evaluator : IPositionEvaluator
        {
            private const int PawnValue = 100;
            private const int KnightValue = 320;
            private const int BishopValue = 325;
            private const int RookValue = 500;
            private const int QueenValue = 975;
            private const int BetrayalRightBonus = 35;
            private const int KingShelterBonusPerPawn = 10;

            private readonly EvaluationWeights _weights;

            public PreAI53Evaluator(EvaluationWeights weights) => _weights = weights;

            public int Evaluate(BoardState board, Team forTeam)
            {
                int score = EvaluateCheap(board, forTeam);
                int pawnScore = PawnStructureDelta(board);
                return forTeam == Team.White ? score + pawnScore : score - pawnScore;
            }

            private int PawnStructureDelta(BoardState board)
            {
                PawnStructure.Score(board, Team.White, out int whiteAttack, out int whiteDefense);
                PawnStructure.Score(board, Team.Black, out int blackAttack, out int blackDefense);

                int whitePawnScore = (int)(whiteAttack * _weights.AttackScale) + (int)(whiteDefense * _weights.DefenseScale);
                int blackPawnScore = (int)(blackAttack * _weights.AttackScale) + (int)(blackDefense * _weights.DefenseScale);

                return whitePawnScore - blackPawnScore;
            }

            public int EvaluateCheap(BoardState board, Team forTeam)
            {
                int phaseWeight = MaterialPhase.Weight(board);
                int whiteScore = MaterialAndPosition(board, Team.White, phaseWeight);
                int blackScore = MaterialAndPosition(board, Team.Black, phaseWeight);
                int score = whiteScore - blackScore;

                if (board.BetrayalRightAvailable)
                {
                    int betrayalTerm = (int)(BetrayalRightBonus * _weights.BetrayalOptionScale);
                    score += (board.CurrentTurn == Team.White ? betrayalTerm : -betrayalTerm);
                }

                return forTeam == Team.White ? score : -score;
            }

            private int MaterialAndPosition(BoardState board, Team team, int phaseWeight)
            {
                int material = 0;
                int attackPst = 0;
                int defensePst = 0;
                var indices = board.GetPieceIndices(team);

                for (int i = 0; i < indices.Count; i++)
                {
                    int idx = indices[i];
                    int x = idx % board.TileCountX;
                    int y = idx / board.TileCountX;
                    PieceData piece = board.GetPiece(x, y);

                    material += BaseValue(piece.Type);

                    int bonus = PieceSquareTables.Bonus(piece.Type, x, y, team, board.TileCountX, board.TileCountY, phaseWeight);
                    int row = team == Team.White ? y : board.TileCountY - 1 - y;
                    if (row >= 4) attackPst += bonus;
                    else defensePst += bonus;
                }

                int shelterBonus = KingShelterBonus(board, team);

                return material
                    + (int)(attackPst * _weights.AttackScale)
                    + (int)((defensePst + shelterBonus) * _weights.DefenseScale);
            }

            private static int KingShelterBonus(BoardState board, Team team)
            {
                if (!board.TryFindKing(team, out Vector2Int kingPos)) return 0;

                int forward = team == Team.White ? 1 : -1;
                int shelterY = kingPos.y + forward;

                int shelteredPawns = 0;
                for (int dx = -1; dx <= 1; dx++)
                {
                    PieceData square = board.GetPiece(kingPos.x + dx, shelterY);
                    if (square.Type == ChessPieceType.Pawn && square.Team == team) shelteredPawns++;
                }

                return shelteredPawns * KingShelterBonusPerPawn;
            }

            private static int BaseValue(ChessPieceType type) => type switch
            {
                ChessPieceType.Pawn => PawnValue,
                ChessPieceType.Knight => KnightValue,
                ChessPieceType.Bishop => BishopValue,
                ChessPieceType.Rook => RookValue,
                ChessPieceType.Queen => QueenValue,
                ChessPieceType.King => 0,
                _ => 0
            };
        }

        [Test]
        public void RunABMeasurement()
        {
            string logPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "AI53_AB_progress.log");
            using var log = new StreamWriter(logPath, append: false) { AutoFlush = true };

            void Log(string msg)
            {
                string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
                UnityEngine.Debug.Log(line);
                log.WriteLine(line);
            }

            var profileProvider = new AIProfileTableProvider();
            AIProfile impossible = profileProvider.Resolve("impossible");
            EvaluationWeights weights = EvaluationWeights.FromProfile(impossible);

            // Sanity gate: the clone must reproduce exactly the pre-AI-53 golden literals (the same
            // numbers EvaluatorWeightsRegressionTests pinned before this ticket's commits) before any
            // game is played -- ShelteredKing here is 300, NOT the post-AI-53 336, because this clone
            // has no king-safety term at all.
            var probe = new PreAI53Evaluator(EvaluationWeights.Identity);
            int symmetricQueens = EvalGolden(probe, "e1:White:King,e8:Black:King,d1:White:Queen,d8:Black:Queen");
            int mirroredRookKnight = EvalGolden(probe, "e1:White:King,e8:Black:King,d1:White:Rook,a4:White:Knight");
            int extraQueen = EvalGolden(probe, "e1:White:King,e8:Black:King,d1:White:Queen");
            int shelteredKing = EvalGolden(probe, "e1:White:King,e8:Black:King,d2:White:Pawn,e2:White:Pawn,f2:White:Pawn");

            Log($"Sanity gate: SymmetricQueens={symmetricQueens} (expect 0), MirroredRookKnight={mirroredRookKnight} (expect 795), ExtraQueen={extraQueen} (expect 970), ShelteredKing={shelteredKing} (expect 300)");
            Assert.That(symmetricQueens, Is.EqualTo(0));
            Assert.That(mirroredRookKnight, Is.EqualTo(795));
            Assert.That(extraQueen, Is.EqualTo(970));
            Assert.That(shelteredKing, Is.EqualTo(300));
            Log("Sanity gate passed. Starting A/B run.");

            int wins = 0, losses = 0, draws = 0;
            int gameIndex = 0;
            int totalGames = CuratedPositionSuite.Count * 2;

            for (int posIndex = 0; posIndex < CuratedPositionSuite.Count; posIndex++)
            {
                for (int newIsWhite = 0; newIsWhite <= 1; newIsWhite++)
                {
                    gameIndex++;
                    bool newEvalIsWhite = newIsWhite == 1;

                    BoardState start = CuratedPositionSuite.Build(posIndex);
                    MatchOutcome result = PlayGame(start, weights, newEvalIsWhite);

                    string outcomeForNew = result switch
                    {
                        MatchOutcome.WhiteWon => newEvalIsWhite ? "WIN" : "LOSS",
                        MatchOutcome.BlackWon => newEvalIsWhite ? "LOSS" : "WIN",
                        _ => "DRAW"
                    };

                    if (outcomeForNew == "WIN") wins++;
                    else if (outcomeForNew == "LOSS") losses++;
                    else draws++;

                    Log($"Game {gameIndex}/{totalGames} pos={posIndex} newEvalColor={(newEvalIsWhite ? "White" : "Black")} outcome={result} newEvalResult={outcomeForNew} running W-L-D={wins}-{losses}-{draws}");
                }
            }

            double aScore = (wins + 0.5 * draws) / totalGames * 100.0;
            Log($"FINAL: new king-safety evaluator {wins}W-{losses}L-{draws}D over {totalGames} games. A-score = {aScore:F1}%");
        }

        private static MatchOutcome PlayGame(BoardState startingPosition, EvaluationWeights weights, bool newEvalIsWhite)
        {
            IChessEngine engine = new ChessEngineAdapter();
            ITurnResolver resolver = new TurnResolver();
            BoardState board = startingPosition.CloneForSnapshot();
            TurnPhase phase = TurnPhase.Normal;

            IPositionEvaluator newEval = new BetrayalAwareEvaluator(weights);
            IPositionEvaluator oldEval = new PreAI53Evaluator(weights);

            IPositionEvaluator whiteEval = newEvalIsWhite ? newEval : oldEval;
            IPositionEvaluator blackEval = newEvalIsWhite ? oldEval : newEval;

            var whiteSearch = new AlphaBetaSearch(engine, whiteEval, transpositionTable: new TranspositionTable(log2Size: 20));
            var blackSearch = new AlphaBetaSearch(engine, blackEval, transpositionTable: new TranspositionTable(log2Size: 20));

            var adjudicator = new MatchAdjudicator(AdjudicationRules.Standard);
            adjudicator.RecordStartingPosition(board);

            var settings = new AISearchSettings(9, new AITimeBudget(MoveBudgetCapMs / 2, MoveBudgetCapMs), BetrayalUsage.Full);

            for (int ply = 0; ply < PlyCap && phase != TurnPhase.GameOver; ply++)
            {
                Team mover = board.CurrentTurn;
                bool isWhite = mover == Team.White;
                AlphaBetaSearch search = isWhite ? whiteSearch : blackSearch;

                using var cts = new CancellationTokenSource();
                cts.CancelAfter(MoveBudgetCapMs);
                MoveCommand move = search.FindBestMove(board, settings, cts.Token);

                int scoreFromMoverPerspective = search.RootMoveCount > 0 ? search.RootScores[search.BestRootIndex] : 0;
                int scoreForWhiteCp = isWhite ? scoreFromMoverPerspective : -scoreFromMoverPerspective;

                TurnAdvanceResult advance = resolver.Advance(board, move);
                phase = advance.NextPhase;

                if (phase != TurnPhase.Normal) continue;

                GameState state = engine.EvaluateGameState(board, board.CurrentTurn, null);
                if (state == GameState.Checkmate)
                {
                    Team winner = mover == Team.White ? Team.Black : Team.White;
                    board.IsGameOver = true;
                    board.Winner = winner;
                    break;
                }
                if (state == GameState.Stalemate)
                {
                    board.IsGameOver = true;
                    board.Winner = null;
                    break;
                }

                MatchOutcome? adjudicated = adjudicator.RecordPly(board, move, ply, scoreForWhiteCp);
                if (adjudicated.HasValue) return adjudicated.Value;
            }

            if (board.IsGameOver)
            {
                return board.Winner switch
                {
                    Team.White => MatchOutcome.WhiteWon,
                    Team.Black => MatchOutcome.BlackWon,
                    _ => MatchOutcome.Draw
                };
            }

            return MatchOutcome.Draw;
        }

        private static int EvalGolden(IPositionEvaluator evaluator, string spec)
        {
            BoardState board = new BoardState(8, 8);
            board.Clear();
            board.CastlingRights = 0;
            board.CurrentTurn = Team.White;
            board.BetrayalRightAvailable = false;

            foreach (string piece in spec.Split(','))
            {
                string[] parts = piece.Split(':');
                (int x, int y) = AlgebraicToXY(parts[0]);
                Team team = parts[1] == "White" ? Team.White : Team.Black;
                ChessPieceType type = Enum.Parse<ChessPieceType>(parts[2]);
                int moveDir = team == Team.White ? 1 : -1;
                board.SetPiece(new PieceData(team, type, moveDir, y), x, y);
            }

            board.ComputeFullZobristHash();
            return evaluator.Evaluate(board, Team.White);
        }

        private static (int, int) AlgebraicToXY(string algebraic)
        {
            int x = algebraic[0] - 'a';
            int y = algebraic[1] - '1';
            return (x, y);
        }
    }
}
