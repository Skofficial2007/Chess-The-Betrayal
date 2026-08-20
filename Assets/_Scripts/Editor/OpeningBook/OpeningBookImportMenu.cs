using System.IO;
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
        [MenuItem("Chess: The Betrayal/AI/Compile Opening Book...")]
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

            int positions;
            try
            {
                positions = OpeningBookBuilder.CompileInto(File.ReadAllText(sourcePath), outputPath);
            }
            catch (OpeningBookParseException ex)
            {
                EditorUtility.DisplayDialog("Opening book compile failed", ex.Message, "OK");
                return;
            }

            Debug.Log($"Compiled opening book: {positions} position(s) from '{sourcePath}' -> '{outputPath}'.");
        }
    }
}
