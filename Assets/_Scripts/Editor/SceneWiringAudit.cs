#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

namespace ChessTheBetrayal.Events.Editor
{
    /// <summary>
    /// One-shot static audit of the open scene's Inspector wiring. Reports every unassigned
    /// [SerializeField] Object reference, every GameEventChannel asset with zero scene
    /// consumers, and every *EventListener-shaped component whose UnityEvent has zero
    /// persistent targets — all in a single console report instead of hunting object-by-object
    /// after moving something in the Editor.
    ///
    /// Run via: Chess: The Betrayal > Audit Scene Wiring
    /// </summary>
    public static class SceneWiringAudit
    {
        [MenuItem("Chess: The Betrayal/Audit Scene Wiring")]
        public static void RunAudit()
        {
            var report = BuildReport();
            PrintReport(report);
        }

        private sealed class Report
        {
            public readonly List<(string path, string field, GameObject go)> NullFields = new List<(string, string, GameObject)>();
            public readonly List<(string channelName, UnityEngine.Object asset)> OrphanedChannels = new List<(string, UnityEngine.Object)>();
            public readonly List<(string path, GameObject go, string reason)> NoOpListeners = new List<(string, GameObject, string)>();
        }

        private static Report BuildReport()
        {
            var report = new Report();
            var allBehaviours = CollectSceneBehaviours();

            FindNullSerializedFields(allBehaviours, report);
            FindOrphanedChannels(allBehaviours, report);
            FindNoOpEventListeners(allBehaviours, report);

            return report;
        }

        /// <summary>
        /// Only project-owned scripts are audited — anything outside this namespace (TextMeshPro,
        /// Unity UI, Cinemachine, PrimeTween, etc.) has its own unrelated set of optional/lazy fields
        /// that are null by design and would otherwise drown the report in false positives.
        /// </summary>
        private const string ProjectNamespacePrefix = "ChessTheBetrayal";

