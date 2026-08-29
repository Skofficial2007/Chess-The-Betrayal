namespace ChessTheBetrayal.Tests.EditMode.Support
{
    /// <summary>
    /// Names for the test categories, so a mistyped one cannot quietly drop a fixture out of both
    /// halves of the suite at once.
    /// </summary>
    internal static class TestCategories
    {
        /// <summary>
        /// Tests that play chess or run a real search against a real clock, rather than checking a
        /// decision that can be made in memory.
        ///
        /// There are about a hundred of them and they account for roughly ninety-five percent of the
        /// time the suite takes — the other thirteen hundred finish in well under a minute between
        /// them. None of it is waste: a ladder inversion and a race between search workers can only
        /// be caught by playing games, and each of these fixtures explains beside itself why it
        /// costs what it does. They are simply a different kind of test from the rest, and pricing
        /// every edit as though they were the same is what makes the suite feel slow.
        ///
        /// Unity gives every test with no category the name "Uncategorized" and offers it in the
        /// Test Runner's category dropdown, so marking these is all that is needed to be able to run
        /// everything else on its own — there is no "everything except" filter to write.
        ///
        /// Do NOT put this on an [Explicit] fixture. A category filter counts as asking for a test
        /// by name, so it would start running the recording harnesses, one of which is allowed eight
        /// hours.
        /// </summary>
        public const string Slow = "Slow";

        /// <summary>
        /// The recording harnesses and long measurement runs, every one of them already [Explicit]
        /// so that neither Run All nor an ordinary filter starts one by accident.
        ///
        /// They need a name of their own because Unity calls anything without a category
        /// "Uncategorized", and asking for that by name counts as asking for a test by name — which
        /// is enough to start an [Explicit] one. Without this, running the quick half of the suite
        /// would kick off a capture allowed to run for eleven minutes and a book scan allowed eight
        /// hours.
        /// </summary>
        public const string OnDemand = "OnDemand";
    }
}
