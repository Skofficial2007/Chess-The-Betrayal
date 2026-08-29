using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ChessTheBetrayal.Tests.EditMode.Architecture
{
    /// <summary>
    /// Finds this project's assembly definitions on disk and answers which of them owns a given
    /// source file. Imported third-party assemblies are left out: their layering is their authors'
    /// decision, and a package update would otherwise fail a test about our own structure.
    /// </summary>
    internal static class ProjectAssemblies
    {
        private const string ProjectPrefix = "ChessTheBetrayal.";

        public static string ProjectRoot =>
            Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');

        public static string AbsolutePathOf(string projectRelative) => ProjectRoot + "/" + projectRelative;

        public static IReadOnlyList<AssemblyManifest> ReadAll()
        {
            var found = new List<AssemblyManifest>();

            foreach (string path in Directory.GetFiles(Application.dataPath, "*.asmdef", SearchOption.AllDirectories))
            {
                string folder = ToProjectRelative(Path.GetDirectoryName(path));
                AssemblyManifest manifest = AssemblyManifest.Parse(File.ReadAllText(path), folder);

                if (manifest != null && manifest.Name.StartsWith(ProjectPrefix, StringComparison.Ordinal))
                    found.Add(manifest);
            }

            found.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            return found;
        }

        /// <summary>
        /// Every C# file the given assembly compiles, as project-relative paths. A file belongs to
        /// the nearest assembly definition above it rather than the outermost one, which is what
        /// lets a definition sit inside another assembly's folder - Gameplay/Bootstrap and
        /// UI/SafeArea both do.
        /// </summary>
        public static IReadOnlyList<string> SourceFilesOf(AssemblyManifest assembly, IReadOnlyList<AssemblyManifest> all)
        {
            var nestedInside = new List<string>();
            foreach (AssemblyManifest other in all)
            {
                if (!ReferenceEquals(other, assembly) && other.Folder.StartsWith(assembly.Folder + "/", StringComparison.Ordinal))
                    nestedInside.Add(other.Folder + "/");
            }

            var owned = new List<string>();
            foreach (string path in Directory.GetFiles(AbsolutePathOf(assembly.Folder), "*.cs", SearchOption.AllDirectories))
            {
                string relative = ToProjectRelative(path);
                if (!IsInsideAnyOf(relative, nestedInside)) owned.Add(relative);
            }

            owned.Sort(StringComparer.Ordinal);
            return owned;
        }

        private static bool IsInsideAnyOf(string relativePath, List<string> folders)
        {
            foreach (string folder in folders)
                if (relativePath.StartsWith(folder, StringComparison.Ordinal)) return true;

            return false;
        }

        private static string ToProjectRelative(string absolute)
        {
            string normalised = absolute.Replace('\\', '/');
            return normalised.StartsWith(ProjectRoot + "/", StringComparison.Ordinal)
                ? normalised.Substring(ProjectRoot.Length + 1)
                : normalised;
        }
    }
}
