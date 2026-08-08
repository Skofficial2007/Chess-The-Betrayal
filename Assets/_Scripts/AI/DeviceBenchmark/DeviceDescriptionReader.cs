using UnityEngine;

namespace ChessTheBetrayal.AI.DeviceBenchmark
{
    /// <summary>
    /// The one place that asks Unity what machine this is. Everything that reports a number off a
    /// device wants the same facts in the same words — the benchmark and an in-match AI report
    /// both do — and a second copy of this would let two reports from the same phone describe it
    /// differently, which is exactly the sort of difference someone reading them later would try to
    /// explain.
    ///
    /// What is deliberately left out, and why, is DeviceDescription's own business.
    /// </summary>
    public static class DeviceDescriptionReader
    {
        /// <summary>
        /// Development Build and the scripting backend are the exception to reading the platform at
        /// runtime: both are baked in at build time rather than queryable from a running app, so the
        /// preprocessor symbols are the only honest way to report which one this build actually is.
        /// </summary>
        public static DeviceDescription Read()
        {
#if DEVELOPMENT_BUILD
            const string buildType = "Development Build";
#else
            const string buildType = "Release Build";
#endif
#if ENABLE_IL2CPP
            const string scriptingBackend = "IL2CPP";
#else
            const string scriptingBackend = "Mono";
#endif
            return new DeviceDescription
            {
                Model = SystemInfo.deviceModel,
                OperatingSystem = SystemInfo.operatingSystem,
                Processor = SystemInfo.processorType,
                ProcessorCount = SystemInfo.processorCount,
                ProcessorFrequencyMhz = SystemInfo.processorFrequency,
                GraphicsDeviceName = SystemInfo.graphicsDeviceName,
                GraphicsDeviceVendor = SystemInfo.graphicsDeviceVendor,
                GraphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                SystemMemoryMb = SystemInfo.systemMemorySize,
                GraphicsMemoryMb = SystemInfo.graphicsMemorySize,
                ScreenWidth = Screen.width,
                ScreenHeight = Screen.height,
                ScreenDpi = Screen.dpi,
                BuildType = buildType,
                ScriptingBackend = scriptingBackend,
                Platform = Application.platform.ToString(),
            };
        }

        /// <summary>The same facts already formatted, for a caller that only wants to hand them to
        /// a report rather than read any single one of them.</summary>
        public static System.Collections.Generic.IReadOnlyList<string> ReadReportLines() =>
            Read().ToReportLines();

        /// <summary>The device model on its own, for anything that has to name this device outside
        /// a report — a filename, say.</summary>
        public static string Model => SystemInfo.deviceModel;
    }
}
