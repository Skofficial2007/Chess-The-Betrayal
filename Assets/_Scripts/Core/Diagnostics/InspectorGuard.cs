using UnityEngine;

namespace ChessTheBetrayal.Core.Diagnostics
{
    /// <summary>
    /// Turns a missing [SerializeField] Inspector reference into a click-to-select console error
    /// instead of a silent no-op. Call from Awake()/OnValidate() on any MonoBehaviour with
    /// required Object references.
    /// </summary>
    public static class InspectorGuard
    {
        public static bool Require(UnityEngine.Object field, string fieldName, UnityEngine.Object context)
        {
            if (field != null) return true;
            Debug.LogError($"[{context.GetType().Name}] Required field '{fieldName}' is unassigned on '{context.name}'.", context);
            return false;
        }
    }
}
