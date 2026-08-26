using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tooling.Agreement;
using ChessTheBetrayal.Tooling.Strength;

namespace ChessTheBetrayal.Tests.EditMode.Tooling.Strength
{
    /// <summary>
    /// Produces the agreement harness's first real number: how often each tier plays the move a
    /// much deeper search would, over the curated position set.
    ///
    /// The harness's own tests prove it measures what it claims to — that it is self-consistent,
    /// deterministic, and rejects stale answers. None of that establishes that a real run says
    /// anything useful, which is a separate question and the one this answers. Until it has been
    /// run once, the harness is an instrument with no reading on its dial.
    ///
    /// Positions whose best move changes with depth are reported separately rather than folded into
    /// the headline. On those the reference's answer is a fact about the depth that was searched,
    /// so counting them would mix ply parity into a strength figure.
    ///
    /// Explicit: the reference costs about eleven seconds per position at its default depth, and the
    /// stability screen adds the two shallower searches on top, so a full sweep runs in minutes and
    /// has no place in a per-commit suite.
    /// </summary>
    [TestFixture]
    [Explicit("Measurement only — run by hand to record an agreement baseline.")]
    public class ReferenceAgreementBaselineProbe
    {
        /// <summary>
        /// Well past the default per-test limit, which a run like this exceeds several times over:
        /// the reference alone costs about eleven seconds per position and every case here sweeps
        /// the whole set. Without this the measurement still completes and still logs its numbers, but
        /// the case is reported as failed on time, which reads as a broken measurement rather than
        /// a long one.
        /// </summary>
        private const int MeasurementTimeoutMs = 3600000;

        /// <summary>The tiers whose separation is in question. The lower rungs of the ladder are
        /// already known to be healthy, so measuring them here would spend the budget where there
        /// is no open question.</summary>
        private static readonly string[] SubjectProfileIds = { "hard", "extreme", "impossible" };

        private static AIProfile Profile(string id) => AIProfileTable.BuiltIn.Single(p => p.Id == id);

        [Test]
        [Timeout(MeasurementTimeoutMs)]
        public void ScreenPositionsForDepthStability()
        {
            // Run first: the headline figures below are only meaningful over positions whose answer
            // holds still, and which those are is not knowable without measuring.
            var oracle = new ReferenceMoveOracle(Profile("impossible"));
            var stable = new List<int>();
            var unstable = new List<int>();

            for (int index = 0; index < CuratedPositionSuite.Count; index++)
            {
                BoardState board = CuratedPositionSuite.Build(index);
                bool isStable = oracle.IsStableAcrossDepths(board);
                (isStable ? stable : unstable).Add(index);
                Debug.Log($"position {index}: stable={isStable}");
            }

            Debug.Log($"STABLE ({stable.Count}/{CuratedPositionSuite.Count}): {string.Join(",", stable)}");
            Debug.Log($"UNSTABLE ({unstable.Count}/{CuratedPositionSuite.Count}): {string.Join(",", unstable)}");
        }

        [TestCase("hard")]
        [TestCase("extreme")]
        [TestCase("impossible")]
        [Timeout(MeasurementTimeoutMs)]
        public void MeasureAgreementBaseline(string subjectId)
        {
            var oracle = new ReferenceMoveOracle(Profile("impossible"));
            var allPositions = new List<int>(CuratedPositionSuite.Count);
            for (int i = 0; i < CuratedPositionSuite.Count; i++) allPositions.Add(i);

            AgreementReport report = ReferenceAgreementRunner.Run(Profile(subjectId), oracle, allPositions);

            Debug.Log($"AGREEMENT BASELINE for '{subjectId}':");
            Debug.Log(report.Describe());
            Debug.Log($"SUMMARY {subjectId}: raw {report.RawAgreedCount}/{report.PositionCount} "
                + $"({report.RawAgreement:P1}), as-played {report.SelectedAgreedCount}/{report.PositionCount} "
                + $"({report.SelectedAgreement:P1}), cut short {report.CutShortCount}");
        }
    }
}
