using NUnit.Framework;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Finds the per-move budget at which the difficulty tiers actually separate, so the per-commit
    /// gate's constants come from a measurement rather than from a guess.
    ///
    /// The gate compresses the clock so that many games fit into a commit. That compression is not
    /// free: what separates a deeper tier from a shallower one IS the depth its budget buys, so
    /// squeezing the clock far enough removes the difference the gate exists to detect. Squeezed too
    /// far, every pairing lands near an even score and a floor placed anywhere above that is tripped
    /// by ordinary sampling noise rather than by a real fault — which is exactly what the gate did
    /// for a long time at 150ms, failing roughly one run in four with nothing wrong.
    ///
    /// Two readings of every cell, because a single one is worthless here and two that agree are
    /// only slightly better: a budget that beats its neighbour by less than its own repeat spread has
    /// not been shown to beat it at all. Expect roughly 25 minutes for a full sweep.
    ///
    /// `extreme` against `hard` is included precisely because it never separates — flat near an even
    /// score at every budget, which is why the gate does not assert it. Re-run this if anyone
    /// proposes gating the top of the ladder again.
    /// </summary>
    [TestFixture]
    [Explicit("Calibration sweep — run manually when choosing the per-commit gate's budget and floor.")]
    [Timeout(45 * 60 * 1000)]
    public class StrengthGateCalibrationTests
    {
        private static readonly int[] BudgetsMs = { 150, 400, 800, 1500 };

        // Eight positions (sixteen games) per cell, matching the gate, so a reading here means the
        // same thing a reading there does.
        private const int PositionCount = 8;
        private const int PlyCap = 60;
        private const int Repeats = 2;

        private static readonly (string stronger, string weaker, int pairIndex)[] Pairings =
        {
            ("normal", "easy", 0),
            ("hard", "normal", 1),
            ("aggressive", "normal", 4),
            ("extreme", "hard", 2),
        };

        [Test]
        public void SweepMoveBudget_ShowsWhereTheTiersSeparate()
        {
            foreach ((string stronger, string weaker, int pairIndex) in Pairings)
            {
                foreach (int budget in BudgetsMs)
                {
                    for (int repeat = 1; repeat <= Repeats; repeat++)
                    {
                        float winRate = StrengthLadder.PlayWinRate(
                            stronger, weaker, pairIndex, PositionCount, PlyCap, budget, null);

                        TestContext.WriteLine(
                            $"[gate-calib] {stronger} vs {weaker} budgetMs={budget} repeat={repeat} " +
                            $"winRate={winRate:P1} games={PositionCount * 2}");
                    }
                }
            }
        }
    }
}
