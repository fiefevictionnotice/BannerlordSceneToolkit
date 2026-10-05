using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MaterialSwapTool.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace MaterialSwapTool.Backup
{
    // Copies scene.xscene + terrain.bin + terrain_ed.bin for the currently-open scene into a
    // timestamped folder. Purely filesystem-level - it backs up whatever is currently on disk,
    // it does not try to force the editor to save first (see PromptSaveBeforeApply / the
    // post-apply reminder for that half of the workflow).
    //
    // THE ONE BACKUP IMPLEMENTATION since v0.7. All three tools used to run their own copy of
    // this class - three independent 4-minute timers copying the same three files into three
    // separate Backups folders, and only this copy ever got the later improvements (the F9
    // settings panel, the unchanged-skip, copy verification, notification gating). The
    // PrefabSwapperTool/PrefabCreatorTool BackupManager classes still exist for their call
    // sites' sake but delegate everything here, so there is one timer, one skip check, one
    // Backups folder (this tool's - existing history and backup_settings.json stay where they
    // were, no migration), and the scene-switch state invalidation below runs no matter which
    // tools are enabled in tool_toggles.txt. Tick may be called up to three times per frame
    // (once per enabled tool's patch), which is why every cadence in it is wall-clock-based
    // rather than accumulated from dt - repeat calls in the same frame fall through for free.
    public static class BackupManager
    {
        // Kill switch for the scene-file backup (scene.xscene/terrain.bin copying) ONLY - the
        // in-tool undo/change-history systems (ChangeLogger, BatchHistory, PrefabSwapLogger) are
        // entirely separate and unaffected by this.
        //
        // RE-ENABLED 2026-08-20. This was switched off on 2026-08-17 while chasing crash-on-save,
        // on the theory that a background File.Copy reading scene.xscene while the native editor
        // writes it was the culprit. That theory was wrong. The crash was diagnosed from the
        // engine's own logs: a build deployed module files at 23:36:52, the game read one at
        // 23:36:55 and died at 23:36:58 - builds were being run while the editor was open,
        // overwriting files underneath it. Five other sessions in the same crash folder exited
        // cleanly. Backups were never implicated, and had in fact been off since the 17th, so they
        // cannot explain the crashes on the 19th either.
        //
        // Leaving them off had a real cost: sixteen confirmation dialogs across the three tools
        // promise "a backup is taken first", and every one of them was lying - including the two
        // irreversible bulk operations (Delete Interior Entities, Break Prefab Links).
        // Now settings-backed rather than a compile-time const - see BackupSettings for why.
        // Kept as a property with the same name so every existing call site is unchanged.
        public static bool BackupsEnabled => BackupSettings.Current.Enabled;

        private static float AutoBackupIntervalSeconds => BackupSettings.Current.IntervalMinutes * 60f;
        // Wall-clock timestamps, not accumulated dt - Tick can arrive up to three times per
        // frame now that every tool's patch drives this one implementation (see the class
        // comment). MinValue means "never", which makes every elapsed-time check fire on the
        // first opportunity, matching the old zero-initialized-accumulator behaviour.
        private static DateTime _lastAutoBackupUtc = DateTime.UtcNow;
        private static string _lastSeenSceneName;
        private static DateTime? _lastKnownSceneWriteTimeUtc;
        private static bool _saveReminderActive;
        private static DateTime _lastReminderNagUtc = DateTime.MinValue;
        private static DateTime _lastSaveCheckUtc = DateTime.MinValue;

        // No-save watchdog state (see Tick). 15 minutes matches what a lost session actually costs
        // here - long enough not to nag during normal work, short enough to catch a drift.
        private const float NoSaveWarnMinutes = 15f;
        private static DateTime _lastNoSaveCheckUtc = DateTime.MinValue;
        private static DateTime _lastNoSaveNagUtc = DateTime.MinValue;
        private static bool _noSaveWarned;

        // Public so the panels can offer an "Open Backups" shortcut. Backups being real files in a
        // known folder is the whole point of them, and until now nothing in the UI told you where
        // that folder was - you had to already know the path to benefit from the safety net.
        public static string BackupRootPath => BackupRoot;

        // Opens the backup folder in Explorer. Falls back to the tool's own folder when no backup
        // has been written yet, so the button explains itself instead of erroring on a missing dir.
        public static string OpenBackupFolder()
        {
            var target = Directory.Exists(BackupRoot)
                ? BackupRoot
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                               "Mount and Blade II Bannerlord", "MaterialSwapTool");
            Directory.CreateDirectory(target);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
            return target;
        }

        // Newest backup across all scenes, or null - lets a panel say how stale your safety net is
        // rather than just claiming one exists.
        public static string DescribeLatestBackup()
        {
            try
            {
                if (!Directory.Exists(BackupRoot)) return null;
                var newest = Directory.GetDirectories(BackupRoot)
                    .SelectMany(Directory.GetDirectories)
                    .Select(d => new DirectoryInfo(d))
                    .OrderByDescending(d => d.LastWriteTime)
                    .FirstOrDefault();
                if (newest == null) return null;
                return $"{newest.Parent?.Name}\\{newest.Name} ({newest.LastWriteTime:MM-dd HH:mm})";
            }
            catch { return null; }
        }

        // TOOL DATA backup - presets, palettes, categories, cultures, the interior whitelist.
        //
        // MANUAL ONLY, never on a timer, by explicit decision: this data changes when you sit down
        // and change it, not continuously like a scene does, so copying it every few minutes would
        // be churn for no benefit. The desktop snapshot watcher already covers this folder anyway.
        // Kept in its own _ToolData subfolder so it never competes with scene backups for the
        // per-scene retention count.
        public static string BackupToolData()
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var dest = Path.Combine(BackupRoot, "_ToolData", stamp);
            Directory.CreateDirectory(dest);

            var toolDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                                       "Mount and Blade II Bannerlord", "MaterialSwapTool");
            int copied = 0;

            foreach (var file in new[] { "material_categories.json", "culture_definitions.json",
                                         "Interior_Entities.txt", "scene_requirements.json",
                                         "backup_settings.json", "PanelPositions.json" })
            {
                var src = Path.Combine(toolDir, file);
                if (!File.Exists(src)) continue;
                try { File.Copy(src, Path.Combine(dest, file), true); copied++; }
                catch (Exception ex) { Log.Warn($"BackupToolData: {file}: {ex.Message}"); }
            }

            foreach (var sub in new[] { "Presets", "Palettes" })
            {
                var srcDir = Path.Combine(toolDir, sub);
                if (!Directory.Exists(srcDir)) continue;
                foreach (var src in Directory.GetFiles(srcDir, "*.json", SearchOption.AllDirectories))
                {
                    try
                    {
                        var rel = src.Substring(srcDir.Length).TrimStart('\\');
                        var target = Path.Combine(dest, sub, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.Copy(src, target, true);
                        copied++;
                    }
                    catch (Exception ex) { Log.Warn($"BackupToolData: {src}: {ex.Message}"); }
                }
            }

            Log.Info($"BackupToolData: copied {copied} file(s) to {dest}");
            return copied > 0 ? dest : null;
        }

        public class BackupStats
        {
            public int SceneCount;
            public int BackupCount;
            public long TotalBytes;          // scene backups only (what retention prunes)
            public long ToolDataBytes;       // the manual _ToolData copies
            public long FolderBytes;         // everything under the backup root, on disk
            public long DriveFreeBytes = -1; // free space on the drive the root sits on; -1 = unknown
            public DateTime? Newest;
            public string NewestLabel;
        }

        // Bytes -> "1.23 GB" / "456 MB" / "12 KB". Panel text only.
        public static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "?";
            const double KB = 1024, MB = KB * 1024, GB = MB * 1024;
            if (bytes >= GB) return $"{bytes / GB:N2} GB";
            if (bytes >= MB) return $"{bytes / MB:N0} MB";
            if (bytes >= KB) return $"{bytes / KB:N0} KB";
            return $"{bytes} B";
        }

        // Feeds the panel's "how stale is my safety net" line. The three-day gap happened because
        // nothing anywhere surfaced this - "backups are on" and "newest backup is from Monday"
        // were able to coexist unnoticed.
        public static BackupStats GetStats()
        {
            var s = new BackupStats();
            try
            {
                if (!Directory.Exists(BackupRoot)) return s;
                foreach (var sceneDir in Directory.GetDirectories(BackupRoot))
                {
                    if (Path.GetFileName(sceneDir).Equals("_ToolData", StringComparison.OrdinalIgnoreCase)) continue;
                    s.SceneCount++;
                    foreach (var stampDir in Directory.GetDirectories(sceneDir))
                    {
                        s.BackupCount++;
                        var info = new DirectoryInfo(stampDir);
                        if (s.Newest == null || info.LastWriteTime > s.Newest)
                        {
                            s.Newest = info.LastWriteTime;
                            s.NewestLabel = $"{Path.GetFileName(sceneDir)}\\{info.Name}";
                        }
                        foreach (var f in Directory.GetFiles(stampDir, "*", SearchOption.AllDirectories))
                        {
                            try { s.TotalBytes += new FileInfo(f).Length; } catch { }
                        }
                    }
                }

                // Whole-folder size, separately: retention only ever prunes scene backups, so
                // the per-scene total above can read "fine" while _ToolData copies and anything
                // dropped in by hand keep growing. The panel shows what the folder actually
                // costs on disk, and how much room is left for it.
                var toolData = Path.Combine(BackupRoot, "_ToolData");
                if (Directory.Exists(toolData))
                {
                    foreach (var f in Directory.GetFiles(toolData, "*", SearchOption.AllDirectories))
                    {
                        try { s.ToolDataBytes += new FileInfo(f).Length; } catch { }
                    }
                }
                foreach (var f in Directory.GetFiles(BackupRoot, "*", SearchOption.AllDirectories))
                {
                    try { s.FolderBytes += new FileInfo(f).Length; } catch { }
                }
                try
                {
                    var rootName = Path.GetPathRoot(Path.GetFullPath(BackupRoot));
                    if (!string.IsNullOrEmpty(rootName))
                        s.DriveFreeBytes = new DriveInfo(rootName).AvailableFreeSpace;
                }
                catch { /* UNC path or unmounted drive - the panel just omits the free-space figure */ }
            }
            catch (Exception ex) { Log.Warn("GetStats failed: " + ex.Message); }
            return s;
        }

        private static string BackupRoot
        {
            get
            {
                var custom = BackupSettings.Current.CustomRoot;
                if (!string.IsNullOrWhiteSpace(custom)) return custom.Trim();
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                                    "Mount and Blade II Bannerlord", "MaterialSwapTool", "Backups");
            }
        }

        // NOTIFICATION GATES.
        //
        // Both of these always log. Only the on-screen popup is optional. A backup that failed
        // with no popup AND no log line would leave nothing to find later, which defeats the
        // point of having backups at all.
        private static void NotifyBackupWarning(string message)
        {
            Log.Warn("[BackupWarning] " + message);
            if (!BackupSettings.Current.BackupWarningsEnabled) return;
            try { MBEditor.AddEditorWarning(message); } catch { }
        }

        private static void NotifySaveReminder(string message)
        {
            Log.Warn("[SaveReminder] " + message);
            if (!BackupSettings.Current.SaveRemindersEnabled) return;
            try { MBEditor.AddEditorWarning(message); } catch { }
        }

        public static void Tick(float dt)
        {
            TickAutoPhysicsStrip();

            // The auto-backup clock is a single global timestamp, not scoped per scene. Without this
            // check, switching scenes mid-countdown could leave the newly-opened scene with no
            // backup of its own for up to the full interval, since the timer keeps counting from
            // whenever it last fired against a DIFFERENT scene. Fire immediately on scene change so
            // every scene you open gets covered promptly, independent of the periodic timer's phase.
            // (Confirmed live: a scene switch right after an auto-backup fired for the PREVIOUS
            // scene left the newly-opened one uncovered when the game froze a few minutes later.)
            var currentSceneName = EntitySelector.CurrentScene?.GetName();

            // THE SCENE WAS CLOSED, not swapped for another one. The block below only ever fired
            // on a transition to a DIFFERENT NAMED scene, so closing a scene outright - back to
            // no scene at all - ran no invalidation whatsoever: every cached GameEntity, undo
            // step and open layer stayed pointing into a scene the engine was tearing down.
            // Reported as "CC_76_testing crashes every time I close the file", and the engine log
            // for that crash shows the sequence plainly: HandleDeactivate, Scene_view::clear_all,
            // HandleFinalize, then a Qt null-receiver warning as the process dies. Whatever else
            // is going on natively, our own statics must not still be holding that scene.
            if (string.IsNullOrEmpty(currentSceneName) && _lastSeenSceneName != null)
            {
                Log.Info($"[SceneGuard] scene '{_lastSeenSceneName}' closed - dropping cached state and closing panels.");
                _lastSeenSceneName = null;
                InvalidateSceneState("scene closed");
                return;
            }

            if (!string.IsNullOrEmpty(currentSceneName) && currentSceneName != _lastSeenSceneName)
            {
                var isFirstSceneSeen = _lastSeenSceneName == null;

                // Every undo step holds live GameEntity references belonging to the scene being
                // left. Restoring a frame onto - or removing - a freed native pointer is the class
                // of bug that takes the whole editor down, so the stack is dropped here rather
                // than filtered. This is the only place that reliably sees the transition.
                if (!isFirstSceneSeen) InvalidateSceneState($"scene changed to '{currentSceneName}'");

                _lastSeenSceneName = currentSceneName;
                _lastAutoBackupUtc = DateTime.UtcNow;
                if (!isFirstSceneSeen)
                {
                    try { BackupNow("auto-scene-switch"); }
                    catch (Exception ex) { Log.Warn("Scene-switch auto-backup failed: " + ex.Message); }
                }

                // SELF-HEAL ON SCENE OPEN (2026-08-23): sweep for copies contaminated by the
                // pre-fix CopyFrom flags (clean root, runtime-flagged children - that signature
                // is what makes a whole-scene sweep safe; see
                // PrefabDistributor.RepairFlaggedCopySubtrees). Cheap (flags only), and paired
                // with the cleanse-at-birth in ManipulationWatcher it means the contamination
                // dies out without the user ever pressing the manual F5 repair button. Runs for
                // the FIRST scene too - that is exactly the legacy stock this exists for.
                // Entities still streaming in at this instant are covered by the duplicate-birth
                // cleanse and the manual button.
                try
                {
                    var (repairedSubtrees, repairedEntities) =
                        PrefabSwapperTool.Core.PrefabDistributor.RepairFlaggedCopySubtrees(Core.EntitySelector.CurrentScene);
                    if (repairedEntities > 0)
                        Log.Info($"[FlagRepair] scene-open sweep of '{currentSceneName}': repaired {repairedSubtrees} subtree(s), {repairedEntities} entit(y/ies).");
                }
                catch (Exception ex) { Log.Warn("[FlagRepair] scene-open sweep failed: " + ex.Message); }

                // A scene saved while isolated carries Isolate's tags in its file; this arms the
                // (deferred) scan that finds them again. See IsolationManager.
                Try(() => Core.IsolationManager.OnSceneLive(currentSceneName), "IsolationManager.OnSceneLive");
            }

            // PeriodicEnabled gates ONLY this timer - before-apply backups stay live when it is off.
            if (BackupSettings.Current.PeriodicEnabled &&
                (DateTime.UtcNow - _lastAutoBackupUtc).TotalSeconds >= AutoBackupIntervalSeconds)
            {
                _lastAutoBackupUtc = DateTime.UtcNow;
                try { BackupNow("auto-4min"); }
                catch (Exception ex) { Log.Warn("Periodic auto-backup failed: " + ex.Message); }
            }

            // NO-SAVE WATCHDOG. Independent of the post-apply reminder below, which only arms
            // after an Apply. This fires whenever the open scene's file on disk has not moved for
            // 15 minutes, however that came about - because the backup copies DISK state, a long
            // unsaved stretch means the newest backup is equally stale, and nothing previously
            // said so. Checked once a second, nagged at most every 5 minutes so it stays a warning
            // rather than a spam source.
            if ((DateTime.UtcNow - _lastNoSaveCheckUtc).TotalSeconds >= 1)
            {
                _lastNoSaveCheckUtc = DateTime.UtcNow;
                try
                {
                    var xscenePath = TryFindSceneFile("scene.xscene");
                    if (xscenePath != null && File.Exists(xscenePath))
                    {
                        var idle = DateTime.Now - File.GetLastWriteTime(xscenePath);
                        if (idle.TotalMinutes >= NoSaveWarnMinutes)
                        {
                            if ((DateTime.UtcNow - _lastNoSaveNagUtc).TotalSeconds >= 300 || !_noSaveWarned)
                            {
                                _lastNoSaveNagUtc = DateTime.UtcNow;
                                _noSaveWarned = true;
                                NotifySaveReminder(
                                    $"No save for {(int)idle.TotalMinutes} minutes. Backups copy the SAVED file - " +
                                    "anything since your last save is not backed up.");
                            }
                        }
                        else
                        {
                            _noSaveWarned = false;   // they saved; re-arm for the next stretch
                            _lastNoSaveNagUtc = DateTime.MinValue;
                        }
                    }
                }
                catch { }
            }

            if (_saveReminderActive)
            {
                // Was checking the file every single tick - a synchronous File.Exists +
                // GetLastWriteTimeUtc syscall 60x/sec for as long as any Apply's reminder stayed
                // armed, unconditionally, regardless of whether any panel was even open. Once a
                // second is still fast enough to notice a save promptly.
                if ((DateTime.UtcNow - _lastSaveCheckUtc).TotalSeconds >= 1)
                {
                    _lastSaveCheckUtc = DateTime.UtcNow;
                    var xscenePath = TryFindSceneFile("scene.xscene");
                    if (xscenePath != null && File.Exists(xscenePath))
                    {
                        var writeTime = File.GetLastWriteTimeUtc(xscenePath);
                        if (_lastKnownSceneWriteTimeUtc.HasValue && writeTime > _lastKnownSceneWriteTimeUtc.Value)
                        {
                            _saveReminderActive = false; // they saved - the on-disk file moved forward
                            return;
                        }
                    }
                }

                if ((DateTime.UtcNow - _lastReminderNagUtc).TotalSeconds >= 30)
                {
                    _lastReminderNagUtc = DateTime.UtcNow;
                    NotifySaveReminder("Scene Toolkit: unsaved changes since the last apply. Save the scene.");
                }
            }
        }

        // EVERYTHING THAT MUST LET GO OF A SCENE, in one place. Called both when the scene is
        // swapped for another and when it is closed outright - the second case had no handling
        // at all before 2026-08-22, which is the "crashes every time I close the file" report.
        //
        // Order matters: drop the managed caches first, then close the layers. Every one of these
        // is individually guarded, because a failure partway through must not stop the rest -
        // a half-invalidated state is exactly what leaves a stale pointer behind for the next
        // tick to dereference.
        // Called from the editor-screen teardown patch (see SceneTeardownPatch), which is the
        // only signal that arrives EARLY ENOUGH.
        //
        // WHY THE TICK-BASED CHECK BELOW CANNOT DO THIS. Our per-frame hook is a Harmony patch on
        // MBEditor.TickSceneEditorPresentation - a method the scene editor screen drives. Closing
        // a scene deactivates that screen, so the ticks STOP, and a "has the scene name gone
        // empty?" test inside the tick never gets a frame in which to observe it. Confirmed
        // empirically on 2026-08-22: the close-detection added earlier that day was present in
        // the running build (module loaded 18:59:25, DLL deployed 18:50) and its [SceneGuard] line
        // never once appeared before the crash at ~19:02. The tick check is kept as a cheap
        // backstop for any path that does leave us running, but this is the one that fires.
        public static void OnEditorScreenTearingDown()
        {
            Log.Info("[SceneGuard] editor screen tearing down - dropping cached state and closing panels.");
            // DO NOT null _lastSeenSceneName here. This same HandleDeactivate hook fires when the
            // editor screen deactivates to ENTER TEST MODE, not only on a real scene close - the two
            // are indistinguishable at this hook. Nulling the remembered scene name made LEAVING test
            // mode look like a brand-new scene open, so the first resumed tick re-ran
            // RepairFlaggedCopySubtrees (a whole-scene native entity walk) against a scene that was
            // still re-streaming - dereferencing a not-yet-valid entity in native code and taking the
            // editor down every single time (confirmed 2026-08-31 by dump: null-deref in
            // TaleWorlds_Native reached from our tick via UMThunkStub; A/B: no crash with the mod off).
            // Cache invalidation below still runs (it drops the stale GameEntity refs that a real
            // close must not leave dangling), and a genuine scene close is still marked by the
            // tick's own currentScene==null branch, which nulls the name when it can. Leaving the
            // name intact means returning to the SAME scene (test mode) is correctly seen as "no
            // change" and skips the sweep; a switch to a DIFFERENT scene still sweeps that scene once
            // it is live. A same-scene close+reopen skips the sweep too, which is harmless - a fresh
            // disk load carries no runtime CopyFrom contamination for the sweep to clean.
            InvalidateSceneState("editor screen closing");
        }

        private static void InvalidateSceneState(string reason)
        {
            Try(() => BannerlordSceneToolkit.EditUndo.Clear(reason), "EditUndo");
            Try(() => Core.DeferredSelection.Cancel(), "DeferredSelection");
            Try(() => Core.IsolationManager.Clear(reason), "IsolationManager");
            Try(() => Core.SelectionMemory.Clear(), "SelectionMemory");
            Try(() => Core.SelectionGrow.Clear(reason), "SelectionGrow");
            Try(() => PrefabSwapperTool.Core.NumericTransform.Clear(reason), "NumericTransform");
            Try(() => PrefabSwapperTool.Core.ManipulationWatcher.Reset(), "ManipulationWatcher");
            Try(() => GUI.SelectionGrowLayer.Close(), "SelectionGrowLayer");
            Try(() => GUI.NumericTransformLayer.Close(), "NumericTransformLayer");
            Try(() => GUI.LastOperationHudLayer.Close(), "LastOperationHudLayer");
        }

        private static void Try(Action action, string what)
        {
            try { action(); }
            catch (Exception ex) { Log.Warn($"[SceneGuard] {what} cleanup failed: {ex.Message}"); }
        }

        // Serializes the background copy/prune work below - cheap, and avoids two overlapping
        // backups (e.g. the auto timer firing mid-manual-backup) racing on the same scene's
        // backup folder listing.
        private static readonly object BackupIoLock = new object();

        // (A): before an apply. (C) is the caller's job - ask "save first?" before calling this,
        // so the files being copied actually reflect the work about to be changed.
        //
        // The scene lookup (native Scene.GetName + a Directory.EnumerateDirectories over the
        // installed modules) happens synchronously here since it's cheap and touches the engine's
        // own objects, which isn't safe to do off the main thread. The actual file copy and old-
        // backup pruning below is dispatched to a background thread - terrain.bin/terrain_ed.bin
        // can be large, and on a real scene (not this project's near-empty test scene) or under
        // antivirus real-time scanning, a handful of synchronous File.Copy/Directory.Delete calls
        // on the main tick thread was stalling the entire game (rendering, audio, input) for
        // however long the disk I/O took - confirmed live: the periodic 4-minute auto-backup was
        // firing unconditionally regardless of whether any tool panel was even open, so it was
        // hanging the game on a fixed timer with no user action needed to trigger it. Nothing here
        // needs to be synchronous: no caller in this codebase waits on or verifies the copy's
        // completion, they only use the returned path for a status message.
        // Outcome of the most recent backup. The copy runs on a background task, so BackupNow
        // returns a path BEFORE the files exist - reporting that as "backed up" states an
        // intention as a result, which is how "a backup is taken first" managed to be false in
        // sixteen dialogs for three days. Callers can read this to report what actually happened.
        public class BackupResult
        {
            public string Path;
            public string Reason;
            public bool Completed;      // the background copy has finished (success or not)
            public bool Success;
            public string Error;
            public int FilesCopied;
            public long Bytes;
            public bool Skipped;        // nothing changed since the last backup
        }

        public static BackupResult LastResult { get; private set; }

        public static string DescribeLastResult()
        {
            var r = LastResult;
            if (r == null) return "No backup attempted this session.";
            if (r.Skipped) return $"Last backup skipped ({r.Reason}): scene unchanged since the previous one.";
            if (!r.Completed) return $"Backup in progress ({r.Reason}) -> {r.Path}";
            return r.Success
                ? $"Last backup OK ({r.Reason}): {r.FilesCopied} file(s), {r.Bytes / 1024f / 1024f:N1} MB -> {r.Path}"
                : $"Last backup FAILED ({r.Reason}): {r.Error}";
        }

        // True when the three source files are byte-identical (size + mtime) to the newest existing
        // backup for this scene. Cheap - three FileInfo reads, no hashing.
        private static bool NothingChangedSinceLastBackup(string sceneDir, string sceneName)
        {
            try
            {
                var sceneRoot = Path.Combine(BackupRoot, sceneName);
                if (!Directory.Exists(sceneRoot)) return false;
                var newest = Directory.GetDirectories(sceneRoot)
                    .Select(d => new DirectoryInfo(d))
                    .OrderByDescending(d => d.Name)
                    .FirstOrDefault();
                if (newest == null) return false;

                foreach (var fileName in new[] { "scene.xscene", "terrain.bin", "terrain_ed.bin" })
                {
                    var src = new FileInfo(Path.Combine(sceneDir, fileName));
                    var bak = new FileInfo(Path.Combine(newest.FullName, fileName));
                    if (src.Exists != bak.Exists) return false;
                    if (!src.Exists) continue;
                    if (src.Length != bak.Length) return false;
                    if (src.LastWriteTimeUtc != bak.LastWriteTimeUtc) return false;
                }
                return true;
            }
            catch { return false; }   // any doubt -> back up rather than skip
        }

        public static string BackupNow(string reason)
        {
            if (!BackupsEnabled)
            {
                Log.Info($"Backup skipped ({reason}): scene backups are currently disabled.");
                return null;
            }

            var scene = EntitySelector.CurrentScene;
            var sceneName = scene?.GetName() ?? "unknown_scene";

            var sceneDir = TryFindSceneDir(sceneName);
            if (sceneDir == null)
            {
                Log.Warn($"Backup skipped: could not locate a SceneObj folder for '{sceneName}'.");
                return null;
            }

            // Skip when nothing has changed on disk - otherwise an editor left open on an
            // untouched scene evicts real history at the same rate as active work (50 kept /
            // 15 per hour = ~3.3 hours), re-copies a 19-27 MB terrain_ed.bin to save nothing,
            // and (since v0.7, when the copy warning moved behind this check) raises an "avoid
            // Save" warning nobody needed. before-apply skips too now: if the files are
            // byte-identical to the newest backup, that backup IS the known-good marker - the
            // log line says so, which answers the old objection that the newest backup would
            // be labelled with an unrelated reason. Only an explicit manual Backup Now always
            // copies: a deliberate click should do the thing it says.
            bool isManual = reason == "manual";
            if (!isManual && NothingChangedSinceLastBackup(sceneDir, sceneName))
            {
                LastResult = new BackupResult { Reason = reason, Completed = true, Success = true, Skipped = true };
                Log.Info(reason == "before-apply"
                    ? $"Backup skipped (before-apply): '{sceneName}' unchanged on disk - the newest existing backup is the pre-apply marker."
                    : $"Backup skipped ({reason}): '{sceneName}' unchanged on disk since the last backup.");
                return null;
            }

            // The backup copies what is ON DISK. Unsaved editor changes are invisible to it, so
            // say how stale that is rather than letting "backup taken" imply "everything you can
            // see is safe".
            try
            {
                var xscene = Path.Combine(sceneDir, "scene.xscene");
                if (File.Exists(xscene))
                {
                    var age = DateTime.Now - File.GetLastWriteTime(xscene);
                    if (age.TotalMinutes >= 5)
                        Log.Warn($"Backup ({reason}): '{sceneName}' was last SAVED {(int)age.TotalMinutes} min ago - " +
                                 "this backs up that saved state, not unsaved editor changes.");
                }
            }
            catch { }

            var destDir = Path.Combine(BackupRoot, sceneName, $"{DateTime.Now:yyyyMMdd_HHmmss}_{reason}");
            var result = new BackupResult { Path = destDir, Reason = reason };
            LastResult = result;

            // The background copy below reads scene.xscene off disk while the native editor's own
            // Save writes to that exact same file - if both land at once, the copy could pick up a
            // half-written file (silently bad backup) or, worse, the two might contend for the file
            // handle in a way the engine's own save path doesn't expect. No confirmed root cause
            // yet for the crash-on-save pattern seen this session, but this is a real, easy-to-avoid
            // window - warn the user to hold off saving until it's done.
            NotifyBackupWarning($"{sceneName}: backup saving in the background - avoid Save for the next ~10 seconds.");

            System.Threading.Tasks.Task.Run(() =>
            {
                lock (BackupIoLock)
                {
                    try
                    {
                        Directory.CreateDirectory(destDir);
                        int copied = 0; long bytes = 0;
                        var problems = new List<string>();

                        foreach (var fileName in new[] { "scene.xscene", "terrain.bin", "terrain_ed.bin" })
                        {
                            var src = Path.Combine(sceneDir, fileName);
                            if (!File.Exists(src)) continue;
                            var dst = Path.Combine(destDir, fileName);
                            File.Copy(src, dst, overwrite: true);

                            // VERIFY, don't assume. File.Copy not throwing is not proof the file
                            // landed intact - a truncated copy from a disk-full or a mid-write
                            // source would sail through and leave a backup that only fails you at
                            // the moment you need it.
                            var s = new FileInfo(src);
                            var d = new FileInfo(dst);
                            if (!d.Exists) problems.Add($"{fileName}: not written");
                            else if (d.Length != s.Length) problems.Add($"{fileName}: {d.Length} bytes vs source {s.Length}");
                            else { copied++; bytes += d.Length; }
                        }

                        result.FilesCopied = copied;
                        result.Bytes = bytes;
                        result.Success = problems.Count == 0 && copied > 0;
                        result.Error = problems.Count > 0 ? string.Join("; ", problems)
                                     : (copied == 0 ? "no source files found to copy" : null);
                        result.Completed = true;

                        if (result.Success)
                        {
                            Log.Info($"Backed up scene '{sceneName}' ({reason}): {copied} file(s), {bytes / 1024f / 1024f:N1} MB -> {destDir}");
                            PruneBackups(sceneName);
                        }
                        else
                        {
                            Log.Warn($"Backup of '{sceneName}' ({reason}) INCOMPLETE: {result.Error}");
                            NotifyBackupWarning($"{sceneName}: backup did NOT complete - {result.Error}");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Completed = true;
                        result.Success = false;
                        result.Error = ex.Message;
                        Log.Warn($"Background backup of '{sceneName}' ({reason}) failed: {ex.Message}");
                        NotifyBackupWarning($"{sceneName}: backup FAILED - {ex.Message}");
                    }
                }
            });

            return destDir;
        }

        private static int MaxBackupsPerScene => BackupSettings.Current.MaxPerScene;

        // Tiered retention: full 4-minute cadence for the first hour, then progressively coarser
        // buckets - one kept backup per bucket, newest in the bucket wins. At these granularities
        // (20 min / 30 min / 1 hour) 50 files covers roughly 26 hours: 15 native-cadence backups
        // in hour 1, 9 more out to 4 hours, 8 more out to 8 hours (32 total), leaving 18 slots at
        // 1-hour spacing - another 18 hours. The hard cap is a backstop in case manual/before-apply
        // backups cluster faster than the tiering alone would prune.
        private static readonly (TimeSpan MaxAge, TimeSpan Granularity)[] RetentionTiers =
        {
            (TimeSpan.FromHours(4), TimeSpan.FromMinutes(20)),
            (TimeSpan.FromHours(8), TimeSpan.FromMinutes(30)),
            (TimeSpan.MaxValue, TimeSpan.FromHours(1)),
        };

        private static void PruneBackups(string sceneName)
        {
            var sceneDir = Path.Combine(BackupRoot, sceneName);
            if (!Directory.Exists(sceneDir)) return;

            var entries = Directory.GetDirectories(sceneDir)
                .Select(d => (Dir: d, CreatedUtc: Directory.GetCreationTimeUtc(d)))
                .OrderByDescending(e => e.CreatedUtc)
                .ToList();

            var now = DateTime.UtcNow;
            var kept = new List<string>();
            var bucketsSeen = new HashSet<(int Tier, long Bucket)>();

            foreach (var entry in entries)
            {
                var age = now - entry.CreatedUtc;

                if (age < TimeSpan.FromHours(1))
                {
                    kept.Add(entry.Dir); // native cadence - keep everything under an hour old
                    continue;
                }

                int tierIndex = 0;
                while (tierIndex < RetentionTiers.Length - 1 && age >= RetentionTiers[tierIndex].MaxAge)
                    tierIndex++;
                var granularity = RetentionTiers[tierIndex].Granularity;

                var bucket = (long)(age.TotalMinutes / granularity.TotalMinutes);
                if (bucketsSeen.Add((tierIndex, bucket)))
                    kept.Add(entry.Dir); // first (=newest, entries are sorted newest-first) wins the bucket
            }

            if (kept.Count > MaxBackupsPerScene)
                kept = kept.Take(MaxBackupsPerScene).ToList(); // still newest-first, so this drops the oldest

            var keepSet = new HashSet<string>(kept);
            foreach (var entry in entries)
            {
                if (keepSet.Contains(entry.Dir)) continue;
                try { Directory.Delete(entry.Dir, recursive: true); }
                catch (Exception ex) { Log.Warn($"Failed to prune backup '{entry.Dir}': {ex.Message}"); }
            }
        }

        // (D): call once right after a successful apply.
        public static void ArmSaveReminder()
        {
            var xscenePath = TryFindSceneFile("scene.xscene");
            _lastKnownSceneWriteTimeUtc = xscenePath != null && File.Exists(xscenePath)
                ? File.GetLastWriteTimeUtc(xscenePath)
                : (DateTime?)null;
            _saveReminderActive = true;
            _lastReminderNagUtc = DateTime.UtcNow;   // first nag comes 30s after arming, as before
        }

        // AUTO RE-STRIP after every save (2026-08-23, "the strip physics button isn't working?"
        // - it worked; the EDITOR SAVED 13 seconds later and re-emitted all 36 <physics> nodes,
        // confirmed by matching timestamps): file surgery is undone by ANY later editor save,
        // and the save-first/strip/don't-save-again ritual is too easy to get wrong. The scene
        // file is already being watched here, so every detected save re-runs the stripper
        // whenever the file carries pile_nophys pieces. Loop-safe: the stripper's own write
        // updates the baseline, and a re-run that finds nothing to strip never writes.
        private static DateTime? _lastStripSeenWriteUtc;
        private static DateTime _lastStripPollUtc = DateTime.MinValue;

        private static void TickAutoPhysicsStrip()
        {
            if ((DateTime.UtcNow - _lastStripPollUtc).TotalSeconds < 2) return;
            _lastStripPollUtc = DateTime.UtcNow;
            try
            {
                var path = TryFindSceneFile("scene.xscene");
                if (path == null || !File.Exists(path)) { _lastStripSeenWriteUtc = null; return; }

                var writeTime = File.GetLastWriteTimeUtc(path);
                if (_lastStripSeenWriteUtc == null) { _lastStripSeenWriteUtc = writeTime; return; }   // baseline on first sight
                if (writeTime <= _lastStripSeenWriteUtc.Value) return;
                _lastStripSeenWriteUtc = writeTime;

                // A save landed. Only act when stripped pieces exist in the file at all.
                string text;
                try { text = File.ReadAllText(path); } catch { return; }
                if (!text.Contains(PrefabCreatorTool.Core.SavedScenePhysicsStripper.Tag)) return;
                if (!text.Contains("<physics")) return;

                var status = PrefabCreatorTool.Core.SavedScenePhysicsStripper.StripCurrentScene();
                Log.Info("[Pile] auto re-strip after save: " + status);
                try { _lastStripSeenWriteUtc = File.GetLastWriteTimeUtc(path); } catch { }

                if (status.StartsWith("Removed", StringComparison.OrdinalIgnoreCase))
                    try { TaleWorlds.MountAndBlade.MBEditor.AddEditorWarning("Pile physics re-stripped from the saved file after that save - reload to get collision-free pieces."); } catch { }
            }
            catch (Exception ex) { Log.Warn("[Pile] auto re-strip failed: " + ex.Message); }
        }

        // Internal (2026-08-23): SavedScenePhysicsStripper resolves the saved scene.xscene
        // through this too.
        internal static string TryFindSceneFile(string fileName)
        {
            var scene = EntitySelector.CurrentScene;
            var sceneName = scene?.GetName();
            if (string.IsNullOrEmpty(sceneName)) return null;
            var dir = TryFindSceneDir(sceneName);
            return dir == null ? null : Path.Combine(dir, fileName);
        }

        // The scene's SceneObj folder could belong to any installed module, not just one we know
        // in advance - search all of them for a match rather than assuming. Public so Scene
        // Analyzer's broken-prefab fixer can reuse the exact same lookup instead of duplicating it.
        public static string TryFindSceneDir(string sceneName)
        {
            var modulesRoot = Path.Combine(BasePath.Name, "Modules");
            if (!Directory.Exists(modulesRoot)) return null;

            return Directory.EnumerateDirectories(modulesRoot)
                .Select(moduleDir => Path.Combine(moduleDir, "SceneObj", sceneName))
                .FirstOrDefault(Directory.Exists);
        }
    }
}
