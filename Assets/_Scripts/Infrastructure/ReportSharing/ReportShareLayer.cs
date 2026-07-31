namespace ChessTheBetrayal.Infrastructure
{
    /// <summary>Which way a finished report actually reached the tester.</summary>
    internal enum ReportShareLayer
    {
        /// <summary>Written into the public Downloads folder via MediaStore and attached to a
        /// share-sheet intent — the tester can find it later even if they dismiss the sheet.</summary>
        MediaStoreDownloads,

        /// <summary>Handed to a share-sheet intent as plain text. No file is attached, but it needs
        /// no storage permission and works on the older API levels MediaStore.Downloads doesn't
        /// cover.</summary>
        TextIntent,

        /// <summary>Nothing platform-specific was attempted. The report is still on the clipboard
        /// and written to the app's own storage — this is what every platform falls back to.</summary>
        ClipboardOnly
    }

    /// <summary>
    /// Decides which share layer to attempt first, from nothing but the platform and API level —
    /// no Unity API, no JNI, so this is the one piece of the export path that can be checked without
    /// a device. Runtime failure (MediaStore refusing the write, a share intent throwing) still
    /// forces a fallback the selector can't predict; it only picks where to start.
    /// </summary>
    internal static class ReportShareLayerSelector
    {
        /// <summary>MediaStore.Downloads was introduced in Android 10; asking for it on an older
        /// device throws rather than failing gracefully, so the API level has to be checked first.</summary>
        internal const int MinMediaStoreDownloadsApiLevel = 29;

        public static ReportShareLayer Choose(bool isAndroidRuntime, int androidApiLevel)
        {
            if (!isAndroidRuntime) return ReportShareLayer.ClipboardOnly;

            return androidApiLevel >= MinMediaStoreDownloadsApiLevel
                ? ReportShareLayer.MediaStoreDownloads
                : ReportShareLayer.TextIntent;
        }
    }
}
