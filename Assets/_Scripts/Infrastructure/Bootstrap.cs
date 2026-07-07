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
    ///
    /// Also owns the one true prewarm-at-startup step (<see cref="IShaderPrewarmService"/>): unlike
    /// the three services above, this needs to run exactly once per app session (not once per
    /// StartNewGame/Replay), and it must finish before BoardVisuals ever draws a real piece — so it
    /// runs here, synchronously, in Awake(), ahead of any GameStarted event.
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        [Header("Shader Prewarm")]
        [Tooltip("Materials to force-compile at startup so their first real draw doesn't show a " +
                 "visible shader-compile flash (e.g. the piece material's default/unlit look).")]
        [SerializeField] private Material[] materialsToPrewarm;

        private void Awake()
        {
            IShaderPrewarmService prewarmService = new ShaderVariantPrewarmService(materialsToPrewarm);
            ServiceLocator.Instance.Register(prewarmService);
            prewarmService.Prewarm();
        }

        private void OnDestroy()
        {
            ServiceLocator.Instance.Clear();
        }
    }
}
