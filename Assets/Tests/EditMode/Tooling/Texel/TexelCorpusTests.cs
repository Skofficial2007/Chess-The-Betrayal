using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tooling;
using ChessTheBetrayal.Tooling.Match;
using ChessTheBetrayal.Tooling.Texel;
using ChessTheBetrayal.Tooling.Tournament;

namespace ChessTheBetrayal.Tests.EditMode.Tooling.Texel
{
    /// <summary>
    /// Pins the corpus building blocks below the EditorTools generator: the board codec's round-trip
    /// fidelity, the position record's line encoding, and the writer's thread-safety under concurrent
    /// producers — the exact shape a parallel generation run stresses.
    /// </summary>
    [TestFixture]
    public class TexelCorpusTests
    {
        [Test]
        public void TexelBoardCodec_StandardStartingPosition_RoundTripsEveryPiece()
        {
            BoardState original = BoardSetup.CreateStandard();

            string encoded = TexelBoardCodec.Encode(original);
            BoardState decoded = TexelBoardCodec.Decode(encoded);

            Assert.That(decoded.TileCountX, Is.EqualTo(original.TileCountX));
            Assert.That(decoded.TileCountY, Is.EqualTo(original.TileCountY));
            for (int y = 0; y < original.TileCountY; y++)
            {
                for (int x = 0; x < original.TileCountX; x++)
                {
                    PieceData originalPiece = original.GetPiece(x, y);
                    PieceData decodedPiece = decoded.GetPiece(x, y);
                    Assert.That(decodedPiece.IsEmpty, Is.EqualTo(originalPiece.IsEmpty), $"square ({x},{y})");
                    if (!originalPiece.IsEmpty)
                    {
                        Assert.That(decodedPiece.Team, Is.EqualTo(originalPiece.Team), $"square ({x},{y})");
                        Assert.That(decodedPiece.Type, Is.EqualTo(originalPiece.Type), $"square ({x},{y})");
                    }
                }
            }
        }

        [Test]
        public void TexelBoardCodec_SparseCustomBoard_RoundTripsExactly()
        {
            BoardState original = BoardSetup.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("a8", Team.White, ChessPieceType.Rook)
                .WithPiece("g8", Team.Black, ChessPieceType.King)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                .WithComputedHash();

            BoardState decoded = TexelBoardCodec.Decode(TexelBoardCodec.Encode(original));

            Assert.That(decoded.GetPiece(4, 0).Type, Is.EqualTo(ChessPieceType.King));
            Assert.That(decoded.GetPiece(4, 0).Team, Is.EqualTo(Team.White));
            Assert.That(decoded.GetPiece(0, 7).Type, Is.EqualTo(ChessPieceType.Rook));
            Assert.That(decoded.GetPiece(6, 7).Type, Is.EqualTo(ChessPieceType.King));
            Assert.That(decoded.GetPiece(6, 7).Team, Is.EqualTo(Team.Black));
            Assert.That(decoded.GetPiece(7, 6).Type, Is.EqualTo(ChessPieceType.Pawn));
            Assert.That(decoded.GetPiece(7, 6).Team, Is.EqualTo(Team.Black));
            Assert.That(decoded.GetPiece(0, 0).IsEmpty, Is.True);
        }

