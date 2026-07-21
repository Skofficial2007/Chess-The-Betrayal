using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// The absolute strength signal: does the top tier find the provably correct move on every
    /// yardstick position? Unlike every tier-vs-tier check elsewhere in this harness, this needs no
    /// opponent and no games — a position either gets solved or it doesn't, checked against an
    /// answer YardstickPositionProofTests already verified independently. Runs in seconds, so it's
    /// a per-commit test, not [Explicit].
    /// </summary>
    [TestFixture]
    public class YardstickStrengthTests
    {
        private static AIProfile TopTier => AIProfileTable.BuiltIn.Single(p => p.Id == "impossible");

        private static IEnumerable<TestCaseData> AllPositions()
        {
            foreach (YardstickPosition position in YardstickSuite.All)
                yield return new TestCaseData(position).SetName(position.Name);
        }

        [TestCaseSource(nameof(AllPositions))]
        public void TopTier_SolvesEveryYardstickPosition(YardstickPosition position)
        {
            YardstickResult result = YardstickRunner.Run(position, TopTier);

            Assert.That(result.Solved, Is.True, result.DescribeFailure());
        }
    }
}
