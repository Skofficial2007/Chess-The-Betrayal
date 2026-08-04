namespace ChessTheBetrayal.AI.DeviceBenchmark
{
    /// <summary>
    /// Decides whether starting a run is safe, or would throw away a finished report nobody kept.
    ///
    /// A run replaces the previous one's report outright — there is only ever one, with no history
    /// behind it. That is what keeps two readings from blending into a single misleading average,
    /// but it also means a finished run that was never saved is gone the moment the next one
    /// begins. The first device to run both plans back to back lost its tester numbers exactly that
    /// way, and had no way of knowing until the file arrived with one tier in it.
    ///
    /// So the first press after an unsaved run only warns, and a second press goes ahead. Both
    /// start buttons share the one warning, since either of them discards the same report and being
    /// told once is enough.
    ///
    /// No Unity types here, so the whole rule can be checked in a plain test instead of by pressing
    /// buttons in a scene and watching what happens.
    /// </summary>
    public sealed class UnsavedReportGuard
    {
        /// <summary>True once a run has finished and its report has not been saved since.</summary>
        public bool HasUnsavedReport { get; private set; }

        /// <summary>True when a start has already been refused once and the next one will go
        /// through — what a screen reads to know it should be showing the warning.</summary>
        public bool IsAwaitingConfirmation { get; private set; }

        /// <summary>
        /// Whether the caller should actually start the run. False means this press was spent on
        /// the warning instead; pressing again returns true and gives up the old report.
        /// </summary>
        public bool RequestStart()
        {
            if (HasUnsavedReport && !IsAwaitingConfirmation)
            {
                IsAwaitingConfirmation = true;
                return false;
            }

            HasUnsavedReport = false;
            IsAwaitingConfirmation = false;
            return true;
        }

        /// <summary>
        /// The warning was declined — whoever it asked chose to keep the report. The next start
        /// press has to ask all over again rather than sailing through on a confirmation that was
        /// answered "no", which is the difference between a question and a formality.
        /// </summary>
        public void CancelConfirmation() => IsAwaitingConfirmation = false;

        /// <summary>A run finished, so from here on there is a report worth not losing.</summary>
        public void NoteRunCompleted()
        {
            HasUnsavedReport = true;
            IsAwaitingConfirmation = false;
        }

        /// <summary>
        /// Someone tried to save the report. The warning comes down either way, since it asked a
        /// question and this is the answer to it — but only a write that actually landed makes the
        /// report safe to replace. A failed one leaves the next start press to warn all over again,
        /// which is exactly what should happen: the report is every bit as much at risk as it was
        /// before, and treating a failed save as good enough would hide the one failure that
        /// matters most.
        /// </summary>
        public void NoteShareAttempted(bool saved)
        {
            IsAwaitingConfirmation = false;
            if (saved) HasUnsavedReport = false;
        }
    }
}
