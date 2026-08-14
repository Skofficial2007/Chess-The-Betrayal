using System.Text;

namespace ChessTheBetrayal.UI.Controls
{
    /// <summary>
    /// Composes the text of a warning raised on a tool screen — today, the device benchmark's
    /// "you never saved that report" — so every one of them reads the same way.
    ///
    /// One of two shapes a warning can take, not the only one. A warning raised over the board
    /// mid-match ends quietly instead (see <see cref="MatchWarningMessage"/>), because the question
    /// there names both answers and they are directly below it. Here the thing that fixes the
    /// problem is a button somewhere else on the screen, so the panel has to end by pointing at it.
    ///
    /// A warning is three things, and they are not equally important:
    ///
    ///   Headline — what is wrong, stated once and plainly.
    ///   Detail   — the consequence, for whoever wants it. Deliberately the smallest thing on the
    ///              panel: it explains, it does not compete.
    ///   Action   — what to do about it. The LARGEST thing on the panel, larger even than the
    ///              headline, because someone who reads one line should read the one that tells
    ///              them what to press.
    ///
    /// A popup shows whatever string it is handed, so nothing forces a caller through here. What
    /// this buys is that nobody has to hand-write size tags to match a panel they cannot see, and a
    /// change to the house style is one edit rather than a hunt through every call site. Detail and
    /// action are both optional — a one-line question stays one line rather than growing empty
    /// sections to fill a shape.
    ///
    /// Plain string building with no Unity types, so the exact markup can be pinned by a test
    /// instead of checked by eye against a running scene.
    /// </summary>
    public static class WarningMessage
    {
        // Relative sizes, never absolute: the panel's own font size stays the one place the overall
        // scale is set, so these hold up whether the popup is drawn on a phone or a tablet.
        private const string HeadlineSize = "115%";
        private const string DetailSize = "70%";
        private const string ActionSize = "150%";

        // A short run of dots between the headline and the detail. It reads as a break in the
        // message rather than as punctuation, and it is drawn at the detail's own small size so it
        // separates without announcing itself.
        private const string DetailSeparator = "....";

        /// <summary>
        /// Builds the panel text. Pass null or empty for detail or action to leave that part out
        /// entirely, separators and all.
        /// </summary>
        public static string Build(string headline, string detail = null, string action = null)
        {
            var text = new StringBuilder();
            text.Append($"<size={HeadlineSize}><b>{headline}</b></size>");

            if (!string.IsNullOrEmpty(detail))
                text.Append($"\n<size={DetailSize}>{DetailSeparator}\n{detail}</size>");

            if (!string.IsNullOrEmpty(action))
                text.Append($"\n\n<size={ActionSize}><b>{action}</b></size>");

            return text.ToString();
        }
    }
}
