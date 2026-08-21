namespace ChessTheBetrayal.Core.Data
{
    /// <summary>
    /// Player-facing AI difficulty selection. Currently a UI-only distinction — Easy and Hard both
    /// resolve to the same AISearchSettings.Ultimate() depth-7 search as Normal until the difficulty
    /// levels ticket lands (see AI_System_Design doc: deferred, non-blocking).
    /// Kept as a real enum now so the UI and PracticeMatchSettings don't need to change shape later.
    /// </summary>
    public enum AIDifficulty
    {
        Easy,
        Normal,
        Hard
    }
}
