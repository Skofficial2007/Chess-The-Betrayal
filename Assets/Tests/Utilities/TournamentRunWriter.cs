using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Threading;

namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>
    /// Streams a tournament run's games to disk as each one finishes, so killing the run at any
    /// point leaves real, parseable results instead of nothing — the gap that made every past
    /// benchmark attempt disappear the moment it was interrupted.
    ///
    /// A single background thread owns the file and does all the writing; callers only ever hand a
    /// finished record to a queue and return immediately, so persistence never sits on a worker
    /// thread's path back to the next game. The background thread flushes on a fixed interval
    /// rather than after every line — appending is cheap, but a flush call is a real syscall, and a
    /// run playing hundreds of games a second under heavy parallelism would otherwise spend more
    /// time flushing than searching. No fsync anywhere: an OS-level crash losing the last quarter
    /// second of buffered lines is an acceptable trade for not serializing the tournament behind
    /// disk IO on every single game.
    /// </summary>
    public sealed class TournamentRunWriter : IDisposable
    {
        private const int FlushIntervalMs = 250;

        private readonly BlockingCollection<string> _pendingLines = new BlockingCollection<string>();
        private readonly Thread _writerThread;
        private readonly string _runFilePath;
        private Exception _writerFault;

        public string RunDirectory { get; }

        /// <summary>Creates the run directory and starts the background writer. header is written
        /// as the file's first line immediately, before any game record, so even a run killed
        /// before its first game completes still leaves a directory that identifies what was being
        /// attempted.</summary>
        public TournamentRunWriter(string runDirectory, string headerLine)
        {
            RunDirectory = runDirectory;
            Directory.CreateDirectory(runDirectory);
            _runFilePath = Path.Combine(runDirectory, "run.jsonl");

            using (var initialWriter = new StreamWriter(_runFilePath, append: false))
            {
                initialWriter.WriteLine(headerLine);
            }

            _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "TournamentRunWriter" };
            _writerThread.Start();
        }

        /// <summary>Enqueues one finished game's record for the background thread to append.
        /// Returns immediately — never blocks on disk IO.</summary>
        public void WriteGame(TournamentRunRecord record)
        {
            _pendingLines.Add(record.ToLine());
        }

        private void WriterLoop()
        {
            try
            {
                using (var writer = new StreamWriter(_runFilePath, append: true))
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

        /// <summary>Signals no more games are coming, waits for every already-queued line to reach
        /// disk, and closes the file. Safe to call once the run is done OR once it's been
        /// cancelled — either way, everything queued up to this point is guaranteed to land before
        /// this returns, which is what lets a kill leave a complete, consistent file rather than one
        /// racing an in-flight write.</summary>
        public void Dispose()
        {
            _pendingLines.CompleteAdding();
            _writerThread.Join();
            _pendingLines.Dispose();

            if (_writerFault != null)
                throw new IOException($"Tournament run writer failed for '{_runFilePath}'.", _writerFault);
        }

        /// <summary>Builds the header line every run.jsonl starts with — schema version first, so a
        /// future format change can be detected before a reader tries to parse fields that no
        /// longer mean what they used to.</summary>
        public static string BuildHeaderLine(int schemaVersion, string mode, int runSeed, int totalGames,
            string timeControl, int workerCount, DateTime startUtc)
        {
            return string.Join("\t",
                schemaVersion.ToString(CultureInfo.InvariantCulture),
                mode,
                runSeed.ToString(CultureInfo.InvariantCulture),
                totalGames.ToString(CultureInfo.InvariantCulture),
                timeControl,
                workerCount.ToString(CultureInfo.InvariantCulture),
                startUtc.ToString("O", CultureInfo.InvariantCulture));
        }
    }
}
