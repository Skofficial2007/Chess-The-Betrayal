using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace ChessTheBetrayal.Tests.EditMode.Architecture
{
    /// <summary>
    /// Rules that exist in one place, and stay there.
    ///
    /// A rule the shipping code applies and a measurement harness re-implements is worse than a
    /// duplicated helper, because the two only have to disagree once. The harness keeps reporting a
    /// clean number while measuring an engine nobody plays, and nothing in the suite says so — which
    /// is not a hypothetical: with the rescore margin below deliberately broken, one test out of
    /// fourteen hundred noticed, and every tournament, ladder and agreement run passed.
    ///
    /// A convention cannot hold on its own here. Four copies of the rule below were replaced by the
    /// property once already, and five more were left behind because the search stopped at the two
    /// files that were being edited at the time.
    /// </summary>
    [TestFixture]
    public class SingleSourceOfTruthTests
    {
        /// <summary>
        /// This file names the very thing it forbids, so it would find itself. Skipping it by name
        /// is the honest version of the trick — the alternative is spelling the pattern in pieces so
        /// it cannot match its own source, which hides what is being looked for from whoever reads
        /// this next.
        /// </summary>
        private const string ThisFile = "SingleSourceOfTruthTests.cs";

        /// <summary>
        /// `Math.Max(BlunderMarginCp, TieBreakWindowCp)` in either argument order, with or without a
        /// `System.` in front. It does not catch the same rule written out as a ternary — a wider
        /// net would start matching ordinary code, and every copy this has had so far took this
        /// exact shape.
        /// </summary>
        private static readonly Regex HandRolledRescoreMargin = new Regex(
            @"Math\.Max\s*\([^)]*\b(BlunderMarginCp[^)]*TieBreakWindowCp|TieBreakWindowCp[^)]*BlunderMarginCp)\b",
            RegexOptions.Compiled);

        [Test]
        public void TheRescoreMarginIsAskedFor_NeverWorkedOutAgain()
        {
            var offenders = new List<string>();

            foreach (string path in Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(path) == ThisFile) continue;

                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (HandRolledRescoreMargin.IsMatch(lines[i]))
                    {
                        offenders.Add($"{Relative(path)}:{i + 1}");
                    }
                }
            }

            Assert.That(offenders, Is.Empty, Explain(offenders));
        }

        private static string Explain(List<string> offenders)
        {
            var message = new StringBuilder();
            message.AppendLine("Which dial sets the rescore margin is AIProfile.RescoreMarginCp's answer to give.");
            message.AppendLine("Ask it for the value instead of working the rule out again here:");
            foreach (string offender in offenders) message.AppendLine("    " + offender);
            message.Append("A copy stays right until the rule changes, and then it measures an engine nobody plays.");
            return message.ToString();
        }

        private static string Relative(string absolutePath)
        {
            string root = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
            string full = absolutePath.Replace('\\', '/');
            return full.StartsWith(root) ? full.Substring(root.Length + 1) : full;
        }
    }
}
