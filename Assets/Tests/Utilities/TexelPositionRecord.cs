using System;
using System.Globalization;
using System.Text;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>
    /// One quiet position sampled from a simulated game, labelled by that game's final result — the
    /// unit a Texel-style tuner trains against. Deliberately hand-rolled line encoding, the same
    /// reasoning as TournamentRunRecord: one background thread appends these one line at a time, and
    /// the format only needs to round-trip this one flat shape, tolerating a line torn by a kill.
    /// </summary>
    public readonly struct TexelPositionRecord
    {
        public readonly string BoardEncoding;
        public readonly Team SideToMove;
        public readonly bool BetrayalRightAvailable;
        public readonly bool PostDefectionOccurred;

        /// <summary>White-relative game result: 1.0 White won, 0.5 draw, 0.0 Black won — the
        /// standard Texel training target.</summary>
        public readonly double Label;

        public TexelPositionRecord(string boardEncoding, Team sideToMove, bool betrayalRightAvailable,
            bool postDefectionOccurred, double label)
        {
            BoardEncoding = boardEncoding;
            SideToMove = sideToMove;
            BetrayalRightAvailable = betrayalRightAvailable;
            PostDefectionOccurred = postDefectionOccurred;
            Label = label;
        }

        public static double LabelFor(MatchOutcome outcome) => outcome switch
        {
            MatchOutcome.WhiteWon => 1.0,
            MatchOutcome.BlackWon => 0.0,
            MatchOutcome.Draw => 0.5,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown match outcome.")
        };

        /// <summary>Reconstructs the sampled position as a BoardState, suitable for feeding straight
        /// into IPositionEvaluator.Evaluate — see TexelBoardCodec's own doc comment for exactly what
        /// is and isn't restored.</summary>
        public BoardState ToBoardState()
        {
            BoardState board = TexelBoardCodec.Decode(BoardEncoding);
            board.CurrentTurn = SideToMove;
            board.BetrayalRightAvailable = BetrayalRightAvailable;
            return board;
        }

        public string ToLine()
        {
            var sb = new StringBuilder(BoardEncoding.Length + 32);
            sb.Append(BoardEncoding).Append('\t');
            sb.Append(SideToMove == Team.White ? '0' : '1').Append('\t');
            sb.Append(BetrayalRightAvailable ? '1' : '0').Append('\t');
            sb.Append(PostDefectionOccurred ? '1' : '0').Append('\t');
            sb.Append(Label.ToString("F1", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>Parses one line written by ToLine. Returns false instead of throwing when the
        /// line is malformed — the expected shape for the final line of a file a process was killed
        /// while writing, which a reader must treat as "not there" rather than a fatal error.</summary>
        public static bool TryParse(string line, out TexelPositionRecord record)
        {
            record = default;
            if (string.IsNullOrEmpty(line)) return false;

            string[] fields = line.Split('\t');
            if (fields.Length != 5) return false;

            string boardEncoding = fields[0];
            if (fields[1] != "0" && fields[1] != "1") return false;
            Team sideToMove = fields[1] == "0" ? Team.White : Team.Black;
            bool betrayalRightAvailable = fields[2] == "1";
            bool postDefectionOccurred = fields[3] == "1";
            if (!double.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double label)) return false;

            record = new TexelPositionRecord(boardEncoding, sideToMove, betrayalRightAvailable, postDefectionOccurred, label);
            return true;
        }
    }
}
