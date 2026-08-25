using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace ChessTheBetrayal.Tests.EditMode.Architecture
{
    /// <summary>
    /// Holds every file to the namespace its folder implies.
    ///
    /// rootNamespace on an assembly definition only reaches scripts created through the Editor. A
    /// file written by hand, or moved with its header left alone, keeps whatever namespace it had,
    /// and the compiler is perfectly happy either way - so until now the convention was upheld by
    /// whoever happened to be looking. This is the part of the layout a reader relies on most: the
    /// namespace in an error message should tell you which folder to open.
    /// </summary>
    [TestFixture]
    public class NamespaceConventionTests
    {
        // Folders that group files for whoever edits them without adding a level for whoever reads
        // them. A namespace is how a consumer names a type; a folder is how an author finds one.
        // Where nothing outside the assembly can name what is inside, a level for it is noise -
        // which is a licence, not a preference, so the second test below goes and checks the
        // condition holds rather than taking this list at its word.
        private static readonly string[] FoldersThatDeclineANamespaceLevel =
        {
            "Assets/_Scripts/AI/Evaluation/Terms",
            "Assets/_Scripts/Core/Engine/Movement/Pieces",
            "Assets/Tests/EditMode/AI/Support",
            "Assets/Tests/EditMode/Gameplay/Support",
        };

        private static readonly Regex NamespaceDeclaration =
            new Regex(@"^\s*namespace\s+([A-Za-z0-9_.]+)", RegexOptions.Multiline);

        private static IReadOnlyList<AssemblyManifest> _assemblies;

        [OneTimeSetUp]
        public void ReadTheAssemblyDefinitions() => _assemblies = ProjectAssemblies.ReadAll();

        [Test]
        public void EveryFileDeclaresTheNamespaceItsFolderImplies()
        {
            var wrong = new StringBuilder();

            foreach (AssemblyManifest assembly in _assemblies)
            {
                foreach (string file in ProjectAssemblies.SourceFilesOf(assembly, _assemblies))
                {
                    string source = File.ReadAllText(ProjectAssemblies.AbsolutePathOf(file));
                    Match declared = NamespaceDeclaration.Match(source);

                    if (!declared.Success)
                    {
                        // A file carrying only assembly-level attributes has nothing to put in a
                        // namespace. Every one of them grants the tests access to an assembly.
                        if (source.Contains("[assembly:")) continue;

                        wrong.AppendLine($"{file}\n    declares no namespace at all");
                        continue;
                    }

                    string expected = ExpectedNamespaceFor(assembly, FolderOf(file));
                    if (declared.Groups[1].Value == expected) continue;

                    wrong.AppendLine($"{file}\n    declares {declared.Groups[1].Value}\n    folder implies {expected}");
                }
            }

            Assert.That(wrong.ToString(), Is.Empty,
                "A namespace and its folder have come apart. Either move the file to the folder its " +
                "namespace names, or rename the namespace to the folder it is actually in - and if " +
                "the folder is deliberately declining a level, say so in the list this test reads.\n" + wrong);
        }

        [Test]
        public void EveryFolderThatDeclinesALevelHoldsOnlyTypesNobodyOutsideItsAssemblyCanName()
        {
            var wrong = new StringBuilder();

            foreach (string folder in FoldersThatDeclineANamespaceLevel)
            {
                string absolute = ProjectAssemblies.AbsolutePathOf(folder);
                if (!Directory.Exists(absolute))
                {
                    wrong.AppendLine($"{folder}\n    is on the list but no longer exists, so it licenses nothing");
                    continue;
                }

                AssemblyManifest owner = OwnerOf(folder);
                Assembly compiled = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == owner.Name);

                if (compiled == null)
                {
                    wrong.AppendLine($"{folder}\n    belongs to {owner.Name}, which is not loaded here, " +
                        "so this check cannot see what is in it");
                    continue;
                }

                Type[] declared = compiled.GetTypes().Where(t => !t.IsNested).ToArray();

                // One top-level type per file throughout, so the file name identifies the type.
                // Where it does not, say so rather than quietly checking nothing.
                foreach (string file in Directory.GetFiles(absolute, "*.cs", SearchOption.TopDirectoryOnly))
                {
                    string typeName = Path.GetFileNameWithoutExtension(file);
                    Type[] matching = declared.Where(t => t.Name == typeName).ToArray();

                    if (matching.Length != 1)
                    {
                        wrong.AppendLine($"{folder}/{Path.GetFileName(file)}\n    " +
                            $"{matching.Length} types in {owner.Name} are called {typeName}, so this " +
                            "check cannot tell whether the licence holds");
                        continue;
                    }

                    if (matching[0].IsPublic)
                        wrong.AppendLine($"{folder}/{Path.GetFileName(file)}\n    {typeName} is public, " +
                            "so somewhere outside this assembly can name it and the folder owes it a level");
                }
            }

            Assert.That(wrong.ToString(), Is.Empty,
                "A folder is skipping a namespace level it has not earned. The licence for skipping " +
                "one is that the types in it are an implementation detail of the assembly around " +
                "them; a public type is not.\n" + wrong);
        }

        private static string ExpectedNamespaceFor(AssemblyManifest assembly, string folder)
        {
            var levels = new List<string> { assembly.RootNamespace };
            string walked = assembly.Folder;
            string below = folder.Substring(assembly.Folder.Length).Trim('/');

            if (below.Length > 0)
            {
                foreach (string level in below.Split('/'))
                {
                    walked += "/" + level;
                    if (!FoldersThatDeclineANamespaceLevel.Contains(walked)) levels.Add(level);
                }
            }

            return string.Join(".", levels);
        }

        private static AssemblyManifest OwnerOf(string folder)
        {
            return _assemblies
                .Where(a => folder == a.Folder || folder.StartsWith(a.Folder + "/", StringComparison.Ordinal))
                .OrderByDescending(a => a.Folder.Length)
                .First();
        }

        private static string FolderOf(string projectRelativeFile) =>
            projectRelativeFile.Substring(0, projectRelativeFile.LastIndexOf('/'));
    }
}
