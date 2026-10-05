using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;

namespace MaterialSwapTool.Core
{
    // "Isolate": hide everything except the current selection, then put it back.
    //
    // THE PART THAT MATTERS IS THE RESTORE. Un-isolating must NOT simply make everything visible -
    // a scene routinely has things hidden on purpose (an interior layer, a work-in-progress
    // building, anything you hid yourself five minutes ago). Blanket-unhiding would silently undo
    // that and there would be no way to tell what had been hidden deliberately. So only the
    // entities Isolate ITSELF hid are recorded, and only those are made visible again - entities
    // that were already hidden are never touched, in either direction.
    //
    // Visibility only. Nothing here moves, deletes or edits an entity, which is why it does not
    // need the undo stack: Restore() IS the undo, and it is exact.
    //
    // THE RECORD LIVES IN THE SCENE, NOT ONLY IN MEMORY (2026-10-03). The first version kept the
    // restore record as a dictionary of live entity pointers and assumed hidden state never
    // reached disk. Both halves were wrong in practice: the editor DOES save visibility
    // (scene.xscene carries visible="false"), and the pointer record was dropped on every
    // scene-screen teardown - which includes ENTERING TEST MODE, not just closing a scene. So:
    // isolate, save, reload (or isolate, test mode, come back) left thousands of entities hidden
    // with Shift+O insisting nothing was isolated. Now every entity Isolate hides also gets the
    // tag below, and tags are part of the entity, saved with the scene and untouched by test
    // mode. Restore un-hides whatever carries the tag, with or without the memory record, and
    // after any teardown or scene open the scene is scanned for the tag so a leftover isolation
    // is rediscovered and announced instead of silently stranding the scene.
    public static class IsolationManager
    {
        // Persisted marker: "this entity is hidden because Isolate hid it". Removed on restore.
        public const string IsolatedTag = "mst_isolated";

        // KEYED BY NATIVE POINTER, NOT BY GameEntity REFERENCE. GameEntity is a managed wrapper
        // around a native pointer, and two separate queries hand back two separate wrapper
        // instances for the same entity - reference equality is meaningless across queries.
        private static readonly Dictionary<UIntPtr, GameEntity> Hidden = new Dictionary<UIntPtr, GameEntity>();

        // Deferred tag scan: armed by a teardown (test mode / scene close) and by every scene
        // open, run from Tick once the scene has been live for a moment. Deferred, not
        // immediate, because a whole-scene native query on the first resumed frame after test
        // mode is exactly what crashed the editor on 2026-08-31 (see BackupManager).
        private static bool _recoverPending;
        private static DateTime _recoverNotBeforeUtc;
        private static string _recoverReason = "";

        // Save-while-isolated detection: the scene file's write time when isolation began (or
        // was recovered); a later write means the hidden state is now on disk.
        private static DateTime? _sceneWriteTimeBaselineUtc;
        private static DateTime _lastSaveCheckUtc;

        public static bool IsActive => Hidden.Count > 0;

        private static bool Alive(GameEntity e) => e != null && e.Pointer != UIntPtr.Zero;

        // Toggles. Returns a status line for whichever panel asked. A pending recovery runs
        // first: the user pressing the key is proof the scene is live, and the result decides
        // whether this press isolates or restores.
        public static string Toggle()
        {
            if (_recoverPending) RunPendingRecovery();
            return IsActive ? Restore() : Isolate();
        }

