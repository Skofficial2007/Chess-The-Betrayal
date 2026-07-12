namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// Resolution seam between a stored profile id (e.g. from PracticeMatchSettings.AiProfileId)
    /// and the AIProfile data it names. Never throws for a bad id — falls back to a safe default,
    /// since a corrupt PlayerPrefs value must not crash match setup.
    /// </summary>
    public interface IAIProfileProvider
    {
        AIProfile Resolve(string id);
    }
}
