using System;
using System.Collections.Generic;
using System.Linq;
using PrefabSwapperTool.Backup;
using TaleWorlds.Engine;

namespace PrefabSwapperTool.Core
{
    // Undo/Redo execution + the session-only live-reference cache for Prefab Swapper, pulled out
    // of PrefabSwapperVM so both the main panel's one-click "Undo Last Swap" and the separate
    // History flyout (GUI/PrefabHistoryVM.cs) can trigger the same logic without one VM owning
    // state the other needs to reach into.
    public static class PrefabSwapHistory
    {
        // BatchId -> live results, this session only. Cleared naturally as the game runs; the
        // persisted log (PrefabSwapLogger) is what survives a restart. Redo never uses this cache
        // (the original NewEntity reference is already gone/replaced by the time an Undo has run),
        // so it's only ever written by a fresh swap and read/removed by Undo.
        private static readonly Dictionary<string, List<LivePrefabSwapper.SwapResult>> _sessionBatches = new Dictionary<string, List<LivePrefabSwapper.SwapResult>>();

        // How close a live entity's position has to be to a logged one to count as "the same
        // entity" when matching by name+position instead of a live reference.
        private const double PositionMatchTolerance = 0.05;

        public static void RecordSessionBatch(string batchId, List<LivePrefabSwapper.SwapResult> results) =>
            _sessionBatches[batchId] = results;

        public class BatchActionResult
        {
            public bool Success;
            public string Message;
        }

        // Refuses to act on a batch that belongs to a different scene.
        //
        // The list being scoped is not enough on its own: Undo's fallback path re-finds entities
        // by NAME AND POSITION across the whole open scene, so a batch from another scene can
        // resolve onto a same-named entity sitting at the same coordinates here and swap the
        // wrong thing. That is a silent corruption, not an error, which is why this is a hard
        // guard at the point of action rather than only a filter on the list.
        //
        // A batch with no recorded scene (older log lines) is allowed through - refusing would
        // strand history that predates the field, and those entries carry no contradiction.
        private static BatchActionResult CheckBatchBelongsToOpenScene(string batchId)
        {
            string batchScene;
            try { batchScene = PrefabSwapLogger.GetBatchSceneName(batchId); }
            catch (Exception ex)
            {
                Log.Warn("Scene guard could not read the batch: " + ex.Message);
                return null;   // can't prove a mismatch; don't block on a read failure
            }

            if (string.IsNullOrEmpty(batchScene)) return null;

            var openScene = EntitySelector.CurrentSceneName;
            if (string.Equals(batchScene, openScene, StringComparison.OrdinalIgnoreCase)) return null;

            Log.Warn($"[SceneGuard] refused batch {batchId} from '{batchScene}' while '{openScene}' is open.");
            return new BatchActionResult
            {
                Success = false,
                Message = $"That batch belongs to scene '{batchScene}', but '{openScene}' is open. " +
                          "Open that scene to undo or redo it."
            };
        }

