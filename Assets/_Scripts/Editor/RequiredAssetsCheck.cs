#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ChessTheBetrayal.EditorTools
{
    /// <summary>
    /// One Asset Store package the project needs but does not ship.
    /// </summary>
    internal readonly struct RequiredAsset
    {
        /// <summary>The asset id of one file inside the package, used to tell whether it is here.
        /// Ids live inside the package itself and stay the same wherever it is imported, so this
        /// works no matter which folder the user drops it in.</summary>
        public readonly string ProbeGuid;

        public readonly string DisplayName;
        public readonly string UsedFor;
        public readonly string StoreUrl;

        public RequiredAsset(string probeGuid, string displayName, string usedFor, string storeUrl)
        {
            ProbeGuid = probeGuid;
            DisplayName = displayName;
            UsedFor = usedFor;
            StoreUrl = storeUrl;
        }

        public bool IsPresent => !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(ProbeGuid));
    }

    /// <summary>
    /// Tells whoever just cloned the repository which free Asset Store packages they still need.
    ///
    /// The art and the board models are free but cannot be redistributed, so they are not in the
    /// repository. Without them the game opens to an empty board and no sky, which looks like a
    /// broken checkout rather than a missing download — so this says plainly what is absent and
    /// links to each store page.
    /// </summary>
    [InitializeOnLoad]
    internal static class RequiredAssetsCheck
    {
        /// <summary>Probe ids are real files the scenes and prefabs already point at, so if one
        /// resolves the package it belongs to is genuinely imported and wired up.</summary>
        internal static readonly RequiredAsset[] All =
        {
            new RequiredAsset(
                "9e4e635002b6a524ca896adb28e8e388",
                "Low Poly Chess Pack",
                "the board and all twelve piece models",
                "https://assetstore.unity.com/packages/3d/props/low-poly-chess-pack-50405"),
            new RequiredAsset(
                "7416536dbe0334cfd888dd1beb75a9fb",
                "AllSky Free",
                "the skybox behind the board",
                "https://assetstore.unity.com/packages/2d/textures-materials/sky/allsky-free-10-sky-skybox-set-146014"),
            new RequiredAsset(
                "1829b6d899893fc43b3e043a36f98e4f",
                "Chess Mega Set (Free)",
                "the dark wood material on the table",
                "https://assetstore.unity.com/packages/3d/props/chess-mega-set-free-version-287294"),
        };

        // Asking once per editor session is the point where a reminder stays useful instead of
        // becoming something you learn to dismiss without reading. A recompile is not a new session.
        private const string AlreadyAskedKey = "ChessTheBetrayal.RequiredAssetsCheck.Asked";

        static RequiredAssetsCheck()
        {
            // The asset database is not ready while static constructors run, so the probe has to
            // wait for the editor to finish coming up or every package looks missing.
            EditorApplication.delayCall += ShowIfAnythingMissing;
        }

        internal static List<RequiredAsset> Missing()
        {
            var missing = new List<RequiredAsset>();
            foreach (var asset in All)
            {
                if (!asset.IsPresent) missing.Add(asset);
            }

            return missing;
        }

        private static void ShowIfAnythingMissing()
        {
            if (SessionState.GetBool(AlreadyAskedKey, false)) return;
            if (Missing().Count == 0) return;

            SessionState.SetBool(AlreadyAskedKey, true);
            RequiredAssetsWindow.Open();
        }

        [MenuItem("Chess: The Betrayal/Check Required Assets")]
        private static void OpenFromMenu() => RequiredAssetsWindow.Open();
    }

    /// <summary>
    /// Lists whichever packages are still missing, with a way through to each store page.
    /// </summary>
    internal sealed class RequiredAssetsWindow : EditorWindow
    {
        internal static void Open()
        {
            var window = GetWindow<RequiredAssetsWindow>(utility: true, title: "Required Assets");
            window.minSize = new Vector2(460f, 260f);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            var missing = RequiredAssetsCheck.Missing();

            EditorGUILayout.Space(6f);
            if (missing.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Everything is here. The board, the pieces and the sky should all render.",
                    MessageType.Info);
                if (GUILayout.Button("Close")) Close();
                return;
            }

            EditorGUILayout.HelpBox(
                "These free Asset Store packages are missing. They are not included in the "
                + "repository because the Asset Store licence does not allow redistributing them.\n\n"
                + "Open each page, press \"Add to My Assets\", then import through "
                + "Window > Package Manager > My Assets. Every reference reconnects on import — "
                + "there is nothing to wire up by hand.",
                MessageType.Warning);

            EditorGUILayout.Space(4f);
            foreach (var asset in missing)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(asset.DisplayName, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(asset.UsedFor, EditorStyles.wordWrappedMiniLabel);
                    if (GUILayout.Button("Open Asset Store page")) Application.OpenURL(asset.StoreUrl);
                }
            }

            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                // Importing does not repaint this window on its own, so the button is how you find
                // out the list actually shrank.
                if (GUILayout.Button("Re-check")) Repaint();
                if (GUILayout.Button("Close")) Close();
            }

            EditorGUILayout.LabelField(
                "Reopen from Chess: The Betrayal > Check Required Assets.",
                EditorStyles.centeredGreyMiniLabel);
        }
    }
}
#endif
