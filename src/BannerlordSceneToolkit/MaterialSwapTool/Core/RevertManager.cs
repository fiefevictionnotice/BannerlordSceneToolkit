using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using BannerlordSceneToolkit;

namespace MaterialSwapTool.Core
{
    public class RevertOutcome
    {
        public List<ChangeLogEntry> AutoReverted { get; } = new List<ChangeLogEntry>();
        public List<(ChangeLogEntry Entry, GameEntity Candidate)> NeedsConfirmation { get; } = new List<(ChangeLogEntry, GameEntity)>();
        public List<ChangeLogEntry> NotFound { get; } = new List<ChangeLogEntry>();
    }

    // Undo AND redo for any of the last N logged batches, not just the single most recent one.
    // Redo doesn't need its own stack/state - a batch's log entries already carry both Old* and
    // New* values, so "redo" is just "revert" run in the opposite direction against the same
    // entries. Entity resolution (UID tag + position cross-check) is identical either way.
    //
    // Precedence: a UID tag whose live position roughly matches the logged position is trusted
    // and applied immediately. A UID tag whose position does NOT match (the clone-inherits-tag
    // case) is demoted to a position-based candidate instead of trusted outright. No UID at all
    // also falls back to position+name proximity, always surfaced for confirmation rather than
    // applied silently - it's a best guess, not a certainty.
    public static class RevertManager
    {
        private const float PositionToleranceMeters = 0.5f;
        private const string UidTagPrefix = "mst_uid_";

        // FIXED 2026-08-19: this took the globally most recent batch out of a log shared by every
        // scene, then resolved it against whatever scene happened to be open - so "Undo Last
        // Batch" could target work done in a completely different file. Now scoped to the open
        // scene's own most recent batch.
        public static RevertOutcome RevertLastBatch(bool applyAutoReverts = true)
        {
            var last = ChangeLogger.ListRecentBatches(1, EntitySelector.CurrentSceneName).FirstOrDefault();
            return last == null ? new RevertOutcome() : RevertBatch(last.BatchId, applyAutoReverts);
        }

        public static RevertOutcome RevertBatch(string batchId, bool applyAutoReverts = true) =>
            RunBatch(batchId, applyAutoReverts, RevertOne);

        public static RevertOutcome RedoBatch(string batchId, bool applyAutoReverts = true) =>
            RunBatch(batchId, applyAutoReverts, RedoOne);

        public static void ConfirmAndRevert(ChangeLogEntry entry, GameEntity candidate) => RevertOne(candidate, entry);
        public static void ConfirmAndRedo(ChangeLogEntry entry, GameEntity candidate) => RedoOne(candidate, entry);

        private static RevertOutcome RunBatch(string batchId, bool applyAutoReverts, Action<GameEntity, ChangeLogEntry> apply)
        {
            var outcome = new RevertOutcome();
            var batch = ChangeLogger.ReadBatch(batchId);
            if (batch.Count == 0) return outcome;

            if (!EntitySelector.HasOpenScene)
                throw new InvalidOperationException("No scene is currently open in the editor.");
            var scene = EntitySelector.CurrentScene;

            var allEntities = new List<GameEntity>();
            scene.GetEntities(ref allEntities);
            allEntities = allEntities.Where(EntitySelector.IsValidEntity).ToList();

            // Batch history can be switched to show every scene's batches, so a cross-scene
            // Undo/Redo is still REACHABLE - it just must never auto-apply. The usual safety net
            // (no matching UID tag, nothing within tolerance) does not hold across scenes in this
            // project's actual workflow: the fief_*/material_test_* scenes are copies of each
            // other, so entities sit at near-identical coordinates under near-identical names,
            // and a copied scene carries the SAME mst_uid_ tags. That's the clone case the
            // resolver already distrusts, except here position matches too - which would flip it
            // to trusted and silently apply to the wrong file. Demoted to confirmation instead.
            var batchScene = batch[0].SceneName;
            var currentScene = EntitySelector.CurrentSceneName;
            bool crossScene = !string.Equals(batchScene, currentScene, StringComparison.OrdinalIgnoreCase);
            if (crossScene)
                Log.Warn($"Batch '{batchId}' was logged against scene '{batchScene}' but '{currentScene}' is open - " +
                         "every entry will require confirmation rather than auto-applying.");

            var tolerance = GetPositionTolerance(batchId, batchScene);

            foreach (var entry in batch)
            {
                var candidate = ResolveEntity(entry, allEntities, tolerance, out bool trusted);

                if (candidate == null)
                {
                    outcome.NotFound.Add(entry);
                    continue;
                }

                if (trusted && !crossScene)
                {
                    outcome.AutoReverted.Add(entry);
                    if (applyAutoReverts)
                        apply(candidate, entry);
                }
                else
                {
                    outcome.NeedsConfirmation.Add((entry, candidate));
                }
            }

            return outcome;
        }

