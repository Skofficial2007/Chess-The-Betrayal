using System.Collections.Generic;
using System.Text;
using ChessTheBetrayal.AI.Search;
using ChessTheBetrayal.Core.Match;

namespace ChessTheBetrayal.AI.MatchTelemetry
{
    /// <summary>
    /// Records what the AI actually did across one real match, as opposed to the synthetic,
    /// nothing-on-screen cold searches the device benchmark runs. Holds no Unity API and no
    /// threading primitive of its own, so it can be built and asserted on directly in an EditMode
    /// test with no scene or Play mode involved — the same reasoning BenchmarkReport is built on.
    ///
    /// RecordMove only ever appends a struct to a list; nothing is formatted into text until
    /// Render() is called. A real match has the main thread busy drawing the board and animating
    /// pieces while the AI searches in the background, so building strings on every move would be
    /// real, felt work — deferring all of it to one Render() call, at most once per match when a
    /// player presses a share button, costs nothing during play.
    /// </summary>
    public sealed class AiMatchTelemetry
    {
        private readonly List<string> _headerLines = new List<string>();
        private readonly List<AiMoveRecord> _moves = new List<AiMoveRecord>();
        private string _result;

        public AiMatchTelemetry(string matchId)
        {
            MatchId = matchId;
        }

        public string MatchId { get; }

        public int MoveCount => _moves.Count;

        /// <summary>A fact that holds for the whole match — device model, build config, and so on.
        /// This class touches no Unity API itself, so a caller that does supplies these, the same
        /// seam BenchmarkReport.AppendHeaderLine uses.</summary>
        public void AppendHeaderLine(string line) => _headerLines.Add(line);

        public void RecordMove(AiMoveRecord record) => _moves.Add(record);

        /// <summary>
        /// How the match ended, in the words a caller has already chosen. Taken as text rather than
        /// as a result type because the vocabulary for an ending lives outside this assembly, and
        /// this one deliberately depends on nothing but the core rules - the same reason the device
        /// facts in the header arrive as lines instead of as a device.
        ///
        /// Worth having at all because a report without it is forty-odd lines of timings about a
        /// game whose outcome the reader has to be told separately, or guess.
        /// </summary>
        public void SetResult(string result) => _result = result;

        /// <summary>
        /// Drops every ply recorded above <paramref name="lastSurvivingPlyNumber"/> — the report's
        /// half of a takeback, the same job MatchMoveLog.RemoveLast does for the move log. The
        /// caller unmakes the plies on the board; this only keeps the report in step with them.
        ///
        /// Keyed on the ply number rather than on how many plies came off the board, because this
        /// holds only the AI's own plies and any Defection. A takeback that unmade three plies may
        /// have unmade none of them, one, or all three, and a count cannot tell those apart. The
        /// number can: every record takes its number from the board's own ply count, and a takeback
        /// rewinds that count too, so anything numbered above where the board now sits describes a
        /// ply that no longer happened.
        /// </summary>
        public void RemoveAfterPly(int lastSurvivingPlyNumber)
        {
            for (int i = _moves.Count - 1; i >= 0; i--)
            {
                if (_moves[i].PlyNumber > lastSurvivingPlyNumber) _moves.RemoveAt(i);
            }

            // Any ending recorded for this match described a position that has just been taken back.
            // A checkmate can be undone - the undo path clears the board's own game-over state for
            // exactly that reason - so a result left standing here would outlive the mate it came
            // from and head a report about a game still being played.
            _result = null;
        }

