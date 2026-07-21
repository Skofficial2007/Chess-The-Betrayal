using System.Threading;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>One yardstick position's result — enough to localize a failure without playing a
    /// single game. Comparing DepthReached against the search's own configured ceiling tells apart
    /// two very different failure shapes: choosing wrong at full depth is an evaluation defect,
    /// choosing wrong at a shallow depth (search cut short) is a speed defect.</summary>
    public sealed class YardstickResult
    {
        public readonly YardstickPosition Position;
        public readonly bool Solved;
        public readonly MoveCommand ChosenMove;
        public readonly int ChosenMoveRootScore;
        public readonly int ExpectedMoveRootScore;
        public readonly int DepthReached;
        public readonly double ElapsedMs;

        public YardstickResult(YardstickPosition position, bool solved, MoveCommand chosenMove,
            int chosenMoveRootScore, int expectedMoveRootScore, int depthReached, double elapsedMs)
        {
            Position = position;
            Solved = solved;
            ChosenMove = chosenMove;
            ChosenMoveRootScore = chosenMoveRootScore;
            ExpectedMoveRootScore = expectedMoveRootScore;
            DepthReached = depthReached;
            ElapsedMs = elapsedMs;
        }

        /// <summary>Everything a contributor needs to diagnose a failure without re-running
        /// anything: the position's name and proof, what was expected and chosen, both moves' root
        /// scores, and how deep the search actually got.</summary>
        public string DescribeFailure()
        {
            return $"[{Position.Name}] expected {Position.ExpectedMoveDescription} (proof: {Position.ProofClass}) " +
                $"but chose {ChosenMove.StartPosition}->{ChosenMove.EndPosition} " +
                $"(chosen score {ChosenMoveRootScore}cp, expected-move score {ExpectedMoveRootScore}cp, " +
                $"depth reached {DepthReached}, {ElapsedMs:F0}ms). {Position.Note}";
        }
    }

    /// <summary>
    /// Runs the real search against every YardstickSuite position and reports whether it found the
    /// proven-correct move — an absolute strength signal that needs no opponent and no games, only
    /// seconds, unlike every other measurement this harness produces which is necessarily relative
    /// (tier A beat tier B). See YardstickPosition/YardstickSuite for what "proven correct" means.
    /// </summary>
    public static class YardstickRunner
    {
        public static YardstickResult Run(YardstickPosition position, AIProfile profile)
        {
            BoardState board = position.BuildBoard();
            var engine = new ChessEngineAdapter();
            var search = new AlphaBetaSearch(engine, new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(profile)));
            var settings = AISearchSettings.FromProfile(BetrayalUsage.Full, profile);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(settings.TimeBudget.HardMs);
            MoveCommand chosen = search.FindBestMove(board, settings, cts.Token, enableInstabilityTimeManagement: true);
            stopwatch.Stop();

            bool solved = position.Matches(chosen);
            int chosenScore = 0;
            int expectedScore = 0;
            for (int i = 0; i < search.RootMoveCount; i++)
            {
                MoveCommand candidate = search.RootMoves[i];
                if (candidate.StartPosition == chosen.StartPosition && candidate.EndPosition == chosen.EndPosition && candidate.Stage == chosen.Stage)
                    chosenScore = search.RootScores[i];
                if (position.Matches(candidate))
                    expectedScore = search.RootScores[i];
            }

            return new YardstickResult(position, solved, chosen, chosenScore, expectedScore,
                search.Stats.LastCompletedDepth, stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}
