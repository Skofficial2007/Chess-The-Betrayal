using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI.Evaluation
{
    /// <summary>
    /// Answers the question this whole ticket exists to answer: does the AI, at its real search
    /// depth, actually finish games it has already won? Unlike a tier-vs-tier A/B (which never
    /// reaches an endgame — CuratedPositionSuite is all early-middlegame openings), this drives each
    /// EndgameConversionSuite position out move by move with the real search on both sides.
    ///
    /// Explicit by design (not a per-commit gate): each position plays out up to 80 real plies of
    /// search on both sides, so this takes tens of seconds to a few minutes, not the sub-second
    /// budget a per-commit suite needs.
    /// </summary>
    [TestFixture]
    [Explicit("Plays each position out to completion with real search on both sides — seconds to minutes per position, not a per-commit budget.")]
    public class DefectionAwareEndgameTests
    {
        private static AIProfile Attacker => AIProfileTable.BuiltIn.Single(p => p.Id == "impossible");
        private static AIProfile Defender => AIProfileTable.BuiltIn.Single(p => p.Id == "easy");

        private static IEnumerable<TestCaseData> AllPositions()
        {
            foreach (EndgameConversionPosition position in EndgameConversionSuite.All)
                yield return new TestCaseData(position).SetName(position.Name);
        }

        [TestCaseSource(nameof(AllPositions))]
        public void WonEndgame_ConvertsWithinTheStandardPlyBudget(EndgameConversionPosition position)
        {
            ConversionResult result = EndgameConversionRunner.Run(position, Attacker, Defender);

            Assert.That(result.Verdict, Is.EqualTo(ConversionVerdict.Converted), result.DescribeFailure());
        }

        [TestCaseSource(nameof(AllPositions))]
        public void WonEndgame_NeverReturnsAFalseDrawWhileTheBetrayalRightIsLive(EndgameConversionPosition position)
        {
            // Every EndgameConversionSuite fixture already plays with the right spent
            // (BetrayalRightAvailable = false) — this is the regression guard for the ticket's
            // central correctness claim, documenting that no false-draw path exists today rather
            // than merely asserting it once by inspection. If a future change ever introduces a
            // Betrayal-aware draw short-circuit anywhere in Core or the search, this rebuilds the
            // position with the right explicitly LIVE and re-asserts the same conversion.
            BoardState board = position.BuildBoard();
            Assume.That(board.BetrayalRightAvailable, Is.False,
                $"{position.Name}: fixture is expected to start with the Betrayal right already spent.");

            var liveRightPosition = new EndgameConversionPosition(position.Name + "_LiveRight", position.Goal,
                position.AttackingTeam, position.Note, () => CloneWithLiveRight(position));

            ConversionResult result = EndgameConversionRunner.Run(liveRightPosition, Attacker, Defender);

            Assert.That(result.Verdict, Is.Not.EqualTo(ConversionVerdict.Drawn),
                $"{position.Name}: with the Betrayal right still live, the game was called a draw before the goal was reached — {result.DescribeFailure()}");
        }

        private static BoardState CloneWithLiveRight(EndgameConversionPosition position)
        {
            BoardState board = position.BuildBoard();
            board.BetrayalRightAvailable = true;
            board.ComputeFullZobristHash();
            return board;
        }
    }
}
