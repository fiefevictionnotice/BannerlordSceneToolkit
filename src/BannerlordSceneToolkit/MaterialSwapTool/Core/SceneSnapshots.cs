using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MaterialSwapTool.Backup;

namespace MaterialSwapTool.Core
{
    // The scene.xscene files Scene Diff can compare for the open scene: the saved file in the
    // module's SceneObj folder, and every backup BackupManager has written for that scene
    // (<backup root>\<scene name>\<yyyyMMdd_HHmmss>_<reason>\scene.xscene).
    //
    // Also owns the Reports folder, shared by every "Write Report" button on the F7 fly-outs.
    public static class SceneSnapshots
    {
        public sealed class Snapshot
        {
            public string Path;
            public bool IsCurrent;        // the saved file itself, not a backup
            public DateTime Taken;        // when the backup was taken (folder stamp); for the saved file, its write time
            public DateTime SavedAt;      // write time of the xscene inside - i.e. which SAVE this is a copy of
            public long Bytes;
            public string Reason;         // "auto-4min", "before-apply", "manual"...
            public bool SameAsCurrent;    // byte size and save time equal the saved file's
            public string Label;          // one line for the list
            public string ShortLabel;     // for headers and report lines
        }

        // Backups are capped at 50 per scene by default, and the list is one row widget set per
        // entry - so the cap here is a guard against a hand-raised retention, not a normal limit.
        public const int MaxListed = 60;

        // Newest first, the saved file on top. olderNotListed says how many backups fell off
        // the end of the cap. Must be called on the main thread: it asks the engine for the
        // scene name.
        public static List<Snapshot> List(out string sceneName, out string problem, out int olderNotListed)
        {
            var result = new List<Snapshot>();
            problem = null;
            olderNotListed = 0;
            sceneName = null;

            if (!EntitySelector.HasOpenScene) { problem = "No scene is currently open."; return result; }
            try { sceneName = EntitySelector.CurrentScene.GetName(); } catch { }
            if (string.IsNullOrEmpty(sceneName)) { problem = "The open scene has no name yet - save it first."; return result; }

            Snapshot current = null;
            try
            {
                var dir = BackupManager.TryFindSceneDir(sceneName);
                var path = dir == null ? null : System.IO.Path.Combine(dir, "scene.xscene");
                if (path != null && File.Exists(path))
                {
                    var info = new FileInfo(path);
                    current = new Snapshot
                    {
                        Path = path,
                        IsCurrent = true,
                        Taken = info.LastWriteTime,
                        SavedAt = info.LastWriteTime,
                        Bytes = info.Length,
                        Reason = "saved file",
                    };
                    current.ShortLabel = "saved file (" + Stamp(info.LastWriteTime) + ")";
                    current.Label = "SAVED FILE   last saved " + Stamp(info.LastWriteTime) + "   " + BackupManager.FormatBytes(info.Length);
                    result.Add(current);
                }
            }
            catch (Exception ex) { Log.Warn("[SceneDiff] locating the saved scene file failed: " + ex.Message); }

            try
            {
                // BOTH backup locations (2026-11-12). BackupRootPath follows the F9 "Location"
                // setting, so new backups are always found. But changing that setting does not
                // move the backups already written, and those were silently missing from this
                // list. The default folder is therefore searched as well whenever a custom
                // location is in use.
                var roots = new List<string> { BackupManager.BackupRootPath };
                var defaultRoot = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Mount and Blade II Bannerlord", "MaterialSwapTool", "Backups");
                if (!roots.Any(r => string.Equals(System.IO.Path.GetFullPath(r).TrimEnd('\\'),
                                                  System.IO.Path.GetFullPath(defaultRoot).TrimEnd('\\'),
                                                  StringComparison.OrdinalIgnoreCase)))
                    roots.Add(defaultRoot);

                var sceneFolder = sceneName;   // out parameters cannot be captured by a lambda
                var sceneRoots = roots.Select(r => System.IO.Path.Combine(r, sceneFolder)).Where(Directory.Exists).ToList();
                if (sceneRoots.Count > 0)
                {
                    // Folder names start with a sortable stamp, so name order IS time order -
                    // the same assumption BackupManager's unchanged-skip check makes.
                    var dirs = sceneRoots.SelectMany(r => Directory.GetDirectories(r))
                        .OrderByDescending(d => System.IO.Path.GetFileName(d), StringComparer.Ordinal)
                        .ToList();

                    int listed = 0;
                    foreach (var d in dirs)
                    {
                        var path = System.IO.Path.Combine(d, "scene.xscene");
                        if (!File.Exists(path)) continue;
                        if (listed >= MaxListed) { olderNotListed++; continue; }

                        var info = new FileInfo(path);
                        var folder = System.IO.Path.GetFileName(d);
                        DateTime taken;
                        string reason = "";
                        if (folder.Length >= 15 && DateTime.TryParseExact(folder.Substring(0, 15), "yyyyMMdd_HHmmss",
                                CultureInfo.InvariantCulture, DateTimeStyles.None, out taken))
                            reason = folder.Length > 16 ? folder.Substring(16) : "";
                        else
                        {
                            taken = Directory.GetCreationTime(d);
                            reason = folder;
                        }

                        var snap = new Snapshot
                        {
                            Path = path,
                            Taken = taken,
                            SavedAt = info.LastWriteTime,
                            Bytes = info.Length,
                            Reason = reason,
                            // File.Copy keeps the source's write time, so "same size, same
                            // write time" means this backup is a copy of the save that is on
                            // disk right now. Comparing those two finds nothing by definition.
                            SameAsCurrent = current != null && info.Length == current.Bytes
                                            && info.LastWriteTimeUtc == new FileInfo(current.Path).LastWriteTimeUtc,
                        };
                        snap.ShortLabel = "backup " + Stamp(taken) + (reason.Length > 0 ? " " + reason : "");
                        snap.Label = "Backup " + Stamp(taken) + "   " + (reason.Length > 0 ? reason : "?")
                                     + "   (save of " + info.LastWriteTime.ToString("HH:mm", CultureInfo.InvariantCulture) + ", "
                                     + BackupManager.FormatBytes(info.Length) + ")"
                                     + (snap.SameAsCurrent ? "   = identical to the saved file" : "");
                        result.Add(snap);
                        listed++;
                    }
                }
            }
            catch (Exception ex) { Log.Warn("[SceneDiff] listing backups failed: " + ex.Message); }

            if (result.Count == 0)
                problem = "No saved scene.xscene and no backups were found for '" + sceneName + "'.";
            return result;
        }

        private static string Stamp(DateTime t) => t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        // Next to tool.log, in its own folder so reports do not pile up beside the settings files.
        public static string ReportsDir => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "MaterialSwapTool", "Reports");

        // Writes a plain-text report and returns its full path. kind is the file-name prefix
        // ("scene-diff", "scene-stats"). Throws on I/O failure - callers show the message.
        public static string WriteReport(string kind, string sceneName, IEnumerable<string> lines)
        {
            Directory.CreateDirectory(ReportsDir);
            var safeScene = string.IsNullOrEmpty(sceneName) ? "scene" : sceneName;
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) safeScene = safeScene.Replace(c, '_');
            var path = System.IO.Path.Combine(ReportsDir,
                kind + "_" + safeScene + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt");
            File.WriteAllLines(path, lines);
            Log.Info("[Report] wrote " + path);
            return path;
        }
    }
}
