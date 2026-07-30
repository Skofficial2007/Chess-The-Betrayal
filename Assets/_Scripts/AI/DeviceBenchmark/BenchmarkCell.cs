namespace ChessTheBetrayal.AI.DeviceBenchmark
{
    /// <summary>
    /// One unit of work in a benchmark run: search this position, at this tier, for the nth time,
    /// on either a thread-pool worker (what a real match does) or the calling thread (the control
    /// that shows whether a device's scheduler treats the two differently).
    /// </summary>
    public readonly struct BenchmarkCell
    {
        public BenchmarkCell(int positionIndex, string profileId, int repeatIndex, bool onWorkerThread)
        {
            PositionIndex = positionIndex;
            ProfileId = profileId;
            RepeatIndex = repeatIndex;
            OnWorkerThread = onWorkerThread;
        }

        public int PositionIndex { get; }

        /// <summary>The tier's id, not the profile itself — whoever runs the cell resolves it
        /// through the same provider a real match uses, so a guardrail clamp applies here too.</summary>
        public string ProfileId { get; }

        public int RepeatIndex { get; }

        public bool OnWorkerThread { get; }
    }
}
