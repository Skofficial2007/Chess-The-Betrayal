using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ChessTheBetrayal.AI.DeviceBenchmark;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// The header block of a report that gets sent to other people. Pulled out of the MonoBehaviour
    /// that reads SystemInfo so both halves can be checked here: that the lines say what they should,
    /// and — the reason this fixture is worth having at all — that they do not say anything about
    /// whose device it was.
    /// </summary>
    [TestFixture]
    public class DeviceDescriptionTests
    {
        /// <summary>
        /// A whitelist rather than a search for today's known offenders. A report once carried the
        /// device's unique identifier and its owner-chosen name, and nothing failed when they were
        /// there — so this asserts the exact set of facts allowed out, and any new one has to be
        /// added here deliberately by someone who has read why the list is short.
        /// </summary>
        [Test]
        public void ToReportLines_CarryOnlyHardwareOsAndBuildFacts_NeverAnIdentifierOrAnOwnersName()
        {
            IReadOnlyList<string> lines = SampleDescription().ToReportLines();

            IEnumerable<string> labels = lines.Select(l => l.Contains(':') ? l.Substring(0, l.IndexOf(':')) : l);
            Assert.That(labels, Is.EquivalentTo(new[]
            {
                "Device model", "OS", "CPU", "GPU (chipset proxy)", "RAM", "Screen", "Build",
            }));
        }

        [Test]
        public void Read_InTheEditor_DoesNotClaimToBeABuild()
        {
            // These tests run in the Editor, which is neither a Release nor a Development build.
            // Falling through to "Release Build" put that on the header of every report a Play-mode
            // session produced, where in a table it reads as a measurement taken under the same
            // conditions as a real device row.
            Assert.That(DeviceDescriptionReader.Read().BuildType, Does.Not.Contain("Release Build"));
            Assert.That(DeviceDescriptionReader.Read().BuildType, Does.Contain("Editor"));
        }

        [Test]
        public void ToReportLines_SayNothingAboutTheValuesThatUsedToIdentifyTheOwner()
        {
            var description = SampleDescription();

            string text = string.Join("\n", description.ToReportLines());

            Assert.That(text, Does.Not.Contain("unique"),
                "A stable per-device id travels with every copy of a forwarded file and tells a " +
                "timing result nothing it needs.");
            Assert.That(text, Does.Not.Contain("Device name"),
                "On Android that field is whatever the owner typed into their settings, which is " +
                "often their own name.");
        }

        [Test]
        public void ToReportLines_DescribeTheHardwareWellEnoughToTellTwoChipsApart()
        {
            IReadOnlyList<string> lines = SampleDescription().ToReportLines();
            string text = string.Join("\n", lines);

            Assert.That(text, Does.Contain("TestPhone"));
            Assert.That(text, Does.Contain("ARM64 FP ASIMD AES (8 cores, 2400MHz)"));
            Assert.That(text, Does.Contain("Mali-G68 MC4 [ARM], API Vulkan"),
                "The GPU is the closest thing to a chipset name Unity will report, so it has to " +
                "carry its vendor and API alongside it.");
            Assert.That(text, Does.Contain("7636MB system, 7599MB graphics"));
            Assert.That(text, Does.Contain("1080x2460"));
        }

        /// <summary>
        /// A number captured under a different scripting backend or a development build is not
        /// comparable with one that wasn't, so the report has to say which it was — this is the line
        /// that makes a device row honest about what it is being compared against.
        /// </summary>
        [Test]
        public void ToReportLines_StateTheBuildConfigurationTheRunHappenedUnder()
        {
            IReadOnlyList<string> lines = SampleDescription().ToReportLines();

            Assert.That(lines.Single(l => l.StartsWith("Build:")),
                Is.EqualTo("Build: Release Build, IL2CPP, platform Android"));
        }

        [Test]
        public void ToReportLines_PutTheDeviceModelFirst_SinceItIsWhatNamesTheWholeReport()
        {
            IReadOnlyList<string> lines = SampleDescription().ToReportLines();

            Assert.That(lines[0], Does.StartWith("Device model:"));
        }

        private static DeviceDescription SampleDescription() => new DeviceDescription
        {
            Model = "TestPhone",
            OperatingSystem = "Android OS 14 / API-34",
            Processor = "ARM64 FP ASIMD AES",
            ProcessorCount = 8,
            ProcessorFrequencyMhz = 2400,
            GraphicsDeviceName = "Mali-G68 MC4",
            GraphicsDeviceVendor = "ARM",
            GraphicsApi = "Vulkan",
            SystemMemoryMb = 7636,
            GraphicsMemoryMb = 7599,
            ScreenWidth = 1080,
            ScreenHeight = 2460,
            ScreenDpi = 384f,
            BuildType = "Release Build",
            ScriptingBackend = "IL2CPP",
            Platform = "Android",
        };
    }
}
