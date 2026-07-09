using UnityEngine;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// Persists the player's Practice Match Setup choices across sessions via PlayerPrefs. Kept as
    /// a plain static helper (not on PracticeMatchSettings itself) since Core.Data must stay
    /// Unity-free — PlayerPrefs is a UnityEngine API, so this lives in the UI assembly next to its
    /// only caller, AIMatchSettingsUI.
    /// </summary>
    public static class PracticeMatchSettingsStorage
    {
        private const string KeyBetrayalEnabled = "PracticeMatch.BetrayalEnabled";
        private const string KeyAiDefendOnly = "PracticeMatch.AiDefendOnly";
        private const string KeyRetributionSkipAllowed = "PracticeMatch.RetributionSkipAllowed";
        private const string KeyDifficulty = "PracticeMatch.Difficulty";

        /// <summary>Returns the last saved settings, or PracticeMatchSettings.Default if none were ever saved.</summary>
        public static PracticeMatchSettings Load()
        {
            if (!PlayerPrefs.HasKey(KeyBetrayalEnabled))
            {
                return PracticeMatchSettings.Default;
            }

            return new PracticeMatchSettings(
                betrayalEnabled: PlayerPrefs.GetInt(KeyBetrayalEnabled, 1) != 0,
                aiDefendOnly: PlayerPrefs.GetInt(KeyAiDefendOnly, 0) != 0,
                retributionSkipAllowed: PlayerPrefs.GetInt(KeyRetributionSkipAllowed, 1) != 0,
                difficulty: (AIDifficulty)PlayerPrefs.GetInt(KeyDifficulty, (int)AIDifficulty.Normal));
        }

        public static void Save(PracticeMatchSettings settings)
        {
            PlayerPrefs.SetInt(KeyBetrayalEnabled, settings.BetrayalEnabled ? 1 : 0);
            PlayerPrefs.SetInt(KeyAiDefendOnly, settings.AiDefendOnly ? 1 : 0);
            PlayerPrefs.SetInt(KeyRetributionSkipAllowed, settings.RetributionSkipAllowed ? 1 : 0);
            PlayerPrefs.SetInt(KeyDifficulty, (int)settings.Difficulty);
            PlayerPrefs.Save();
        }
    }
}
