using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using ChessTheBetrayal.Events.Channels;

namespace ChessTheBetrayal.EditorTools.Events
{
    /// <summary>
    /// Finds every event channel asset in the project.
    /// </summary>
    /// <remarks>
    /// Separate from the window that displays them so the search can be checked without opening
    /// anything. That matters here more than it usually would: the monitor spent a long time
    /// listing only the channels that carry no payload, because it searched for the concrete
    /// payload-free type rather than the base both shapes share, and a dashboard that quietly
    /// omits two thirds of its subject looks exactly like one that is working.
    /// </remarks>
    public static class EventChannelCatalog
    {
        /// <summary>Every channel asset in the project, ordered by name.</summary>
        public static List<EventChannelBase> FindAll() =>
            AssetDatabase.FindAssets($"t:{nameof(EventChannelBase)}")
                .Select(guid => AssetDatabase.LoadAssetAtPath<EventChannelBase>(
                    AssetDatabase.GUIDToAssetPath(guid)))
                .Where(channel => channel != null)
                .OrderBy(channel => channel.name)
                .ToList();
    }
}
