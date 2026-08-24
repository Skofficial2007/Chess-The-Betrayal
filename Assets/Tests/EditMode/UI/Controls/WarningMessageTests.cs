using NUnit.Framework;
using ChessTheBetrayal.UI.Controls;

namespace ChessTheBetrayal.Tests.EditMode.UI.Controls
{
    /// <summary>
    /// The house style for a confirmation panel's text. Worth pinning rather than eyeballing: the
    /// markup is invisible until it renders, a wrong tag degrades into text that merely looks a bit
    /// off instead of failing, and the whole reason this lives in one place is so a second warning
    /// added later cannot quietly look like a different app.
    /// </summary>
    [TestFixture]
    public class WarningMessageTests
    {
        [Test]
        public void Build_LaysOutHeadlineThenDetailThenAction_InTheDesignedSizes()
        {
            string text = WarningMessage.Build(
                headline: "This report has not been shared yet.",
                detail: "Starting another run replaces it.",
                action: "Go back and press Share Report to keep it.");

            Assert.That(text, Is.EqualTo(
                "<size=115%><b>This report has not been shared yet.</b></size>"
                + "\n<size=70%>....\nStarting another run replaces it.</size>"
                + "\n\n<size=150%><b>Go back and press Share Report to keep it.</b></size>"));
        }

        /// <summary>
        /// The one relationship that carries the design: someone who reads a single line should be
        /// reading the one that says what to press, so the action outranks even the headline and the
        /// explanation sits below both.
        /// </summary>
        [Test]
        public void Build_MakesTheActionLargestAndTheDetailSmallest()
        {
            string text = WarningMessage.Build("headline", "detail", "action");

            int headlineSize = SizeOfPartContaining(text, "headline");
            int detailSize = SizeOfPartContaining(text, "detail");
            int actionSize = SizeOfPartContaining(text, "action");

            Assert.That(actionSize, Is.GreaterThan(headlineSize), "The instruction outranks the statement.");
            Assert.That(headlineSize, Is.GreaterThan(detailSize), "The explanation never competes with either.");
        }

        [Test]
        public void Build_WithHeadlineOnly_AddsNoEmptySectionsOrStraySeparator()
        {
            string text = WarningMessage.Build("Nothing else to say.");

            Assert.That(text, Is.EqualTo("<size=115%><b>Nothing else to say.</b></size>"),
                "A one-line question must stay one line rather than growing blank sections to fill a shape.");
        }

        [Test]
        public void Build_WithNoDetail_DropsTheSeparatorWithIt()
        {
            string text = WarningMessage.Build("Headline.", detail: null, action: "Do the thing.");

            Assert.That(text, Does.Not.Contain("...."),
                "The dots separate a headline from its detail; with no detail they separate nothing.");
            Assert.That(text, Does.Contain("Do the thing."));
        }

        [Test]
        public void Build_TreatsEmptyPartsTheSameAsMissingOnes()
        {
            string omitted = WarningMessage.Build("Headline.", detail: null, action: null);
            string empty = WarningMessage.Build("Headline.", detail: "", action: "");

            Assert.That(empty, Is.EqualTo(omitted),
                "A caller building text from data will pass empty strings, not nulls, and must not " +
                "get a panel with a separator floating under a heading.");
        }

        [Test]
        public void Build_KeepsLineBreaksTheCallerPutIn()
        {
            string text = WarningMessage.Build("Headline.", action: "Go back and press\nShare Report.");

            Assert.That(text, Does.Contain("Go back and press\nShare Report."),
                "Deliberate breaks balance a line at the panel's width; the style must not flatten them.");
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
