using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ChessTheBetrayal.Gameplay.Manager
{
    /// <summary>
    /// Writes a long diagnostic to the log in pieces, because Android drops the end of one that
    /// arrives whole.
    ///
    /// A log entry on Android is capped at roughly four kilobytes and the rest is discarded with no
    /// mark where the cut happened. A capture off a real phone has two entries ending mid-identifier,
    /// which is what makes this worth fixing rather than assuming: the move log goes out as a single
    /// entry, so a game long enough to cross the cap loses its most recent plies and reads as a
    /// complete record of a shorter game. Nothing warns anybody, and the plies that go missing are
    /// the ones nearest whatever the log was opened to explain.
    ///
    /// Splitting is on line boundaries, so a ply is never cut in half, and every piece says which
    /// one it is out of how many, so a reader can see at a glance whether one failed to arrive.
    /// </summary>
    public static class LongLogMessage
    {
        /// <summary>
        /// Comfortably inside Android's own limit rather than up against it. The cap covers the tag
        /// and the priority as well as the text, and it counts bytes where this counts characters,
        /// so the two only agree while everything stays ASCII. Leaving room costs an extra entry on
        /// a long game and removes the need for either of those to be exactly right.
        /// </summary>
        public const int MaxCharactersPerEntry = 3000;

        /// <summary>Writes <paramref name="body"/> under <paramref name="header"/>, in as many
        /// entries as it takes. A body that fits goes out as one entry reading exactly as it did
        /// before this existed, so the common case is undisturbed.</summary>
        public static void Write(string header, string body)
        {
            IReadOnlyList<string> parts = Split(body, MaxCharactersPerEntry);

            if (parts.Count <= 1)
            {
                Debug.Log($"{header}:\n{body}");
                return;
            }

            for (int i = 0; i < parts.Count; i++)
            {
                Debug.Log($"{header}, part {i + 1} of {parts.Count}:\n{parts[i]}");
            }
        }

        /// <summary>
        /// The decision half, kept free of Unity so it can be checked without a scene: how a body
        /// divides into entries no longer than <paramref name="maxCharacters"/>.
        ///
        /// A single line longer than the limit is emitted on its own and over length rather than
        /// being cut. Nothing this logs produces one - a ply is a couple of dozen characters - and a
        /// silent truncation in the code that exists to prevent silent truncation would be worse
        /// than an entry the platform trims for us.
        /// </summary>
        public static IReadOnlyList<string> Split(string body, int maxCharacters)
        {
            var parts = new List<string>();
            if (string.IsNullOrEmpty(body)) return parts;

            var current = new StringBuilder();
            foreach (string line in body.Split('\n'))
            {
                // +1 for the newline this line will be joined back with.
                if (current.Length > 0 && current.Length + line.Length + 1 > maxCharacters)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }

                if (current.Length > 0) current.Append('\n');
                current.Append(line);
            }

            if (current.Length > 0) parts.Add(current.ToString());
            return parts;
        }
    }
}
