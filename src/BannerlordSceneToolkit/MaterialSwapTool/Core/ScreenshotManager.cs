using System;
using System.Collections.Generic;
using System.IO;
using TaleWorlds.Engine;

namespace MaterialSwapTool.Core
{
    // One screenshot per batch, named after the batch's own ID so Batch History can find it by
    // the same key it already uses for everything else - no separate lookup table needed.
    // TaleWorlds.Engine.Utilities.TakeScreenshot(path) is the engine's own screenshot capture
    // (verified via decompile - it exists as a direct wrapper over the native screenshot pipeline,
    // same one a manual in-game screenshot key would use). Only called from deliberate, occasional
    // actions (a real Apply, Revert to Normal) - NOT from Continuous Recolor, which can fire many
    // times in quick succession while armed, and a screenshot involves a GPU readback that could
    // reintroduce exactly the kind of per-action stutter this session already spent effort
    // eliminating elsewhere.
    public static class ScreenshotManager
    {
        private static string ScreenshotDir => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "MaterialSwapTool", "Screenshots");

        public static void CaptureForBatch(string batchId)
        {
            if (string.IsNullOrWhiteSpace(batchId)) return;
            try
            {
                Directory.CreateDirectory(ScreenshotDir);
                var path = System.IO.Path.Combine(ScreenshotDir, batchId + ".png");
                Utilities.TakeScreenshot(path);
            }
            catch (Exception ex)
            {
                Log.Warn($"ScreenshotManager: capture failed for batch '{batchId}': {ex.Message}");
            }
        }

        public static string GetPathForBatch(string batchId)
        {
            if (string.IsNullOrWhiteSpace(batchId)) return null;
            var path = System.IO.Path.Combine(ScreenshotDir, batchId + ".png");
            return File.Exists(path) ? path : null;
        }

        // Called by ChangeLogger right after it prunes the log itself, so a screenshot never
        // outlives the batch it belongs to - keepBatchIds is whatever batches are still in the
        // 30-deep window.
        public static void PruneExcept(IEnumerable<string> keepBatchIds)
        {
            try
            {
                if (!Directory.Exists(ScreenshotDir)) return;
                var keep = new HashSet<string>(keepBatchIds, StringComparer.Ordinal);
                foreach (var file in Directory.GetFiles(ScreenshotDir, "*.png"))
                {
                    var batchId = System.IO.Path.GetFileNameWithoutExtension(file);
                    if (keep.Contains(batchId)) continue;
                    try { File.Delete(file); }
                    catch (Exception ex) { Log.Warn($"ScreenshotManager: failed to prune '{file}': {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("ScreenshotManager: prune failed: " + ex.Message);
            }
        }
    }
}
