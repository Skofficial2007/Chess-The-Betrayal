using UnityEngine;
using ChessTheBetrayal.Core.Diagnostics;

namespace ChessTheBetrayal.Events
{
    /// <summary>
    /// Shared Play-mode validation for the *EventListener family. Subclasses expose their
    /// serialized channel reference via <see cref="ChannelObject"/> so a missing Inspector
    /// wiring becomes a click-to-select console error instead of a silent no-op.
    /// </summary>
    public abstract class EventListenerBase : MonoBehaviour
    {
        protected abstract UnityEngine.Object ChannelObject { get; }

        protected virtual void Awake()
        {
            InspectorGuard.Require(ChannelObject, "_channel", this);
        }
    }
}
