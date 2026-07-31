using System;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("ChessTheBetrayal.Tests.EditMode")]

namespace ChessTheBetrayal.Infrastructure
{
    /// <summary>What came of trying to get a report to the tester, in terms a person can be shown.</summary>
    public readonly struct ReportExportResult
    {
        public ReportExportResult(bool saved, string path, string message)
        {
            Saved = saved;
            Path = path;
            Message = message;
        }

        public bool Saved { get; }

        public string Path { get; }

        /// <summary>Ready to put in front of whoever pressed the button — which layer got the
        /// report to them, or what went wrong.</summary>
        public string Message { get; }
    }

    /// <summary>
    /// Gets a finished text report to the tester through whichever of three layers the platform
    /// and API level allow, falling back rather than stopping at the first failure so a device
    /// that refuses one layer still leaves the tester with something. Used by both the device
    /// benchmark screen and in-match AI telemetry — platform I/O like this belongs here rather
    /// than inside either feature's own UI folder.
    ///
    /// The file write and the clipboard copy happen unconditionally, on every platform, before any
    /// platform-specific sharing is attempted — the same write the Editor performs when this class
    /// is exercised there, so an Editor run genuinely proves that half of the path. Everything
    /// Android-specific lives in AndroidReportShare, which only compiles into a device build.
    /// </summary>
    public static class ReportExporter
    {
        public static ReportExportResult Save(string fileName, string contents)
        {
            GUIUtility.systemCopyBuffer = contents;

            string path = Path.Combine(Application.persistentDataPath, fileName);
            try
            {
                File.WriteAllText(path, contents);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ReportExporter] Could not write {path}: {e}");
                return new ReportExportResult(false, path, $"Could not save the report: {e.Message}");
            }

            Debug.Log($"[ReportExporter] Saved report to {path}");
            return new ReportExportResult(true, path, BuildMessage(fileName, contents, path));
        }

        private static string BuildMessage(string fileName, string contents, string path)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ReportShareLayer layer =
                ReportShareLayerSelector.Choose(isAndroidRuntime: true, AndroidReportShare.ApiLevel());

            if (layer == ReportShareLayer.MediaStoreDownloads)
            {
                if (AndroidReportShare.TryShareViaDownloads(fileName, contents))
                    return $"Shared via your Downloads folder. Report copied to clipboard and saved to: {path}";

                layer = ReportShareLayer.TextIntent;
            }

            if (layer == ReportShareLayer.TextIntent && AndroidReportShare.TryShareAsText(fileName, contents))
                return $"Shared as text. Report copied to clipboard and saved to: {path}";
#endif

            return $"Report copied to clipboard and saved to: {path}";
        }
    }
}
