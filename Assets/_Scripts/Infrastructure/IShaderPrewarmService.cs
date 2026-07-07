namespace ChessTheBetrayal.Infrastructure
{
    /// <summary>
    /// Warms the GPU shader-compilation cache for materials that would otherwise show a visible
    /// "default/unlit" flash the first time they're actually drawn — Unity compiles a shader
    /// variant lazily on its first real draw call, and that compile can take long enough to show
    /// through for a frame or more. Kept as its own interface (SRP: this service does exactly one
    /// thing — force those variants to compile early) so Bootstrap can depend on the abstraction
    /// rather than a concrete Unity API, and so a future headless/test context can swap in a no-op
    /// implementation the same way NullPieceAnimator stands in for PrimeTweenPieceAnimator.
    /// </summary>
    public interface IShaderPrewarmService
    {
        /// <summary>
        /// Forces every tracked material's currently-active shader variant to compile immediately,
        /// blocking the calling frame. Intended to run once, before the first real scene render.
        /// </summary>
        void Prewarm();
    }
}
