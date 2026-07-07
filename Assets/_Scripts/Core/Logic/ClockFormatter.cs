using System.Text;

namespace ChessTheBetrayal.Core.Logic
{
    /// <summary>
    /// Formats a remaining-time value into the mm:ss text shown on the clock.
    /// Writes into a StringBuilder the caller owns and caches the two-digit
    /// strings up front, so refreshing the clock every frame does not build a
    /// fresh string each time.
    /// </summary>
    public static class ClockFormatter
    {
        private static readonly string[] TwoDigits;

        static ClockFormatter()
        {
            TwoDigits = new string[60];
            for (int i = 0; i < 60; i++)
            {
                TwoDigits[i] = i.ToString("D2");
            }
        }

        /// <summary>
        /// Formats the total milliseconds into the provided StringBuilder in-place.
        /// Format is M:SS when minutes > 0, and SS.d (tenths precision) for low-time urgency.
        /// </summary>
        public static void FormatInto(StringBuilder sb, long totalMs)
        {
            sb.Clear();

            if (totalMs <= 0L)
            {
                sb.Append("0:00");
                return;
            }

            long totalSeconds = totalMs / 1000L;
            int  minutes      = (int)(totalSeconds / 60L);
            int  seconds      = (int)(totalSeconds % 60L);

            if (minutes > 0)
            {
                sb.Append(minutes);
                sb.Append(':');
                sb.Append(TwoDigits[seconds]);
            }
            else
            {
                long tenths = (totalMs % 1000L) / 100L;
                sb.Append(TwoDigits[seconds]);
                sb.Append('.');
                sb.Append(tenths);
            }
        }
    }
}
