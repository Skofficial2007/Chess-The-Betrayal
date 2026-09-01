using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ChessTheBetrayal.AI.DeviceBenchmark;

namespace ChessTheBetrayal.Tests.EditMode.AI.DeviceBenchmark
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
                // Names the software, not the phone: a version somebody chose and an id regenerated
                // per build. Neither is derived from the device or from whoever is holding it, which
                // is the question this list exists to keep asking.
                "App version",
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
        /// A number captured under a different scripting backend, a development build or the
        /// narrower of the two binaries is not comparable with one that wasn't, so the report has to
        /// say which it was — this is the line that makes a device row honest about what it is being
        /// compared against.
        /// </summary>
        [Test]
        public void ToReportLines_StateTheBuildConfigurationTheRunHappenedUnder()
        {
            IReadOnlyList<string> lines = SampleDescription().ToReportLines();

            Assert.That(lines.Single(l => l.StartsWith("Build:")),
                Is.EqualTo("Build: Release Build, IL2CPP, 64-bit, platform Android"));
        }

        /// <summary>
        /// The case the bit width is carried for. An Android build ships both binaries and the phone
        /// picks one; the 32-bit binary searches fewer positions in the same milliseconds, so its row
        /// reads as a slower phone unless the report says which ran. Both widths are asserted here
        /// rather than only the one this machine is, because no machine that runs these tests will
        /// ever produce the 32-bit case on its own.
        /// </summary>
        [Test]
        public void ToReportLines_SayWhichOfTheTwoBinariesRan()
        {
            DeviceDescription narrow = SampleDescription();
            narrow.ProcessBits = 32;

            Assert.That(narrow.ToReportLines().Single(l => l.StartsWith("Build:")), Does.Contain("32-bit"));
            Assert.That(SampleDescription().ToReportLines().Single(l => l.StartsWith("Build:")),
                Does.Contain("64-bit"));
        }

        /// <summary>
        /// The one way this fails quietly: a width nobody filled in reports every run as "0-bit",
        /// which is wrong rather than merely absent, and a reader has no way to tell that from a
        /// real answer.
        /// </summary>
        [Test]
        public void Read_ReportsARealBitWidth_NeverAnUnsetZero()
        {
            Assert.That(DeviceDescriptionReader.Read().ProcessBits, Is.EqualTo(64).Or.EqualTo(32));
        }

        [Test]
        public void ToReportLines_PutTheDeviceModelFirst_SinceItIsWhatNamesTheWholeReport()
        {
            IReadOnlyList<string> lines = SampleDescription().ToReportLines();

            Assert.That(lines[0], Does.StartWith("Device model:"));
        }

        /// <summary>
        /// The report a tester sends back has to name the build that produced it, or it cannot be
        /// read against the code. Both halves are asserted because they answer different questions:
        /// the version is what a person quotes, and the id is what actually separates two builds
        /// carrying the same version, which is most of them.
        /// </summary>
        [Test]
        public void ToReportLines_NameTheBuildThatProducedThem()
        {
            string line = SampleDescription().ToReportLines().Single(l => l.StartsWith("App version:"));

            Assert.That(line, Does.Contain("0.4.1"));
            Assert.That(line, Does.Contain("3f9c1d5b7a2e4086"));
        }

        /// <summary>
        /// An editor session was never built, so Unity hands back an empty build id. Saying so beats
        /// printing a line that trails off after the word "build", which reads as a report that was
        /// cut short rather than one honestly reporting it has no build to name.
        /// </summary>
        [Test]
        public void ToReportLines_WithNoBuildIdToReport_SayThereIsNoneRatherThanTrailingOff()
        {
            DeviceDescription editorRun = SampleDescription();
            editorRun.BuildId = string.Empty;

            string line = editorRun.ToReportLines().Single(l => l.StartsWith("App version:"));

            Assert.That(line, Does.Not.EndWith("build "));
            Assert.That(line, Does.Contain("not a build"));
        }

        /// <summary>
        /// The startup log line and the report header are the same sentence from the same place. Two
        /// copies would only have to drift once for a log and the report beside it to claim different
        /// builds, which is worse than neither of them saying anything.
        ///
        /// Asserted against a description with nothing in its build id on purpose. A hand-written
        /// second copy of this line renders identically to the real one whenever every field happens
        /// to be filled in, so a fully-populated sample lets a duplicate pass as the original — this
        /// check was written that way first and stayed green while exactly that was done to it. The
        /// empty field is where a copy has to reproduce the fallback wording or diverge.
        /// </summary>
        [Test]
        public void TheStartupLogLineAndTheReportHeaderAreOneSentence()
        {
            DeviceDescription editorRun = SampleDescription();
            editorRun.BuildId = string.Empty;

            Assert.That(editorRun.ToReportLines(), Does.Contain(editorRun.AppVersionLine));
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
            ProcessBits = 64,
            Platform = "Android",
            AppVersion = "0.4.1",
            BuildId = "3f9c1d5b7a2e4086",
        };
    }
}