        [Test]
        public void TexelBoardCodec_Decode_MalformedInput_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => TexelBoardCodec.Decode("not a valid encoding"));
        }

        [TestCase(MatchOutcome.WhiteWon, 1.0)]
        [TestCase(MatchOutcome.Draw, 0.5)]
        [TestCase(MatchOutcome.BlackWon, 0.0)]
        public void TexelPositionRecord_LabelFor_MatchesStandardTexelConvention(MatchOutcome outcome, double expectedLabel)
        {
            Assert.That(TexelPositionRecord.LabelFor(outcome), Is.EqualTo(expectedLabel));
        }

        [Test]
        public void TexelPositionRecord_ToLineThenTryParse_RoundTripsEveryField()
        {
            BoardState board = BoardSetup.CreateStandard();
            var original = new TexelPositionRecord(
                TexelBoardCodec.Encode(board), Team.Black, betrayalRightAvailable: true,
                postDefectionOccurred: true, label: 0.5);

            bool parsed = TexelPositionRecord.TryParse(original.ToLine(), out TexelPositionRecord roundTripped);

            Assert.That(parsed, Is.True);
            Assert.That(roundTripped.BoardEncoding, Is.EqualTo(original.BoardEncoding));
            Assert.That(roundTripped.SideToMove, Is.EqualTo(Team.Black));
            Assert.That(roundTripped.BetrayalRightAvailable, Is.True);
            Assert.That(roundTripped.PostDefectionOccurred, Is.True);
            Assert.That(roundTripped.Label, Is.EqualTo(0.5));
        }

        [TestCase("")]
        [TestCase("only\tthree\tfields")]
        [TestCase("board\t2\t1\t0\t1.0")] // side-to-move field must be "0" or "1", not "2"
        public void TexelPositionRecord_TryParse_MalformedLine_ReturnsFalseRatherThanThrowing(string malformed)
        {
            bool parsed = TexelPositionRecord.TryParse(malformed, out _);

            Assert.That(parsed, Is.False);
        }

        [Test]
        public void TexelPositionRecord_ToBoardState_RestoresSideToMoveAndBetrayalRight()
        {
            BoardState original = BoardSetup.CreateStandard();
            var record = new TexelPositionRecord(
                TexelBoardCodec.Encode(original), Team.Black, betrayalRightAvailable: false,
                postDefectionOccurred: false, label: 1.0);

            BoardState reconstructed = record.ToBoardState();

            Assert.That(reconstructed.CurrentTurn, Is.EqualTo(Team.Black));
            Assert.That(reconstructed.BetrayalRightAvailable, Is.False);
        }

        [Test]
        public void TexelPositionRecord_ToBoardState_ZobristHashStaysConsistentAfterOverridingTurnAndBetrayalRight()
        {
            // Decode computes a hash assuming its own defaults (White to move, Betrayal right
            // available); ToBoardState then overrides both from the record. Without recomputing, the
            // hash would silently disagree with the board it claims to describe on every record where
            // either override actually changes something from that default.
            BoardState original = BoardSetup.CreateStandard();
            var record = new TexelPositionRecord(
                TexelBoardCodec.Encode(original), Team.Black, betrayalRightAvailable: false,
                postDefectionOccurred: false, label: 1.0);

            BoardState reconstructed = record.ToBoardState();

            Assert.DoesNotThrow(() => reconstructed.AssertZobristConsistency());
        }

        [Test]
        public void TexelCorpusSampler_BuffersUntilGameCompletes_ThenStampsEveryPositionWithTheSameLabel()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "TexelCorpusSamplerTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                var writer = new TexelCorpusWriter(tempDir, TexelCorpusWriter.BuildHeaderLine(1, runSeed: 1, DateTime.UtcNow));
                var sampler = new TexelCorpusSampler(writer);
                BoardState board = BoardSetup.CreateStandard();

                sampler.OnQuietPosition(board, Team.White, ply: 0, postDefectionOccurred: false);
                sampler.OnQuietPosition(board, Team.Black, ply: 1, postDefectionOccurred: false);
                sampler.OnGameComplete(MatchOutcome.WhiteWon);

                writer.Dispose();
                List<TexelPositionRecord> records = ReadAllRecords(tempDir);

                Assert.That(records, Has.Count.EqualTo(2));
                Assert.That(records, Has.All.Matches<TexelPositionRecord>(r => r.Label == 1.0));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            }
        }

        [Test]
        public void TexelCorpusWriter_ConcurrentProducers_LosesNoPositions()
        {
            // The exact shape a parallel generation run stresses: many "games" (here, threads)
            // writing to ONE shared writer at once. TournamentRunWriter's own thread-safety proof
            // is the model this mirrors.
            string tempDir = Path.Combine(Path.GetTempPath(), "TexelCorpusWriterConcurrencyTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                const int producers = 8;
                const int positionsPerProducer = 50;

                using (var writer = new TexelCorpusWriter(tempDir, TexelCorpusWriter.BuildHeaderLine(1, runSeed: 1, DateTime.UtcNow)))
                {
                    BoardState board = BoardSetup.CreateStandard();
                    string encoded = TexelBoardCodec.Encode(board);

                    Parallel.For(0, producers, producerIndex =>
                    {
                        for (int i = 0; i < positionsPerProducer; i++)
                        {
                            writer.WritePosition(new TexelPositionRecord(
                                encoded, Team.White, betrayalRightAvailable: true,
                                postDefectionOccurred: false, label: 1.0));
                        }
                    });
                }

                List<TexelPositionRecord> records = ReadAllRecords(tempDir);

                Assert.That(records, Has.Count.EqualTo(producers * positionsPerProducer));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            }
        }

        private static List<TexelPositionRecord> ReadAllRecords(string corpusDirectory)
        {
            string[] lines = File.ReadAllLines(Path.Combine(corpusDirectory, "corpus.jsonl"));
            var records = new List<TexelPositionRecord>();
            // Line 0 is the header, never a position record.
            foreach (string line in lines.Skip(1))
            {
                if (TexelPositionRecord.TryParse(line, out TexelPositionRecord record))
                    records.Add(record);
            }
            return records;
        }
    }
}