        public static string Isolate()
        {
            if (IsActive) return Restore();

            if (!EntitySelector.HasOpenScene) return "No scene is currently open.";

            var selection = MaterialSwapTool.Core.SelectionMemory.GetSelection();
            if (selection.Count == 0) return "Nothing selected - select what you want to keep visible first.";

            // Keep the selection AND everything inside it: hiding a selected building's own child
            // meshes would "isolate" it into invisibility, which is the opposite of the point.
            var keep = new HashSet<UIntPtr>();
            foreach (var e in selection)
                foreach (var d in EntitySelector.EnumerateSelfAndDescendants(e))
                    if (Alive(d)) keep.Add(d.Pointer);

            // Parents too - an entity whose parent is hidden is hidden regardless of its own flag
            // (that is what IsVisibleIncludeParents means), so hiding the ancestors of the thing
            // you selected would hide the thing you selected.
            foreach (var e in selection)
            {
                var p = SafeParent(e);
                int guard = 0;
                while (Alive(p) && guard++ < 64) { keep.Add(p.Pointer); p = SafeParent(p); }
            }

            var all = new List<GameEntity>();
            EntitySelector.CurrentScene.GetEntities(ref all);

            int hidden = 0, alreadyHidden = 0, tagFailed = 0;
            foreach (var e in all)
            {
                if (!Alive(e) || keep.Contains(e.Pointer)) continue;
                try
                {
                    // Already hidden = not ours. No record, no tag, and Restore never touches it.
                    if (!e.GetVisibilityExcludeParents()) { alreadyHidden++; continue; }
                    e.SetVisibilityExcludeParents(false);
                    Hidden[e.Pointer] = e;
                    hidden++;
                    try { e.AddTag(IsolatedTag); }
                    catch (Exception ex) { tagFailed++; Log.Warn($"[Isolate] could not tag '{e.Name}': {ex.Message}"); }
                }
                catch (Exception ex) { Log.Warn($"[Isolate] could not hide '{e.Name}': {ex.Message}"); }
            }

            if (Hidden.Count == 0) return "Nothing to isolate - the selection is the whole scene.";

            _sceneWriteTimeBaselineUtc = SceneFileWriteTimeUtc();
            Log.Info($"[Isolate] kept={keep.Count} hidden={hidden} alreadyHidden={alreadyHidden} tagFailed={tagFailed}");
            var msg = $"Isolated {selection.Count} selected ({keep.Count} entity(ies) incl. children/parents); hid {hidden}. Press again to restore.";
            if (tagFailed > 0) msg += $" {tagFailed} could not be tagged - those will not survive a reload.";
            return msg;
        }

        public static string Restore()
        {
            if (_recoverPending) RunPendingRecovery();
            if (!IsActive && CountTagged() == 0) return "Nothing is isolated.";

            int restored = 0, gone = 0, failed = 0, swept = 0;
            var done = new HashSet<UIntPtr>();

            foreach (var kvp in Hidden)
            {
                var e = kvp.Value;
                if (!Alive(e)) { gone++; continue; }
                if (Unhide(e, ref failed)) { restored++; done.Add(e.Pointer); }
            }

            // Anything still tagged but not in the memory record was hidden by an Isolate the
            // record no longer knows about (saved + reloaded, or a teardown dropped the record)
            // - the tag is the authoritative list, so un-hide those too.
            foreach (var e in TaggedEntities())
            {
                if (!Alive(e) || done.Contains(e.Pointer)) continue;
                if (Unhide(e, ref failed)) { swept++; done.Add(e.Pointer); }
            }

            var count = Hidden.Count;
            Hidden.Clear();
            _sceneWriteTimeBaselineUtc = null;

            var msg = $"Un-isolated - restored visibility on {restored} of {count} entity(ies).";
            if (swept > 0) msg += $" Plus {swept} found by tag from an earlier isolation.";
            if (gone > 0) msg += $" {gone} no longer exist.";
            if (failed > 0) msg += $" {failed} failed (see tool.log).";
            Log.Info($"[Isolate] restore: restored={restored} swept={swept} gone={gone} failed={failed}");
            return msg;
        }

        private static bool Unhide(GameEntity e, ref int failed)
        {
            try
            {
                e.SetVisibilityExcludeParents(true);
                try { e.RemoveTag(IsolatedTag); } catch { /* tag may already be gone */ }
                return true;
            }
            catch (Exception ex)
            {
                failed++;
                Log.Warn($"[Isolate] restore failed on '{e.Name}': {ex.Message}");
                return false;
            }
        }

        private static GameEntity SafeParent(GameEntity e)
        {
            try { return e?.Parent; } catch { return null; }
        }