        // The further back a batch sits in history, the more time there's been for something
        // else to happen to the entity in between (moved, cloned, re-tagged) - so older batches
        // get a stricter auto-trust tolerance, biasing toward "needs confirmation" rather than a
        // silently-wrong auto-apply. Only the single most recent batch gets the full tolerance.
        //
        // Depth is measured within the batch's OWN scene. It used to be measured globally, so a
        // batch that was the newest thing in its scene still got the stricter tolerance merely
        // because unrelated scenes had been edited more recently - penalising a batch for edits
        // that could not possibly have disturbed its entities.
        private static float GetPositionTolerance(string batchId, string batchScene)
        {
            var recent = ChangeLogger.ListRecentBatches(ChangeLogger.MaxHistoryDepth, batchScene);
            int depth = recent.FindIndex(b => b.BatchId == batchId);
            return depth <= 0 ? PositionToleranceMeters : PositionToleranceMeters * 0.5f;
        }

        private static GameEntity ResolveEntity(ChangeLogEntry entry, List<GameEntity> allEntities, float tolerance, out bool trusted)
        {
            trusted = false;

            if (!string.IsNullOrEmpty(entry.Uid))
            {
                var tagged = allEntities.Where(e => e.HasTag(UidTagPrefix + entry.Uid)).ToList();
                if (tagged.Count == 1)
                {
                    var frame = tagged[0].GetGlobalFrame();
                    if (Distance(frame.origin, entry) <= tolerance)
                    {
                        trusted = true;
                        return tagged[0];
                    }
                    // Tag present but position drifted too far to trust blindly (likely a clone
                    // carrying a copied tag) - fall through to position-based matching below.
                }
                else if (tagged.Count > 1)
                {
                    // Multiple entities sharing one UID - a clone situation (cloning duplicates
                    // tags). POSITION disambiguates (2026-08-24, "the MST undo is like fully
                    // broken" - the user's recolor-then-shift-drag-copies workflow duplicated
                    // the UID and this branch demoted the whole batch to confirmation): the
                    // ORIGINAL is still exactly where the entry logged it - a recolor moves
                    // nothing, and the clones were dragged elsewhere by definition. When
                    // precisely ONE tag-sharer sits within the tight tolerance, that is the
                    // original and it is trusted; genuine ambiguity (two sharers equally close)
                    // still demotes, same caution as before.
                    var atLoggedSpot = tagged.Where(e => Distance(e.GetGlobalFrame().origin, entry) <= 0.1f).ToList();
                    if (atLoggedSpot.Count == 1)
                    {
                        trusted = true;
                        return atLoggedSpot[0];
                    }

                    var nearest = tagged
                        .OrderBy(e => Distance(e.GetGlobalFrame().origin, entry))
                        .FirstOrDefault();
                    if (nearest != null) return nearest;
                }
            }

            var named = allEntities
                .Where(e => string.Equals(e.Name, entry.EntityName, StringComparison.Ordinal))
                .OrderBy(e => Distance(e.GetGlobalFrame().origin, entry))
                .ToList();
            if (named.Count == 0) return null;

            // NO-UID TRUST (2026-08-23, "you might have just totally broken the undo? It no
            // longer works at all"): entries without a UID were NEVER auto-trusted, so a whole
            // batch of them undid as "low-confidence, not applied" - visibly nothing. A recolor
            // does not move anything, so when EXACTLY ONE same-named entity sits within 10cm of
            // the logged position, that is the entity - ambiguity (a second name-match equally
            // close) still demotes to confirmation, same caution as before.
            if (string.IsNullOrEmpty(entry.Uid))
            {
                var withinTight = named.Count(e => Distance(e.GetGlobalFrame().origin, entry) <= 0.1f);
                if (withinTight == 1 && Distance(named[0].GetGlobalFrame().origin, entry) <= 0.1f)
                    trusted = true;
            }

            return named[0];
        }

