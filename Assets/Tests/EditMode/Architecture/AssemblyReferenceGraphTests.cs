using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace ChessTheBetrayal.Tests.EditMode.Architecture
{
    /// <summary>
    /// Pins the layering the assembly definitions draw.
    ///
    /// Every boundary in this project is enforced by refusing to compile: an absent reference, a
    /// narrowed internals grant, an assembly built without the engine. That works in one direction
    /// only. Code that breaks a rule stops compiling, but deleting the rule cannot break code that
    /// was already obeying it, so a reference put back is a green diff and nobody finds out until
    /// something reaches across the boundary months later. Reading the table below is the review
    /// that used to depend on somebody noticing an .asmdef in a diff.
    /// </summary>
    [TestFixture]
    public class AssemblyReferenceGraphTests
    {
        private static readonly Dictionary<string, string[]> ExpectedReferences =
            new Dictionary<string, string[]>
            {
                // The chess rules, and nothing else. Everything below depends on this; it depends
                // on nothing, which is what lets it be tested with no engine and no scene.
                ["ChessTheBetrayal.Core"] = new string[0],

                // Platform edges: files, device details, the clock. Deliberately empty, because it
                // knows how to talk to the machine and nothing at all about chess.
                ["ChessTheBetrayal.Infrastructure"] = new string[0],

                // A notch cut out of a phone screen is not a UI concern in any sense the rest of
                // the UI shares, and it needs nothing to do its job.
                ["ChessTheBetrayal.UI.SafeArea"] = new string[0],

                ["ChessTheBetrayal.AI"] = new[] { "ChessTheBetrayal.Core" },
                ["ChessTheBetrayal.Events"] = new[] { "ChessTheBetrayal.Core" },
                ["ChessTheBetrayal.Gameplay.Flow"] = new[] { "ChessTheBetrayal.Core" },
                ["ChessTheBetrayal.Network"] = new[] { "ChessTheBetrayal.Core" },

                ["ChessTheBetrayal.Gameplay.Interaction"] = new[]
                {
                    "ChessTheBetrayal.Core",
                    "ChessTheBetrayal.Events",
                    "ChessTheBetrayal.Infrastructure",
                },

                ["ChessTheBetrayal.Gameplay.Manager"] = new[]
                {
                    "ChessTheBetrayal.Core",
                    "ChessTheBetrayal.AI",
                    "ChessTheBetrayal.Gameplay.Interaction",
                    "ChessTheBetrayal.Events",
                },

                // The one assembly allowed to know about nearly everything, because wiring the
                // parts together at startup is the single job that genuinely needs to see them all.
                ["ChessTheBetrayal.App"] = new[]
                {
                    "ChessTheBetrayal.Core",
                    "ChessTheBetrayal.AI",
                    "ChessTheBetrayal.Events",
                    "ChessTheBetrayal.Infrastructure",
                    "ChessTheBetrayal.Gameplay.Flow",
                    "ChessTheBetrayal.Gameplay.Manager",
                    "ChessTheBetrayal.UI",
                },

                ["ChessTheBetrayal.UI"] = new[]
                {
                    "ChessTheBetrayal.Core",
                    "ChessTheBetrayal.AI",
                    "ChessTheBetrayal.Events",
                    "ChessTheBetrayal.Infrastructure",
                    "Unity.TextMeshPro",
                    "PrimeTween.Runtime",
                },

                // No reference to UI, and that absence is the point: what happens on the board and
                // what happens on a canvas travel in one direction, through events.
                ["ChessTheBetrayal.View"] = new[]
                {
                    "ChessTheBetrayal.Core",
                    "ChessTheBetrayal.Events",
                    "ChessTheBetrayal.Infrastructure",
                    "ChessTheBetrayal.Gameplay.Interaction",
                    "Unity.Cinemachine",
                    "Unity.InputSystem",
                    "PrimeTween.Runtime",
                },

                ["ChessTheBetrayal.EditorTools"] = new[]
                {
                    "ChessTheBetrayal.AI",
                    "ChessTheBetrayal.Core",
                    "ChessTheBetrayal.Events",
                    "ChessTheBetrayal.UI",
                    "ChessTheBetrayal.Tooling",
                },

                ["ChessTheBetrayal.Tooling"] = new[]
                {
                    "ChessTheBetrayal.Core",
                    "ChessTheBetrayal.AI",
                    "ChessTheBetrayal.Events",
                    "ChessTheBetrayal.Gameplay.Manager",
                },

                ["ChessTheBetrayal.Tests.EditMode"] = new[]
                {
                    "ChessTheBetrayal.Core",
                    "ChessTheBetrayal.Gameplay.Flow",
                    "ChessTheBetrayal.Gameplay.Interaction",
                    "ChessTheBetrayal.Gameplay.Manager",
                    "ChessTheBetrayal.AI",
                    "ChessTheBetrayal.Events",
                    "ChessTheBetrayal.Tooling",
                    "ChessTheBetrayal.EditorTools",
                    "ChessTheBetrayal.UI.SafeArea",
                    "ChessTheBetrayal.UI",
                    "ChessTheBetrayal.View",
                    "ChessTheBetrayal.Infrastructure",
                    "PrimeTween.Runtime",

                    // The input system, and the harness that can drive a fake touchscreen through
                    // it. Granted for the same reason PrimeTween is: what the game reads from a
                    // touchscreen when several fingers are involved cost a device session to work
                    // out, and the only way to hold that knowledge is to exercise a real one.
                    "Unity.InputSystem",
                    "Unity.InputSystem.TestFramework",
                },
            };

        // Everything that exists to build or measure the game rather than to be part of it. Unity
        // reads an empty platform list as every platform, so empty is the shipping case.
        private static readonly string[] EditorOnlyAssemblies =
        {
            "ChessTheBetrayal.EditorTools",
            "ChessTheBetrayal.Tooling",
            "ChessTheBetrayal.Tests.EditMode",
        };

        private static IReadOnlyList<AssemblyManifest> _assemblies;

        [OneTimeSetUp]
        public void ReadTheAssemblyDefinitions() => _assemblies = ProjectAssemblies.ReadAll();

        [Test]
        public void EveryAssemblyDefinitionInTheProjectIsAccountedForHere()
        {
            IEnumerable<string> onDisk = _assemblies.Select(a => a.Name);

            Assert.That(onDisk, Is.EquivalentTo(ExpectedReferences.Keys),
                "An assembly was added or removed without the layering table being updated. A new " +
                "assembly is a new boundary, and it is worth saying out loud what it may depend on.");
        }

        [Test]
        public void EveryAssemblyReferencesExactlyWhatTheLayeringGrantsIt()
        {
            var wrong = new StringBuilder();

            foreach (AssemblyManifest assembly in _assemblies)
            {
                if (!ExpectedReferences.TryGetValue(assembly.Name, out string[] expected)) continue;

                string[] added = assembly.References.Except(expected).ToArray();
                string[] withdrawn = expected.Except(assembly.References).ToArray();
                if (added.Length == 0 && withdrawn.Length == 0) continue;

                wrong.AppendLine($"{assembly.Name} ({assembly.Folder})");
                if (added.Length > 0)
                    wrong.AppendLine($"    references, but is not granted: {string.Join(", ", added)}");
                if (withdrawn.Length > 0)
                    wrong.AppendLine($"    is granted, but no longer references: {string.Join(", ", withdrawn)}");
            }

            Assert.That(wrong.ToString(), Is.Empty,
                "The assembly definitions and the layering table disagree. Both directions matter: a " +
                "reference nobody granted is a boundary crossed, and a granted one that has gone is a " +
                "boundary quietly withdrawn.\n" + wrong);
        }

        [Test]
        public void OnlyTheRulesAssemblyIsBuiltWithoutTheEngine()
        {
            IEnumerable<string> engineFree = _assemblies.Where(a => a.NoEngineReferences).Select(a => a.Name);

            Assert.That(engineFree, Is.EquivalentTo(new[] { "ChessTheBetrayal.Core" }),
                "Core is built with no reference to UnityEngine so a chess rule cannot reach for an " +
                "engine type, which is also what lets the rules be tested without starting one. " +
                "Turning that off compiles green and takes the guarantee with it.");
        }

        [Test]
        public void EveryAssemblyNamesItsRootNamespaceAfterItself()
        {
            IEnumerable<string> mismatched = _assemblies
                .Where(a => a.RootNamespace != a.Name)
                .Select(a => $"{a.Name} declares rootNamespace '{a.RootNamespace}'");

            Assert.That(mismatched, Is.Empty,
                "rootNamespace is what puts the right namespace on a script created through the " +
                "Editor. Where it disagrees with the assembly name, every new file starts out in the " +
                "wrong place and somebody has to catch it by eye.");
        }

        [Test]
        public void TheAssembliesThatBuildAndMeasureTheGameStayOutOfIt()
        {
            var wrong = new StringBuilder();

            foreach (AssemblyManifest assembly in _assemblies)
            {
                bool shouldBeEditorOnly = EditorOnlyAssemblies.Contains(assembly.Name);
                bool isEditorOnly = assembly.IncludePlatforms.Length == 1
                    && assembly.IncludePlatforms[0] == "Editor";

                if (shouldBeEditorOnly && !isEditorOnly)
                    wrong.AppendLine($"{assembly.Name} would ship: includePlatforms is " +
                        $"[{string.Join(", ", assembly.IncludePlatforms)}]");
                else if (!shouldBeEditorOnly && assembly.IncludePlatforms.Length > 0)
                    wrong.AppendLine($"{assembly.Name} is part of the game but restricts platforms: " +
                        $"[{string.Join(", ", assembly.IncludePlatforms)}]");
            }

            Assert.That(wrong.ToString(), Is.Empty,
                "Tournaments, benchmarks and the book compiler are development instruments. They pull " +
                "in test-shaped code and none of them has ever been sized for a phone, so a player's " +
                "build should not carry them.\n" + wrong);
        }
    }
}
