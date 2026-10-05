namespace PrefabSwapperTool
{
    // Every crash report this session (Qt5Core.dll, 0xc0000005) has died with zero managed frames
    // on any thread - the fault is entirely inside native code, so no amount of ordinary
    // "we did X" logging can show what was happening AT the crash. What it CAN do is narrow down
    // WHICH native call was in flight: bracket a native call with Enter/Exit, and if the process
    // dies mid-call, the log ends on an unmatched Enter instead of a vague "last action was X" -
    // exactly what tool.log has been missing every time so far. Log.cs already flushes every write
    // (File.AppendAllText, no buffering), so nothing here is lost when the process dies instantly.
    //
    // Deliberately only wrapped around one-off, user-triggered scene-mutating calls (Instantiate,
    // RemoveEntity, SetGlobalFrame, AddChild, SetMaterial) - never around per-tick or per-frame
    // code, where the extra file I/O this adds (one open+write+close per Enter and per Exit) would
    // be a real cost instead of a rounding error.
    internal static class NativeTrace
    {
        public static void Around(string label, System.Action call)
        {
            Log.Info($"[NativeTrace] ENTER {label}");
            call();
            Log.Info($"[NativeTrace] EXIT {label}");
        }

        public static T Around<T>(string label, System.Func<T> call)
        {
            Log.Info($"[NativeTrace] ENTER {label}");
            var result = call();
            Log.Info($"[NativeTrace] EXIT {label}");
            return result;
        }
    }
}
