using System;
using System.Collections.Generic;
using System.IO;
using TaleWorlds.Engine;

namespace PrefabSwapperTool.Core
{
    // Ported from MaterialSwapTool.Core.ScreenshotManager - one screenshot per batch, named after
    // the batch's own ID so History can find it by the same key it already uses for everything
    // else. Utilities.TakeScreenshot(path) is the engine's own screenshot capture (same one backing
    // MaterialSwapTool's Batch History thumbnails). Only called from RunSwap/RunSetSwap - deliberate,
    // occasional actions, not anything that could fire in a tight loop.
    //
    // System.IO.Path fully qualified throughout - "using TaleWorlds.Engine;" brings in
    // TaleWorlds.Engine.Path (the scene spline type), which collides with System.IO.Path by name.
    public static class ScreenshotManager
    {
        private static string ScreenshotDir => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "PrefabSwapperTool", "Screenshots");

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

        // Called by PrefabSwapLogger right after it prunes the log itself, so a screenshot never
        // outlives the batch it belongs to - keepBatchIds is whatever batches are still in the
        // MaxHistoryDepth-deep window.
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
