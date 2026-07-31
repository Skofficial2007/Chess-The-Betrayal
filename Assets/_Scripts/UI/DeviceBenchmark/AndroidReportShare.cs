#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Text;
using UnityEngine;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// The Android-only half of exporting a report: everything that talks to the platform through
    /// JNI. Kept out of BenchmarkReportExporter so that class stays readable as "write the file,
    /// then hand it off" instead of a few hundred lines of AndroidJavaObject calls. Only compiled
    /// into an Android device build, so none of this can be exercised from an Editor test — the
    /// selection logic in ReportShareLayerSelector is what stays testable.
    /// </summary>
    internal static class AndroidReportShare
    {
        internal static int ApiLevel()
        {
            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            return version.GetStatic<int>("SDK_INT");
        }

        /// <summary>
        /// Writes the report into the public Downloads folder via MediaStore and raises a share
        /// sheet with it attached. Returns false on any failure (the write, or the share) so the
        /// caller can fall back to the text-only intent instead of leaving the tester with nothing.
        /// </summary>
        internal static bool TryShareViaDownloads(string fileName, string contents)
        {
            try
            {
                using AndroidJavaObject uri = WriteToDownloads(fileName, contents);
                if (uri == null) return false;

                using var intentClass = new AndroidJavaClass("android.content.Intent");
                using var intent = new AndroidJavaObject("android.content.Intent",
                    intentClass.GetStatic<string>("ACTION_SEND"));

                CallAndRelease(intent, "setType", "text/plain");
                CallAndRelease(intent, "putExtra", intentClass.GetStatic<string>("EXTRA_STREAM"), uri);
                CallAndRelease(intent, "addFlags", intentClass.GetStatic<int>("FLAG_GRANT_READ_URI_PERMISSION"));

                using var chooser = intentClass.CallStatic<AndroidJavaObject>(
                    "createChooser", intent, "Send benchmark report");
                using var activity = CurrentActivity();
                activity.Call("startActivity", chooser);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BenchmarkReportExporter] Could not share via Downloads " +
                    $"({e.Message}); falling back to a text share.");
                return false;
            }
        }

        /// <summary>
        /// Hands the report to whatever the tester already uses to send things — a chat app, mail,
        /// a drive — as plain text. No file is attached, so it needs no storage permission and no
        /// manifest entry, at the cost of the tester having to paste rather than open a file.
        /// </summary>
        internal static bool TryShareAsText(string fileName, string contents)
        {
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
                using var activity = CurrentActivity();
                activity.Call("startActivity", chooser);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BenchmarkReportExporter] Could not offer the report for sharing " +
                    $"({e.Message}).");
                return false;
            }
        }

        /// <summary>
        /// Inserts a new entry into MediaStore.Downloads and writes the report into the stream it
        /// hands back. The column names are passed as their raw strings rather than looked up
        /// through MediaStore.MediaColumns/Downloads — they're part of Android's public SDK
        /// contract and have not changed since MediaStore.Downloads was introduced, so a lookup
        /// would only add JNI round trips for no extra safety.
        /// </summary>
        private static AndroidJavaObject WriteToDownloads(string fileName, string contents)
        {
            using var activity = CurrentActivity();
            using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");

            using var values = new AndroidJavaObject("android.content.ContentValues");
            values.Call("put", "_display_name", fileName);
            values.Call("put", "mime_type", "text/plain");
            values.Call("put", "relative_path", "Download");

            using var downloadsClass = new AndroidJavaClass("android.provider.MediaStore$Downloads");
            using var collection = downloadsClass.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");

            AndroidJavaObject uri = resolver.Call<AndroidJavaObject>("insert", collection, values);
            if (uri == null) return null;

            // Unity's JNI bridge marshals a Java byte[] as a C# sbyte[]; BlockCopy reinterprets the
            // same bytes rather than converting values, since byte and sbyte share a bit layout.
            byte[] utf8 = Encoding.UTF8.GetBytes(contents);
            var javaBytes = new sbyte[utf8.Length];
            Buffer.BlockCopy(utf8, 0, javaBytes, 0, utf8.Length);

            using (AndroidJavaObject stream = resolver.Call<AndroidJavaObject>("openOutputStream", uri))
            {
                stream.Call("write", javaBytes);
                stream.Call("flush");
                stream.Call("close");
            }

            return uri;
        }

        private static AndroidJavaObject CurrentActivity()
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            return player.GetStatic<AndroidJavaObject>("currentActivity");
        }

        // These calls hand back the same intent again as a fresh wrapper each time; releasing them
        // keeps a chain of builder-style calls from leaving a pile of live references behind.
        private static void CallAndRelease(AndroidJavaObject target, string method, params object[] args)
        {
            using (AndroidJavaObject result = target.Call<AndroidJavaObject>(method, args)) { }
        }
    }
}
#endif
