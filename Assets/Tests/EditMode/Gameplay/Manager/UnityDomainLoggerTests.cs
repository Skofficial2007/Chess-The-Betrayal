using System;
using NUnit.Framework;
using ChessTheBetrayal.Core.Diagnostics;
using ChessTheBetrayal.Gameplay.Manager;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Manager
{
    /// <summary>
    /// The wording of every line this project writes to the log.
    ///
    /// The project already decided its reports stay inside plain ASCII, after a file came back off a
    /// phone with every em dash rendered as mojibake: a plain UTF-8 file with nothing identifying it
    /// is read as the local codepage by enough Windows text viewers to matter, and a byte order mark
    /// did not fix it. Three checks hold the report builders to that rule.
    ///
    /// The log lines were outside all of them, purely because they are formatted here instead of in
    /// a report class - and seven of them carried an em dash, three in Betrayal messages. They are
    /// also the lines a tester is most likely to paste somewhere, since a logcat is what you reach
    /// for when something looks wrong. Nothing referenced this file at all before this fixture.
    /// </summary>
    [TestFixture]
    public class UnityDomainLoggerTests
    {
        /// <summary>
        /// Every code, not every entry in the prefix table. Three codes have no prefix and fall
        /// through to a generic form, and a walk over the table alone would never visit them - nor
        /// would it notice a code added later that nobody wired up.
        /// </summary>
        [Test]
        public void EveryDomainEventReadsBackInPlainAscii()
        {
            int checkedCodes = 0;

            foreach (DomainEventCode code in Enum.GetValues(typeof(DomainEventCode)))
            {
                foreach (string message in new[] { null, "context from the caller" })
                {
                    string line = UnityDomainLogger.Format(new DomainLogEvent(code, message: message, auxInt: 7));

                    foreach (char c in line)
                    {
                        if (c < 128) continue;

                        // Compared as a code point. NUnit's LessThan over a char does not fail the
                        // way it reads, and the first version of this project's other ASCII check
                        // was built on it and passed with an em dash still in the string.
                        Assert.Fail($"Non-ASCII character '{c}' (U+{(int)c:X4}) in {code}: {line}");
                    }
                }

                checkedCodes++;
            }

            Assert.That(checkedCodes, Is.EqualTo(Enum.GetValues(typeof(DomainEventCode)).Length),
                "Every code has to be visited, or a new one could carry anything.");
        }

        /// <summary>
        /// A code with no entry in the prefix table still has to produce something readable, since
        /// that is the branch three of them take today and any code added later takes until somebody
        /// writes it a prefix.
        /// </summary>
        [Test]
        public void ACodeWithNoPrefixStillNamesItselfAndItsNumber()
        {
            string line = UnityDomainLogger.Format(
                new DomainLogEvent(DomainEventCode.Betrayal_RetributionSkipped, message: "skipped", auxInt: 12));

            Assert.That(line, Does.Contain("Betrayal_RetributionSkipped"));
            Assert.That(line, Does.Contain("12"));
            Assert.That(line, Does.Contain("skipped"));
        }

        [Test]
        public void AMappedCodeReadsAsItsSentenceRatherThanItsEnumName()
        {
            string line = UnityDomainLogger.Format(
                new DomainLogEvent(DomainEventCode.Betrayal_DefectionResolved, auxInt: 0));

            Assert.That(line, Does.StartWith("[Betrayal] Defection resolved"));
            Assert.That(line, Does.Not.Contain("Domain:"));
        }
    }
}
