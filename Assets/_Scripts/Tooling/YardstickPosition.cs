using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Tooling
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
        BetrayalTrap,

        /// <summary>A move that captures nothing and mates nothing, yet wins material by force
        /// several plies later — a pawn push that cannot be stopped from queening being the typical
        /// shape. Verified by searching every legal move with a material-only evaluator and
        /// requiring the expected move to come out ahead by a clear margin, so the production
        /// evaluator never votes on its own admissibility. The weakest guarantee of the four in the
        /// sense that it depends on a chosen proving depth rather than on an immediate fact, and by
        /// far the most useful for measuring evaluation: a position whose answer is decided by
        /// material already on the board tells you nothing about the terms that judge everything
        /// else.</summary>
        QuietPositionalGain
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

        /// <summary>How deep the QuietPositionalGain proof searches. Ignored by the other proof
        /// classes, which answer immediate questions and need no depth at all. Kept per-position
        /// because how far away a quiet move's payoff sits is a property of that position — a pawn
        /// three squares from queening needs fewer plies than one that is five.</summary>
        public readonly int ProvingDepth;

        /// <summary>How far ahead of every alternative the expected move must score for a
        /// QuietPositionalGain proof to accept it. A margin rather than a bare "is best" so a
        /// position whose top two moves are a rounding error apart is rejected: the AI choosing
        /// either would be defensible, and a position that punishes a defensible choice measures
        /// nothing.</summary>
        public readonly int ProvingMarginCp;

        private readonly System.Func<BoardState> _buildBoard;
        private readonly Vector2Int _expectedFrom;
        private readonly Vector2Int _expectedTo;
        private readonly BetrayalStage _expectedStage;

        public YardstickPosition(string name, YardstickProofClass proofClass, string note,
            System.Func<BoardState> buildBoard, Vector2Int expectedFrom, Vector2Int expectedTo,
            BetrayalStage expectedStage = BetrayalStage.None,
            int provingDepth = 0, int provingMarginCp = 0)
        {
            Name = name;
            ProofClass = proofClass;
            Note = note;
            ProvingDepth = provingDepth;
            ProvingMarginCp = provingMarginCp;
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
