using UnityEngine;

namespace ChessTheBetrayal.Infrastructure
{
    /// <summary>
    /// Composition root marker for the scene's three singleton-shaped services (GameManager,
    /// UIManager, BoardVisuals). Those MonoBehaviours register themselves into
    /// <see cref="ServiceLocator"/> from their own Awake() — Unity gives no ordering guarantee
    /// between sibling Awake() calls, and every consumer in this project resolves services from
    /// Start(), Update(), or an event callback, all of which run strictly after every Awake() in
    /// the scene, so no explicit registration order is required here.
    ///
    /// This class exists to own the registry's lifecycle (clearing stale entries on scene unload,
    /// so a reload can't resolve a destroyed MonoBehaviour) and to be the one file a reader opens
    /// to see "these three are the app's composition root" — add [DefaultExecutionOrder] here,
    /// not on the services themselves, if a future service genuinely needs to run first.
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        private void OnDestroy()
        {
            ServiceLocator.Instance.Clear();
        }
    }
}
