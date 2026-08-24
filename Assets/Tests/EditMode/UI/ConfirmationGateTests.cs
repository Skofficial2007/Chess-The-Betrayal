using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ChessTheBetrayal.UI.Controls;

namespace ChessTheBetrayal.Tests.EditMode.UI
{
    /// <summary>
    /// The gate's whole reason to exist is that a caller can set something aside — a move it has
    /// resolved but not yet played — and be certain of hearing back. Every test here is really the
    /// same question asked from a different angle: can a caller end up waiting forever?
    ///
    /// The panel is stood in for, so these run with no scene, no font asset and no buttons to click.
    /// </summary>
    [TestFixture]
    public class ConfirmationGateTests
    {
        /// <summary>
        /// Stands in for the real panel, including the one detail that matters to the gate: a panel
        /// stops being open the moment a button is pressed, before the answer travels anywhere.
        /// </summary>
        private sealed class FakeConfirmationView : IConfirmationView
        {
            private Action _onConfirm;
            private Action _onCancel;

            public bool IsOpen { get; private set; }
            public int ShowCount { get; private set; }
            public int CloseCount { get; private set; }
            public string Message { get; private set; }
            public string ConfirmLabel { get; private set; }
            public string CancelLabel { get; private set; }

            public void Show(string message, string confirmLabel, string cancelLabel, Action onConfirm, Action onCancel)
            {
                ShowCount++;
                Message = message;
                ConfirmLabel = confirmLabel;
                CancelLabel = cancelLabel;
                _onConfirm = onConfirm;
                _onCancel = onCancel;
                IsOpen = true;
            }

            public void Close()
            {
                CloseCount++;
                IsOpen = false;
            }

            public void PressConfirm()
            {
                IsOpen = false;
                _onConfirm?.Invoke();
            }

            public void PressCancel()
            {
                IsOpen = false;
                _onCancel?.Invoke();
            }
        }

        private static readonly ConfirmationRequest AnyQuestion =
            new ConfirmationRequest("Are you sure?", "Continue", "Back");

        private FakeConfirmationView _view;
        private ConfirmationGate _gate;
        private int _confirmed;
        private int _cancelled;

        [SetUp]
        public void SetUp()
        {
            _view = new FakeConfirmationView();
            _gate = new ConfirmationGate(_view);
            _confirmed = 0;
            _cancelled = 0;
        }

        private void Confirm() => _confirmed++;

        private void Cancel() => _cancelled++;

        [Test]
        public void Ask_PutsTheQuestionUpAndWaits()
        {
            _gate.Ask(enabled: true, AnyQuestion, Confirm, Cancel);

            Assert.That(_view.ShowCount, Is.EqualTo(1));
            Assert.That(_view.Message, Is.EqualTo("Are you sure?"));
            Assert.That(_gate.IsOpen, Is.True);
            Assert.That(_confirmed + _cancelled, Is.Zero,
                "Nobody has pressed anything yet, so nothing has been decided.");
        }

        [Test]
        public void Ask_PassesTheQuestionsOwnButtonLabelsThrough()
        {
            _gate.Ask(enabled: true, new ConfirmationRequest("Delete it?", "Delete", "Keep"), Confirm, Cancel);

            Assert.That(_view.ConfirmLabel, Is.EqualTo("Delete"));
            Assert.That(_view.CancelLabel, Is.EqualTo("Keep"));
        }

        [Test]
        public void Ask_LeavesUnnamedButtonsToThePanel()
        {
            _gate.Ask(enabled: true, new ConfirmationRequest("Are you sure?"), Confirm, Cancel);

            Assert.That(_view.ConfirmLabel, Is.Null);
            Assert.That(_view.CancelLabel, Is.Null,
                "A question with no labels of its own must leave the panel's buttons reading whatever they already did.");
        }

        [Test]
        public void Agreeing_RunsOnlyTheConfirmCallback()
        {
            _gate.Ask(enabled: true, AnyQuestion, Confirm, Cancel);
            _view.PressConfirm();

            Assert.That(_confirmed, Is.EqualTo(1));
            Assert.That(_cancelled, Is.Zero);
            Assert.That(_gate.IsOpen, Is.False);
        }

        [Test]
        public void BackingOut_RunsOnlyTheCancelCallback()
        {
            _gate.Ask(enabled: true, AnyQuestion, Confirm, Cancel);
            _view.PressCancel();

            Assert.That(_cancelled, Is.EqualTo(1));
            Assert.That(_confirmed, Is.Zero);
            Assert.That(_gate.IsOpen, Is.False);
        }

        /// <summary>
        /// The switch a settings screen will eventually drive. Off means the action happens as it
        /// did before any of this existed — same call, same moment.
        /// </summary>
        [Test]
        public void Ask_WithConfirmationTurnedOff_GoesStraightAheadAndShowsNothing()
        {
            _gate.Ask(enabled: false, AnyQuestion, Confirm, Cancel);

            Assert.That(_confirmed, Is.EqualTo(1));
            Assert.That(_cancelled, Is.Zero);
            Assert.That(_view.ShowCount, Is.Zero);
            Assert.That(_gate.IsOpen, Is.False);
        }

