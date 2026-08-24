using System;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.AI.MatchTelemetry;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// AiMatchTelemetry touches no Unity API and holds no threading primitive of its own, so every
    /// one of these runs with no scene or Play mode involved — the same reasoning
    /// BenchmarkReportTests is built on.
    /// </summary>
    [TestFixture]
    public class AiMatchTelemetryTests
    {
        private static MoveCommand Move(int fromX, int fromY, int toX, int toY)
        {
            var piece = new PieceData(Team.White, ChessPieceType.Pawn, moveDirection: 1, startRow: 1);
            return new MoveCommand(new Vector2Int(fromX, fromY), new Vector2Int(toX, toY), piece);
        }

        private static AiMoveRecord Searched(int ply, Team team, int elapsedMs, int depth,
            SearchStopReason stopReason = SearchStopReason.Budget) =>
            new AiMoveRecord(ply, team, Move(0, 1, 0, 2), fromBook: false, elapsedMs, depth, stopReason);

        private static AiMoveRecord Book(int ply, Team team) =>
            new AiMoveRecord(ply, team, Move(0, 1, 0, 2), fromBook: true, elapsedMs: 0, completedDepth: 0,
                stopReason: SearchStopReason.Unset);

        [Test]
        public void Render_IncludesTheMatchId()
        {
            var telemetry = new AiMatchTelemetry("20260731-090000");

            string text = telemetry.Render();

            Assert.That(text, Does.Contain("20260731-090000"));
        }

        [Test]
        public void Render_HeaderLinesComeBeforeSummary_WhichComesBeforeMoves()
        {
            var telemetry = new AiMatchTelemetry("match");
            telemetry.AppendHeaderLine("Device model: TestPhone");
            telemetry.RecordMove(Searched(1, Team.Black, elapsedMs: 500, depth: 5));

            string text = telemetry.Render();

            int headerIndex = text.IndexOf("Device model: TestPhone", StringComparison.Ordinal);
            int summaryIndex = text.IndexOf("--- Summary ---", StringComparison.Ordinal);
            int movesIndex = text.IndexOf("--- Moves ---", StringComparison.Ordinal);

            Assert.That(headerIndex, Is.LessThan(summaryIndex),
                "Device/build facts must appear before the summary, not after it.");
            Assert.That(summaryIndex, Is.LessThan(movesIndex),
                "The summary is the part worth reading first, so it must come before the full move list.");
        }

        [Test]
        public void Render_WithNoMovesRecorded_ReportsZeroRatherThanShowingAnEmptySection()
        {
            var telemetry = new AiMatchTelemetry("match");

            string text = telemetry.Render();

            Assert.That(text, Does.Contain("0 moves total (0 from the opening book, 0 searched)"));
            Assert.That(text, Does.Contain("no searched moves recorded"));
        }

        [Test]
        public void Render_CountsBookAndSearchedMovesSeparately()
        {
            var telemetry = new AiMatchTelemetry("match");
            telemetry.RecordMove(Book(1, Team.White));
            telemetry.RecordMove(Book(2, Team.Black));
            telemetry.RecordMove(Searched(3, Team.White, elapsedMs: 800, depth: 6));

            string text = telemetry.Render();

            Assert.That(text, Does.Contain("3 moves total (2 from the opening book, 1 searched)"));
        }

        [Test]
        public void Render_SummaryAggregatesElapsedAndDepth_OverSearchedMovesOnly()
        {
            var telemetry = new AiMatchTelemetry("match");
            telemetry.RecordMove(Book(1, Team.White)); // must not pull the averages toward its own zeros
            telemetry.RecordMove(Searched(2, Team.Black, elapsedMs: 100, depth: 5));
            telemetry.RecordMove(Searched(3, Team.White, elapsedMs: 300, depth: 3));

            string text = telemetry.Render();

            Assert.That(text, Does.Contain("elapsed ms: worst=300 mean=200 min=100"));
            Assert.That(text, Does.Contain("depth reached: worst=3 mean=4.0"),
                "'Worst' depth means the shallowest reached, matching the device benchmark's own convention.");
        }

        [Test]
        public void Render_MovesAppearInTheOrderTheyWereRecorded()
        {
            var telemetry = new AiMatchTelemetry("match");
            telemetry.RecordMove(Searched(1, Team.White, elapsedMs: 100, depth: 4));
            telemetry.RecordMove(Searched(2, Team.Black, elapsedMs: 200, depth: 5));
            telemetry.RecordMove(Searched(3, Team.White, elapsedMs: 300, depth: 6));

            string text = telemetry.Render();

            int first = text.IndexOf("ply 1:", StringComparison.Ordinal);
            int second = text.IndexOf("ply 2:", StringComparison.Ordinal);
            int third = text.IndexOf("ply 3:", StringComparison.Ordinal);

            Assert.That(first, Is.LessThan(second));
            Assert.That(second, Is.LessThan(third));
        }

        [Test]
        public void Render_BookMoveLine_NamesNoDepthOrElapsed()
        {
            var telemetry = new AiMatchTelemetry("match");
            telemetry.RecordMove(Book(1, Team.White));

            string text = telemetry.Render();

            Assert.That(text, Does.Contain("ply 1: White plays"));
            Assert.That(text, Does.Contain("(book)"));
            Assert.That(text, Does.Not.Contain("depth 0"),
                "A book move never ran a search, so it must not read as a zero-depth search result.");
        }

        [Test]
        public void Render_SearchedMoveLine_NamesDepthStopReasonAndElapsed()
        {
            var telemetry = new AiMatchTelemetry("match");
            telemetry.RecordMove(Searched(1, Team.Black, elapsedMs: 1234, depth: 7, SearchStopReason.SettledEarly));

            string text = telemetry.Render();

            Assert.That(text, Does.Contain("ply 1: Black plays"));
            Assert.That(text, Does.Contain("depth 7"));
            Assert.That(text, Does.Contain("SettledEarly"));
            Assert.That(text, Does.Contain("1234ms"));
        }

        [Test]
        public void MoveCount_ReflectsEveryRecordedMove_BookAndSearchedAlike()
        {
            var telemetry = new AiMatchTelemetry("match");
            telemetry.RecordMove(Book(1, Team.White));
            telemetry.RecordMove(Searched(2, Team.Black, elapsedMs: 500, depth: 5));

            Assert.That(telemetry.MoveCount, Is.EqualTo(2));
        }
    }
}
