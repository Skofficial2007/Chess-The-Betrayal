using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Tooling.Agreement
{
    /// <summary>
    /// One position's agreement outcome — enough detail to understand a disagreement without
    /// re-running anything, the same philosophy YardstickResult follows.
    ///
    /// Two separate agreement flags, because a profile produces two different moves. RawAgreed
    /// compares the search's own best move, which is the clean measure of evaluation and search
    /// quality. SelectedAgreed compares the move the profile would actually play after its
    /// personality dials have had their say. The gap between them is what those dials cost that
    /// tier — for the top profile they are both switched off and the two are identical by
    /// construction, while a weak profile deliberately discards good moves and the difference shows
    /// exactly how often.
    ///
    /// Recording DepthReached is what keeps a disagreement diagnosable. Choosing a different move
    /// while searching as deep as it was allowed to is an evaluation disagreement; choosing a
    /// different move having been cut short is a speed problem instead. Without the depth, the two
    /// are indistinguishable and the whole measurement says very little. BlunderRollFired supplies
    /// the third possible cause for the selected move specifically: the profile rolled to play
    /// something other than its own best idea on purpose.
    /// </summary>
    public sealed class AgreementResult
    {
        /// <summary>Index of the position within the set that was run, so a failure names something
        /// a contributor can go and look at.</summary>
        public readonly int PositionIndex;

        /// <summary>Whether the search's own best move matched the reference.</summary>
        public readonly bool RawAgreed;

        /// <summary>Whether the move the profile would actually play matched the reference.</summary>
        public readonly bool SelectedAgreed;

        public readonly MoveCommand RawMove;
        public readonly MoveCommand SelectedMove;
        public readonly MoveCommand ReferenceMove;

        /// <summary>The subject's score for its own best move, from the mover's point of view.</summary>
        public readonly int SubjectScoreCp;

        /// <summary>The reference's score for its choice, from the mover's point of view.</summary>
        public readonly int ReferenceScoreCp;

        /// <summary>How deep the subject actually got, against the ceiling it was allowed.</summary>
        public readonly int DepthReached;

        /// <summary>The subject profile's configured depth ceiling.</summary>
        public readonly int DepthCeiling;

        /// <summary>The depth the reference searched to.</summary>
        public readonly int ReferenceDepth;

        /// <summary>Whether the profile's blunder roll actually replaced its best move.</summary>
        public readonly bool BlunderRollFired;

        public readonly double SubjectElapsedMs;

        /// <summary>
        /// Whether the reference names the same move two plies shallower as it does at its own
        /// depth. Where it does not, its answer here is a fact about the depth somebody chose
        /// rather than about the position, and agreeing or disagreeing with it means nothing.
        /// </summary>
        public readonly bool ReferenceIsStable;

        public AgreementResult(int positionIndex, bool rawAgreed, bool selectedAgreed,
            MoveCommand rawMove, MoveCommand selectedMove, MoveCommand referenceMove,
            int subjectScoreCp, int referenceScoreCp, int depthReached, int depthCeiling,
            int referenceDepth, bool blunderRollFired, double subjectElapsedMs,
            bool referenceIsStable)
        {
            PositionIndex = positionIndex;
            RawAgreed = rawAgreed;
            SelectedAgreed = selectedAgreed;
            RawMove = rawMove;
            SelectedMove = selectedMove;
            ReferenceMove = referenceMove;
            SubjectScoreCp = subjectScoreCp;
            ReferenceScoreCp = referenceScoreCp;
            DepthReached = depthReached;
            DepthCeiling = depthCeiling;
            ReferenceDepth = referenceDepth;
            BlunderRollFired = blunderRollFired;
            SubjectElapsedMs = subjectElapsedMs;
            ReferenceIsStable = referenceIsStable;
        }

        /// <summary>
        /// Everything needed to diagnose one disagreement: which position, what each side chose and
        /// scored, how deep the subject actually got against how deep it was allowed, and whether
        /// the profile threw its own best move away on purpose.
        /// </summary>
        public string DescribeDisagreement()
        {
            string depthNote = DepthReached >= DepthCeiling
                ? "searched its full depth, so this is an evaluation disagreement"
                : "was cut short of its depth ceiling, so this may be a speed problem rather than an evaluation one";

            string selectionNote = BlunderRollFired
                ? " The profile's blunder roll fired, so the selected move was deliberately not its own best."
                : "";

            return $"[position {PositionIndex}] reference (depth {ReferenceDepth}) chose " +
                $"{ReferenceMove.StartPosition}->{ReferenceMove.EndPosition} ({ReferenceScoreCp}cp) " +
                $"but the subject chose {RawMove.StartPosition}->{RawMove.EndPosition} ({SubjectScoreCp}cp) " +
                $"at depth {DepthReached}/{DepthCeiling} in {SubjectElapsedMs:F0}ms — it {depthNote}.{selectionNote}";
        }
    }
}
