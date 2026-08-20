using System.Collections.Generic;
using System.Text;

namespace ChessTheBetrayal.Tooling
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
        /// An empty run reports 0 rather than dividing by zero.</summary>
        public double RawAgreement => PositionCount == 0 ? 0.0 : (double)RawAgreedCount / PositionCount;

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

            for (int i = 0; i < Results.Count; i++)
            {
                if (!Results[i].RawAgreed)
                    builder.AppendLine("  " + Results[i].DescribeDisagreement());
            }

            return builder.ToString();
        }
    }
}
