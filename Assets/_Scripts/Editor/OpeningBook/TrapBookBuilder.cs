using System;
using System.IO;
using ChessTheBetrayal.AI.OpeningBook;
using UnityEditor;
using UnityEngine;

namespace ChessTheBetrayal.EditorTools.OpeningBook
{
    /// <summary>
    /// Compiles the project's trap book source into its shipped asset. Shared by the interactive
    /// menu command and by a headless entry point, so a build machine rebuilds it the same way a
    /// developer does.
    ///
    /// Same reasoning as the opening book's builder: compiling is a manual step, so a source edit
    /// with no rebuild leaves the old asset in place and nothing says so. Making it scriptable is
    /// what lets that be checked automatically instead of remembered.
    /// </summary>
    public static class TrapBookBuilder
    {
        /// <summary>The trap source every normal build compiles. Project-relative.</summary>
        public const string DefaultSourcePath = "Assets/_Scripts/AI/OpeningBook/Data/traps.book.txt";

        /// <summary>The compiled asset the game loads. Project-relative.</summary>
        public const string DefaultAssetPath = "Assets/AI/Opening Book/TrapBook.asset";

        /// <summary>
        /// Compiles sourceText into the asset at assetPath, creating it if it doesn't exist, and
        /// returns the number of traps written. Lets TrapBookParseException escape — a trap book
        /// that won't compile must stop the caller rather than quietly leave the old asset behind.
        /// </summary>
        public static int CompileInto(string sourceText, string assetPath)
        {
            var (keys, blunderMoves, bestMoves, names, schemeVersion) = TrapBookCompiler.Compile(sourceText);

            TrapBookAsset asset = AssetDatabase.LoadAssetAtPath<TrapBookAsset>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<TrapBookAsset>();
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            asset.SetEntries(keys, blunderMoves, bestMoves, names, schemeVersion);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            return keys.Length;
        }

        /// <summary>
        /// Rebuilds the shipped trap book from the default source path. Safe to call from a menu
        /// item; also the target of the headless entry point below.
        /// </summary>
        public static int CompileDefaultTrapBook()
        {
            string absoluteSourcePath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? string.Empty, DefaultSourcePath);

            return CompileInto(File.ReadAllText(absoluteSourcePath), DefaultAssetPath);
        }

        [MenuItem("Chess: The Betrayal/AI/Rebuild Trap Book")]
        private static void RebuildFromMenu()
        {
            try
            {
                int traps = CompileDefaultTrapBook();
                Debug.Log($"Rebuilt trap book: {traps} trap(s) from '{DefaultSourcePath}'.");
            }
            catch (TrapBookParseException ex)
            {
                EditorUtility.DisplayDialog("Trap book compile failed", ex.Message, "OK");
            }
        }

        /// <summary>
        /// Headless entry point: Unity.exe -batchmode -quit -executeMethod
        /// ChessTheBetrayal.EditorTools.OpeningBook.TrapBookBuilder.CompileDefaultTrapBookHeadless
        /// Exits non-zero on a bad book so an automated caller fails loudly instead of carrying on
        /// with a stale asset.
        /// </summary>
        public static void CompileDefaultTrapBookHeadless()
        {
            try
            {
                int traps = CompileDefaultTrapBook();
                Debug.Log($"TRAP BOOK REBUILT: {traps} trap(s) from '{DefaultSourcePath}'.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"TRAP BOOK COMPILE FAILED: {ex.Message}");
                EditorApplication.Exit(1);
            }
        }
    }
}
