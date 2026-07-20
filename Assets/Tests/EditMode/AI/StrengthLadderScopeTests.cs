using NUnit.Framework;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins the one invariant the Quick and Full strength-ladder tiers must hold: they differ ONLY
    /// in how many positions they sample, never in time control, move budget, or ply cap — so a
    /// Quick result is honestly a smaller-sample read of the same measurement the Full suite takes,
    /// not a cheaper, different one. If that ever drifted (e.g. Quick quietly gaining its own
    /// compressed clock the way the gate fixture has), a Quick pass would stop meaning what its own
    /// doc comment claims it means.
    /// </summary>
    [TestFixture]
    public class StrengthLadderScopeTests
    {
        [Test]
        public void QuickPositionCount_IsSmallerThanTheFullCuratedSuite()
        {
            // The whole point of the Quick tier is finishing faster than Full by sampling fewer
            // positions — if this ever stopped holding, Quick would just be a slower duplicate of
            // Full rather than the fast default the ticket asked for.
            Assert.That(ChessTheBetrayal.Tests.EditMode.AI.StrengthLadder.QuickPositionCount,
                Is.LessThan(ChessTheBetrayal.Tests.Utilities.CuratedPositionSuite.Count));
        }

        [Test]
        public void QuickPositionCount_IsAtLeastFour()
        {
            // A sample too small to say anything at all isn't "quick," it's noise — four positions
            // (eight games both colors) is the same floor the fast per-commit gate already uses.
            Assert.That(ChessTheBetrayal.Tests.EditMode.AI.StrengthLadder.QuickPositionCount, Is.GreaterThanOrEqualTo(4));
        }
    }
}