        private static void RevertOne(GameEntity entity, ChangeLogEntry entry)
        {
            if (entry.IsEntityWideColor)
            {
                // PER-MESH RESTORE when the entry carries a snapshot (2026-08-23, "when we undo
                // ... a per-layer color factor colors the ENTIRE entity afterwards"):
                // SetFactorColor writes across EVERY mesh's own color, so restoring the single
                // logged OldColorFactor repainted the whole entity flat, destroying per-layer
                // colors the batch never touched. The forward apply now snapshots every mesh's
                // color first; restoring those individually puts back exactly what was there.
                if (!string.IsNullOrEmpty(entry.MeshColorSnapshot))
                {
                    foreach (var part in entry.MeshColorSnapshot.Split('|'))
                    {
                        var bits = part.Split(':');
                        if (bits.Length != 3) continue;
                        if (!int.TryParse(bits[0], out var sm) || !int.TryParse(bits[1], out var si)) continue;
                        if (!ColorHex.TryParse(bits[2], out var meshColor)) continue;
                        try
                        {
                            var snapMeta = entity.GetMetaMesh(sm);
                            var snapMesh = snapMeta != null && snapMeta.IsValid && si < snapMeta.MeshCount ? snapMeta.GetMeshAtIndex(si) : null;
                            if (snapMesh != null) snapMesh.Color = meshColor;
                        }
                        catch { }
                    }
                    return;
                }

                // Legacy entries (no snapshot): the old single-color behaviour is all there is.
                if (!string.IsNullOrEmpty(entry.OldColorFactor) && ColorHex.TryParse(entry.OldColorFactor, out var oldEntityColor))
                    entity.SetFactorColor(oldEntityColor);
                return;
            }

            if (entity.MultiMeshComponentCount <= entry.MetaMeshIndex) return;
            var meta = entity.GetMetaMesh(entry.MetaMeshIndex);
            if (meta == null || meta.MeshCount <= entry.MeshIndex) return;

            var mesh = meta.GetMeshAtIndex(entry.MeshIndex);
            if (mesh == null) return;

            // Some entries (a MetaMesh-level or per-mesh color reset with no accompanying
            // material change - see RevertToNormalEngine) leave OldMaterial/NewMaterial both
            // empty. Only the material identity check + set applies when this entry actually
            // represents a material change - otherwise there's nothing to match against and
            // skipping it would incorrectly bail out of the color reverts below too.
            bool isMaterialChange = !string.IsNullOrEmpty(entry.OldMaterial) || !string.IsNullOrEmpty(entry.NewMaterial);
            if (isMaterialChange)
            {
                var currentName = mesh.GetMaterial()?.Name;
                if (!string.Equals(currentName, entry.NewMaterial, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warn($"Skipped revert on '{entry.EntityName}' slot {entry.MetaMeshIndex}/{entry.MeshIndex}: " +
                             $"expected current material '{entry.NewMaterial}' but found '{currentName}' - it was likely changed again since.");
                    return;
                }
                mesh.SetMaterial(entry.OldMaterial);
            }

            // Color factor lives on the MetaMesh (LOD slot), not the individual Mesh - see the
            // comment in MaterialSwapEngine. Reverting it here is a softer guarantee than the
            // material revert above: it doesn't check the slot's current Factor1 against
            // NewColorFactor first, it just trusts the log. Reverting the same slot's color
            // multiple times (once per entry that touched it) is harmless - same value each time.
            if (!string.IsNullOrEmpty(entry.OldColorFactor) && ColorHex.TryParse(entry.OldColorFactor, out var oldColor))
                meta.SetFactor1(oldColor);

            // Per-individual-mesh color (Mesh.Color/Color2) - a third, independent tint, see
            // ChangeLogEntry. Same trust-the-log approach as the MetaMesh color above.
            if (!string.IsNullOrEmpty(entry.OldMeshColor) && ColorHex.TryParse(entry.OldMeshColor, out var oldMeshColor))
                mesh.Color = oldMeshColor;
            if (!string.IsNullOrEmpty(entry.OldMeshColor2) && ColorHex.TryParse(entry.OldMeshColor2, out var oldMeshColor2))
                mesh.Color2 = oldMeshColor2;
        }

        // Mirror of RevertOne - re-applies New* instead of restoring Old*. Same entity resolution,
        // same "does the live state still match what we expect" guard, just walking the log in
        // the other direction.
        private static void RedoOne(GameEntity entity, ChangeLogEntry entry)
        {
            if (entry.IsEntityWideColor)
            {
                if (!string.IsNullOrEmpty(entry.NewColorFactor) && ColorHex.TryParse(entry.NewColorFactor, out var newEntityColor))
                    entity.SetFactorColor(newEntityColor);
                return;
            }

            if (entity.MultiMeshComponentCount <= entry.MetaMeshIndex) return;
            var meta = entity.GetMetaMesh(entry.MetaMeshIndex);
            if (meta == null || meta.MeshCount <= entry.MeshIndex) return;

            var mesh = meta.GetMeshAtIndex(entry.MeshIndex);
            if (mesh == null) return;

            bool isMaterialChange = !string.IsNullOrEmpty(entry.OldMaterial) || !string.IsNullOrEmpty(entry.NewMaterial);
            if (isMaterialChange)
            {
                var currentName = mesh.GetMaterial()?.Name;
                if (!string.Equals(currentName, entry.OldMaterial, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warn($"Skipped redo on '{entry.EntityName}' slot {entry.MetaMeshIndex}/{entry.MeshIndex}: " +
                             $"expected current material '{entry.OldMaterial}' but found '{currentName}' - it was likely changed again since.");
                    return;
                }
                mesh.SetMaterial(entry.NewMaterial);
            }

            if (!string.IsNullOrEmpty(entry.NewColorFactor) && ColorHex.TryParse(entry.NewColorFactor, out var newColor))
                meta.SetFactor1(newColor);

            if (!string.IsNullOrEmpty(entry.NewMeshColor) && ColorHex.TryParse(entry.NewMeshColor, out var newMeshColor))
                mesh.Color = newMeshColor;
            if (!string.IsNullOrEmpty(entry.NewMeshColor2) && ColorHex.TryParse(entry.NewMeshColor2, out var newMeshColor2))
                mesh.Color2 = newMeshColor2;
        }

        private static float Distance(TaleWorlds.Library.Vec3 pos, ChangeLogEntry entry)
        {
            float dx = pos.x - entry.PosX, dy = pos.y - entry.PosY, dz = pos.z - entry.PosZ;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
