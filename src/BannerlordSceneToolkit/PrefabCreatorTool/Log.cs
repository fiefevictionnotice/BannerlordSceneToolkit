using System;
using System.IO;

namespace PrefabCreatorTool
{
    // Deliberately not TaleWorlds' own logging: we want a plain, always-on file we can tail
    // ourselves without relying on the game's log verbosity settings. Since v0.7 the actual
    // writing (and the 10 MB rotation) lives in BannerlordSceneToolkit.LogFile - this class is
    // just the tool's name, path, and call-site surface.
    internal static class Log
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "PrefabCreatorTool", "tool.log");

        public static void Info(string message) => BannerlordSceneToolkit.LogFile.Write(LogPath, "INFO", message);
        public static void Warn(string message) => BannerlordSceneToolkit.LogFile.Write(LogPath, "WARN", message);
        public static void Error(string message) => BannerlordSceneToolkit.LogFile.Write(LogPath, "ERROR", message);
    }
}
