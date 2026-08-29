using System;
using System.Collections.Generic;
using ChessTheBetrayal.AI.Profiles;

namespace ChessTheBetrayal.EditorTools.Benchmark
{
    /// <summary>
    /// Editable dial set for a hand-built tier. What actually gets played is this run through the
    /// guardrail, so the window cannot assemble a profile the game itself would have refused.
    /// </summary>
    [Serializable]
    public struct CustomProfileDraft
    {
        public string Id;
        public int MaxDepth;
        public int SoftTimeBudgetMs;
        public int HardTimeBudgetMs;
        public float BlunderRate;
        public int BlunderMarginCp;
        public float BetrayalAggression;
        public float AttackDefenseBias;
        public int TieBreakWindowCp;

        public static CustomProfileDraft Default(string id) => new CustomProfileDraft
        {
            Id = id,
            MaxDepth = 3,
            SoftTimeBudgetMs = 1000,
            HardTimeBudgetMs = 1500,
            BlunderRate = 0f,
            BlunderMarginCp = 0,
            BetrayalAggression = 0f,
            AttackDefenseBias = 1f,
            TieBreakWindowCp = 0,
        };

        // The simulator never consults the opening book — deliberately, so a tournament measures
        // raw search strength rather than book coverage (several curated starting positions ARE
        // reachable book lines, so a book probe would short-circuit real searches). The flag is
        // fixed off rather than shown as a dial that would do nothing.
        public AIProfile Build() => AIProfileGuardrails.Apply(new AIProfile(
            Id, MaxDepth, new AITimeBudget(SoftTimeBudgetMs, HardTimeBudgetMs), BlunderRate, BlunderMarginCp,
            BetrayalAggression, AttackDefenseBias, TieBreakWindowCp, useOpeningBook: false));
    }

    /// <summary>
    /// Turns what somebody picked in the tournament window into the profile the games are played
    /// with.
    ///
    /// Separate from the window because it is the part that can be wrong without looking wrong. The
    /// window draws a list of tier names and remembers an index into it; getting that index back to
    /// the right tier is what decides which two players a tournament actually measured, and a
    /// result attributed to the wrong tier reads exactly like a real one.
    /// </summary>
    public static class TournamentProfileChoice
    {
        /// <summary>The index standing for "not a built-in tier, use the dials below".</summary>
        public const int Custom = -1;

        private static readonly IAIProfileProvider Provider = new AIProfileTableProvider();

        /// <summary>Every shipped tier in roster order, with the custom entry last.</summary>
        public static string[] Labels()
        {
            var labels = new List<string>();
            foreach (AIProfile profile in AIProfileTable.BuiltIn) labels.Add(profile.Id);
            labels.Add(CustomLabel);
            return labels.ToArray();
        }

        public const string CustomLabel = "custom...";

        /// <summary>Where the custom entry sits in <see cref="Labels"/>, which is the last row.</summary>
        public static int CustomLabelIndex => AIProfileTable.BuiltIn.Count;

        /// <summary>
        /// The profile a given picker index plays as.
        ///
        /// Both branches resolve the same way the game does. The built-in one used to index the
        /// roster directly and skip the guardrail the custom one applies — which changed nothing,
        /// because every shipped row already sits inside it, and would have stopped changing nothing
        /// the moment somebody added a shallow tier with strong dials.
        /// </summary>
        public static AIProfile Resolve(int choice, CustomProfileDraft draft)
        {
            if (choice == Custom) return draft.Build();

            if (choice < 0 || choice >= AIProfileTable.BuiltIn.Count)
                throw new ArgumentOutOfRangeException(nameof(choice),
                    $"No tier at index {choice}; the roster has {AIProfileTable.BuiltIn.Count}.");

            return Provider.Resolve(AIProfileTable.BuiltIn[choice].Id);
        }
    }
}
