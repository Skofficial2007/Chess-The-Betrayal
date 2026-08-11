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
            new AiMoveRecord(ply, team, Move(0, 1, 0, 2), AiMoveSource.Searched, elapsedMs, depth, stopReason);

        private static AiMoveRecord Book(int ply, Team team) =>
            new AiMoveRecord(ply, team, Move(0, 1, 0, 2), AiMoveSource.Book, elapsedMs: 0, completedDepth: 0,
                stopReason: SearchStopReason.Unset);

        private static AiMoveRecord Defection(int ply, Team gainedBy)
        {
            var betrayer = new PieceData(Team.White, ChessPieceType.Queen, moveDirection: 1, startRow: 0);
            var square = new Vector2Int(0, 0);
            return AiMoveRecord.ForDefection(ply, gainedBy, MoveCommand.CreateDefectionMove(square, betrayer));
        }

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
            int movesIndex = text.IndexOf("--- Plies ---", StringComparison.Ordinal);

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

            Assert.That(text, Does.Contain("0 plies recorded (0 from the opening book, 0 searched, 0 by Defection)"));
            Assert.That(text, Does.Contain("no searched moves recorded"));
        }

        [Test]
        public void Render_CountsBookSearchedAndDefectionPliesSeparately()
        {
            var telemetry = new AiMatchTelemetry("match");
            telemetry.RecordMove(Book(1, Team.White));
            telemetry.RecordMove(Book(2, Team.Black));
            telemetry.RecordMove(Searched(3, Team.White, elapsedMs: 800, depth: 6));
            telemetry.RecordMove(Defection(4, Team.Black));

            string text = telemetry.Render();

            Assert.That(text, Does.Contain("4 plies recorded (2 from the opening book, 1 searched, 1 by Defection)"));
        }

        [Test]
        public void Render_SummaryAggregatesElapsedAndDepth_OverSearchedMovesOnly()
        {
            var telemetry = new AiMatchTelemetry("match");
            telemetry.RecordMove(Book(1, Team.White)); // must not pull the averages toward its own zeros
            telemetry.RecordMove(Defection(2, Team.Black)); // nor may this one
            telemetry.RecordMove(Searched(3, Team.Black, elapsedMs: 100, depth: 5));
            telemetry.RecordMove(Searched(4, Team.White, elapsedMs: 300, depth: 3));

            string text = telemetry.Render();

            Assert.That(text, Does.Contain("elapsed ms: worst=300 mean=200 min=100"));
            Assert.That(text, Does.Contain("depth reached over 2 moves: worst=3 mean=4.0"),
                "'Worst' depth means the shallowest reached, matching the device benchmark's own convention.");
        }

        [Test]
        public void Render_DefectionPly_NamesTheSquareAndWhoseThePieceNowIs()
        {
            var telemetry = new AiMatchTelemetry("match");
            telemetry.RecordMove(Defection(23, Team.Black));

            string text = telemetry.Render();

            // Without this line a reader sees the AI move a queen it was never given. One real
            // match had exactly that, on a1, and nothing in the log could explain it.
            Assert.That(text, Does.Contain("ply 23:"));
            Assert.That(text, Does.Contain("Qa1 defects"));
            Assert.That(text, Does.Contain("now Black's"));
            Assert.That(text, Does.Not.Contain("depth 0"),
                "Nothing searched for this ply, so it must not read as a zero-depth search result.");
        }

        [Test]
        public void Render_SaysThatTheOpponentsPliesAreNotRecorded()
        {
            var telemetry = new AiMatchTelemetry("match");
            telemetry.RecordMove(Searched(3, Team.Black, elapsedMs: 100, depth: 5));

            // Only one side's plies are ever recorded, so the numbers skip by design. Said out
            // loud, because a gap in them otherwise reads as something having been dropped.
            Assert.That(telemetry.Render(), Does.Contain("ply numbers skip"));
        }

        [Test]
        public void Render_AMateFoundEarly_DoesNotSetTheWorstDepthForTheWholeMatch()
        {
            var telemetry = new AiMatchTelemetry("match");
            telemetry.RecordMove(Searched(1, Team.Black, elapsedMs: 2000, depth: 6));
            telemetry.RecordMove(Searched(2, Team.Black, elapsedMs: 2000, depth: 5));
            // A forced mate found at depth 2 is the search doing the best thing available — no
            // deeper look could change the answer — not the search struggling.
            telemetry.RecordMove(Searched(3, Team.Black, elapsedMs: 40, depth: 2, SearchStopReason.MateFound));

            string text = telemetry.Render();

            Assert.That(text, Does.Contain("depth reached over 2 moves: worst=5"),
                "A mate found early must not become the headline depth for the whole match.");
            Assert.That(text, Does.Contain("1 more stopped early on a forced mate"));
        }

        [Test]
        public void Render_WhenEverySearchFoundAMate_SaysSoRatherThanReportingNoDepthAtAll()
        {
            var telemetry = new AiMatchTelemetry("match");
            telemetry.RecordMove(Searched(1, Team.Black, elapsedMs: 40, depth: 2, SearchStopReason.MateFound));

            string text = telemetry.Render();

            Assert.That(text, Does.Contain("every searched move stopped early on a forced mate"));
            Assert.That(text, Does.Not.Contain(int.MaxValue.ToString()),
                "With no depth samples left, the running shallowest must never reach the page.");
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
        public void Render_WritesEveryPlyInTheSameNotation_WhateverProducedIt()
        {
            var telemetry = new AiMatchTelemetry("match");
            telemetry.RecordMove(Book(1, Team.White));
            telemetry.RecordMove(Searched(3, Team.White, elapsedMs: 900, depth: 6));
            telemetry.RecordMove(Defection(5, Team.Black));

            string text = telemetry.Render();

            // A MoveCommand's own ToString gives "White Pawn from (0, 1) to (0, 2)", which asks the
            // reader to know x is the file and both are zero-based. One report carrying that on
            // most lines and algebraic on the Defection left them decoding one line and reading
            // the next.
            Assert.That(text, Does.Not.Contain("from ("),
                "No line may fall back to a coordinate pair:\n" + text);
            Assert.That(text, Does.Contain("a2-a3"),
                "The pawn push should read as the move log writes it.");
        }

        [Test]
        public void Render_SaysWhatItsElapsedClockActuallyCovers()
        {
            var telemetry = new AiMatchTelemetry("match");
            telemetry.RecordMove(Searched(1, Team.White, elapsedMs: 3044, depth: 6));

            // 3044ms against a 3000ms budget reads as a missed deadline next to a benchmark whose
            // worst overshoot across 200 searches was 10ms. It is not the same measurement: this
            // clock starts when the move is asked for and stops when it reaches the board, so it
            // carries the wait for the next frame as well.
            Assert.That(telemetry.Render(), Does.Contain("reaching the board"));
        }

        [Test]
        public void Render_ProducesNothingOutsidePlainAscii()
        {
            var telemetry = new AiMatchTelemetry("match");
            telemetry.AppendHeaderLine("Device model: TestPhone");
            telemetry.RecordMove(Book(1, Team.White));
            telemetry.RecordMove(Searched(3, Team.White, elapsedMs: 3044, depth: 6));
            telemetry.RecordMove(Searched(5, Team.White, elapsedMs: 36, depth: 2, SearchStopReason.MateFound));
            telemetry.RecordMove(Defection(7, Team.Black));

            BenchmarkReportTests.AssertPlainAscii(telemetry.Render());
        }

        [Test]
        public void MoveCount_ReflectsEveryRecordedPly_WhateverProducedIt()
        {
            var telemetry = new AiMatchTelemetry("match");
            telemetry.RecordMove(Book(1, Team.White));
            telemetry.RecordMove(Searched(2, Team.Black, elapsedMs: 500, depth: 5));
            telemetry.RecordMove(Defection(3, Team.Black));

            Assert.That(telemetry.MoveCount, Is.EqualTo(3));
        }

        /// <summary>
        /// The exact shape a real match produced: a searched ply, a Defection, and the reply on top
        /// of it, all taken back by two presses of Undo. Every one of them must leave the report,
        /// including the Defection — a Betrayal right can only be spent once per match, so a report
        /// claiming two of them describes a game that cannot happen.
        /// </summary>
        [Test]
        public void RemoveAfterPly_DropsEveryPlyRecordedAboveIt()
        {
            var telemetry = new AiMatchTelemetry("match");
            telemetry.RecordMove(Searched(45, Team.White, elapsedMs: 2248, depth: 7));
            telemetry.RecordMove(Searched(47, Team.White, elapsedMs: 216, depth: 7));
            telemetry.RecordMove(Defection(49, Team.White));
            telemetry.RecordMove(Searched(50, Team.White, elapsedMs: 61, depth: 7));

            telemetry.RemoveAfterPly(46);

            Assert.That(telemetry.MoveCount, Is.EqualTo(1));

            string report = telemetry.Render();
            Assert.That(report, Does.Contain("ply 45:"));
            Assert.That(report, Does.Not.Contain("ply 47:"));
            Assert.That(report, Does.Not.Contain("ply 49:"));
            Assert.That(report, Does.Not.Contain("ply 50:"));
            Assert.That(report, Does.Contain("0 by Defection"),
                "The summary counts what is left, not what was ever recorded.");
        }

        [Test]
        public void RemoveAfterPly_LeavesTheReportAloneWhenNothingWasTakenBack()
        {
            var telemetry = new AiMatchTelemetry("match");
            telemetry.RecordMove(Book(1, Team.White));
            telemetry.RecordMove(Searched(3, Team.White, elapsedMs: 900, depth: 6));

            telemetry.RemoveAfterPly(3);

            Assert.That(telemetry.MoveCount, Is.EqualTo(2),
                "A ply numbered exactly at the surviving count is still on the board.");
        }

        [Test]
        public void RemoveAfterPly_KeepsTheElapsedAndDepthSummaryHonest()
        {
            var telemetry = new AiMatchTelemetry("match");
            telemetry.RecordMove(Searched(1, Team.White, elapsedMs: 100, depth: 7));
            telemetry.RecordMove(Searched(3, Team.White, elapsedMs: 3000, depth: 4));

            telemetry.RemoveAfterPly(1);

            string report = telemetry.Render();
            Assert.That(report, Does.Contain("worst=100"),
                "A search that was taken back must stop setting the headline for the ones that stood.");
            Assert.That(report, Does.Contain("worst=7"));
        }
    }
}
