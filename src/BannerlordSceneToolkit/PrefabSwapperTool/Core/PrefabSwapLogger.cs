using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace PrefabSwapperTool.Core
{
    public class PrefabSwapBatchSummary
    {
        public string BatchId { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string SceneName { get; set; }
        public int EntryCount { get; set; }
        public bool AllUndone { get; set; }
        public string Label { get; set; } // e.g. "module_wall_plank_a -> module_wall_plank_a_fixed_fief x3"
    }

    // Persisted, append-only JSONL log for Prefab Swapper (F6) specifically - same file-format
    // convention as ChangeLogger (one JSON object per line, so a crash mid-batch can't corrupt
    // earlier entries) but a separate file and separate 30-batch retention window, since a prefab
    // swap's undo needs different data (old/new PREFAB NAME, not old/new material) and a different
    // matching strategy (see PrefabSwapperVM.RunUndoBatch - by entity reference when still resolvable
    // this session, falling back to name+position matching otherwise, since the swapped entity is a
    // brand new GameEntity instance, not something a stored reference can outlive a restart on).
    public static class PrefabSwapLogger
    {
        public const int MaxHistoryDepth = 30;

        public static string LogDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "PrefabSwapperTool", "Logs");

        public static string CurrentLogPath => Path.Combine(LogDir, "prefab_swaps.jsonl");

        public static string NewBatchId() => Guid.NewGuid().ToString("N").Substring(0, 12);

        public static void Append(IEnumerable<PrefabSwapLogEntry> entries)
        {
            Directory.CreateDirectory(LogDir);
            using (var writer = new StreamWriter(CurrentLogPath, append: true))
            {
                foreach (var entry in entries)
                    writer.WriteLine(JsonConvert.SerializeObject(entry));
            }
            PruneOldBatches();
        }

        // Rewrites every entry for the given batch with Undone=true, so it's not offered for undo
        // again and the recent-swaps list can show it as already reverted.
        public static void MarkBatchUndone(string batchId)
        {
            var all = ReadAll();
            bool changed = false;
            foreach (var e in all.Where(e => e.BatchId == batchId && !e.Undone))
            {
                e.Undone = true;
                changed = true;
            }
            if (!changed) return;

            using (var writer = new StreamWriter(CurrentLogPath, append: false))
            {
                foreach (var entry in all)
                    writer.WriteLine(JsonConvert.SerializeObject(entry));
            }
        }

        // Inverse of MarkBatchUndone - rewrites every entry for the batch back to Undone=false, so
        // Redo can put it back in the normal "undo me" pool afterward.
        public static void MarkBatchRedone(string batchId)
        {
            var all = ReadAll();
            bool changed = false;
            foreach (var e in all.Where(e => e.BatchId == batchId && e.Undone))
            {
                e.Undone = false;
                changed = true;
            }
            if (!changed) return;

            using (var writer = new StreamWriter(CurrentLogPath, append: false))
            {
                foreach (var entry in all)
                    writer.WriteLine(JsonConvert.SerializeObject(entry));
            }
        }

        private static void PruneOldBatches()
        {
            var all = ReadAll();
            if (all.Count == 0) return;

            var keepBatchIds = all
                .GroupBy(e => e.BatchId)
                .Select(g => new { BatchId = g.Key, TimestampUtc = g.Min(e => e.TimestampUtc) })
                .OrderByDescending(b => b.TimestampUtc)
                .Take(MaxHistoryDepth)
                .Select(b => b.BatchId)
                .ToHashSet(StringComparer.Ordinal);

            var kept = all.Where(e => keepBatchIds.Contains(e.BatchId)).ToList();
            if (kept.Count < all.Count)
            {
                using (var writer = new StreamWriter(CurrentLogPath, append: false))
                {
                    foreach (var entry in kept)
                        writer.WriteLine(JsonConvert.SerializeObject(entry));
                }
            }

            // Screenshots are keyed by batch ID too - pruning them here, right after the log's own
            // prune, means a screenshot never outlives the batch it belongs to.
            ScreenshotManager.PruneExcept(keepBatchIds);
        }

        public static List<PrefabSwapLogEntry> ReadAll()
        {
            if (!File.Exists(CurrentLogPath)) return new List<PrefabSwapLogEntry>();

            var result = new List<PrefabSwapLogEntry>();
            foreach (var line in File.ReadLines(CurrentLogPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonConvert.DeserializeObject<PrefabSwapLogEntry>(line);
                    if (entry != null) result.Add(entry);
                }
                catch (JsonException ex)
                {
                    Log.Warn($"Skipping unparseable prefab swap log line: {ex.Message}");
                }
            }
            return result;
        }

        public static List<PrefabSwapLogEntry> ReadBatch(string batchId) =>
            ReadAll().Where(e => e.BatchId == batchId).ToList();

        // Most-recent-first, undone batches included (shown as such) so the list is a real history,
        // not just a pending-action queue.
        //
        // sceneName scopes the result to batches logged against that scene. The log is a single
        // shared file across every scene you have ever swapped in, so an unscoped list offers you
        // Undo buttons for batches that have nothing to do with what is open - and Undo re-finds
        // entities by name and position, which can land on a same-named entity in the wrong scene.
        // Null/empty means "every scene" and is now an explicit opt-in rather than the default.
        // Which scene a batch belongs to, for the guard in PrefabSwapHistory.Undo/Redo.
        public static string GetBatchSceneName(string batchId)
        {
            if (string.IsNullOrEmpty(batchId)) return null;
            foreach (var entry in ReadAll())
                if (entry.BatchId == batchId) return entry.SceneName;
            return null;
        }

        public static List<PrefabSwapBatchSummary> ListRecentBatches(int maxBatches = MaxHistoryDepth, string sceneName = null)
        {
            var all = ReadAll();
            IEnumerable<PrefabSwapLogEntry> scoped = all;
            if (!string.IsNullOrEmpty(sceneName))
                scoped = all.Where(e => string.Equals(e.SceneName, sceneName, StringComparison.OrdinalIgnoreCase));

            return scoped
                .GroupBy(e => e.BatchId)
                .Select(g =>
                {
                    var first = g.First();
                    var distinctPairs = g.Select(e => $"{e.OldPrefabName} -> {e.NewPrefabName}").Distinct().ToList();
                    var label = distinctPairs.Count == 1
                        ? $"{distinctPairs[0]} x{g.Count()}"
                        : $"{distinctPairs.Count} prefab pair(s), {g.Count()} entit(y/ies)";
                    return new PrefabSwapBatchSummary
                    {
                        BatchId = g.Key,
                        TimestampUtc = g.Min(e => e.TimestampUtc),
                        SceneName = first.SceneName,
                        EntryCount = g.Count(),
                        AllUndone = g.All(e => e.Undone),
                        Label = label,
                    };
                })
                .OrderByDescending(b => b.TimestampUtc)
                .Take(maxBatches)
                .ToList();
        }
    }
}
