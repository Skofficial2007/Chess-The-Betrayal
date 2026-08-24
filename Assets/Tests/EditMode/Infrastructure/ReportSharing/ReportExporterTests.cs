using System.IO;
using System.Text;
using NUnit.Framework;
using ChessTheBetrayal.Infrastructure.ReportSharing;

namespace ChessTheBetrayal.Tests.EditMode.Infrastructure.ReportSharing
{
    /// <summary>
    /// Covers the bytes a report actually reaches disk as. The write is deliberately exercised
    /// through a path of the test's own choosing rather than through Save, which also touches the
    /// clipboard and the platform's persistent-data folder — neither of which this is about, and
    /// the clipboard is not something to lean on in a headless run.
    /// </summary>
    [TestFixture]
    public class ReportExporterTests
    {
        private string _path;

        [SetUp]
        public void SetUp() => _path = Path.Combine(Path.GetTempPath(), $"chess-report-test-{Path.GetRandomFileName()}.txt");

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }

        [Test]
        public void WriteReport_StartsTheFileWithAByteOrderMark()
        {
            ReportExporter.WriteReport(_path, "plain ascii only");

            byte[] written = File.ReadAllBytes(_path);

            Assert.That(written.Length, Is.GreaterThanOrEqualTo(3));
            Assert.That(new[] { written[0], written[1], written[2] }, Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }),
                "Without the mark, a reader that guesses reads the file as its own ANSI codepage.");
        }

        [Test]
        public void WriteReport_AReaderThatGuessesTheEncoding_GetsTheTextBackIntact()
        {
            // An em-dash, because every section title and every per-cell line in a benchmark
            // report carries one, and it is what came back as mojibake from the first real device.
            const string report = "STATUS: COMPLETE — 54/54 cells";

            ReportExporter.WriteReport(_path, report);

            // A reader that detects a mark if one is there and falls back to a single-byte encoding
            // if it isn't — which is what a text viewer guessing at an unmarked file does, and is
            // the whole failure being guarded against. Opening it as UTF-8 outright would pass with
            // or without the mark and prove nothing about the case that actually broke.
            using var reader = new StreamReader(_path, Encoding.ASCII, detectEncodingFromByteOrderMarks: true);
            Assert.That(reader.ReadToEnd(), Is.EqualTo(report));
        }
    }
}
