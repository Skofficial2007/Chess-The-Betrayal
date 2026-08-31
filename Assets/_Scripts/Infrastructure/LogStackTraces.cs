using UnityEngine;

namespace ChessTheBetrayal.Infrastructure
{
    /// <summary>
    /// Drops the script stack trace from ordinary log lines in a build, so a log somebody sends back
    /// is mostly the thing they were trying to show us.
    ///
    /// Unity attaches a stack trace to every log type by default, and that setting is a project one:
    /// it applies to a release build exactly as it does to a development build, which is easy to
    /// assume otherwise and wrong. Measured on a capture off a real phone, twenty minutes of ordinary
    /// play wrote fifty-four lines of our own and five hundred and thirty-eight lines of file, because
    /// each of those lines carried about eleven frames of stack under it. A move log, a phase change
    /// and a turn change say everything they have to say on one line; the eleven under them name the
    /// same handful of methods every time and push the interesting part off the top of whatever the
    /// tester pasted.
    ///
    /// Only the plain Log level loses its trace. A warning, an error or an exception is exactly where
    /// the frames earn their space, and those keep them.
    ///
    /// Not applied in the editor, where the trace is what makes a console line clickable and nobody
    /// is pasting the console into a bug report. Runs off <see cref="RuntimeInitializeOnLoadMethod"/>
    /// for the same reason <see cref="DisplayFrameRate"/> does: a rule that only holds in scenes
    /// somebody remembered to wire it into is a rule that will be missing from one of them.
    /// </summary>
    public static class LogStackTraces
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Apply()
        {
#if !UNITY_EDITOR
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
#endif
        }
    }
}
