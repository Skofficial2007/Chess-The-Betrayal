using System;

namespace ChessTheBetrayal.AI
{
    /// <summary>Resolves profile ids against the code-side <see cref="AIProfileTable.BuiltIn"/> roster.</summary>
    public sealed class AIProfileTableProvider : IAIProfileProvider
    {
        public AIProfile Resolve(string id)
        {
            var table = AIProfileTable.BuiltIn;

            for (int i = 0; i < table.Count; i++)
            {
                if (string.Equals(table[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return table[i];
            }

            for (int i = 0; i < table.Count; i++)
            {
                if (string.Equals(table[i].Id, AIProfileTable.DefaultId, StringComparison.OrdinalIgnoreCase))
                    return table[i];
            }

            throw new InvalidOperationException("AIProfileTable.BuiltIn must contain the DefaultId row.");
        }
    }
}