        private static List<MonoBehaviour> CollectSceneBehaviours()
        {
            var result = new List<MonoBehaviour>();
            int sceneCount = EditorSceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    result.AddRange(root
                        .GetComponentsInChildren<MonoBehaviour>(includeInactive: true)
                        .Where(mb => mb != null && IsProjectOwned(mb.GetType())));
                }
            }
            return result;
        }

        private static bool IsProjectOwned(Type type) =>
            type.Namespace != null && type.Namespace.StartsWith(ProjectNamespacePrefix, StringComparison.Ordinal);

        /// <summary>
        /// Reflects over every MonoBehaviour's [SerializeField] fields of a UnityEngine.Object
        /// type and reports which ones are null — the exact condition InspectorGuard catches at
        /// runtime, surfaced here without needing to press Play.
        /// </summary>
        private static void FindNullSerializedFields(List<MonoBehaviour> behaviours, Report report)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var mb in behaviours)
            {
                if (mb == null) continue;
                Type type = mb.GetType();

                foreach (FieldInfo field in type.GetFields(flags))
                {
                    if (!IsSerializedObjectField(field)) continue;

                    object value = field.GetValue(mb);
                    bool isNull = value == null || value is UnityEngine.Object uo && uo == null;
                    if (!isNull) continue;

                    // Arrays/lists of Object are audited for emptiness, not per-element nullness —
                    // BoardVisuals' prefab arrays are the motivating case (Phase 1 flags them as a unit).
                    report.NullFields.Add((GetPath(mb.transform), field.Name, mb.gameObject));
                }

                foreach (FieldInfo field in type.GetFields(flags))
                {
                    if (!IsSerializedObjectArrayField(field)) continue;

                    var array = field.GetValue(mb) as Array;
                    if (array != null && array.Length > 0) continue;

                    report.NullFields.Add((GetPath(mb.transform), field.Name + " (empty array)", mb.gameObject));
                }
            }
        }

        private static bool IsSerializedObjectField(FieldInfo field)
        {
            if (!IsSerialized(field)) return false;
            return typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType);
        }

        private static bool IsSerializedObjectArrayField(FieldInfo field)
        {
            if (!IsSerialized(field)) return false;
            if (!field.FieldType.IsArray) return false;
            return typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType.GetElementType());
        }

        private static bool IsSerialized(FieldInfo field)
        {
            if (field.IsStatic) return false;
            bool isPublic = field.IsPublic;
            bool hasSerializeField = field.GetCustomAttribute<SerializeField>() != null;
            bool hasNonSerialized = field.GetCustomAttribute<NonSerializedAttribute>() != null;
            return (isPublic || hasSerializeField) && !hasNonSerialized;
        }

        /// <summary>
        /// Cross-references every GameEventChannel/GameEventChannel&lt;T&gt; asset in the project
        /// against the scene's serialized fields. A channel referenced by zero scene MonoBehaviours
        /// is either dead or referenced only from a prefab/other scene — worth a second look either way.
        /// </summary>
        private static void FindOrphanedChannels(List<MonoBehaviour> behaviours, Report report)
        {
            var referencedChannels = new HashSet<UnityEngine.Object>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var mb in behaviours)
            {
                if (mb == null) continue;
                foreach (FieldInfo field in mb.GetType().GetFields(flags))
                {
                    if (!IsSerializedObjectField(field)) continue;
                    if (!typeof(ScriptableObject).IsAssignableFrom(field.FieldType)) continue;

                    if (field.GetValue(mb) is UnityEngine.Object so && so != null)
                    {
                        referencedChannels.Add(so);
                    }
                }
            }

            var allChannelAssets = AssetDatabase.FindAssets("t:ScriptableObject")
                .Select(guid => AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(asset => asset != null && IsEventChannelType(asset.GetType()))
                .ToList();

            foreach (var channel in allChannelAssets)
            {
                if (!referencedChannels.Contains(channel))
                {
                    report.OrphanedChannels.Add((channel.name, channel));
                }
            }
        }

        private static bool IsEventChannelType(Type type)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                if (t == typeof(GameEventChannel)) return true;
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(GameEventChannel<>)) return true;
            }
            return false;
        }

        /// <summary>
        /// Flags *EventListener components whose channel is assigned but whose UnityEvent has no
        /// persistent (Inspector-wired) targets — a likely no-op: the channel will fire and nothing
        /// will happen, with no error anywhere to point at why.
        /// </summary>
        private static void FindNoOpEventListeners(List<MonoBehaviour> behaviours, Report report)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var mb in behaviours)
            {
                if (mb == null) continue;
                Type type = mb.GetType();
                if (!type.Name.EndsWith("EventListener", StringComparison.Ordinal)) continue;

                UnityEventBase unityEvent = null;
                UnityEngine.Object channel = null;

                foreach (FieldInfo field in type.GetFields(flags))
                {
                    if (!IsSerialized(field)) continue;

                    if (unityEvent == null && typeof(UnityEventBase).IsAssignableFrom(field.FieldType))
                    {
                        unityEvent = field.GetValue(mb) as UnityEventBase;
                    }
                    else if (channel == null && typeof(ScriptableObject).IsAssignableFrom(field.FieldType))
                    {
                        channel = field.GetValue(mb) as UnityEngine.Object;
                    }
                }

                if (channel == null || unityEvent == null) continue; // already reported as a null field

                string reason = DiagnoseNoOp(unityEvent);
                if (reason != null)
                {
                    report.NoOpListeners.Add((GetPath(mb.transform), mb.gameObject, reason));
                }
            }
        }

        /// <summary>
        /// GetPersistentEventCount() counts Inspector list *rows*, not configured calls — clicking
        /// "+" in the Inspector adds a row with a null target and "No Function" before you've wired
        /// anything, and that row still counts toward it. Walks every row and returns a human-readable
        /// reason describing exactly what's missing (empty slot / no target / no method), or null if
        /// at least one row is fully wired — that specificity is the point: it should read out as the
        /// fix, not just "something's wrong here."
        /// </summary>
        private static string DiagnoseNoOp(UnityEventBase unityEvent)
        {
            int count = unityEvent.GetPersistentEventCount();
            if (count == 0) return "no listener slots added (On Event Raised list is empty)";

            var rowIssues = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                UnityEngine.Object target = unityEvent.GetPersistentTarget(i);
                string methodName = unityEvent.GetPersistentMethodName(i);
                bool hasTarget = target != null;
                bool hasMethod = !string.IsNullOrEmpty(methodName);

                if (hasTarget && hasMethod)
                {
                    return null; // at least one row is fully wired — not a no-op
                }

                if (!hasTarget && !hasMethod)
                {
                    rowIssues.Add($"slot {i}: no target object and no function assigned");
                }
                else if (!hasTarget)
                {
                    rowIssues.Add($"slot {i}: function '{methodName}' assigned but target object is None");
                }
                else
                {
                    rowIssues.Add($"slot {i}: target '{target.name}' assigned but function is 'No Function'");
                }
            }

            return string.Join("; ", rowIssues);
        }

        private static string GetPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        private static void PrintReport(Report report)
        {
            int totalIssues = report.NullFields.Count + report.OrphanedChannels.Count + report.NoOpListeners.Count;

            if (totalIssues == 0)
            {
                UnityEngine.Debug.Log("[SceneWiringAudit] Clean — no unassigned references, orphaned channels, or no-op listeners found.");
                return;
            }

            UnityEngine.Debug.LogWarning($"[SceneWiringAudit] {totalIssues} issue(s) found. See entries below.");

            foreach (var (path, field, go) in report.NullFields)
            {
                UnityEngine.Debug.LogError($"[SceneWiringAudit] Unassigned field '{field}' on '{path}'.", go);
            }

            foreach (var (channelName, asset) in report.OrphanedChannels)
            {
                UnityEngine.Debug.LogWarning($"[SceneWiringAudit] Channel '{channelName}' has zero scene consumers (unreferenced by any open-scene MonoBehaviour).", asset);
            }

            foreach (var (path, go, reason) in report.NoOpListeners)
            {
                UnityEngine.Debug.LogWarning($"[SceneWiringAudit] '{path}' is a no-op listener — {reason}.", go);
            }
        }
    }
}
#endif
