using System;
using System.Text;

namespace ChessTheBetrayal.AI.DeviceBenchmark
{
    /// <summary>
    /// Names a saved report after the device that produced it and the moment it ran.
    ///
    /// Both parts earn their place. Reports arrive from several testers at different times and end
    /// up in one folder together, so a name that doesn't say which phone it came from is a report
    /// nobody can attribute, and two runs from the same phone must not overwrite each other. The
    /// device model is whatever the phone reports about itself, which is free-form text that
    /// regularly contains spaces, commas and occasionally characters no filesystem will accept — so
    /// it is reduced to something safe rather than trusted.
    /// </summary>
    public static class BenchmarkReportFileName
    {
        private const string Prefix = "chess-ai-benchmark";
        private const string UnknownDevice = "unknown-device";

        // Long enough to tell two phones apart, short enough that the whole name stays readable and
        // well inside any filesystem's limit. Some devices report remarkably verbose model strings.
        private const int MaxDeviceNameLength = 40;

        public static string Build(string deviceModel, DateTime timestamp) =>
            $"{Prefix}_{Sanitize(deviceModel)}_{timestamp:yyyyMMdd-HHmmss}.txt";

        /// <summary>
        /// Keeps letters, digits and dashes; everything else becomes a dash, with runs collapsed so
        /// a model like "samsung SM-F415F (GT)" doesn't turn into a hedge of them. Anything that
        /// reduces to nothing at all falls back to a placeholder — a report named after an empty
        /// string is worse than one that says plainly it doesn't know the device.
        /// </summary>
        private static string Sanitize(string deviceModel)
        {
            if (string.IsNullOrWhiteSpace(deviceModel)) return UnknownDevice;

            var safe = new StringBuilder(deviceModel.Length);
            foreach (char c in deviceModel)
            {
                if (char.IsLetterOrDigit(c))
                    safe.Append(c);
                else if (safe.Length > 0 && safe[safe.Length - 1] != '-')
                    safe.Append('-');

                if (safe.Length >= MaxDeviceNameLength) break;
            }

            while (safe.Length > 0 && safe[safe.Length - 1] == '-') safe.Length--;

            return safe.Length == 0 ? UnknownDevice : safe.ToString();
        }
    }
}
