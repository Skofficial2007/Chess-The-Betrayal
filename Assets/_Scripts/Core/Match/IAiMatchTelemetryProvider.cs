namespace ChessTheBetrayal.Core.Match
{
    /// <summary>
    /// The seam GameOverUI uses to ask whether there's a real-match AI report worth offering to
    /// share, without depending on GameManager or the AI assembly directly — Core has no reference
    /// to AI (AI depends on Core, never the reverse), so this can only ever hand back a plain
    /// string, already rendered by whoever implements it. GameManager implements this.
    /// </summary>
    public interface IAiMatchTelemetryProvider
    {
        /// <summary>The rendered report for the match that just ended, or null if no AI played this
        /// match, telemetry sharing wasn't enabled for it, or the AI never actually got to move.</summary>
        string GetLastAiMatchReport();
    }
}
