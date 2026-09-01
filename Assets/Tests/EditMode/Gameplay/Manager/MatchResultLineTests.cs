using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Events.Payloads;
using ChessTheBetrayal.Gameplay.Manager;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Manager
{
    /// <summary>
    /// The one line a match report opens with. Every ending the game can produce has to come out as
    /// a sentence, on one line, in plain ASCII - a report is written to be pasted into a chat or a
    /// file and read by somebody who was not there.
    /// </summary>
    [TestFixture]
    public class MatchResultLineTests
    {
        [Test]
        public void ACheckmateNamesTheWinnerAndHowTheyWon()
        {
            Assert.That(MatchResultLine.Describe(Team.Black, GameEndReason.Checkmate),
                Is.EqualTo("Black won by checkmate."));
        }

        [Test]
        public void AWinOnTimeSaysSoRatherThanCallingItACheckmate()
        {
            Assert.That(MatchResultLine.Describe(Team.White, GameEndReason.Timeout),
                Is.EqualTo("White won on time."));
        }

        [Test]
        public void AResignationIsNotAMate()
        {
            Assert.That(MatchResultLine.Describe(Team.White, GameEndReason.Resignation),
                Is.EqualTo("White won by resignation."));
        }

        /// <summary>
        /// A draw has to say which kind it was. The reasons are precisely the endings a player
        /// cannot read off the final position, which is what makes them worth writing down.
        /// </summary>
        [Test]
        [TestCase(GameEndReason.Stalemate, "Draw by stalemate.")]
        [TestCase(GameEndReason.Repetition, "Draw by repetition.")]
        [TestCase(GameEndReason.FiftyMoveRule, "Draw by the fifty-move rule.")]
        [TestCase(GameEndReason.Timeout, "Draw on time, neither side with the material to mate.")]
        public void EachKindOfDrawNamesItself(GameEndReason reason, string expected)
        {
            Assert.That(MatchResultLine.Describe(winner: null, reason), Is.EqualTo(expected));
        }

        /// <summary>
        /// Every ending, win and draw alike, has to produce something a report can print. A reason
        /// added later that nobody wires up here would otherwise land in a shared file as an empty
        /// result or as a bare enum name.
        /// </summary>
        [Test]
        public void EveryEndingProducesOneFlatSentence()
        {
            int covered = 0;

            foreach (GameEndReason reason in System.Enum.GetValues(typeof(GameEndReason)))
            {
                foreach (Team? winner in new Team?[] { null, Team.White, Team.Black })
                {
                    string line = MatchResultLine.Describe(winner, reason);

                    Assert.That(line, Is.Not.Null.And.Not.Empty, $"{reason} / {winner} said nothing.");
                    Assert.That(line, Does.Not.Contain("\n"), $"{reason} / {winner} broke across lines.");
                    Assert.That(line, Does.EndWith("."), $"{reason} / {winner} is not a sentence.");
                    foreach (char c in line)
                    {
                        if (c < 128) continue;
                        Assert.Fail($"Non-ASCII character '{c}' (U+{(int)c:X4}) in: {line}");
                    }

                    covered++;
                }
            }

            Assert.That(covered, Is.EqualTo(System.Enum.GetValues(typeof(GameEndReason)).Length * 3),
                "Every reason has to be tried both ways round, or this proves less than it looks.");
        }
    }
}
