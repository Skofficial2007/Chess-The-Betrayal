using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ChessTheBetrayal.UI.Controls;

namespace ChessTheBetrayal.Tests.EditMode.UI.Controls
{
    /// <summary>
    /// The exact words the game puts in front of someone about to spend something they cannot get
    /// back. Three different kinds of check live here, and only one of them is a copy of the copy:
    ///
    ///   The full text is pinned so that editing it fails loudly. That is a tripwire, not a proof —
    ///   what it buys is that nobody changes this wording without going and looking at the panel.
    ///
    ///   The markup is checked for real. An unclosed tag does not fail anywhere; it renders as
    ///   literal "&lt;size=110%&gt;" in the middle of a sentence, and the first person to see that
    ///   is a player.
    ///
    ///   The characters are checked for real. A curly quote or an em dash pasted in from a document
    ///   survives every test that only reads the string back in C# and then arrives on a device as
    ///   mojibake.
    /// </summary>
    [TestFixture]
    public class BetrayalPromptsTests
    {
        [Test]
        public void Act_ReadsExactlyAsAuthored()
        {
            Assert.That(BetrayalPrompts.Act.Message, Is.EqualTo(
                "<size=110%><b>Are you sure you want to \ntrigger a Betrayal?</b></size>"
                + "\n\n<size=85%>This is a <color=#FFD700>once-per-match</color> global ability. "
                + "\nOnce used, neither player can betray again."
                + "\n\nYou must have a <color=#FF9800>Retribution</color> move ready to execute the betrayer, "
                + "otherwise they will permanently \n<color=#FF9800>defect</color> to the enemy team!</size>"
                + "\n\n<size=75%><i>Press Continue to commit, or Back to cancel.</i></size>"));
        }

        [Test]
        public void SkipRetribution_ReadsExactlyAsAuthored()
        {
            Assert.That(BetrayalPrompts.SkipRetribution.Message, Is.EqualTo(
                "<size=110%><b>Are you sure you want to<br>spare the betrayer?</b></size>"
                + "\n\n<size=85%>Skipping your <b><color=#FF9800>Retribution</color></b> move means<br>"
                + "you are choosing not to execute them."
                + "\n\nThe piece will immediately and<br>permanently <b><color=#FF9800>defect</color></b> "
                + "to the enemy team!</size>"
                + "\n\n<size=75%><i>Press Continue to commit, or Back to cancel.</i></size>"));
        }

        [TestCaseSource(nameof(EveryPrompt))]
        public void EveryPrompt_ClosesEveryTagItOpens(string name, ConfirmationRequest prompt)
        {
            Assert.That(UnclosedTagIn(prompt.Message), Is.Null,
                $"{name} leaves a tag open, which renders as visible markup rather than failing.");
        }

        /// <summary>
        /// Compares code points as numbers on purpose. Handing NUnit two chars and asking which is
        /// smaller has let non-ASCII through here before — the assertion reads perfectly and passes
        /// anyway, which is the worst way for a check like this to fail.
        /// </summary>
        [TestCaseSource(nameof(EveryPrompt))]
        public void EveryPrompt_IsPlainAscii(string name, ConfirmationRequest prompt)
        {
            foreach (char c in prompt.Message)
            {
                Assert.That((int)c, Is.LessThan(128),
                    $"{name} carries '{c}' (U+{(int)c:X4}), which reads correctly in the source and arrives on a device as mojibake.");
            }
        }

        [TestCaseSource(nameof(EveryPrompt))]
        public void EveryPrompt_BreaksLinesWithoutACarriageReturn(string name, ConfirmationRequest prompt)
        {
            Assert.That(prompt.Message, Does.Not.Contain("\r"),
                $"{name} picked up a carriage return, most likely pasted in — one convention, so a "
                + "later edit cannot leave the file with two.");
        }

        /// <summary>
        /// Both prompts end by naming the two buttons out loud, so both have to actually set them.
        /// The panel keeps whatever the previous question left behind, and a hint that names a
        /// button which is no longer on screen is worse than no hint at all.
        /// </summary>
        [TestCaseSource(nameof(EveryPrompt))]
        public void EveryPrompt_NamesTheButtonsItTellsThePlayerToPress(string name, ConfirmationRequest prompt)
        {
            Assert.That(prompt.ConfirmLabel, Is.EqualTo("Continue"), name);
            Assert.That(prompt.CancelLabel, Is.EqualTo("Back"), name);
            Assert.That(prompt.Message, Does.Contain($"Press {prompt.ConfirmLabel} to commit, or {prompt.CancelLabel} to cancel."), name);
        }

        [TestCaseSource(nameof(EveryPrompt))]
        public void EveryPrompt_IsWorthPuttingInFrontOfSomeone(string name, ConfirmationRequest prompt)
        {
            Assert.That(prompt.IsValid, Is.True, name);
        }

        /// <summary>
        /// The Act's soft breaks each keep a space in front of them. Invisible in the source and
        /// invisible in a diff, but the panel centres every line, so losing one moves the text.
        /// </summary>
        [Test]
        public void Act_KeepsTheSpaceInFrontOfEverySoftBreak()
        {
            string message = BetrayalPrompts.Act.Message;

            Assert.That(message, Does.Contain("Are you sure you want to \ntrigger"));
            Assert.That(message, Does.Contain("global ability. \nOnce used"));
            Assert.That(message, Does.Contain("permanently \n<color=#FF9800>defect"));
        }

        // Named through {m} so each case reports as "check(prompt)". Left as the prompt name alone,
        // all six checks against a given prompt report under one identical name, and a run that goes
        // red cannot say which of them caught it.
        private static IEnumerable<TestCaseData> EveryPrompt()
        {
            yield return new TestCaseData(nameof(BetrayalPrompts.Act), BetrayalPrompts.Act)
                .SetName("{m}(Act)");
            yield return new TestCaseData(nameof(BetrayalPrompts.SkipRetribution), BetrayalPrompts.SkipRetribution)
                .SetName("{m}(SkipRetribution)");
        }

        // Tags that stand alone and close nothing, so a scan for unclosed tags must skip them.
        private static readonly HashSet<string> StandaloneTags = new HashSet<string> { "br" };

        /// <summary>
        /// Returns the name of the first tag left open, or null when every one is closed in order.
        /// Deliberately order-sensitive: "&lt;b&gt;&lt;i&gt;text&lt;/b&gt;&lt;/i&gt;" is not the
        /// same document as the properly nested version, and text layout treats it differently.
        /// </summary>
        private static string UnclosedTagIn(string text)
        {
            var open = new Stack<string>();

            foreach (Match match in Regex.Matches(text, "<(/?)([a-zA-Z]+)[^>]*>"))
            {
                bool isClosing = match.Groups[1].Value == "/";
                string name = match.Groups[2].Value;

                if (StandaloneTags.Contains(name)) continue;

                if (!isClosing)
                {
                    open.Push(name);
                    continue;
                }

                if (open.Count == 0 || open.Pop() != name) return name;
            }

            return open.Count > 0 ? open.Peek() : null;
        }
    }
}
