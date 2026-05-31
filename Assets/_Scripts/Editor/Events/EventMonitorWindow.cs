#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace ChessTheBetrayal.Events.Editor
{
    /// <summary>
    /// A live dashboard showing every GameEventChannel asset in the project.
    /// Displays listener counts, trace toggles, and Ping buttons.
    /// Open via: Chess: The Betrayal > Event Monitor
    /// </summary>
    public sealed class EventMonitorWindow : EditorWindow
    {
        private Vector2 _scroll;
        private List<GameEventChannel> _channels;
        private double _lastRefresh;

        [MenuItem("Chess: The Betrayal/Event Monitor")]
        public static void Open() => GetWindow<EventMonitorWindow>("Event Monitor");

        private void OnEnable() => RefreshChannelList();

        private void OnGUI()
        {
            if (EditorApplication.isPlaying &&
                EditorApplication.timeSinceStartup - _lastRefresh > 0.5)
            {
                _lastRefresh = EditorApplication.timeSinceStartup;
                Repaint();
            }

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton)) RefreshChannelList();
            if (GUILayout.Button("Clear All Logs", EditorStyles.toolbarButton))
                _channels?.ForEach(c => c.ClearDebugLog());
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_channels == null || _channels.Count == 0)
            {
                EditorGUILayout.HelpBox("No GameEventChannel assets found.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            foreach (var ch in _channels)
            {
                if (ch == null) continue;
                EditorGUILayout.BeginVertical(GUI.skin.box);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(ch.name, EditorStyles.boldLabel);
                
                int count = EditorApplication.isPlaying ? ch.ListenerCount : 0;
                EditorGUILayout.LabelField(
                    EditorApplication.isPlaying ? $"{count} listener(s)" : "(enter Play Mode)",
                    GUILayout.Width(130));
                
                bool traced = EditorGUILayout.Toggle("Trace", ch.DebugTrace, GUILayout.Width(70));
                if (traced != ch.DebugTrace)
                {
                    ch.DebugTrace = traced;
                    EditorUtility.SetDirty(ch);
                }
                
                if (GUILayout.Button("Ping", GUILayout.Width(44)))
                    EditorGUIUtility.PingObject(ch);
                
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }

        private void RefreshChannelList()
        {
            _channels = AssetDatabase
                .FindAssets("t:GameEventChannel")
                .Select(guid => AssetDatabase.LoadAssetAtPath<GameEventChannel>(
                    AssetDatabase.GUIDToAssetPath(guid)))
                .Where(c => c != null)
                .OrderBy(c => c.name)
                .ToList();
        }
    }
}
#endif
