namespace ChessTheBetrayal.Core.Data
{
    /// <summary>
    /// Mutable value-type snapshot of the clock at a single instant.
    /// UI and network layers read a struct copy to prevent heap allocation.
    /// </summary>
    public struct ClockState
    {
        public long WhiteRemainingMs;
        public long BlackRemainingMs;

        /// <summary>Whose clock is currently counting down.</summary>
        public Team ActiveSide;

        /// <summary>False before the first move and during paused phases.</summary>
        public bool IsRunning;

        /// <summary>Latches to true once either side hits zero.</summary>
        public bool IsExpired;

        /// <summary>
        /// Returns the remaining time for the specified team.
        /// </summary>
        public long GetRemaining(Team team)
        {
            return team == Team.White ? WhiteRemainingMs : BlackRemainingMs;
        }

        /// <summary>
        /// Writes the remaining time for the specified team.
        /// </summary>
        public void SetRemaining(Team team, long ms)
        {
            if (team == Team.White)
            {
                WhiteRemainingMs = ms;
            }
            else
            {
                BlackRemainingMs = ms;
            }
        }
    }
}