using System.Collections.Generic;
using System.Text;

namespace ChessTheBetrayal.Tooling.Agreement
{
    /// <summary>
    /// What a whole agreement run concluded: the headline fractions plus every position's own
    /// outcome, so a change in the headline can always be traced to the positions that moved
    /// without paying to run the whole set again.
    ///
    /// RawAgreement is the primary number. SelectedAgreement is what the profile would really play,
    /// and sits at or below the raw figure by however much the profile's personality dials discard.
    /// </summary>
    public sealed class AgreementReport
    {
        public readonly IReadOnlyList<AgreementResult> Results;

        /// <summary>The profile that was measured.</summary>
        public readonly string SubjectProfileId;

        /// <summary>The depth the reference searched to, recorded so a stored number can never be
        /// compared against one produced by a shallower and therefore weaker oracle.</summary>
        public readonly int ReferenceDepth;

        public AgreementReport(IReadOnlyList<AgreementResult> results, string subjectProfileId, int referenceDepth)
        {
            Results = results;
            SubjectProfileId = subjectProfileId;
            ReferenceDepth = referenceDepth;
        }

        public int PositionCount => Results.Count;

        public int RawAgreedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Results.Count; i++)
                    if (Results[i].RawAgreed) count++;
                return count;
            }
        }

        public int SelectedAgreedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Results.Count; i++)
                    if (Results[i].SelectedAgreed) count++;
                return count;
            }
        }

        /// <summary>Fraction of positions where the search's own best move matched the reference.
        /// An empty run reports 0 rather than dividing by zero. Counts every position, including any
        /// the reference could not hold still on — see <see cref="RawAgreementWhereTheReferenceHeld"/>
        /// for the same figure with those left out.</summary>
        public double RawAgreement => PositionCount == 0 ? 0.0 : (double)RawAgreedCount / PositionCount;

        /// <summary>Positions where the reference names the same move two plies shallower as it does
        /// at its own depth, so its answer is about the position rather than the depth.</summary>
        public int StablePositionCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Results.Count; i++)
                    if (Results[i].ReferenceIsStable) count++;
                return count;
            }
        }

        /// <summary>
        /// Raw agreement counted only over the positions the reference held still on.
        ///
        /// The headline figure above divides by every position, which means a position where the
        /// reference changes its mind between plies contributes a match or a miss decided by which
        /// depth somebody picked. This is the same measurement with that noise left out, and where
        /// the two figures disagree it is this one worth reading.
        /// </summary>
        public double RawAgreementWhereTheReferenceHeld
        {
            get
            {
                int stable = 0;
                int agreed = 0;
                for (int i = 0; i < Results.Count; i++)
                {
                    if (!Results[i].ReferenceIsStable) continue;
                    stable++;
                    if (Results[i].RawAgreed) agreed++;
                }

                return stable == 0 ? 0.0 : (double)agreed / stable;
            }
        }

        /// <summary>Fraction where the move the profile would actually play matched.</summary>
        public double SelectedAgreement => PositionCount == 0 ? 0.0 : (double)SelectedAgreedCount / PositionCount;

        /// <summary>
        /// How often the subject ran out of search before reaching the depth it was configured for.
        /// A low agreement figure means something quite different depending on this number: mostly
        /// full-depth disagreements point at the evaluator, mostly cut-short ones point at speed.
        /// </summary>
        public int CutShortCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Results.Count; i++)
                    if (Results[i].DepthReached < Results[i].DepthCeiling) count++;
                return count;
            }
        }

        /// <summary>A short summary followed by each disagreement in full — the whole point being
        /// that a contributor can read why the number moved without re-running the set.</summary>
        public string Describe()
        {
            var builder = new StringBuilder();
            builder.AppendLine($"'{SubjectProfileId}' vs depth-{ReferenceDepth} reference over {PositionCount} positions: " +
                $"raw agreement {RawAgreedCount}/{PositionCount} ({RawAgreement:P1}), " +
                $"as-played {SelectedAgreedCount}/{PositionCount} ({SelectedAgreement:P1}), " +
                $"{CutShortCount} cut short of their depth ceiling.");
            builder.AppendLine($"  Reference held still on {StablePositionCount}/{PositionCount}; " +
                $"raw agreement over those alone {RawAgreementWhereTheReferenceHeld:P1}. Where this " +
                "differs from the figure above, the difference is positions the reference answers " +
                "differently from one ply to the next.");

            for (int i = 0; i < Results.Count; i++)
            {
                if (!Results[i].RawAgreed)
                    builder.AppendLine("  " + Results[i].DescribeDisagreement());
            }

            return builder.ToString();
        }
    }
}
