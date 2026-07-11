using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Guards the wall-clock half of iterative deepening: AISearchSettings.SoftTimeBudgetMs must
    /// actually bound every individual FindBestMove call, turn after turn — not just the first one.
    /// Before this was wired up (AsyncAIAgent.RequestBestMove now arms CancellationTokenSource.
    /// CancelAfter(settings.SoftTimeBudgetMs)), the budget was dead data: a search could run to
    /// MaxDepth regardless of how long that took, which is why a single depth-7 call could take
    /// several seconds with no way to cap it turn-over-turn in a real match.
    ///
    /// This exercises FindBestMove directly (not AsyncAIAgent) with a realistic budget across
    /// several consecutive turns on the SAME search instance — so the shared TranspositionTable
    /// carries state between turns exactly like a real match (see AsyncAIAgent's ctor comment: the
    /// TT persists per-match precisely to fight successive-turn escalation).
    /// </summary>
    [TestFixture]
    public class SearchTimeBudgetTests
    {
        private ChessEngineAdapter _engine;

        [SetUp]
        public void Setup() => _engine = new ChessEngineAdapter();

        private static BoardState MidgamePosition() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("g1", Team.White, ChessPieceType.King)
                .WithPiece("g8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("d8", Team.Black, ChessPieceType.Queen)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("f1", Team.White, ChessPieceType.Rook)
                .WithPiece("a8", Team.Black, ChessPieceType.Rook)
                .WithPiece("f8", Team.Black, ChessPieceType.Rook)
                .WithPiece("c3", Team.White, ChessPieceType.Bishop)
                .WithPiece("f3", Team.White, ChessPieceType.Knight)
                .WithPiece("c6", Team.Black, ChessPieceType.Bishop)
                .WithPiece("f6", Team.Black, ChessPieceType.Knight)
                .WithPiece("g2", Team.White, ChessPieceType.Bishop)
                .WithPiece("d2", Team.White, ChessPieceType.Knight)
                .WithPiece("g7", Team.Black, ChessPieceType.Bishop)
                .WithPiece("b8", Team.Black, ChessPieceType.Knight)
                .WithPiece("a2", Team.White, ChessPieceType.Pawn)
                .WithPiece("b2", Team.White, ChessPieceType.Pawn)
                .WithPiece("c2", Team.White, ChessPieceType.Pawn)
                .WithPiece("e4", Team.White, ChessPieceType.Pawn)
                .WithPiece("f2", Team.White, ChessPieceType.Pawn)
                .WithPiece("g3", Team.White, ChessPieceType.Pawn)
                .WithPiece("h2", Team.White, ChessPieceType.Pawn)
                .WithPiece("a7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("b7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("c7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("e5", Team.Black, ChessPieceType.Pawn)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("g6", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

        /// <summary>
        /// Plays several consecutive real turns (self-play: the same search instance answers for
        /// both sides, exactly like AsyncAIAgent would across a match) with a budget matching the
        /// ADR's actual DoD target, and asserts EVERY turn individually stays within a small
        /// tolerance of that budget — not just the first move, which is all the old single-shot
        /// benchmark ever proved.
        /// </summary>
        [Test]
        public void FindBestMove_MultipleConsecutiveTurns_EachTurnRespectsSoftTimeBudget()
        {
            const int softBudgetMs = 3000;
            const int toleranceMs = 1500; // generous slack for CI jitter / cancellation-check granularity
            const int turnsToPlay = 6;

            BoardState board = MidgamePosition();
            var settings = new AISearchSettings(maxDepth: 32, softTimeBudgetMs: softBudgetMs, BetrayalUsage.Full);
            var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());

            for (int turn = 0; turn < turnsToPlay; turn++)
            {
                using var cts = new CancellationTokenSource();
                cts.CancelAfter(settings.SoftTimeBudgetMs);

                var stopwatch = Stopwatch.StartNew();
                MoveCommand best = search.FindBestMove(board, settings, cts.Token);
                stopwatch.Stop();

                System.Console.WriteLine(
                    $"Turn {turn} ({board.CurrentTurn}): {stopwatch.Elapsed.TotalMilliseconds:F0}ms, " +
                    $"best={best}, stats={search.Stats}");

                Assert.That(stopwatch.Elapsed.TotalMilliseconds, Is.LessThan(softBudgetMs + toleranceMs),
                    $"Turn {turn} took {stopwatch.Elapsed.TotalMilliseconds:F0}ms against a " +
                    $"{softBudgetMs}ms budget — SoftTimeBudgetMs must bound every individual turn, " +
                    "not just the first one in a match.");

                ChessEngine.ApplyMoveToBoard(board, best, recordHistory: false);
                if (AlphaBetaSearch.StageFlipsTurn(best.Stage))
                    board.CurrentTurn = board.CurrentTurn == Team.White ? Team.Black : Team.White;

                // A Betrayal sequence (Act/Retribution/Defection/ForcedSave) can require the SAME
                // side to move again before the opponent gets a turn — keep resolving until the turn
                // actually changes hands or the game ends, so each loop iteration is one real "turn".
                int guard = 0;
                while (board.PendingBetrayerSquare.HasValue && guard++ < 8)
                {
                    using var subCts = new CancellationTokenSource();
                    subCts.CancelAfter(settings.SoftTimeBudgetMs);

                    MoveCommand sub = search.FindBestMove(board, settings, subCts.Token);
                    ChessEngine.ApplyMoveToBoard(board, sub, recordHistory: false);
                    if (AlphaBetaSearch.StageFlipsTurn(sub.Stage))
                        board.CurrentTurn = board.CurrentTurn == Team.White ? Team.Black : Team.White;
                }
            }
        }
    }
}