        public static BatchActionResult Undo(Scene scene, string batchId)
        {
            if (scene == null) return new BatchActionResult { Success = false, Message = "No scene is currently open." };

            var guard = CheckBatchBelongsToOpenScene(batchId);
            if (guard != null) return guard;

            // UNDO ALWAYS REPLACES, AT THE FRAME AS IT STANDS (2026-08-23, "when I tried to
            // undo the last swap in the F5 tool, that didn't work either"): the restore runs
            // through SwapEntity, which honours the PANEL's global toggles - with ADD mode on,
            // "undo" quietly placed the restored prefab NEXT TO the thing it should remove;
            // with 1x Scale on, restoring a scaled original stripped its scale; the Z-rot/scale
            // extras would distort restores the same way. All forced neutral for the duration.
            var prevAdd = LivePrefabSwapper.AddModeKeepOriginals;
            var prevScale = LivePrefabSwapper.InheritSourceScale;
            var prevZRot = LivePrefabSwapper.SwapZRotationDegrees;
            var prevMult = LivePrefabSwapper.SwapScaleMultiplier;
            LivePrefabSwapper.AddModeKeepOriginals = false;
            LivePrefabSwapper.InheritSourceScale = true;
            LivePrefabSwapper.SwapZRotationDegrees = 0f;
            LivePrefabSwapper.SwapScaleMultiplier = 1f;
            try
            {
                BackupManager.BackupNow("before-apply");
                int restored = 0, failed = 0;

                if (_sessionBatches.TryGetValue(batchId, out var sessionResults))
                {
                    // Fast, precise path: we still have live GameEntity references from this
                    // session's swap, no matching needed.
                    foreach (var swap in sessionResults)
                    {
                        if (swap.NewEntity == null || !EntitySelector.IsValidEntity(swap.NewEntity)) { failed++; continue; }
                        var back = LivePrefabSwapper.SwapEntity(scene, swap.NewEntity, swap.OldPrefabName);
                        if (back.Success)
                        {
                            restored++;

                            // Restore at the PRE-swap frame, not the swapped entity's current one
                            // (2026-08-23, "undo doesn't undo the Z or Scale"): the current frame
                            // carries whatever Z-rotation / scale multiplier the forward swap
                            // applied, so swapping back in place kept them baked in. A zero basis
                            // means the result predates OriginalFrame - leave those alone.
                            var of = swap.OriginalFrame;
                            bool haveFrame = Math.Abs(of.rotation.s.x) + Math.Abs(of.rotation.s.y) + Math.Abs(of.rotation.s.z) > 0.0001f;
                            if (haveFrame && back.NewEntity != null)
                            {
                                try
                                {
                                    back.NewEntity.SetGlobalFrame(ref of, true);
                                    BannerlordSceneToolkit.EditorFrameSync.Sync(back.NewEntity);
                                }
                                catch (Exception frameEx) { Log.Warn($"Undo: could not restore original frame for '{swap.OldPrefabName}': {frameEx.Message}"); }
                            }
                        }
                        else failed++;
                    }
                    _sessionBatches.Remove(batchId);
                }
                else
                {
                    var logEntries = PrefabSwapLogger.ReadBatch(batchId).Where(e => !e.Undone).ToList();
                    if (logEntries.Count == 0) return new BatchActionResult { Success = false, Message = "Nothing to undo in that batch." };

                    var all = EntitySelector.GetTargets(SelectionMode.WholeScene);
                    foreach (var entry in logEntries)
                    {
                        var candidate = all.FirstOrDefault(e => NameMatchesPrefab(e.Name, entry.NewPrefabName) && IsNearPosition(e, entry));
                        if (candidate == null) { failed++; continue; }

                        var back = LivePrefabSwapper.SwapEntity(scene, candidate, entry.OldPrefabName);
                        if (back.Success) { restored++; all.Remove(candidate); } else failed++;
                    }
                }

                PrefabSwapLogger.MarkBatchUndone(batchId);

                return new BatchActionResult
                {
                    Success = true,
                    Message = failed == 0
                        ? $"Undid batch: {restored} entit{(restored == 1 ? "y" : "ies")} restored to their previous prefab."
                        : $"Undo: {restored} restored, {failed} failed (entity may have been deleted/edited since, or moved too far to match).",
                };
            }
            catch (Exception ex)
            {
                Log.Error("PrefabSwapHistory.Undo failed: " + ex);
                return new BatchActionResult { Success = false, Message = "Undo failed: " + ex.Message };
            }
            finally
            {
                LivePrefabSwapper.AddModeKeepOriginals = prevAdd;
                LivePrefabSwapper.InheritSourceScale = prevScale;
                LivePrefabSwapper.SwapZRotationDegrees = prevZRot;
                LivePrefabSwapper.SwapScaleMultiplier = prevMult;
            }
        }

