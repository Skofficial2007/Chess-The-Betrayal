using System;
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

        /// <summary>
        /// The per-depth clock has to stay out of the editor/development-build guard, and no test
        /// running in the editor can tell whether it is inside one - the symbol is always defined
        /// there, so the code compiles either way and every assertion about it passes either way.
        /// This reads the source instead, which is a proxy and is written down as one.
        ///
        /// It matters because the builds that go to testers are release builds. Behind the guard the
        /// number is compiled out of exactly the builds it exists to describe, which is where it
        /// started and is easy to put back while tidying the two lines together again. The real
        /// proof is a release build's report carrying a non-zero time; this is what fails first.
        /// </summary>
        [Test]
        [TestCase("AssignElapsedMsAfterDepth(depth,", TestName = "TheClimbToEachDepthIsTimed_OnEveryBuild")]
        [TestCase("ResetElapsedMsCurve()", TestName = "AndTheCurveIsClearedPerSearch_OnEveryBuild")]
        public void ThePerDepthClockIsCompiledIntoEveryBuild(string marker)
        {
            string path = Path.Combine(Application.dataPath, "_Scripts/AI/Search/AlphaBetaSearch.cs");
            Assume.That(File.Exists(path), $"Expected the search at {path}.");

            string[] lines = File.ReadAllLines(path);
            int site = Array.FindIndex(lines, l => l.Contains(marker) && !l.TrimStart().StartsWith("//"));
            Assert.That(site, Is.GreaterThan(-1),
                $"'{marker}' has gone, or been renamed - this guard now checks nothing.");

            // Walk back to whichever directive governs it. An #endif above means the nearest block
            // closed before reaching this line, so it sits outside one.
            for (int i = site - 1; i >= 0; i--)
            {
                string line = lines[i].TrimStart();
                if (line.StartsWith("#endif")) return;
                if (line.StartsWith("#if"))
                {
                    Assert.Fail(
                        $"AlphaBetaSearch.cs:{site + 1} sits inside '{line}'.\n"
                        + "A release build defines neither symbol, so this compiles out of the builds handed "
                        + "to testers - the only builds it exists to describe. Depth alone is whole plies and "
                        + "cannot show a device getting slower until it drops one.");
                }
            }
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
