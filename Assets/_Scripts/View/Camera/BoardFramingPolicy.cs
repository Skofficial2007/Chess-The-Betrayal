namespace ChessTheBetrayal.View.Camera
{
    /// <summary>
    /// How wide the lens has to be for the whole board to fit on screen.
    ///
    /// A phone screen is far taller relative to its width than a desktop window, so the lens that
    /// frames the board comfortably on a monitor cuts the near and far ranks off on a handheld.
    /// Widening it puts the board back inside the frame without moving a camera or rescaling
    /// anything on the board itself.
    ///
    /// The rule lives here, with no engine types in it, so it can be checked without a device or a
    /// scene. That matters more than usual: the case this exists for is the one a desktop editor
    /// cannot show you, because the editor is not a handheld and never reports itself as one.
    /// </summary>
    internal static class BoardFramingPolicy
    {
        /// <summary>
        /// What a handheld needs to see the whole board, in millimetres. Chosen on a phone against
        /// the real board rather than derived, because how much empty margin looks right around the
        /// edge is a judgement about the picture and not something an aspect ratio can settle.
        /// </summary>
        internal const float HandheldFocalLengthMm = 17.5f;

        /// <summary>
        /// The focal length the board cameras should use on this device.
        ///
        /// Anything that is not a handheld keeps exactly what the scene was authored with, so a
        /// value tuned in the editor is never quietly replaced by this — which is the whole reason
        /// the authored value is a parameter rather than something this class decides for itself.
        /// </summary>
        internal static float FocalLengthMmFor(bool isHandheld, float authoredFocalLengthMm)
            => isHandheld ? HandheldFocalLengthMm : authoredFocalLengthMm;
    }
}