        /// <summary>
        /// Renders the whole match: a header, a summary (ply counts and the elapsed/depth spread
        /// over searched moves only — a book move and a Defection have neither), then every
        /// recorded ply in order. Summary before detail, same ordering and reasoning as
        /// BenchmarkReport.
        /// </summary>
        public string Render()
        {
            var text = new StringBuilder();

            text.AppendLine($"Match ID: {MatchId}");
            if (_headerLines.Count > 0)
            {
                text.AppendLine("--- Device info ---");
                foreach (string line in _headerLines) text.AppendLine(line);
            }
            text.AppendLine();

            text.AppendLine("--- Summary ---");
            AppendSummary(text);
            text.AppendLine();

            text.AppendLine("--- Plies ---");
            text.AppendLine("(the AI's own plies and any Defection; the opponent's moves are not recorded, so ply numbers skip.");
            text.AppendLine("A Betrayal by either side spends more than one ply in a single turn, so the numbers below");
            text.AppendLine("change parity across one - a run of even plies becomes odd, or the other way about.)");
            foreach (AiMoveRecord move in _moves) text.AppendLine(FormatMove(move));

            return text.ToString();
        }

        private void AppendSummary(StringBuilder text)
        {
            // First line of the summary, because it is the one fact somebody opening this wants
            // before any of the timings mean anything. Absent gets a line of its own rather than
            // nothing at all: a report can legitimately be shared from a game still in progress, and
            // silence there reads as a report that forgot to say instead of one with nothing to say.
            text.AppendLine(_result != null
                ? $"Result: {_result}"
                : "Result: not recorded - this report was taken before the game ended.");

            int searchedCount = 0;
            int bookCount = 0;
            int defectionCount = 0;
            int worstElapsed = 0;
            int minElapsed = int.MaxValue;
            long elapsedSum = 0;

            // A search that finds a forced mate stops on the spot, however shallow it is, because no
            // deeper look can change the answer. Pooling that with a search the clock or the tier
            // ceiling cut short would let the best outcome available set the headline for the worst
            // one: one real match ended on a mate found at depth 2 and was summarised as "depth
            // reached: worst=2", which reads as an AI that struggled all game.
            int matesFound = 0;
            int depthSampleCount = 0;
            int worstDepth = int.MaxValue; // shallowest reached, matching the benchmark's own "worst = shallowest" convention
            long depthSum = 0;

            foreach (AiMoveRecord move in _moves)
            {
                switch (move.Source)
                {
                    case AiMoveSource.Book: bookCount++; continue;
                    case AiMoveSource.Defection: defectionCount++; continue;
                }

                searchedCount++;
                if (move.ElapsedMs > worstElapsed) worstElapsed = move.ElapsedMs;
                if (move.ElapsedMs < minElapsed) minElapsed = move.ElapsedMs;
                elapsedSum += move.ElapsedMs;

                if (move.StopReason == SearchStopReason.MateFound)
                {
                    matesFound++;
                    continue;
                }

                depthSampleCount++;
                if (move.CompletedDepth < worstDepth) worstDepth = move.CompletedDepth;
                depthSum += move.CompletedDepth;
            }

            text.AppendLine($"{_moves.Count} plies recorded ({bookCount} from the opening book, "
                + $"{searchedCount} searched, {defectionCount} by Defection)");

            if (searchedCount == 0)
            {
                text.AppendLine("(no searched moves recorded)");
                return;
            }

            double meanElapsed = elapsedSum / (double)searchedCount;
            text.AppendLine($"elapsed ms over {searchedCount} searched moves: "
                + $"worst={worstElapsed} mean={meanElapsed:F0} min={minElapsed}");
            text.AppendLine("(elapsed runs from asking for a move to the search handing it over, so it includes the "
                + "wait for the next frame. It is not what the search spent and does not compare against a device "
                + "benchmark figure.)");
            AppendGateHoldLine(text);

            string mateNote = matesFound > 0
                ? $" ({matesFound} more stopped early on a forced mate)"
                : "";

            if (depthSampleCount == 0)
            {
                text.AppendLine($"depth reached: every searched move stopped early on a forced mate ({matesFound})");
                return;
            }

            double meanDepth = depthSum / (double)depthSampleCount;
            text.AppendLine($"depth reached over {depthSampleCount} moves: worst={worstDepth} mean={meanDepth:F1}{mateNote}");
        }