        // Re-applies an already-undone batch (OldPrefabName -> NewPrefabName again). Always matches
        // by name+position - the fast session-reference path only ever applies to Undo, since by
        // the time something's eligible for Redo, the live references from the original swap are
        // long gone (replaced by whatever the Undo put there).
        public static BatchActionResult Redo(Scene scene, string batchId)
        {
            if (scene == null) return new BatchActionResult { Success = false, Message = "No scene is currently open." };

            var guard = CheckBatchBelongsToOpenScene(batchId);
            if (guard != null) return guard;

            // Same neutral-toggles bracket as Undo, same reason.
            var prevAdd = LivePrefabSwapper.AddModeKeepOriginals;
            var prevScale = LivePrefabSwapper.InheritSourceScale;
            var prevZRot = LivePrefabSwapper.SwapZRotationDegrees;
            var prevMult = LivePrefabSwapper.SwapScaleMultiplier;
            LivePrefabSwapper.AddModeKeepOriginals = false;
            LivePrefabSwapper.InheritSourceScale = true;
            LivePrefabSwapper.SwapZRotationDegrees = 0f;
            LivePrefabSwapper.SwapScaleMultiplier = 1f;
            try
            {
                var logEntries = PrefabSwapLogger.ReadBatch(batchId).Where(e => e.Undone).ToList();
                if (logEntries.Count == 0) return new BatchActionResult { Success = false, Message = "Nothing to redo in that batch." };

                BackupManager.BackupNow("before-apply");
                int reapplied = 0, failed = 0;
                var all = EntitySelector.GetTargets(SelectionMode.WholeScene);

                foreach (var entry in logEntries)
                {
                    var candidate = all.FirstOrDefault(e => NameMatchesPrefab(e.Name, entry.OldPrefabName) && IsNearPosition(e, entry));
                    if (candidate == null) { failed++; continue; }

                    var forward = LivePrefabSwapper.SwapEntity(scene, candidate, entry.NewPrefabName);
                    if (forward.Success) { reapplied++; all.Remove(candidate); } else failed++;
                }

                PrefabSwapLogger.MarkBatchRedone(batchId);

                return new BatchActionResult
                {
                    Success = true,
                    Message = failed == 0
                        ? $"Redid batch: {reapplied} entit{(reapplied == 1 ? "y" : "ies")} re-swapped."
                        : $"Redo: {reapplied} re-applied, {failed} failed (entity may have been deleted/edited since, or moved too far to match).",
                };
            }
            catch (Exception ex)
            {
                Log.Error("PrefabSwapHistory.Redo failed: " + ex);
                return new BatchActionResult { Success = false, Message = "Redo failed: " + ex.Message };
            }
            finally
            {
                LivePrefabSwapper.AddModeKeepOriginals = prevAdd;
                LivePrefabSwapper.InheritSourceScale = prevScale;
                LivePrefabSwapper.SwapZRotationDegrees = prevZRot;
                LivePrefabSwapper.SwapScaleMultiplier = prevMult;
            }
        }

        // CONFIRMED BUG, fixed 2026-08-18: the name+position fallback (used whenever the fast
        // live-reference path isn't available - a different session, or the in-memory
        // _sessionBatches cache is gone for any other reason) used to require e.Name to EXACTLY
        // equal the logged prefab name. GameEntity.Instantiate auto-suffixes a name to keep it
        // unique within the scene ("fief_empire_villa_v2", then "fief_empire_villa_v2_02", etc.) -
        // on a batch swap where many entities became the SAME new prefab (e.g. a Whole Scene
        // swap), only the one unsuffixed instance would ever match; every other one silently
        // failed to match ANY candidate and was left untouched - "mostly it didn't delete the new
        // thing I placed down." A prefix match (plus the existing tight position tolerance to
        // avoid false positives against an unrelated, similarly-named prefab) tolerates the
        // suffix.
        private static bool NameMatchesPrefab(string entityName, string prefabName)
        {
            if (string.IsNullOrEmpty(entityName) || string.IsNullOrEmpty(prefabName)) return false;
            return entityName.StartsWith(prefabName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNearPosition(GameEntity entity, PrefabSwapLogEntry entry)
        {
            var pos = entity.GetGlobalFrame().origin;
            double dx = pos.x - entry.PosX, dy = pos.y - entry.PosY, dz = pos.z - entry.PosZ;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz) <= PositionMatchTolerance;
        }
    }
}
