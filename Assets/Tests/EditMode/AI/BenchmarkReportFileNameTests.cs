using System;
using System.IO;
using NUnit.Framework;
using ChessTheBetrayal.AI.DeviceBenchmark;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Reports come back from several testers and land in one folder together, so the name has to
    /// survive whatever a phone calls itself and still say which device and which run it was.
    /// </summary>
    [TestFixture]
    public class BenchmarkReportFileNameTests
    {
        private static readonly DateTime Moment = new DateTime(2026, 7, 30, 16, 45, 9);

        [Test]
        public void Build_NamesTheDeviceAndTheMomentItRan()
        {
            string name = BenchmarkReportFileName.Build("Pixel8", Moment);

            Assert.That(name, Is.EqualTo("chess-ai-benchmark_Pixel8_20260730-164509.txt"));
        }

        /// <summary>
        /// Real device models are free-form text with spaces, punctuation and occasionally
        /// characters no filesystem accepts. A name containing one of those doesn't produce a
        /// badly-named file, it produces no file at all — the write throws.
        /// </summary>
        [Test]
        public void Build_ReducesADeviceModelToSomethingAFilesystemWillAccept(
            [Values("samsung SM-F415F", "TECNO/LE8", "realme:NARZO 70x", "Xiaomi <22011119UY>",
                "a|b?c*d\"e")] string deviceModel)
        {
            string name = BenchmarkReportFileName.Build(deviceModel, Moment);

            Assert.That(name.IndexOfAny(Path.GetInvalidFileNameChars()), Is.EqualTo(-1),
                $"'{name}' still contains a character the filesystem will refuse.");
            Assert.That(name, Does.StartWith("chess-ai-benchmark_"));
            Assert.That(name, Does.EndWith("_20260730-164509.txt"));
        }

        [Test]
        public void Build_KeepsTheLettersAndDigitsThatIdentifyTheDevice()
        {
            string name = BenchmarkReportFileName.Build("samsung SM-F415F", Moment);

            Assert.That(name, Does.Contain("samsung"));
            Assert.That(name, Does.Contain("SM-F415F"),
                "Two phones from the same maker are told apart by the model, so it has to survive.");
        }

        [Test]
        public void Build_CollapsesRunsOfSeparators_RatherThanLeavingAHedgeOfThem()
        {
            string name = BenchmarkReportFileName.Build("TECNO   ---   LE8 !!!", Moment);

            Assert.That(name, Does.Contain("TECNO-LE8"));
            Assert.That(name, Does.Not.Contain("--"));
        }

        [Test]
        public void Build_WithNothingUsableForADeviceName_SaysSoRatherThanProducingAGap(
            [Values(null, "", "   ", "!!!")] string deviceModel)
        {
            string name = BenchmarkReportFileName.Build(deviceModel, Moment);

            Assert.That(name, Is.EqualTo("chess-ai-benchmark_unknown-device_20260730-164509.txt"));
        }

        [Test]
        public void Build_TrimsAnAbsurdlyLongDeviceName_SoTheWholeNameStaysReadable()
        {
            string name = BenchmarkReportFileName.Build(new string('x', 500), Moment);

            Assert.That(name.Length, Is.LessThan(100));
            Assert.That(name, Does.EndWith("_20260730-164509.txt"));
        }

        [Test]
        public void Build_TwoRunsOnTheSameDeviceAtDifferentTimes_DoNotCollide()
        {
            string first = BenchmarkReportFileName.Build("Pixel8", Moment);
            string second = BenchmarkReportFileName.Build("Pixel8", Moment.AddMinutes(3));

            Assert.That(first, Is.Not.EqualTo(second),
                "A second reading from the same phone must not overwrite the first.");
        }
    }
}
