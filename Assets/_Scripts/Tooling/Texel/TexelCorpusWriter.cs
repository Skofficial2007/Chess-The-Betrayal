using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Threading;

namespace ChessTheBetrayal.Tooling.Texel
{
    /// <summary>
    /// Streams sampled positions to disk as they're produced, so a killed generation run leaves a
    /// real, parseable partial corpus instead of nothing — the exact same problem
    /// TournamentRunWriter solves for benchmark runs, and the exact same fix.
    ///
    /// A single background thread owns the file and does all the writing; callers only ever hand a
    /// finished record to a queue and return immediately, so persistence never sits on a worker
    /// thread's path back to the next game. Safe for concurrent producers by design (BlockingCollection),
    /// which matters here specifically: corpus generation plays many games in parallel, each with its
    /// own MatchSimulator and its own sampler, all feeding this ONE shared writer.
    /// </summary>
    public sealed class TexelCorpusWriter : IDisposable
    {
        private const int FlushIntervalMs = 250;

        private readonly BlockingCollection<string> _pendingLines = new BlockingCollection<string>();
        private readonly Thread _writerThread;
        private readonly string _corpusFilePath;
        private Exception _writerFault;
        private int _positionsWritten;

        public string CorpusDirectory { get; }

        /// <summary>How many positions have been enqueued so far — incremented the moment
        /// WritePosition is called, not once the background thread actually appends the line, so a
        /// caller reading this right after the last WritePosition call (before Dispose) sees the
        /// true total even though some lines may still be in flight to disk.</summary>
        public int PositionsWritten => _positionsWritten;

        /// <summary>Creates the corpus directory and starts the background writer. header is written
        /// as the file's first line immediately, before any position record, so even a run killed
        /// before its first quiet position is sampled still leaves a directory that identifies what
        /// was being attempted.</summary>
        public TexelCorpusWriter(string corpusDirectory, string headerLine)
        {
            CorpusDirectory = corpusDirectory;
            Directory.CreateDirectory(corpusDirectory);
            _corpusFilePath = Path.Combine(corpusDirectory, "corpus.jsonl");

            using (var initialWriter = new StreamWriter(_corpusFilePath, append: false))
            {
                initialWriter.WriteLine(headerLine);
            }

            _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "TexelCorpusWriter" };
            _writerThread.Start();
        }

        /// <summary>Enqueues one sampled position for the background thread to append. Returns
        /// immediately — never blocks on disk IO. Safe to call from any thread, concurrently.</summary>
        public void WritePosition(TexelPositionRecord record)
        {
            _pendingLines.Add(record.ToLine());
            Interlocked.Increment(ref _positionsWritten);
        }

        private void WriterLoop()
        {
            try
            {
                using (var writer = new StreamWriter(_corpusFilePath, append: true))
                {
                    var lastFlush = DateTime.UtcNow;
                    foreach (string line in _pendingLines.GetConsumingEnumerable())
                    {
                        writer.WriteLine(line);

                        if ((DateTime.UtcNow - lastFlush).TotalMilliseconds >= FlushIntervalMs)
                        {
                            writer.Flush();
                            lastFlush = DateTime.UtcNow;
                        }
                    }

                    writer.Flush();
                }
            }
            catch (Exception ex)
            {
                _writerFault = ex;
            }
        }

        /// <summary>Signals no more positions are coming, waits for every already-queued line to
        /// reach disk, and closes the file. Safe to call once generation is done OR once it's been
        /// cancelled — either way, everything queued up to this point is guaranteed to land before
        /// this returns, which is what lets a kill leave a complete, consistent file rather than one
        /// racing an in-flight write.</summary>
        public void Dispose()
        {
            _pendingLines.CompleteAdding();
            _writerThread.Join();
            _pendingLines.Dispose();

            if (_writerFault != null)
                throw new IOException($"Texel corpus writer failed for '{_corpusFilePath}'.", _writerFault);
        }

        /// <summary>Builds the header line every corpus.jsonl starts with — schema version first, so
        /// a future format change can be detected before a reader tries to parse fields that no
        /// longer mean what they used to.</summary>
        public static string BuildHeaderLine(int schemaVersion, int runSeed, DateTime startUtc)
        {
            return string.Join("\t",
                schemaVersion.ToString(CultureInfo.InvariantCulture),
                runSeed.ToString(CultureInfo.InvariantCulture),
                startUtc.ToString("O", CultureInfo.InvariantCulture));
        }
    }
}
