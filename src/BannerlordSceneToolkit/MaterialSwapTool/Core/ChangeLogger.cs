using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MaterialSwapTool.Core
{
    public class BatchSummary
    {
        public string BatchId { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string SceneName { get; set; }
        public int EntryCount { get; set; }
    }

    // Append-only JSONL log: one JSON object per line, one line per changed material slot.
    // JSONL rather than a single JSON array so a crash mid-batch never corrupts prior entries,
    // and so "read the last batch" is a cheap tail-read rather than a full-file parse.
    public static class ChangeLogger
    {
        // How far back Undo/Redo and the batch history UI look. Solidify writes a fresh log file
        // and leaves this one untouched, so batches from before a Solidify naturally age out of
        // this window rather than needing separate cleanup logic.
        public const int MaxHistoryDepth = 30;

        public static string LogDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "MaterialSwapTool", "Logs");

        public static string CurrentLogPath => Path.Combine(LogDir, "changes.jsonl");

        public static void Append(IEnumerable<ChangeLogEntry> entries)
        {
            Directory.CreateDirectory(LogDir);
            using (var writer = new StreamWriter(CurrentLogPath, append: true))
            {
                foreach (var entry in entries)
                    writer.WriteLine(JsonConvert.SerializeObject(entry));
            }
            PruneOldBatches();
        }

        // MaxHistoryDepth used to only limit what ListRecentBatches DISPLAYS - the underlying
        // file was genuinely append-only forever, and nothing ever cleaned up the screenshots
        // ScreenshotManager saves per batch either. Both now actually get pruned to the same
        // 30-batch window every time a new batch is logged, matching what the "max 30" was always
        // supposed to mean.
        //
        // FIXED 2026-08-19: that window used to be GLOBAL across every scene, so 30 batches of
        // work in one scene silently deleted another scene's entire history AND its screenshots -
        // history you'd never touched, wiped by editing something unrelated. It's now 30 per
        // scene, which is what "the last 30 batches" is useful as. The file grows with the number
        // of scenes edited rather than staying globally capped; that's the intended trade - each
        // scene's depth is still bounded, and a JSONL line is tiny next to the PNG per batch.
        private static void PruneOldBatches()
        {
            var all = ReadAll();
            if (all.Count == 0) return;

            var keepBatchIds = all
                .GroupBy(e => e.BatchId)
                .Select(g => new
                {
                    BatchId = g.Key,
                    TimestampUtc = g.Min(e => e.TimestampUtc),
                    SceneName = g.First().SceneName ?? "",
                })
                .GroupBy(b => b.SceneName, StringComparer.OrdinalIgnoreCase)
                .SelectMany(sceneGroup => sceneGroup
                    .OrderByDescending(b => b.TimestampUtc)
                    .Take(MaxHistoryDepth))
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

            ScreenshotManager.PruneExcept(keepBatchIds);
        }

        public static List<ChangeLogEntry> ReadAll(string path = null)
        {
            path = path ?? CurrentLogPath;
            if (!File.Exists(path)) return new List<ChangeLogEntry>();

            var result = new List<ChangeLogEntry>();
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonConvert.DeserializeObject<ChangeLogEntry>(line);
                    if (entry != null) result.Add(entry);
                }
                catch (JsonException ex)
                {
                    Log.Warn($"Skipping unparseable log line: {ex.Message}");
                }
            }
            return result;
        }

        // Currently unused. Kept deliberately, but note it is NOT scene-scoped: the log is shared
        // by every scene, so "last batch in the file" may well belong to a scene that isn't open.
        // Anything that intends to act on the live scene wants
        // ListRecentBatches(1, EntitySelector.CurrentSceneName) instead - see RevertLastBatch.
        public static List<ChangeLogEntry> ReadLastBatch()
        {
            var all = ReadAll();
            if (all.Count == 0) return all;
            var lastBatchId = all[all.Count - 1].BatchId;
            return all.Where(e => e.BatchId == lastBatchId).ToList();
        }

        public static List<ChangeLogEntry> ReadBatch(string batchId)
        {
            if (string.IsNullOrEmpty(batchId)) return new List<ChangeLogEntry>();
            return ReadAll().Where(e => e.BatchId == batchId).ToList();
        }

        // Most-recent-first. Backs both the Undo/Redo history UI and RevertManager's
        // recency-based trust tolerance (see RevertManager.GetPositionTolerance).
        //
        // sceneName scopes the result to batches logged against that scene; null/empty means
        // "every scene", which is now an explicit opt-in rather than the default. Callers that
        // are about to TOUCH the live scene (RevertManager) must always pass one - the log is a
        // single shared file, and a batch from another scene resolving onto whatever happens to
        // be open is exactly the misapply this scoping exists to prevent.
        public static List<BatchSummary> ListRecentBatches(int maxBatches = MaxHistoryDepth, string sceneName = null)
        {
            var all = ReadAll();
            IEnumerable<ChangeLogEntry> scoped = all;
            if (!string.IsNullOrEmpty(sceneName))
                scoped = all.Where(e => string.Equals(e.SceneName, sceneName, StringComparison.OrdinalIgnoreCase));

            return scoped
                .GroupBy(e => e.BatchId)
                .Select(g => new BatchSummary
                {
                    BatchId = g.Key,
                    TimestampUtc = g.Min(e => e.TimestampUtc),
                    SceneName = g.First().SceneName,
                    EntryCount = g.Count(),
                })
                .OrderByDescending(b => b.TimestampUtc)
                .Take(maxBatches)
                .ToList();
        }

        // The scene a batch was logged against, or null if that batch isn't in the log at all.
        // Used to detect a cross-scene Undo/Redo before it gets anywhere near the live scene.
        public static string GetBatchSceneName(string batchId)
        {
            if (string.IsNullOrEmpty(batchId)) return null;
            foreach (var entry in ReadAll())
                if (string.Equals(entry.BatchId, batchId, StringComparison.Ordinal))
                    return entry.SceneName;
            return null;
        }

        public static string NewBatchId() => Guid.NewGuid().ToString("N").Substring(0, 12);
    }
}
