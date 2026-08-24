using NUnit.Framework;
using ChessTheBetrayal.AI.DeviceBenchmark;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// The rule that keeps a finished benchmark report from being thrown away by the next press.
    /// It is a handful of booleans on purpose: pulled out of the screen it belongs to, it can be
    /// walked through every order a tester might press things in, which is not something anyone was
    /// ever going to do by hand in Play mode against a two-minute run.
    /// </summary>
    [TestFixture]
    public class UnsavedReportGuardTests
    {
        [Test]
        public void RequestStart_WithNoReportToLose_StartsImmediately()
        {
            var guard = new UnsavedReportGuard();

            Assert.That(guard.RequestStart(), Is.True, "The very first run has nothing behind it to discard.");
            Assert.That(guard.IsAwaitingConfirmation, Is.False);
        }

        [Test]
        public void RequestStart_WithAnUnsavedReport_RefusesTheFirstPressAndAsksForConfirmation()
        {
            var guard = new UnsavedReportGuard();
            guard.NoteRunCompleted();

            Assert.That(guard.RequestStart(), Is.False,
                "This is the press that lost a real device's tester numbers — it has to buy a warning, not a run.");
            Assert.That(guard.IsAwaitingConfirmation, Is.True);
            Assert.That(guard.HasUnsavedReport, Is.True, "Refusing must not quietly mark the report as dealt with.");
        }

        [Test]
        public void RequestStart_PressedAgainAfterTheWarning_GoesAhead()
        {
            var guard = new UnsavedReportGuard();
            guard.NoteRunCompleted();
            guard.RequestStart();

            Assert.That(guard.RequestStart(), Is.True);
            Assert.That(guard.IsAwaitingConfirmation, Is.False);
            Assert.That(guard.HasUnsavedReport, Is.False, "The old report is gone now; the new run owns the screen.");
        }

        [Test]
        public void RequestStart_AfterTheReportWasSaved_StartsWithNoWarning()
        {
            var guard = new UnsavedReportGuard();
            guard.NoteRunCompleted();
            guard.NoteShareAttempted(saved: true);

            Assert.That(guard.RequestStart(), Is.True,
                "A report already on disk is not something a second run can lose.");
            Assert.That(guard.IsAwaitingConfirmation, Is.False);
        }

        /// <summary>
        /// The failure worth being careful about: a tester presses Share, the write fails, and the
        /// tool has to keep treating the report as at risk rather than assuming the button worked.
        /// </summary>
        [Test]
        public void RequestStart_AfterAShareThatFailedToSave_StillWarnsFirst()
        {
            var guard = new UnsavedReportGuard();
            guard.NoteRunCompleted();
            guard.NoteShareAttempted(saved: false);

            Assert.That(guard.HasUnsavedReport, Is.True);
            Assert.That(guard.RequestStart(), Is.False,
                "Nothing was written, so the report is exactly as easy to lose as it was a moment ago.");
        }

        /// <summary>
        /// Backing out of the warning has to genuinely mean no. If declining left the confirmation
        /// armed, the very next press — possibly a stray one — would start the run the tester just
        /// said they did not want.
        /// </summary>
        [Test]
        public void RequestStart_AfterTheWarningWasDeclined_WarnsAgainRatherThanStarting()
        {
            var guard = new UnsavedReportGuard();
            guard.NoteRunCompleted();
            guard.RequestStart();

            guard.CancelConfirmation();

            Assert.That(guard.IsAwaitingConfirmation, Is.False);
            Assert.That(guard.HasUnsavedReport, Is.True, "Declining does not save anything.");
            Assert.That(guard.RequestStart(), Is.False,
                "The next press starts the question over; it does not inherit a 'no' as a 'yes'.");
        }

        [Test]
        public void CancelConfirmation_WithNothingToConfirm_ChangesNothing()
        {
            var guard = new UnsavedReportGuard();
            guard.NoteRunCompleted();

            guard.CancelConfirmation();

            Assert.That(guard.HasUnsavedReport, Is.True);
            Assert.That(guard.RequestStart(), Is.False, "Still an unsaved report, so still a warning.");
        }

        [Test]
        public void NoteShareAttempted_TakesTheWarningDownWhetherOrNotTheWriteWorked()
        {
            var guard = new UnsavedReportGuard();
            guard.NoteRunCompleted();
            guard.RequestStart();

            guard.NoteShareAttempted(saved: false);

            Assert.That(guard.IsAwaitingConfirmation, Is.False,
                "The warning asked the tester to share; they did. Leaving it up would be asking twice, " +
                "and would hide the message saying the write failed.");
        }

        [Test]
        public void NoteRunCompleted_ClearsAWarningLeftOverFromBeforeTheRun()
        {
            var guard = new UnsavedReportGuard();
            guard.NoteRunCompleted();
            guard.RequestStart();
            guard.RequestStart();

            guard.NoteRunCompleted();

            Assert.That(guard.IsAwaitingConfirmation, Is.False,
                "A fresh report is not the one the tester was warned about.");
            Assert.That(guard.HasUnsavedReport, Is.True);
        }

        /// <summary>
        /// The sequence the real device actually performed: run the tester plan, never share, start
        /// the thermal plan. It now takes two presses to reach the state that silently ate the first
        /// run's numbers.
        /// </summary>
        [Test]
        public void TesterRunThenThermalRunWithoutSharing_TakesTwoPressesToDiscardTheFirstReport()
        {
            var guard = new UnsavedReportGuard();

            Assert.That(guard.RequestStart(), Is.True, "Tester run starts.");
            guard.NoteRunCompleted();

            Assert.That(guard.RequestStart(), Is.False, "First press at Long Run only warns.");
            Assert.That(guard.RequestStart(), Is.True, "Second press accepts losing the tester report.");
        }
    }
}