        /// <summary>
        /// The rest of what the player waited: how long the longest move sat between being decided
        /// and reaching the board, held behind an animation still playing.
        ///
        /// Reported even when it never happened, because "never held" is the answer this line exists
        /// to give. Its absence would leave a reader assuming the elapsed figures above are the whole
        /// wait, which is exactly the assumption that made a 23ms reply look instant when the capture
        /// it followed was still a good half-second from finishing.
        /// </summary>
        private void AppendGateHoldLine(StringBuilder text)
        {
            int worstHold = 0;
            int heldCount = 0;

            foreach (AiMoveRecord move in _moves)
            {
                if (move.GateHoldMs <= 0) continue;

                heldCount++;
                if (move.GateHoldMs > worstHold) worstHold = move.GateHoldMs;
            }

            text.AppendLine(heldCount == 0
                ? "held behind an animation: never - every move reached the board as soon as it was decided"
                : $"held behind an animation: {heldCount} of {_moves.Count} plies, worst={worstHold}ms "
                    + "(on top of the elapsed times above, not counted in them)");
        }

        /// <summary>
        /// Every line goes through MoveNotation, which exists to be the one way this project writes
        /// down what happened in a ply. A MoveCommand's own ToString spells out a coordinate pair
        /// ("Black Pawn from (7, 6) to (6, 5) capturing Pawn"), which asks a reader to know that x
        /// is the file and that both are counted from zero — and having two notations in one file,
        /// as this had, leaves them decoding one line and reading the next.
        /// </summary>
        private static string FormatMove(AiMoveRecord move) => move.Source switch
        {
            AiMoveSource.Book => $"ply {move.PlyNumber}: {move.Team} plays {MoveNotation.Describe(move.Move)} (book{HeldNote(move)})",
            AiMoveSource.Defection => $"ply {move.PlyNumber}: {MoveNotation.Describe(move.Move)} - now {move.Team}'s",
            _ => $"ply {move.PlyNumber}: {move.Team} plays {MoveNotation.Describe(move.Move)} "
                + $"(depth {move.CompletedDepth}, {move.StopReason}, {move.ElapsedMs}ms{DepthLoopNote(move)}{HeldNote(move)})",
        };

        /// <summary>
        /// Splits the elapsed time into the climb to the depth reported and whatever came after it.
        /// A move reporting its ceiling after three seconds may have reached that ceiling in one of
        /// them and spent the other two choosing between near-equal moves, and those say opposite
        /// things about how hard the device was working. Nothing else in a report separates them,
        /// and the stop reason alone cannot: it describes how the loop ended, not how long it took.
        ///
        /// What the remainder went on depends on that ending, though, and the two are not the same
        /// thing. A search that reached its ceiling spent it in the tie-break pass. A search the
        /// clock stopped spent it on a deeper look it never got to finish - the depth clock only
        /// records depths that completed, so an abandoned one lands here in full. Calling both
        /// "settling" told a reader the device had time to spare on exactly the plies where it had
        /// run out: one real match said it of four moves in thirteen.
        ///
        /// Left off when the climb is all there was - a tier with no personality dials skips the
        /// pass entirely, and a trailing zero on every one of its lines is noise.
        /// </summary>
        private static string DepthLoopNote(AiMoveRecord move)
        {
            if (move.DepthLoopMs <= 0 || move.ElapsedMs <= 0) return "";

            int rest = move.ElapsedMs - move.DepthLoopMs;
            if (rest <= 0) return "";

            string spentOn = move.StopReason == SearchStopReason.Budget
                ? $"{rest}ms more before the clock stopped it"
                : $"{rest}ms settling";
            return $" - {move.DepthLoopMs}ms to depth, {spentOn}";
        }

        /// <summary>
        /// Said only when there was a wait to report. A run of lines each announcing a hold of zero
        /// teaches a reader to stop reading the ones that mean something, which a report on this
        /// project has already managed once - the summary above carries the "never held" case so
        /// nothing is lost by leaving it off the lines themselves.
        /// </summary>
        private static string HeldNote(AiMoveRecord move) =>
            move.GateHoldMs > 0 ? $", on board {move.GateHoldMs}ms later" : "";
    }
}
