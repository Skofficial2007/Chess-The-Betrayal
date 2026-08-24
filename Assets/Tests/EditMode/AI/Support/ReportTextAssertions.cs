using NUnit.Framework;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Assertions about text this project writes for a human to read — a benchmark report, a
    /// telemetry dump, anything that ends up in a log file or on a phone screen.
    /// </summary>
    internal static class ReportTextAssertions
    {
        /// <summary>Compares code points rather than chars: NUnit's LessThan on a char argument does
        /// not fail the way it reads, which let an em-dash through a green run of this very
        /// check.</summary>
        internal static void AssertPlainAscii(string text)
        {
            foreach (char c in text)
            {
                if (c < 128) continue;

                Assert.Fail($"Non-ASCII character '{c}' (U+{(int)c:X4}) in:\n{text}");
            }
        }
    }
}
