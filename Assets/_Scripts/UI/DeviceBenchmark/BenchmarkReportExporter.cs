using System;
using System.IO;
using UnityEngine;

namespace ChessTheBetrayal.UI
{
    /// <summary>What came of trying to save a report, in terms a person can be shown.</summary>
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

        /// <summary>Ready to put in front of whoever pressed the button — the full path on success,
        /// or what went wrong.</summary>
        public string Message { get; }
    }

    /// <summary>
    /// Saves a finished benchmark report so a tester can send it back.
    ///
    /// The write itself is one ordinary file write to the app's own storage, identical on a desktop
    /// and on a phone. That is deliberate: it means running the benchmark in the editor genuinely
    /// exercises the same save a device performs, rather than proving a path only the editor takes.
    ///
    /// On Android the saved file then has a share sheet raised over it, because the app's own
    /// storage is somewhere a phone's file manager will not go — a tester who cannot reach the file
    /// cannot send it, which is the entire point of saving it. The share carries the report as text
    /// rather than as an attached file, so it needs no storage permission and no extra manifest
    /// entries. If it fails for any reason the file is already written and its path is already
    /// reported, so nothing is lost.
    ///
    /// TEMPORARY diagnostic support code — delete alongside the benchmark itself.
    /// </summary>
    public static class BenchmarkReportExporter
    {
        public static ReportExportResult Save(string fileName, string contents)
        {
            string path = System.IO.Path.Combine(Application.persistentDataPath, fileName);

            try
            {
                File.WriteAllText(path, contents);
            }
            catch (Exception e)
            {
                Debug.LogError($"[BenchmarkReportExporter] Could not write {path}: {e}");
                return new ReportExportResult(false, path, $"Could not save the report: {e.Message}");
            }

            Debug.Log($"[BenchmarkReportExporter] Saved report to {path}");
            TryOfferToShare(fileName, contents);

            return new ReportExportResult(true, path, $"Report saved to: {path}");
        }

        /// <summary>
        /// Hands the report to whatever the tester already uses to send things — a chat app, mail,
        /// a drive. Best effort by design: the file is on disk either way, so a device that refuses
        /// the intent leaves the tester no worse off than before.
        /// </summary>
        private static void TryOfferToShare(string fileName, string contents)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var intentClass = new AndroidJavaClass("android.content.Intent");
                using var intent = new AndroidJavaObject("android.content.Intent",
                    intentClass.GetStatic<string>("ACTION_SEND"));

                CallAndRelease(intent, "setType", "text/plain");
                CallAndRelease(intent, "putExtra", intentClass.GetStatic<string>("EXTRA_SUBJECT"), fileName);
                CallAndRelease(intent, "putExtra", intentClass.GetStatic<string>("EXTRA_TEXT"), contents);

                using var chooser = intentClass.CallStatic<AndroidJavaObject>(
                    "createChooser", intent, "Send benchmark report");
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                activity.Call("startActivity", chooser);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BenchmarkReportExporter] Could not offer the report for sharing " +
                    $"({e.Message}). The file is saved regardless.");
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // These calls hand back the same intent again as a fresh wrapper each time; releasing them
        // keeps a chain of builder-style calls from leaving a pile of live references behind.
        private static void CallAndRelease(AndroidJavaObject target, string method, params object[] args)
        {
            using (AndroidJavaObject result = target.Call<AndroidJavaObject>(method, args)) { }
        }
#endif
    }
}
