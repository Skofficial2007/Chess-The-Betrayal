using System.Text;

namespace ChessTheBetrayal.UI.Controls
{
    /// <summary>
    /// Composes the text of a warning raised over the board, mid-match.
    ///
    /// Three parts, in the order someone reads them:
    ///
    ///   Headline — the question itself, and the largest thing on the panel. Someone who reads one
    ///              line has to read the one they are actually answering.
    ///   Body     — what agreeing costs them. Smaller, because it explains rather than asks.
    ///   Hint     — which button does which. The smallest thing there, and italic: by the time
    ///              anyone needs it they have already decided, so it reminds rather than instructs.
    ///
    /// Deliberately a different shape from <see cref="WarningMessage"/> rather than a setting on it.
    /// That one ends on a large instruction because the thing it warns about is fixed by pressing a
    /// button somewhere else on the screen. This one ends quietly, because the question at the top
    /// already names both answers and they are directly below it. A third shape later is another
    /// small class beside these two, not another flag inside one of them.
    ///
    /// Sizes are relative, never absolute, so the panel's own font size stays the one place the
    /// overall scale is set and these hold up on a phone and a tablet alike.
    ///
    /// Plain string building with no Unity types, so the exact markup can be pinned by a test
    /// instead of checked by eye against a running scene.
    /// </summary>
    public static class MatchWarningMessage
    {
        private const string HeadlineSize = "110%";
        private const string BodySize = "85%";
        private const string HintSize = "75%";

        /// <summary>
        /// Builds the panel text. Pass null or empty for body or hint to leave that part out
        /// entirely, blank line and all — a one-line question stays one line rather than growing
        /// empty sections to fill a shape.
        ///
        /// Line breaks inside any part are the caller's to place and are passed through untouched.
        /// Where a line reads better broken at a particular word, only the caller can know that.
        /// </summary>
        public static string Build(string headline, string body = null, string hint = null)
        {
            var text = new StringBuilder();
            text.Append($"<size={HeadlineSize}><b>{headline}</b></size>");

            if (!string.IsNullOrEmpty(body))
                text.Append($"\n\n<size={BodySize}>{body}</size>");

            if (!string.IsNullOrEmpty(hint))
                text.Append($"\n\n<size={HintSize}><i>{hint}</i></size>");

            return text.ToString();
        }
    }
}
