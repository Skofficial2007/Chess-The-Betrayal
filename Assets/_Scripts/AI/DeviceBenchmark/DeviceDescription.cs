using System.Collections.Generic;

namespace ChessTheBetrayal.AI.DeviceBenchmark
{
    /// <summary>
    /// What a report says about the machine a run happened on, and the lines it says it in.
    ///
    /// Hardware, OS and build facts only. No serial number, no install or advertising id, no
    /// user-chosen device name — deliberately, and it is worth saying why rather than leaving the
    /// omission to look accidental. These reports are written to be sent: a tester saves one and
    /// passes it through a chat app or mail to someone they may not know, and it can be forwarded
    /// again from there. A timing number needs to know which chip it ran on. It never needs to know
    /// whose phone that was, and anything carried here that could say so would travel with every
    /// copy of the file forever after. Two reports from the same model stay tellable apart by their
    /// run id and the timestamp in their filename, which is all the attribution this ever needed.
    ///
    /// Filled in by whatever can read the platform (on a device, that is DeviceSearchBenchmark
    /// reading Unity's SystemInfo). Holding the facts as plain values rather than reading them here
    /// is what lets the wording — and the rule above — be checked in a test with no device present.
    /// Assigned by name at its one call site, so no two same-typed fields can quietly swap places.
    /// </summary>
    public sealed class DeviceDescription
    {
        public string Model { get; set; }
        public string OperatingSystem { get; set; }
        public string Processor { get; set; }
        public int ProcessorCount { get; set; }
        public int ProcessorFrequencyMhz { get; set; }
        public string GraphicsDeviceName { get; set; }
        public string GraphicsDeviceVendor { get; set; }
        public string GraphicsApi { get; set; }
        public int SystemMemoryMb { get; set; }
        public int GraphicsMemoryMb { get; set; }
        public int ScreenWidth { get; set; }
        public int ScreenHeight { get; set; }
        public float ScreenDpi { get; set; }
        public string BuildType { get; set; }
        public string ScriptingBackend { get; set; }
        public string Platform { get; set; }

        /// <summary>
        /// The header block, in the order it is read. Unity has no direct "chipset name" API, so the
        /// GPU is labelled for what it actually is here — the closest honest proxy, since a Mali
        /// part implies MediaTek, Exynos or Unisoc and an Adreno one implies Snapdragon. Battery is
        /// not in here: it is the one fact that changes while a run happens, so it is sampled at
        /// both ends instead of stated once.
        /// </summary>
        public IReadOnlyList<string> ToReportLines() => new List<string>
        {
            $"Device model: {Model}",
            $"OS: {OperatingSystem}",
            $"CPU: {Processor} ({ProcessorCount} cores, {ProcessorFrequencyMhz}MHz)",
            $"GPU (chipset proxy): {GraphicsDeviceName} [{GraphicsDeviceVendor}], API {GraphicsApi}",
            $"RAM: {SystemMemoryMb}MB system, {GraphicsMemoryMb}MB graphics",
            $"Screen: {ScreenWidth}x{ScreenHeight} @ {ScreenDpi}dpi",
            $"Build: {BuildType}, {ScriptingBackend}, platform {Platform}",
        };
    }
}
