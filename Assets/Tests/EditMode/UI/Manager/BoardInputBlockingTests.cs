using System;
using NUnit.Framework;
using ChessTheBetrayal.UI.Controls;
using ChessTheBetrayal.UI.Manager;

namespace ChessTheBetrayal.Tests.EditMode.UI.Manager
{
    /// <summary>
    /// The rule that stops the board reacting while something is on screen.
    ///
    /// The panel half is the obvious one. The half worth a test is the question: a confirmation
    /// blocks everything drawn on its own canvas, so it looks handled, and a tap on the board is a
    /// raycast into the world that never touches a canvas. Removing that clause used to fail
    /// nothing at all, and what it costs is a player tapping through the dimmed panel, picking a
    /// piece up and lighting its moves behind the question still asking about the last one.
    ///
    /// What this cannot see: that the manager still hands its real gate in. That is one argument at
    /// one call site, and pinning it would mean driving a MonoBehaviour that reads six panels out of
    /// a live scene.
    /// </summary>
    [TestFixture]
    public class BoardInputBlockingTests
    {
        [Test]
        public void AQuestionOnScreenStopsTheBoardEvenWithNoPanelOverIt()
        {
            Assert.That(BoardInputBlocking.BlocksTheBoard(false, new StubGate { IsOpen = true }), Is.True);
        }

        [Test]
        public void NothingOnScreenLetsTheBoardThrough()
        {
            Assert.That(BoardInputBlocking.BlocksTheBoard(false, new StubGate { IsOpen = false }), Is.False);
        }

        [Test]
        public void APanelOverTheBoardStopsItWhetherOrNotAQuestionIsUp()
        {
            Assert.That(BoardInputBlocking.BlocksTheBoard(true, new StubGate { IsOpen = false }), Is.True);
            Assert.That(BoardInputBlocking.BlocksTheBoard(true, new StubGate { IsOpen = true }), Is.True);
        }

        [Test]
        public void NoGateAtAllBlocksNothingByItself()
        {
            // Null before the gate is built, and null again on a build with nothing to ask with.
            // Neither is a reason to refuse input, and treating it as one would leave the board
            // dead for the rest of the match.
            Assert.That(BoardInputBlocking.BlocksTheBoard(false, null), Is.False);
            Assert.That(BoardInputBlocking.BlocksTheBoard(true, null), Is.True);
        }

        private sealed class StubGate : IConfirmationGate
        {
            public bool IsOpen { get; set; }

            public void Ask(bool enabled, in ConfirmationRequest request, Action onConfirm, Action onCancel = null) =>
                throw new NotSupportedException("These tests only read whether a question is up.");

            public void Dismiss() =>
                throw new NotSupportedException("These tests only read whether a question is up.");
        }
    }
}
