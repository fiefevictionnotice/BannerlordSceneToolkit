using System;
using System.IO;

namespace BannerlordSceneToolkit
{
    // The one place log lines actually get written. Each tool keeps its own Log class (same
    // namespace, same call sites, same tool.log path as before the v0.7 consolidation) - they all
    // just delegate here now instead of carrying four copies of the append logic.
    //
    // ROTATION, added v0.7: tool.log files grew without bound (MaterialSwapTool's had reached
    // 5+ MB in days - the [NativeTrace] lines add up fast on big Apply runs). At 10 MB the file
    // is renamed to tool.log.old (replacing any previous .old) and a fresh file starts, so the
    // on-disk ceiling per tool is ~20 MB and recent history is always intact across the roll.
    // The size check reads cached FileInfo state once per write; that is cheap enough not to
    // matter next to the append itself.
    internal static class LogFile
    {
        private const long MaxBytes = 10 * 1024 * 1024;

        private static readonly object Lock = new object();

        public static void Write(string path, string level, string message)
        {
            try
            {
                lock (Lock)
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    RotateIfNeeded(path);

                    File.AppendAllText(path,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // A logging failure must never take down the editor.
            }
        }

        private static void RotateIfNeeded(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length < MaxBytes) return;

                var old = path + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(path, old);
            }
            catch
            {
                // If the roll fails (file locked by a tail, whatever), keep appending to the
                // oversized file rather than dropping the line - size is a preference, the log
                // line is the point.
            }
        }
    }
}
