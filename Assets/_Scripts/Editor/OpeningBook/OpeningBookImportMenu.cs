using System.IO;
using ChessTheBetrayal.AI.OpeningBook;
using UnityEditor;
using UnityEngine;

namespace ChessTheBetrayal.EditorTools.OpeningBook
{
    /// <summary>
    /// Dev-only menu command that compiles a source .book.txt file into an OpeningBookAsset.
    /// Never reachable outside the editor — there is no player-facing way to author or recompile
    /// a book; it ships as a finished asset alongside the rest of the AI's built-in data.
    /// </summary>
    public static class OpeningBookImportMenu
    {
        [MenuItem("Chess/AI/Compile Opening Book...")]
        private static void CompileFromMenu()
        {
            string sourcePath = EditorUtility.OpenFilePanel(
                "Select opening book source file", Application.dataPath, "txt");
            if (string.IsNullOrEmpty(sourcePath))
                return;

            string outputPath = EditorUtility.SaveFilePanelInProject(
                "Save compiled opening book",
                "OpeningBook",
                "asset",
                "Choose where to save the compiled opening book asset.");
            if (string.IsNullOrEmpty(outputPath))
                return;

            string sourceText = File.ReadAllText(sourcePath);

            (ulong[] keys, uint[] packedMoves, ushort[] weights, ulong schemeVersion) compiled;
            try
            {
                compiled = OpeningBookCompiler.Compile(sourceText);
            }
            catch (OpeningBookParseException ex)
            {
                EditorUtility.DisplayDialog("Opening book compile failed", ex.Message, "OK");
                return;
            }

            OpeningBookAsset asset = AssetDatabase.LoadAssetAtPath<OpeningBookAsset>(outputPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<OpeningBookAsset>();
                AssetDatabase.CreateAsset(asset, outputPath);
            }

            asset.SetEntries(compiled.keys, compiled.packedMoves, compiled.weights, compiled.schemeVersion);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            Debug.Log($"Compiled opening book: {compiled.keys.Length} position(s) from '{sourcePath}' -> '{outputPath}'.");
        }
    }
}
