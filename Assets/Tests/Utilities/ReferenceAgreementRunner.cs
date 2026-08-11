using System.Collections.Generic;
using System.Threading;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Utils;
using ChessTheBetrayal.Gameplay.Manager;

namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>
    /// Measures how often a profile picks the same move as a much deeper reference search over a
    /// fixed set of positions.
    ///
    /// This exists because the obvious alternative — play the two configurations against each other
    /// and compare win rates — turned out to be nearly powerless in practice. Two near-identical
    /// engines draw most of their games, a draw says nothing about which was better, and hundreds
    /// of games can end up carrying only a handful of informative results. Agreement gives one
    /// informative answer per position and has no draws to dilute it.
    /// </summary>
    public static class ReferenceAgreementRunner
    {
        /// <summary>
        /// Runs <paramref name="subject"/> against <paramref name="oracle"/> over the given curated
        /// positions.
        ///
        /// The subject is searched exactly as it would be in a real game — its own depth ceiling and
        /// its own time budget — because the question being asked is what THIS profile concludes,
        /// not what it could conclude given unlimited time. That does mean the subject is
        /// clock-bound while the reference is not, which is intentional and asymmetric: the
        /// reference must be reproducible, the subject must be realistic. Each result records the
        /// depth actually reached so a disagreement caused by running out of clock stays
        /// distinguishable from one caused by disagreeing on the merits.
        ///
        /// <paramref name="rngSeed"/> only affects the as-played move, whose personality dials draw
        /// on it. The raw search result never consults it, so the raw agreement figure is
        /// reproducible regardless of the seed.
        /// </summary>
        public static AgreementReport Run(AIProfile subject, ReferenceMoveOracle oracle,
            IReadOnlyList<int> positionIndices, int rngSeed = 12345)
        {
            var results = new List<AgreementResult>(positionIndices.Count);
            var engine = new ChessEngineAdapter();
            var policy = new MoveSelectionPolicy();
            IRandomSource rng = new SystemRandomSource(rngSeed);

            for (int i = 0; i < positionIndices.Count; i++)
            {
                int positionIndex = positionIndices[i];
                BoardState board = CuratedPositionSuite.Build(positionIndex);

                ReferenceMove reference = oracle.Answer(board);

                var search = new AlphaBetaSearch(engine,
                    new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(subject)));
                var settings = AISearchSettings.FromProfile(BetrayalUsage.Full, subject);

                // The rescore margin mirrors what a real game passes, so the personality dials below
                // act on the same exact scores they would act on in play rather than on the
                // alpha-beta bounds a plain search leaves behind.
                int rescoreMargin = System.Math.Max(subject.BlunderMarginCp, subject.TieBreakWindowCp);

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                using var cts = new CancellationTokenSource();
                cts.CancelAfter(settings.TimeBudget.HardMs);
                MoveCommand rawMove = search.FindBestMove(board.CloneForSnapshot(), settings, cts.Token,
                    rescoreMargin, enableInstabilityTimeManagement: true);
                stopwatch.Stop();

                // Personality dials only get to act when the scores they compare are exact. This
                // mirrors the live agent's own guard: applying a blunder or tie-break window to
                // leftover alpha-beta bounds picks near-randomly among moves whose scores were never
                // really comparable.
                bool selectionOffered = (subject.BlunderRate > 0f || subject.TieBreakWindowCp > 0)
                    && search.RootScoresExactCount > 1;
                bool blunderRollFired = false;
                MoveCommand selectedMove = rawMove;
                if (selectionOffered)
                {
                    selectedMove = policy.SelectFinalMove(
                        search.RootMoves, search.RootScores, search.RootScoresExactCount,
                        search.BestRootIndex, subject, rng, out blunderRollFired);
                }

                int subjectScoreCp = search.RootMoveCount > 0 ? search.RootScores[search.BestRootIndex] : 0;

                results.Add(new AgreementResult(
                    positionIndex,
                    SameMove(rawMove, reference.Move),
                    SameMove(selectedMove, reference.Move),
                    rawMove, selectedMove, reference.Move,
                    subjectScoreCp, reference.ScoreCp,
                    search.Stats.LastCompletedDepth, subject.MaxDepth, oracle.ReferenceDepth,
                    blunderRollFired, stopwatch.Elapsed.TotalMilliseconds));
            }

            return new AgreementReport(results, subject.Id, oracle.ReferenceDepth);
        }

        /// <summary>
        /// Move identity includes the Betrayal stage, not just the squares. A Betrayal Act and an
        /// ordinary move to the same square are entirely different decisions, and treating them as
        /// equal would quietly credit agreement that never happened.
        /// </summary>
        private static bool SameMove(MoveCommand left, MoveCommand right) =>
            left.StartPosition == right.StartPosition
            && left.EndPosition == right.EndPosition
            && left.Stage == right.Stage;
    }
}
