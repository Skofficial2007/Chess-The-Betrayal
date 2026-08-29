using System;
using UnityEngine;

namespace ChessTheBetrayal.Tests.EditMode.Architecture
{
    /// <summary>
    /// One assembly definition file, read as data.
    ///
    /// These tests read the .asmdef text rather than asking the compiler what it resolved, because
    /// the declaration is the part nothing else defends. An assembly that references something it
    /// never uses compiles green; so does one that drops a reference nothing happened to rely on.
    /// Either way a boundary can be erased in a one-line diff without a single test noticing, so
    /// the only thing that can notice is something that reads the file and compares it against
    /// what somebody meant.
    /// </summary>
    internal sealed class AssemblyManifest
    {
        public string Name { get; }
        public string RootNamespace { get; }
        public string[] References { get; }
        public string[] IncludePlatforms { get; }

        /// <summary>True when the assembly is built without a reference to UnityEngine, so a file
        /// in it cannot use an engine type even by accident.</summary>
        public bool NoEngineReferences { get; }

        /// <summary>The folder holding the .asmdef, relative to the project root and written with
        /// forward slashes whatever the platform uses. Failure messages quote it, and the namespace
        /// test walks down from it.</summary>
        public string Folder { get; }

        private AssemblyManifest(Layout layout, string folder)
        {
            Name = layout.name;
            RootNamespace = layout.rootNamespace ?? string.Empty;
            References = layout.references ?? Array.Empty<string>();
            IncludePlatforms = layout.includePlatforms ?? Array.Empty<string>();
            NoEngineReferences = layout.noEngineReferences;
            Folder = folder;
        }

        /// <summary>Returns null for a file that does not name an assembly, which is not a shape
        /// Unity accepts and so is not one worth reporting on.</summary>
        public static AssemblyManifest Parse(string json, string folder)
        {
            Layout layout = JsonUtility.FromJson<Layout>(json);
            return string.IsNullOrEmpty(layout?.name) ? null : new AssemblyManifest(layout, folder);
        }

        // Field names have to match the JSON exactly, which is why they break the naming convention
        // used everywhere else. Nothing here assigns them - JsonUtility does, by reflection, and the
        // compiler cannot see that happen.
#pragma warning disable 0649
        [Serializable]
        private sealed class Layout
        {
            public string name;
            public string rootNamespace;
            public string[] references;
            public string[] includePlatforms;
            public bool noEngineReferences;
        }
#pragma warning restore 0649
    }
}