        /// <summary>
        /// An Inspector slot nobody filled in must cost the confirmation, never the action. The
        /// alternative is a player who cannot take their turn and no way to tell why from the game.
        /// </summary>
        [Test]
        public void Ask_WithNoPanelWired_GoesAheadRatherThanLeavingTheCallerWaiting()
        {
            var gate = new ConfirmationGate(null);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("ConfirmationGate"));

            gate.Ask(enabled: true, AnyQuestion, Confirm, Cancel);

            Assert.That(_confirmed, Is.EqualTo(1));
            Assert.That(_cancelled, Is.Zero);
        }

        [Test]
        public void Ask_WithNothingWrittenOnIt_BacksOutAndShowsNothing()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("ConfirmationGate"));

            _gate.Ask(enabled: true, new ConfirmationRequest(string.Empty), Confirm, Cancel);

            Assert.That(_cancelled, Is.EqualTo(1));
            Assert.That(_confirmed, Is.Zero);
            Assert.That(_view.ShowCount, Is.Zero,
                "A blank panel still covers the board and still swallows every tap.");
        }

        /// <summary>
        /// Not the same slip as an empty string: a caller that never filled the question in at all
        /// hands over a struct with nothing in any field, and that has to land the same safe way.
        /// </summary>
        [Test]
        public void Ask_WithAQuestionNobodyFilledIn_BacksOutAndShowsNothing()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("ConfirmationGate"));

            _gate.Ask(enabled: true, default, Confirm, Cancel);

            Assert.That(_cancelled, Is.EqualTo(1));
            Assert.That(_view.ShowCount, Is.Zero);
        }

        /// <summary>
        /// The question already up belongs to somebody still waiting on it. Answering it on their
        /// behalf is the exact harm this class exists to prevent, so the newcomer is turned away
        /// instead — and told, so whatever it set aside gets put back.
        /// </summary>
        [Test]
        public void Ask_WhileAQuestionIsAlreadyUp_TurnsTheNewOneAwayAndLeavesTheFirstAlone()
        {
            _gate.Ask(enabled: true, new ConfirmationRequest("First question"), Confirm, Cancel);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("ConfirmationGate"));

            int secondConfirmed = 0;
            int secondCancelled = 0;
            _gate.Ask(enabled: true, new ConfirmationRequest("Second question"),
                () => secondConfirmed++, () => secondCancelled++);

            Assert.That(secondCancelled, Is.EqualTo(1));
            Assert.That(secondConfirmed, Is.Zero);
            Assert.That(_view.ShowCount, Is.EqualTo(1));
            Assert.That(_view.Message, Is.EqualTo("First question"));
            Assert.That(_confirmed + _cancelled, Is.Zero,
                "The first caller is still waiting; nothing has been decided for them.");
        }

        [Test]
        public void Dismiss_TakesTheQuestionDownAndCountsAsBackingOut()
        {
            _gate.Ask(enabled: true, AnyQuestion, Confirm, Cancel);

            _gate.Dismiss();

            Assert.That(_view.CloseCount, Is.EqualTo(1));
            Assert.That(_cancelled, Is.EqualTo(1));
            Assert.That(_confirmed, Is.Zero);
            Assert.That(_gate.IsOpen, Is.False);
        }

        [Test]
        public void Dismiss_WithNothingOnScreen_TellsNobodyAndClosesNothing()
        {
            _gate.Dismiss();

            Assert.That(_view.CloseCount, Is.Zero);
            Assert.That(_confirmed + _cancelled, Is.Zero);
        }

        [Test]
        public void Ask_AfterAnAnswer_WorksAgain()
        {
            _gate.Ask(enabled: true, AnyQuestion, Confirm, Cancel);
            _view.PressConfirm();

            _gate.Ask(enabled: true, AnyQuestion, Confirm, Cancel);

            Assert.That(_view.ShowCount, Is.EqualTo(2),
                "An answered question must leave nothing behind that blocks the next one.");
            Assert.That(_gate.IsOpen, Is.True);
        }

        /// <summary>
        /// One question leading straight to another is an ordinary thing for a caller to want, and
        /// it only works if the gate has finished resetting itself before the answer travels.
        /// </summary>
        [Test]
        public void Answering_LetsTheCallerAskTheNextQuestionFromInsideTheAnswer()
        {
            _gate.Ask(enabled: true, new ConfirmationRequest("First question"),
                () => _gate.Ask(enabled: true, new ConfirmationRequest("Follow-up"), Confirm, Cancel));

            _view.PressConfirm();

            Assert.That(_view.ShowCount, Is.EqualTo(2));
            Assert.That(_view.Message, Is.EqualTo("Follow-up"));
            Assert.That(_gate.IsOpen, Is.True);
        }

        [Test]
        public void Answering_AQuestionAskedWithoutACancelCallback_DoesNotThrow()
        {
            _gate.Ask(enabled: true, AnyQuestion, Confirm);

            Assert.DoesNotThrow(() => _view.PressCancel(),
                "A caller with nothing to undo passes no cancel callback, and backing out must still work.");
            Assert.That(_gate.IsOpen, Is.False);
        }
    }
}
