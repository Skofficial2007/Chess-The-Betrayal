using System;
using System.IO;
using ChessTheBetrayal.AI.OpeningBook;
using UnityEditor;
using UnityEngine;

namespace ChessTheBetrayal.EditorTools.OpeningBook
{
    /// <summary>
    /// Compiles the project's opening book source into its shipped asset. Shared by the
    /// interactive menu command and by a headless entry point, so a build machine can rebuild the
    /// book the same way a developer does.
    ///
    /// The headless path exists because the book is compiled by hand: edit the source text,
    /// forget to recompile, and the game keeps playing the old lines with nothing to show for the
    /// change. Making the rebuild scriptable is what lets that be checked automatically instead of
    /// remembered.
    /// </summary>
    public static class OpeningBookBuilder
    {
        /// <summary>The book source every normal build compiles. Project-relative.</summary>
        public const string DefaultSourcePath = "Assets/_Scripts/AI/OpeningBook/Data/openings.book.txt";

        /// <summary>The compiled asset the game loads. Project-relative.</summary>
        public const string DefaultAssetPath = "Assets/AI/Opening Book/OpeningBook.asset";

        /// <summary>
        /// Compiles sourceText into the asset at assetPath, creating it if it doesn't exist, and
        /// returns the number of positions written. Lets OpeningBookParseException escape — a book
        /// that won't compile must stop the caller, never quietly leave the old asset in place.
        /// </summary>
        public static int CompileInto(string sourceText, string assetPath)
        {
            var (keys, packedMoves, weights, schemeVersion) = OpeningBookCompiler.Compile(sourceText);

            OpeningBookAsset asset = AssetDatabase.LoadAssetAtPath<OpeningBookAsset>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<OpeningBookAsset>();
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            asset.SetEntries(keys, packedMoves, weights, schemeVersion);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            return keys.Length;
        }

        /// <summary>
        /// Rebuilds the shipped book from the default source path. Safe to call from a menu item;
        /// also the target of the headless entry point below.
        /// </summary>
        public static int CompileDefaultBook()
        {
            string absoluteSourcePath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? string.Empty, DefaultSourcePath);

            return CompileInto(File.ReadAllText(absoluteSourcePath), DefaultAssetPath);
        }

        [MenuItem("Chess: The Betrayal/AI/Rebuild Opening Book")]
        private static void RebuildFromMenu()
        {
            try
            {
                int positions = CompileDefaultBook();
                Debug.Log($"Rebuilt opening book: {positions} position(s) from '{DefaultSourcePath}'.");
            }
            catch (OpeningBookParseException ex)
            {
                EditorUtility.DisplayDialog("Opening book compile failed", ex.Message, "OK");
            }
        }

        /// <summary>
        /// Headless entry point: Unity.exe -batchmode -quit -executeMethod
        /// ChessTheBetrayal.EditorTools.OpeningBook.OpeningBookBuilder.CompileDefaultBookHeadless
        /// Exits non-zero on a bad book so an automated caller fails loudly instead of carrying on
        /// with a stale asset.
        /// </summary>
        public static void CompileDefaultBookHeadless()
        {
            try
            {
                int positions = CompileDefaultBook();
                Debug.Log($"OPENING BOOK REBUILT: {positions} position(s) from '{DefaultSourcePath}'.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"OPENING BOOK COMPILE FAILED: {ex.Message}");
                EditorApplication.Exit(1);
            }
        }
    }
}
