using NUnit.Framework;
using ChessTheBetrayal.UI.Controls;

namespace ChessTheBetrayal.Tests.EditMode.UI.Controls
{
    /// <summary>
    /// The shape of a warning raised over the board. Worth pinning rather than eyeballing: the
    /// markup is invisible until it renders, a wrong tag degrades into text that merely looks a bit
    /// off instead of failing outright, and the whole point of one style is that a warning added
    /// next year cannot quietly look like a different game.
    /// </summary>
    [TestFixture]
    public class MatchWarningMessageTests
    {
        [Test]
        public void Build_LaysOutHeadlineThenBodyThenHint_SeparatedByBlankLines()
        {
            string text = MatchWarningMessage.Build(
                headline: "Are you sure?",
                body: "This cannot be undone.",
                hint: "Press Continue to commit, or Back to cancel.");

            Assert.That(text, Is.EqualTo(
                "<size=110%><b>Are you sure?</b></size>"
                + "\n\n<size=85%>This cannot be undone.</size>"
                + "\n\n<size=75%><i>Press Continue to commit, or Back to cancel.</i></size>"));
        }

        /// <summary>
        /// The one relationship that carries the design: the question outranks its own explanation,
        /// and the reminder of which button is which sits below both.
        /// </summary>
        [Test]
        public void Build_MakesTheHeadlineLargestAndTheHintSmallest()
        {
            string text = MatchWarningMessage.Build("headline", "body", "hint");

            int headlineSize = SizeOfPartContaining(text, "headline");
            int bodySize = SizeOfPartContaining(text, "body");
            int hintSize = SizeOfPartContaining(text, "hint");

            Assert.That(headlineSize, Is.GreaterThan(bodySize), "The question outranks its own explanation.");
            Assert.That(bodySize, Is.GreaterThan(hintSize), "By the time anyone reads the hint they have already decided.");
        }

        [Test]
        public void Build_ItalicisesOnlyTheHint()
        {
            string text = MatchWarningMessage.Build("headline", "body", "hint");

            Assert.That(text, Does.Contain("<i>hint</i>"));
            Assert.That(text, Does.Not.Contain("<i>headline"));
            Assert.That(text, Does.Not.Contain("<i>body"));
        }

        [Test]
        public void Build_WithHeadlineOnly_AddsNoEmptySectionsOrStrayGaps()
        {
            string text = MatchWarningMessage.Build("Nothing else to say.");

            Assert.That(text, Is.EqualTo("<size=110%><b>Nothing else to say.</b></size>"),
                "A one-line question must stay one line rather than growing blank sections to fill a shape.");
        }

        [Test]
        public void Build_WithNoBody_ClosesTheGapRatherThanLeavingTwo()
        {
            string text = MatchWarningMessage.Build("Headline.", body: null, hint: "Press Continue.");

            Assert.That(text, Is.EqualTo(
                "<size=110%><b>Headline.</b></size>"
                + "\n\n<size=75%><i>Press Continue.</i></size>"));
        }

        [Test]
        public void Build_TreatsEmptyPartsTheSameAsMissingOnes()
        {
            string omitted = MatchWarningMessage.Build("Headline.", body: null, hint: null);
            string empty = MatchWarningMessage.Build("Headline.", body: "", hint: "");

            Assert.That(empty, Is.EqualTo(omitted),
                "A caller building text from data will pass empty strings, not nulls, and must not get "
                + "a panel with a gap floating under a heading.");
        }

        [Test]
        public void Build_KeepsLineBreaksTheCallerPutIn()
        {
            string text = MatchWarningMessage.Build("Are you sure you want to\nspare the betrayer?");

            Assert.That(text, Does.Contain("Are you sure you want to\nspare the betrayer?"),
                "Where a line reads better broken at a particular word, only the caller knows that — "
                + "the style must not flatten it.");
        }

        // Reads the percentage off the size tag wrapping a given piece of text, so a test can compare
        // the three parts without restating the exact numbers the style happens to use today.
        private static int SizeOfPartContaining(string text, string part)
        {
            int partIndex = text.IndexOf(part, System.StringComparison.Ordinal);
            int tagStart = text.LastIndexOf("<size=", partIndex, System.StringComparison.Ordinal);
            int valueStart = tagStart + "<size=".Length;
            int valueEnd = text.IndexOf('%', valueStart);
            return int.Parse(text.Substring(valueStart, valueEnd - valueStart));
        }
    }
}
