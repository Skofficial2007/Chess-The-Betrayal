using System;
using System.Collections.Generic;

namespace ChessTheBetrayal.AI.Profiles
{
    /// <summary>Resolves profile ids against the code-side <see cref="AIProfileTable.BuiltIn"/> roster.</summary>
    public sealed class AIProfileTableProvider : IAIProfileProvider
    {
        private readonly IReadOnlyList<AIProfile> _roster;

        /// <summary>
        /// Resolves against the shipped roster unless handed another one.
        ///
        /// Every shipped row already sits inside the guardrail's range, so the clamp this applies
        /// never changes anything and nothing could tell whether it still ran. Taking the roster as
        /// a parameter is what lets a row that does need clamping be resolved through this exact
        /// code path rather than through a stand-in that reimplements it — and the day profiles come
        /// from authored assets instead of a table, that is the seam they arrive through.
        /// </summary>
        public AIProfileTableProvider(IReadOnlyList<AIProfile> roster = null)
        {
            _roster = roster ?? AIProfileTable.BuiltIn;
        }

        public AIProfile Resolve(string id)
        {
            var table = _roster;

            for (int i = 0; i < table.Count; i++)
            {
                if (string.Equals(table[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return AIProfileGuardrails.Apply(table[i]);
            }

            for (int i = 0; i < table.Count; i++)
            {
                if (string.Equals(table[i].Id, AIProfileTable.DefaultId, StringComparison.OrdinalIgnoreCase))
                    return AIProfileGuardrails.Apply(table[i]);
            }

            throw new InvalidOperationException("AIProfileTable.BuiltIn must contain the DefaultId row.");
        }
    }
}
