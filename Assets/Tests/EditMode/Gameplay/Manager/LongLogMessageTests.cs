using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ChessTheBetrayal.Gameplay.Manager;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Manager
{
    /// <summary>
    /// The move log goes to the log as one entry, and Android throws away whatever of an entry does
    /// not fit in about four kilobytes. A long game therefore used to lose its most recent plies and
    /// still read as a complete record of a shorter one.
    ///
    /// These check the dividing, which is the half that decides anything. What Unity does with the
    /// pieces afterwards is one call per piece and has nothing left to get wrong.
    /// </summary>
    [TestFixture]
    public class LongLogMessageTests
    {
        private static string Lines(int count) =>
            string.Join("\n", Enumerable.Range(1, count).Select(i => $"{i}. e2-e4"));

        [Test]
        public void ABodyThatFits_StaysOnePiece()
        {
            Assert.That(LongLogMessage.Split(Lines(10), maxCharacters: 3000), Has.Count.EqualTo(1));
        }

        /// <summary>
        /// The failure this exists to stop. Every line has to come back, in order, or the split is
        /// doing the same thing the platform was doing - just earlier and with our name on it.
        /// </summary>
        [Test]
        public void ABodyTooLongForOneEntry_ComesBackWholeAndInOrderAcrossThePieces()
        {
            string body = Lines(600);

            IReadOnlyList<string> parts = LongLogMessage.Split(body, maxCharacters: 3000);

            Assert.That(parts.Count, Is.GreaterThan(1), "600 plies has to need more than one entry, or this proves nothing.");
            Assert.That(string.Join("\n", parts), Is.EqualTo(body));
        }

        [Test]
        public void NoPieceGoesPastTheLimitItWasGiven()
        {
            foreach (string part in LongLogMessage.Split(Lines(600), maxCharacters: 3000))
            {
                Assert.That(part.Length, Is.LessThanOrEqualTo(3000));
            }
        }

        /// <summary>
        /// Splitting mid-ply would leave a reader with "12. Qd1x" and no way to know it had been cut,
        /// which is the confusion this whole change exists to remove rather than relocate.
        /// </summary>
        [Test]
        public void APieceNeverStartsOrEndsHalfwayThroughALine()
        {
            foreach (string part in LongLogMessage.Split(Lines(600), maxCharacters: 3000))
            {
                foreach (string line in part.Split('\n'))
                {
                    Assert.That(line, Does.Match(@"^\d+\. e2-e4$"), $"A line was cut: '{line}'");
                }
            }
        }

        /// <summary>
        /// A line of its own that is already over the limit is passed through rather than cut. Ply
        /// notation never gets near this, and truncating inside the code written to stop truncation
        /// would be the worse of the two answers.
        /// </summary>
        [Test]
        public void ASingleLineLongerThanTheLimit_IsKeptWholeRatherThanCut()
        {
            string monster = new string('x', 5000);

            IReadOnlyList<string> parts = LongLogMessage.Split(monster, maxCharacters: 3000);

            Assert.That(parts, Has.Count.EqualTo(1));
            Assert.That(parts[0], Is.EqualTo(monster));
        }

        [Test]
        public void AnEmptyBodyProducesNothingToWrite()
        {
            Assert.That(LongLogMessage.Split(string.Empty, maxCharacters: 3000), Is.Empty);
        }

        /// <summary>
        /// The limit has to leave room for the tag and priority Android counts alongside the text,
        /// and for the difference between the bytes it counts and the characters counted here.
        /// </summary>
        [Test]
        public void TheEntryLimitStaysUnderWhatAndroidWillActuallyCarry()
        {
            Assert.That(LongLogMessage.MaxCharactersPerEntry, Is.LessThan(4000));
        }
    }
}
