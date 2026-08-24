namespace ChessTheBetrayal.View
{
    /// <summary>
    /// Knocks the camera when something on the board hits hard enough to be felt.
    ///
    /// A seam rather than a direct call because the board has no business knowing what a camera is,
    /// let alone which of several it is looking through. What it knows is that a piece just landed
    /// on another one and roughly how heavy that was; turning that into a shake is the camera's job.
    ///
    /// Resolved optionally — a board with no camera registered simply does not shake, which is what
    /// headless play and any scene that never wired one up should do rather than fail.
    /// </summary>
    public interface ICameraShake
    {
        /// <summary>
        /// Shakes for a moment and settles back exactly where it started. strength is 0 for the
        /// lightest knock worth feeling and 1 for the hardest, so callers describe the event rather
        /// than the amplitude and the camera stays the only thing deciding how far it moves.
        /// </summary>
        void Shake(float strength);
    }
}
