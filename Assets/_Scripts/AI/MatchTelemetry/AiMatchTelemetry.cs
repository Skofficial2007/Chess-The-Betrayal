using System.Collections.Generic;
using System.Text;

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
        /// Renders the whole match: a header, a summary (move counts and the elapsed/depth spread
        /// over searched moves only — a book move has neither), then every move in order. Summary
        /// before detail, same ordering and reasoning as BenchmarkReport.
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

            text.AppendLine("--- Moves ---");
            foreach (AiMoveRecord move in _moves) text.AppendLine(FormatMove(move));

            return text.ToString();
        }

        private void AppendSummary(StringBuilder text)
        {
            int searchedCount = 0;
            int worstElapsed = 0;
            int minElapsed = int.MaxValue;
            long elapsedSum = 0;
            int worstDepth = int.MaxValue; // shallowest reached, matching the benchmark's own "worst = shallowest" convention
            long depthSum = 0;

            foreach (AiMoveRecord move in _moves)
            {
                if (move.FromBook) continue;

                searchedCount++;
                if (move.ElapsedMs > worstElapsed) worstElapsed = move.ElapsedMs;
                if (move.ElapsedMs < minElapsed) minElapsed = move.ElapsedMs;
                elapsedSum += move.ElapsedMs;
                if (move.CompletedDepth < worstDepth) worstDepth = move.CompletedDepth;
                depthSum += move.CompletedDepth;
            }

            int bookCount = _moves.Count - searchedCount;
            text.AppendLine($"{_moves.Count} moves total ({bookCount} from the opening book, {searchedCount} searched)");

            if (searchedCount == 0)
            {
                text.AppendLine("(no searched moves recorded)");
                return;
            }

            double meanElapsed = elapsedSum / (double)searchedCount;
            double meanDepth = depthSum / (double)searchedCount;
            text.AppendLine($"elapsed ms: worst={worstElapsed} mean={meanElapsed:F0} min={minElapsed}");
            text.AppendLine($"depth reached: worst={worstDepth} mean={meanDepth:F1}");
        }

        private static string FormatMove(AiMoveRecord move) =>
            move.FromBook
                ? $"ply {move.PlyNumber}: {move.Team} plays {move.Move} (book)"
                : $"ply {move.PlyNumber}: {move.Team} plays {move.Move} (depth {move.CompletedDepth}, {move.StopReason}, {move.ElapsedMs}ms)";
    }
}
