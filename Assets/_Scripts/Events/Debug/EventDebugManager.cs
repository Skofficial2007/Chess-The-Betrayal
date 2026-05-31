using UnityEngine;

namespace ChessTheBetrayal.Events.Debug
{
    /// <summary>
    /// Global debug configuration and registry for the Event Bus.
    /// Used in conjunction with the Event Monitor Editor Window.
    /// </summary>
    [CreateAssetMenu(menuName = "Chess/Events/Debug/Event Debug Manager", fileName = "EventDebugManager")]
    public sealed class EventDebugManager : ScriptableObject
    {
        [Tooltip("Force enable debug tracing on all Event Channels in the project.")]
        public bool ForceGlobalTrace = false;
    }
}
