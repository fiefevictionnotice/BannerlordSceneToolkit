using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MaterialSwapTool.Core
{
    // Snapshots a personal preset's PREVIOUS contents right before an overwrite-save replaces it -
    // same shape as Batch History (a list of timestamped versions you can inspect and restore),
    // but per-preset instead of per-batch. Stored under Documents\...\MaterialSwapTool\Presets\
    // History\{SafeFileName}\{timestamp}.json, alongside (not inside) the live Presets folder, so
    // browsing presets never has to filter history files out.
    public static class PresetHistoryManager
    {
        // Presets get overwritten far less often than batches get applied, so this doesn't need
        // 30 - but unbounded growth was flagged as the main risk when this was designed, so it
        // still needs SOME cap. 20 per preset comfortably covers "I tweaked this a bunch this
        // session" without ever needing pruning logic beyond a flat count.
        private const int MaxVersionsPerPreset = 20;

        private static string HistoryRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "MaterialSwapTool", "Presets", "History");

        private static string DirFor(string presetName) => Path.Combine(HistoryRoot, SafeFileName(presetName));

        // Call BEFORE writing the new version - archives whatever's currently on disk under that
        // name, if anything is. A brand-new preset (nothing to archive yet) is a no-op.
        public static void ArchiveIfExists(string presetName, string currentFilePath)
        {
            if (!File.Exists(currentFilePath)) return;

            try
            {
                var dir = DirFor(presetName);
                Directory.CreateDirectory(dir);
                var destPath = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd_HHmmss}.json");
                File.Copy(currentFilePath, destPath, overwrite: true);
                Prune(dir);
            }
            catch (Exception ex)
            {
                Log.Warn($"PresetHistoryManager: failed to archive previous version of '{presetName}': {ex.Message}");
            }
        }

        private static void Prune(string dir)
        {
            var files = Directory.GetFiles(dir, "*.json")
                .OrderByDescending(f => f) // timestamp-named, so lexicographic == chronological
                .ToList();
            foreach (var stale in files.Skip(MaxVersionsPerPreset))
            {
                try { File.Delete(stale); }
                catch (Exception ex) { Log.Warn($"PresetHistoryManager: failed to prune '{stale}': {ex.Message}"); }
            }
        }

        public class VersionInfo
        {
            public string TimestampLabel; // filename without extension, e.g. "20260816_172830"
            public DateTime TimestampUtc;
            public string FilePath;
        }

        public static List<VersionInfo> ListVersions(string presetName)
        {
            var dir = DirFor(presetName);
            if (!Directory.Exists(dir)) return new List<VersionInfo>();

            return Directory.GetFiles(dir, "*.json")
                .Select(f => new VersionInfo
                {
                    TimestampLabel = Path.GetFileNameWithoutExtension(f),
                    TimestampUtc = File.GetLastWriteTimeUtc(f),
                    FilePath = f,
                })
                .OrderByDescending(v => v.TimestampLabel)
                .ToList();
        }

        public static MaterialSwapPreset LoadVersion(string filePath) =>
            JsonConvert.DeserializeObject<MaterialSwapPreset>(File.ReadAllText(filePath));

        // Writes the old version straight to the current file - deliberately WITHOUT archiving
        // whatever was current first (archiveOldVersion: false). Restoring used to go through the
        // normal Save() path, which meant every single restore minted a fresh history entry -
        // clicking through old versions to compare them flooded the list with near-duplicates.
        // The version you're moving away from is still sitting right there in history if it's
        // already an archived entry; the only real loss case is restoring away from an UNSAVED
        // edit that was never itself archived, which is the same risk any "load a preset" action
        // already carries.
        public static void RestoreVersion(string presetName, string filePath)
        {
            var preset = LoadVersion(filePath);
            if (preset == null) throw new InvalidOperationException($"Could not load history file '{filePath}'.");
            preset.Name = presetName;
            preset.Save(archiveOldVersion: false);
        }

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }
    }
}