        // A scene-screen teardown invalidates every reference, so the pointer record is dropped
        // without restoring. But the teardown is NOT proof the isolation is over: entering test
        // mode fires the same hook, and the scene comes back with the same entities still hidden
        // (and still tagged). So dropping the record also arms a recovery scan - see Tick.
        public static void Clear(string reason)
        {
            if (Hidden.Count > 0)
            {
                Log.Info($"[Isolate] dropped {Hidden.Count} recorded entity(ies): {reason} - will re-scan for '{IsolatedTag}' tags.");
                ArmRecovery(reason, 5);
            }
            Hidden.Clear();
            _sceneWriteTimeBaselineUtc = null;
        }

        // Called when a scene (any scene, including the first) is seen live. A scene saved while
        // isolated carries the tags in its file; the scan is what finds them again.
        public static void OnSceneLive(string sceneName)
        {
            ArmRecovery($"scene '{sceneName}' opened", 3);
        }

        private static void ArmRecovery(string reason, int delaySeconds)
        {
            _recoverPending = true;
            _recoverReason = reason;
            var notBefore = DateTime.UtcNow.AddSeconds(delaySeconds);
            if (notBefore > _recoverNotBeforeUtc) _recoverNotBeforeUtc = notBefore;
        }

        // Driven from the MaterialSwapTool tick. Two cheap jobs: run an armed recovery scan once
        // the scene has settled, and notice a save made while isolated.
        public static void Tick()
        {
            if (_recoverPending && EntitySelector.HasOpenScene && DateTime.UtcNow >= _recoverNotBeforeUtc)
                RunPendingRecovery();

            if (!IsActive || !_sceneWriteTimeBaselineUtc.HasValue) return;
            if ((DateTime.UtcNow - _lastSaveCheckUtc).TotalSeconds < 1) return;
            _lastSaveCheckUtc = DateTime.UtcNow;

            var now = SceneFileWriteTimeUtc();
            if (!now.HasValue || now.Value <= _sceneWriteTimeBaselineUtc.Value) return;
            _sceneWriteTimeBaselineUtc = now;   // one warning per save, not one per second
            var warn = $"Scene saved while ISOLATED: {Hidden.Count} entity(ies) are hidden in the file. " +
                       "Shift+O restores them (then save again) before using the scene anywhere else.";
            Log.Warn("[Isolate] " + warn);
            try { MBEditor.AddEditorWarning(warn); } catch { }
        }

        private static void RunPendingRecovery()
        {
            _recoverPending = false;
            if (!EntitySelector.HasOpenScene) return;
            try
            {
                var tagged = TaggedEntities();
                int added = 0;
                foreach (var e in tagged)
                {
                    if (!Alive(e) || Hidden.ContainsKey(e.Pointer)) continue;
                    Hidden[e.Pointer] = e;
                    added++;
                }
                if (added == 0)
                {
                    Log.Info($"[Isolate] recovery scan ({_recoverReason}): no '{IsolatedTag}' tags.");
                    return;
                }
                _sceneWriteTimeBaselineUtc = SceneFileWriteTimeUtc();
                var msg = $"Isolate is still active from an earlier session ({_recoverReason}): " +
                          $"{added} entity(ies) hidden. Shift+O restores them.";
                Log.Warn("[Isolate] " + msg);
                try { MBEditor.AddEditorWarning(msg); } catch { }
            }
            catch (Exception ex)
            {
                Log.Warn($"[Isolate] recovery scan failed ({_recoverReason}): {ex.Message}");
            }
        }

        private static List<GameEntity> TaggedEntities()
        {
            try
            {
                var scene = EntitySelector.CurrentScene;
                if (scene == null) return new List<GameEntity>();
                return scene.FindEntitiesWithTag(IsolatedTag).ToList();
            }
            catch (Exception ex)
            {
                Log.Warn("[Isolate] tag query failed: " + ex.Message);
                return new List<GameEntity>();
            }
        }

        private static int CountTagged() => TaggedEntities().Count;

        private static DateTime? SceneFileWriteTimeUtc()
        {
            try
            {
                var path = Backup.BackupManager.TryFindSceneFile("scene.xscene");
                return path != null && File.Exists(path) ? File.GetLastWriteTimeUtc(path) : (DateTime?)null;
            }
            catch { return null; }
        }
    }
}
