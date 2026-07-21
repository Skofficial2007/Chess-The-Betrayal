using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>
    /// What makes a yardstick position's expected move admissible without an external chess engine
    /// to defer to. Ordered roughly by how strong a guarantee each class gives.
    /// </summary>
    public enum YardstickProofClass
    {
        /// <summary>A forced mate the move-generator itself proves: after the expected move, every
        /// reply (if any) still ends in mate within the position's stated depth, and no OTHER first
        /// move mates that fast. Decidable by exhaustive search alone — no evaluator opinion
        /// involved.</summary>
        ForcedMate,

        /// <summary>Every legal alternative to the expected move is shown, by a shallow exhaustive
        /// search, to concede material against best defense — the expected move is the only one
        /// that doesn't. A real but weaker guarantee than ForcedMate: "material" is the proof
        /// standard, not "objectively best."</summary>
        ForcedMaterialGain,

        /// <summary>A Betrayal Act whose correctness reduces to ForcedMate or ForcedMaterialGain —
        /// exists as its own label so failures here are legible as "the AI misjudged Betrayal"
        /// rather than folded anonymously into the other two classes.</summary>
        BetrayalTrap
    }

    /// <summary>
    /// One hand-authored position with a provably correct answer — the yardstick's only unit.
    /// Deliberately carries no notion of "second best" or partial credit: either the AI finds the
    /// proven move or it doesn't, and a proof class is required so a failure can be told apart from
    /// "the position wasn't actually provable" (which is a fixture-authoring bug, not an AI one).
    /// </summary>
    public sealed class YardstickPosition
    {
        public readonly string Name;
        public readonly YardstickProofClass ProofClass;
        public readonly string Note;
        private readonly System.Func<BoardState> _buildBoard;
        private readonly Vector2Int _expectedFrom;
        private readonly Vector2Int _expectedTo;
        private readonly BetrayalStage _expectedStage;

        public YardstickPosition(string name, YardstickProofClass proofClass, string note,
            System.Func<BoardState> buildBoard, Vector2Int expectedFrom, Vector2Int expectedTo,
            BetrayalStage expectedStage = BetrayalStage.None)
        {
            Name = name;
            ProofClass = proofClass;
            Note = note;
            _buildBoard = buildBoard;
            _expectedFrom = expectedFrom;
            _expectedTo = expectedTo;
            _expectedStage = expectedStage;
        }

        /// <summary>Builds a fresh board each call — a search mutates the board it's given via
        /// ApplyMove/UndoMove, so every caller (the authoring-time proof AND the AI run) needs its
        /// own independent instance.</summary>
        public BoardState BuildBoard() => _buildBoard();

        public bool Matches(MoveCommand move) =>
            move.StartPosition == _expectedFrom && move.EndPosition == _expectedTo && move.Stage == _expectedStage;

        public string ExpectedMoveDescription =>
            $"{_expectedFrom} -> {_expectedTo}" + (_expectedStage == BetrayalStage.None ? "" : $" ({_expectedStage})");
    }
}
