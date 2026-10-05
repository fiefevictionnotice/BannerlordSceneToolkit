using System;
using System.Collections.Generic;
using System.Linq;
using MaterialSwapTool.Backup;
using MaterialSwapTool.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    // Reuses CulturePickItemVM's shape (name + click callback) for the requirement-set picker -
    // same pattern, different list, no need for a new near-identical VM class.
    //
    // Migrated from offline scene.xscene/XDocument checks to the LIVE GameEntity API
    // (LiveSceneChecks) - live checks reflect your current unsaved editor state and apply/undo
    // instantly with no "reload the scene" step, which is strictly better once you confirm (as we
    // did via decompile) that the live API can reach everything the offline checks needed:
    // scripts (HasScriptComponent), parent/child navigation for zone trees (Parent/GetChildren),
    // body flags, scale, and direct swap/removal (Instantiate/Remove). See LiveSceneChecks for the
    // full rationale and BsaSceneChecks/SceneXmlHelpers for the (now legacy) offline versions still
    // used only for reference-data loading (BuggedPhysicsDict/LodSubstitutions) and
    // references.txt's path lookup.
    public class SceneAnalyzerVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;
        private BrokenPrefabFixer.ScanResult _lastScan;
        private string _brokenPrefabStatusText = "Scan the current scene to check for known-broken prefabs.";
        private string _statusText = "";
        private MBBindingList<CulturePickItemVM> _requirementSets;

        public SceneAnalyzerVM(Action closeAction, Action beginDragAction)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            // Push the default down immediately - the property setter only fires when the text
            // box is edited, so without this the first run would use an empty (unfiltered) value.
            LiveSceneChecks.UnbrokenPrefixFilter = _unbrokenPrefixFilter;
            _requirementSets = new MBBindingList<CulturePickItemVM>();
            foreach (var key in SceneRequirementChecker.AllSetKeys)
                _requirementSets.Add(new CulturePickItemVM(SceneRequirementChecker.GetDisplayName(key), _ => RunRequirementCheck(key)));
        }

        [DataSourceProperty]
        public string BrokenPrefabStatusText
        {
            get => _brokenPrefabStatusText;
            set { if (value != _brokenPrefabStatusText) { _brokenPrefabStatusText = value; OnPropertyChangedWithValue(value, nameof(BrokenPrefabStatusText)); } }
        }

        [DataSourceProperty]
        public string StatusText
        {
            get => _statusText;
            set { if (value != _statusText) { _statusText = value; OnPropertyChangedWithValue(value, nameof(StatusText)); } }
        }

        [DataSourceProperty]
        public MBBindingList<CulturePickItemVM> RequirementSets
        {
            get => _requirementSets;
            set { if (value != _requirementSets) { _requirementSets = value; OnPropertyChangedWithValue(value, nameof(RequirementSets)); } }
        }

        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();
        public void ExecuteOpenDocumentation() => DocumentationLayer.Toggle();

        // ---- shared live-entity access ----
        private bool TryGetEntities(out Scene scene, out List<GameEntity> all)
        {
            scene = null;
            all = null;
            if (!EntitySelector.HasOpenScene)
            {
                StatusText = "No scene is currently open.";
                return false;
            }
            scene = EntitySelector.CurrentScene;
            all = LiveSceneChecks.CollectAll(scene);
            return true;
        }

        public void ExecuteScanBrokenPrefabs()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { BrokenPrefabStatusText = "No scene is currently open."; return; }

                _lastScan = BrokenPrefabFixer.Scan();
                if (_lastScan == null)
                {
                    BrokenPrefabStatusText = "Could not scan the current scene.";
                    return;
                }

                BrokenPrefabStatusText = _lastScan.TotalMatches == 0
                    ? "Scanned - no known-broken prefabs found in this scene."
                    : "Found: " + string.Join(", ", _lastScan.MatchCounts.Select(kv => $"{kv.Key} x{kv.Value}")) +
                      " - click Apply Fix to swap them for their fixed replacements.";
            }
            catch (Exception ex)
            {
                BrokenPrefabStatusText = "Scan failed: " + ex.Message;
                Log.Error("SceneAnalyzer scan failed: " + ex);
            }
        }

        public void ExecuteApplyBrokenPrefabFix()
        {
            if (_lastScan == null || _lastScan.TotalMatches == 0)
            {
                BrokenPrefabStatusText = "Scan first - nothing queued to fix.";
                return;
            }

            var inquiry = new InquiryData(
                "Fix broken prefabs?",
                $"This will instantly swap {_lastScan.TotalMatches} broken prefab instance(s) in the live scene for their " +
                "fixed equivalents, preserving position/rotation. A backup is taken first. Non-default scale (if any) will " +
                "NOT carry over to the replacement - the live API has no way to set it after instantiation. Continue?",
                isAffirmativeOptionShown: true,
                isNegativeOptionShown: true,
                affirmativeText: "Fix",
                negativeText: "Cancel",
                affirmativeAction: RunApplyBrokenPrefabFix,
                negativeAction: null);
            InformationManager.ShowInquiry(inquiry);
        }

        private void RunApplyBrokenPrefabFix()
        {
            try
            {
                BackupManager.BackupNow("before-apply");
                var result = BrokenPrefabFixer.ApplyFix();
                BrokenPrefabStatusText = result.Failed == 0
                    ? $"Fixed {result.EntitiesFixed} entity instance(s) live in the scene."
                    : $"Fixed {result.EntitiesFixed}, {result.Failed} failed: {string.Join("; ", result.Errors.Take(3))}";
                _lastScan = null;
            }
            catch (Exception ex)
            {
                BrokenPrefabStatusText = "Fix failed: " + ex.Message;
                Log.Error("SceneAnalyzer apply fix failed: " + ex);
            }
        }

        // ============================================================
        // Full scan + individual game-mode checks, all ported from BannerlordSceneAnalyzer's
        // Analyze-BannerlordScene.ps1 (see BsaSceneChecks for attribution, including Gotha's
        // BL_AddTestScene for Skirmish/Editor-Spawn), now running against the live GameEntity API
        // via LiveSceneChecks.
        // ============================================================

        private static List<string> FormatFindings(IEnumerable<Finding> findings) =>
            findings.Select(f => $"[{f.Severity}] {f.Message}").ToList();

        // Shared stack across every tool - see EditUndo. Covers the tag buttons here; the
        // DESTRUCTIVE actions on this panel (Delete Interiors, Break Prefab Links, Remove
        // Physics) are deliberately not undoable and still rely on the pre-action backup.
        public void ExecuteUndo()
        {
            var (ok, message) = BannerlordSceneToolkit.EditUndo.UndoLast();
            StatusText = message;
            if (!ok) Log.Info("[EditUndo] " + message);
        }

        public void ExecuteRunFullScan()
        {
            try
            {
                if (!TryGetEntities(out _, out var all)) return;

                var lines = new List<string> { "=== Bugged Physics Shapes ===" };
                lines.AddRange(FormatFindings(LiveSceneChecks.CheckBuggedPhysics(all)));
                lines.Add("");
                lines.Add("=== Misleading Physics ===");
                lines.AddRange(FormatFindings(LiveSceneChecks.CheckMisleadingPhysics(all)));
                lines.Add("");
                lines.Add("=== Duplicate / Overlapping Entities ===");
                lines.AddRange(FormatFindings(LiveSceneChecks.CheckDuplicates(all)));
                lines.Add("");
                lines.Add("=== LOD Substitution Recommendations ===");
                lines.AddRange(FormatFindings(LiveSceneChecks.CheckLodSubstitutions(all)));
                lines.Add("");
                lines.Add("=== Known Bad / Early-Popping LODs ===");
                lines.AddRange(FormatFindings(LiveSceneChecks.CheckKnownBadLods(all)));
                lines.Add("");
                lines.Add("=== Sittable / Animation-Interact Prefabs ===");
                lines.AddRange(FormatFindings(LiveSceneChecks.CheckSittablePrefabs(all)));
                lines.Add("");
                lines.Add("=== map_ Prefix Entities ===");
                lines.AddRange(FormatFindings(LiveSceneChecks.CheckMapPrefixEntities(all)));
                lines.Add("");
                lines.Add("=== Interior Entities ===");
                lines.AddRange(FormatFindings(LiveSceneChecks.CheckInteriorEntities(all)));
                lines.Add("");
                lines.Add("=== Walk / Barrier Volumes ===");
                lines.AddRange(FormatFindings(LiveSceneChecks.CheckWalkBarrierVolumes(all)));
                lines.Add("");
                lines.Add("=== General MP (spawn_visual, camera, envmap, flee_line) ===");
                lines.AddRange(FormatFindings(LiveSceneChecks.CheckGeneralMp(all)));
                lines.Add("");
                lines.Add("=== Climbable Civilian Ladders ===");
                lines.AddRange(FormatFindings(LiveSceneChecks.CheckClimbableCivilianLadders(all)));
                lines.Add("");
                lines.Add("=== Editor Playtest Spawns ===");
                lines.AddRange(FormatFindings(LiveSceneChecks.CheckEditorSpawns(all)));
                lines.Add("");
                lines.Add("=== references.txt Sanity ===");
                lines.AddRange(FormatFindings(LiveSceneChecks.CheckReferencesFile()));

                // OOB and Non-Uniform Scale are deliberately LAST, in that order: they routinely
                // produce far more lines than every other check combined, and reading the scan from
                // the top should not mean scrolling past hundreds of them to reach anything else.
                lines.Add("=== Entities Outside border_soft Boundary ===");
                lines.AddRange(FormatFindings(LiveSceneChecks.CheckEntitiesOutsideBorder(all)));
                lines.Add("");
                lines.Add("=== Non-Uniform Scale on Physics Entities ===");
                lines.AddRange(FormatFindings(LiveSceneChecks.CheckNonUniformScale(all)));
                lines.Add("");

                var errorCount = lines.Count(l => l.StartsWith("[ERROR]"));
                var warnCount = lines.Count(l => l.StartsWith("[WARNING]"));
                DiffPreviewLayer.Open($"Full Scene Scan - {errorCount} error(s), {warnCount} warning(s)", lines);
                StatusText = "Full scan complete - see results panel.";
            }
            catch (Exception ex)
            {
                StatusText = "Full scan failed: " + ex.Message;
                Log.Error("SceneAnalyzer full scan failed: " + ex);
            }
        }

        public void ExecuteCheckBattle() => RunModeCheck("Battle Mode", LiveSceneChecks.CheckBattleMode);
        public void ExecuteCheckSkirmish() => RunModeCheck("Skirmish Mode", LiveSceneChecks.CheckSkirmishMode);
        public void ExecuteCheckSiege() => RunModeCheck("Siege Mode", LiveSceneChecks.CheckSiegeMode);

        private void RunModeCheck(string label, Func<List<GameEntity>, List<Finding>> check)
        {
            try
            {
                if (!TryGetEntities(out _, out var all)) return;
                var findings = check(all);
                var lines = FormatFindings(findings);
                var errorCount = lines.Count(l => l.StartsWith("[ERROR]"));
                var warnCount = lines.Count(l => l.StartsWith("[WARNING]"));
                DiffPreviewLayer.Open($"{label} - {errorCount} error(s), {warnCount} warning(s)", lines);
                StatusText = $"{label} check complete - see results panel.";
            }
            catch (Exception ex)
            {
                StatusText = $"{label} check failed: " + ex.Message;
                Log.Error($"SceneAnalyzer {label} check failed: " + ex);
            }
        }

        // ---- Mutating actions: confirm, backup, apply live ----

        public void ExecuteDeleteDuplicates()
        {
            var inquiry = new InquiryData(
                "Delete duplicate entities?",
                "Finds entities of the same type at the exact same world position and rotation (dist=0, the strict deletion " +
                "threshold - near-matches are left alone as possibly intentional) and removes all but one of each stack, " +
                "instantly in the live scene. A backup is taken first. Continue?",
                isAffirmativeOptionShown: true, isNegativeOptionShown: true,
                affirmativeText: "Delete", negativeText: "Cancel",
                affirmativeAction: () => RunMutatingAction("Delete duplicates", all => LiveSceneChecks.DeleteDuplicates(all), "duplicate(s) removed"),
                negativeAction: null);
            InformationManager.ShowInquiry(inquiry);
        }

        public void ExecuteTagDuplicates()
        {
            var inquiry = new InquiryData(
                "Tag duplicate entities?",
                "Adds a BSA_LIKELY_DUPLICATE tag to likely duplicate entities (same position/rotation) without deleting them, " +
                "so you can find and review them in the editor by searching for that tag. A backup is taken first. Continue?",
                isAffirmativeOptionShown: true, isNegativeOptionShown: true,
                affirmativeText: "Tag", negativeText: "Cancel",
                affirmativeAction: () => RunMutatingAction("Tag duplicates", all => LiveSceneChecks.TagDuplicates(all), "duplicate(s) tagged BSA_LIKELY_DUPLICATE"),
                negativeAction: null);
            InformationManager.ShowInquiry(inquiry);
        }

        public void ExecuteTagBuggedPhysics()
        {
            var inquiry = new InquiryData(
                "Tag bugged-physics entities?",
                "Adds a BSA_BUGGED_PHYSICS_SHAPE tag to every live entity matching the known-bugged-physics reference list, " +
                "so you can find them in the editor by searching for that tag. A backup is taken first. Continue?",
                isAffirmativeOptionShown: true, isNegativeOptionShown: true,
                affirmativeText: "Tag", negativeText: "Cancel",
                affirmativeAction: () => RunMutatingAction("Tag bugged physics", all => LiveSceneChecks.TagBuggedPhysics(all), "entit(y/ies) tagged BSA_BUGGED_PHYSICS_SHAPE"),
                negativeAction: null);
            InformationManager.ShowInquiry(inquiry);
        }

        public void ExecuteTagOutsideBorder()
        {
            var inquiry = new InquiryData(
                "Tag entities outside the soft border?",
                $"Adds a {LiveSceneChecks.OutsideBorderTag} tag to every entity currently outside the border_soft boundary " +
                "(+10 unit margin), so you can find them in the editor by searching for that tag. Non-destructive, safe to " +
                "re-run. A backup is taken first. Continue?",
                isAffirmativeOptionShown: true, isNegativeOptionShown: true,
                affirmativeText: "Tag", negativeText: "Cancel",
                affirmativeAction: RunTagOutsideBorder,
                negativeAction: null);
            InformationManager.ShowInquiry(inquiry);
        }

        private void RunTagOutsideBorder()
        {
            try
            {
                if (!TryGetEntities(out _, out var all)) return;
                BackupManager.BackupNow("before-apply");
                var (tagged, status) = LiveSceneChecks.TagEntitiesOutsideBorder(all);
                StatusText = status != null ? status.Message : $"Tag outside border: {tagged} entit(y/ies) tagged {LiveSceneChecks.OutsideBorderTag}.";
            }
            catch (Exception ex)
            {
                StatusText = "Tag outside border failed: " + ex.Message;
                Log.Error("SceneAnalyzer TagOutsideBorder failed: " + ex);
            }
        }

        public void ExecuteTagInvisible()
        {
            var inquiry = new InquiryData(
                "Tag invisible entities?",
                $"Adds a {LiveSceneChecks.InvisibleTag} tag to every entity that doesn't render - either hidden itself, " +
                "or hidden because a parent is. Find them afterwards by searching that tag in the editor. " +
                "Non-destructive and re-runnable; visibility itself is not changed. Continue?",
                isAffirmativeOptionShown: true, isNegativeOptionShown: true,
                affirmativeText: "Tag", negativeText: "Cancel",
                affirmativeAction: () =>
                {
                    try
                    {
                        if (!TryGetEntities(out _, out var all)) return;
                        var (tagged, ownFlag, inherited) = LiveSceneChecks.TagInvisibleEntities(all);
                        StatusText = (ownFlag + inherited) == 0
                            ? "No invisible entities found - everything in the scene renders."
                            : $"Tagged {tagged} entit(y/ies) {LiveSceneChecks.InvisibleTag}: {ownFlag} hidden by their own flag, " +
                              $"{inherited} hidden because a parent is.";
                    }
                    catch (Exception ex)
                    {
                        StatusText = "Tag invisible failed: " + ex.Message;
                        Log.Error("SceneAnalyzer TagInvisible failed: " + ex);
                    }
                },
                negativeAction: null);
            InformationManager.ShowInquiry(inquiry);
        }

        // --- Select in editor (new alongside the tag buttons; tags kept for now) ---
        private void SelectMatching(string what, Func<GameEntity, bool> predicate)
        {
            try
            {
                if (!TryGetEntities(out _, out var all)) return;
                var n = LiveSceneChecks.SelectInEditor(all, predicate);
                StatusText = n == 0 ? $"No {what} to select." : $"Selected {n} {what} in the editor.";
            }
            catch (Exception ex)
            {
                StatusText = $"Select {what} failed: " + ex.Message;
                Log.Error($"SceneAnalyzer Select {what} failed: " + ex);
            }
        }

        public void ExecuteSelectInterior() =>
            SelectMatching("interior entit(y/ies)", e => LiveSceneChecks.IsInteriorEntity(e, out _));

        public void ExecuteSelectInvisible() =>
            SelectMatching("invisible entit(y/ies)", LiveSceneChecks.IsInvisible);

        public void ExecuteSelectUnbrokenPrefabs() =>
            SelectMatching("unbroken non-native prefab(s)", LiveSceneChecks.IsUnbrokenNonNativePrefab);

        // Promote whatever parts are selected to their top-level prefab roots - the panel
        // counterpart of the Ctrl+Shift+P shortcut, for when the hotkey is switched off or
        // just not remembered. Unlike the SelectMatching family this reads the CURRENT
        // selection rather than scanning the scene for a predicate.
        public void ExecuteSelectTopLevel()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }

                var selection = Core.SelectionMemory.GetSelection();
                if (selection.Count == 0) { StatusText = "Nothing selected - click the part of a prefab first."; return; }

                var roots = EntitySelector.PromoteToRoots(selection);
                LiveSceneChecks.SetEditorSelection(roots);
                StatusText = $"Selected {roots.Count} top-level prefab(s) from {selection.Count} selected part(s). " +
                             "(Applied a couple of ticks after the click - see the docs on delayed selection.)";
                Log.Info($"[SelectRoot] panel: {selection.Count} selected -> {roots.Count} root(s).");
            }
            catch (Exception ex)
            {
                StatusText = "Select Whole Prefab failed: " + ex.Message;
                Log.Error("SceneAnalyzer ExecuteSelectTopLevel failed: " + ex);
            }
        }

        // --- Unbroken custom prefabs ---
        private string _unbrokenPrefixFilter = "ff_, fief_";

        // Comma-separated prefixes scoping the three unbroken-prefab actions. Defaults to your
        // own naming conventions rather than empty: empty means "everything not positively
        // identified as native", and the native reference list only covers 265 names, so an empty
        // filter matches almost the entire scene. Clear it deliberately if that is what you want.
        [DataSourceProperty]
        public string UnbrokenPrefixFilter
        {
            get => _unbrokenPrefixFilter;
            set
            {
                if (value != _unbrokenPrefixFilter)
                {
                    _unbrokenPrefixFilter = value;
                    LiveSceneChecks.UnbrokenPrefixFilter = value;
                    OnPropertyChangedWithValue(value, nameof(UnbrokenPrefixFilter));
                }
            }
        }

        public void ExecuteTagUnbrokenPrefabs()
        {
            try
            {
                if (!TryGetEntities(out _, out var all)) return;
                var (tagged, found) = LiveSceneChecks.TagUnbrokenPrefabs(all);
                var scope = string.IsNullOrWhiteSpace(UnbrokenPrefixFilter) ? "no prefix filter" : $"prefix: {UnbrokenPrefixFilter}";
                StatusText = found == 0
                    ? $"No unbroken non-native prefab links found ({scope})."
                    : $"Tagged {tagged} of {found} entit(y/ies) {LiveSceneChecks.UnbrokenPrefabTag} ({scope}).";
            }
            catch (Exception ex)
            {
                StatusText = "Tag unbroken prefabs failed: " + ex.Message;
                Log.Error("SceneAnalyzer TagUnbrokenPrefabs failed: " + ex);
            }
        }

        public void ExecuteBreakUnbrokenPrefabs()
        {
            try
            {
                if (!TryGetEntities(out _, out var all)) return;
                var preview = LiveSceneChecks.PreviewUnbrokenPrefabs(all);
                if (preview.Count == 0) { StatusText = "No unbroken non-native prefab links found."; return; }

                var total = preview.Sum(p => p.Count);
                var unknown = preview.Count(p => p.Origin == LiveSceneChecks.PrefabOrigin.Unknown);
                var lines = preview.Take(10).Select(p => $"  {p.Prefab} x{p.Count}   [{p.Origin}]");
                var more = preview.Count > 10 ? $"\n  ...and {preview.Count - 10} more" : "";

                var inquiry = new InquiryData(
                    $"Break {total} prefab link(s)?",
                    "Bakes each prefab's contents into the scene so it loads without needing the prefab file - "
                        + "the entities keep working and stay where they are.\n\n"
                        + string.Join("\n", lines) + more
                        + (unknown > 0 ? $"\n\n{unknown} prefab name(s) are UNKNOWN - not in the native list (which only covers 265 names from 17 official maps) nor in Custom_Module_Prefabs.txt. Some may be native." : "")
                        + "\n\nThis cannot be undone from this tool. A backup is taken first.",
                    isAffirmativeOptionShown: true, isNegativeOptionShown: true,
                    affirmativeText: "Break", negativeText: "Cancel",
                    affirmativeAction: () =>
                    {
                        try
                        {
                            if (!TryGetEntities(out _, out var live)) return;
                            var backupPath = BackupManager.BackupNow("before-apply");
                            var broken = LiveSceneChecks.BreakUnbrokenPrefabs(live);
                            StatusText = $"Broke {broken} prefab link(s) - the scene no longer needs those prefab files."
                                         + (backupPath != null ? $" Backup: {backupPath}" : "  WARNING: no backup was written - see tool.log.");
                        }
                        catch (Exception ex)
                        {
                            StatusText = "Break prefabs failed: " + ex.Message;
                            Log.Error("SceneAnalyzer BreakUnbrokenPrefabs failed: " + ex);
                        }
                    },
                    negativeAction: null);
                InformationManager.ShowInquiry(inquiry);
            }
            catch (Exception ex)
            {
                StatusText = "Break prefabs failed: " + ex.Message;
                Log.Error("SceneAnalyzer BreakUnbrokenPrefabs preview failed: " + ex);
            }
        }

        // --- Backup shortcuts (mirrored on F8's Material Swap panel) ---
        public void ExecuteBackupNow()
        {
            var path = BackupManager.BackupNow("manual");
            // "Backed up" would be a claim about a copy that has not run yet - BackupNow returns
            // as soon as the background task is queued. Report it as started, and let the F9 panel
            // (or tool.log) carry the verified outcome.
            StatusText = path != null
                ? $"Backup started -> {path}   (F9 shows the verified result)"
                : (BackupManager.BackupsEnabled
                    ? "Nothing backed up - scene unchanged, or no scene folder found. See tool.log."
                    : "Backups are OFF - nothing was written.");
        }

        public void ExecuteOpenBackups()
        {
            try
            {
                var opened = BackupManager.OpenBackupFolder();
                var latest = BackupManager.DescribeLatestBackup();
                StatusText = latest != null
                    ? $"Opened {opened}   (latest backup: {latest})"
                    : $"Opened {opened} - no backups written yet.";
            }
            catch (Exception ex)
            {
                StatusText = "Couldn't open the backup folder: " + ex.Message;
                Log.Error("SceneAnalyzer OpenBackups failed: " + ex);
            }
        }

        public void ExecuteOpenInteriorWhitelist() =>
            InteriorWhitelistLayer.Open(() => StatusText = "Interior whitelist saved - protection updated.");

        public void ExecuteTagInterior()
        {
            try
            {
                if (!TryGetEntities(out _, out var all)) return;
                var (tagged, found, knownGood) = LiveSceneChecks.TagInteriorEntities(all);
                StatusText = found == 0
                    ? "No interior entities found in this scene."
                    : $"Tagged {tagged} of {found} interior entit(y/ies) {LiveSceneChecks.InteriorTag}."
                      + (knownGood > 0 ? $" {knownGood} of them are classed KNOWN-GOOD (worth keeping in MP)." : "");
            }
            catch (Exception ex)
            {
                StatusText = "Tag interior failed: " + ex.Message;
                Log.Error("SceneAnalyzer TagInterior failed: " + ex);
            }
        }

        public void ExecuteUntagInterior()
        {
            try
            {
                if (!TryGetEntities(out _, out var all)) return;
                var removed = LiveSceneChecks.UntagInteriorEntities(all);
                StatusText = removed == 0
                    ? $"No entities carried the {LiveSceneChecks.InteriorTag} tag."
                    : $"Removed {LiveSceneChecks.InteriorTag} from {removed} entit(y/ies).";
            }
            catch (Exception ex)
            {
                StatusText = "Untag interior failed: " + ex.Message;
                Log.Error("SceneAnalyzer UntagInterior failed: " + ex);
            }
        }

        // Destructive and irreversible in-session, so the confirmation names every distinct entity
        // being removed with its count rather than just a total - "delete 43 things" tells you
        // nothing about whether the match caught what you meant.
        public void ExecuteDeleteInteriors()
        {
            try
            {
                if (!TryGetEntities(out _, out var all)) return;
                var preview = LiveSceneChecks.PreviewInteriorEntities(all);
                if (preview.Count == 0)
                {
                    StatusText = "No interior entities found in this scene.";
                    return;
                }

                // Split the preview by what will ACTUALLY be removed. Listing protected entries in
                // the same breath as the doomed ones, under a total that counts both, is how a
                // confirmation ends up lying about its own scope.
                var doomed = preview.Where(p => !p.KnownGood).ToList();
                var kept = preview.Where(p => p.KnownGood).ToList();
                var total = doomed.Sum(p => p.Count);
                var keptCount = kept.Sum(p => p.Count);

                if (total == 0)
                {
                    StatusText = keptCount > 0
                        ? $"Nothing to delete - all {keptCount} interior entit(y/ies) here are classed KNOWN-GOOD and are protected."
                        : "No interior entities found in this scene.";
                    return;
                }

                var lines = doomed.Take(10).Select(p => $"  {p.Name} x{p.Count}");
                var more = doomed.Count > 10 ? $"\n  ...and {doomed.Count - 10} more name(s)" : "";
                var keptNote = keptCount > 0
                    ? $"\n\nPROTECTED - not deleted: {keptCount} entit(y/ies) across {kept.Count} name(s) classed KNOWN-GOOD "
                      + $"(they serve a real purpose in MP): {string.Join(", ", kept.Take(5).Select(k => k.Name))}"
                      + (kept.Count > 5 ? ", ..." : "")
                    : "";

                var inquiry = new InquiryData(
                    $"Delete {total} interior entit(y/ies)?",
                    $"Removes interior entities EXCEPT any classed KNOWN-GOOD in Interior_Entities.txt:\n\n"
                        + string.Join("\n", lines) + more + keptNote
                        + "\n\nDeleting a parent also removes its children. A backup is taken first. This cannot be undone from this tool.",
                    isAffirmativeOptionShown: true, isNegativeOptionShown: true,
                    affirmativeText: "Delete", negativeText: "Cancel",
                    affirmativeAction: () =>
                    {
                        try
                        {
                            if (!TryGetEntities(out _, out var live)) return;
                            var backupPath = BackupManager.BackupNow("before-apply");
                            var protectedNow = LiveSceneChecks.CountProtectedInteriors(live);
                            var deleted = LiveSceneChecks.DeleteInteriorEntities(live);
                            StatusText = $"Deleted {deleted} interior entit(y/ies)."
                                         + (protectedNow > 0 ? $" Left {protectedNow} KNOWN-GOOD one(s) in place." : "")
                                         + (backupPath != null ? $" Backup: {backupPath}" : "  WARNING: no backup was written - see tool.log.");
                        }
                        catch (Exception ex)
                        {
                            StatusText = "Delete interiors failed: " + ex.Message;
                            Log.Error("SceneAnalyzer DeleteInteriors failed: " + ex);
                        }
                    },
                    negativeAction: null);
                InformationManager.ShowInquiry(inquiry);
            }
            catch (Exception ex)
            {
                StatusText = "Delete interiors failed: " + ex.Message;
                Log.Error("SceneAnalyzer DeleteInteriors preview failed: " + ex);
            }
        }

        public void ExecuteTagLocked()
        {
            var inquiry = new InquiryData(
                "Tag locked entities?",
                $"Adds a {LiveSceneChecks.LockedTag} tag to every entity marked non-modifiable in the editor, so you can " +
                "find them by tag search. The lock itself is not changed - this only labels them. Continue?",
                isAffirmativeOptionShown: true, isNegativeOptionShown: true,
                affirmativeText: "Tag", negativeText: "Cancel",
                affirmativeAction: () =>
                {
                    try
                    {
                        if (!TryGetEntities(out _, out var all)) return;
                        var (tagged, locked, error) = LiveSceneChecks.TagLockedEntities(all);
                        StatusText = error != null
                            ? error
                            : $"Tagged {tagged} of {locked} locked entit(y/ies) {LiveSceneChecks.LockedTag}. (Read from the saved scene file - lock the entity and SAVE first, or it won't be seen.)";
                    }
                    catch (Exception ex)
                    {
                        StatusText = "Tag locked failed: " + ex.Message;
                        Log.Error("SceneAnalyzer TagLocked failed: " + ex);
                    }
                },
                negativeAction: null);
            InformationManager.ShowInquiry(inquiry);
        }

        public void ExecuteUntagLocked()
        {
            try
            {
                if (!TryGetEntities(out _, out var all)) return;
                var removed = LiveSceneChecks.UntagLockedEntities(all);
                StatusText = removed == 0
                    ? $"No entities carried the {LiveSceneChecks.LockedTag} tag."
                    : $"Removed {LiveSceneChecks.LockedTag} from {removed} entit(y/ies).";
            }
            catch (Exception ex)
            {
                StatusText = "Untag locked failed: " + ex.Message;
                Log.Error("SceneAnalyzer UntagLocked failed: " + ex);
            }
        }

        public void ExecuteUntagInvisible()
        {
            try
            {
                if (!TryGetEntities(out _, out var all)) return;
                var removed = LiveSceneChecks.UntagInvisibleEntities(all);
                StatusText = removed == 0
                    ? $"No entities carried the {LiveSceneChecks.InvisibleTag} tag."
                    : $"Removed {LiveSceneChecks.InvisibleTag} from {removed} entit(y/ies).";
            }
            catch (Exception ex)
            {
                StatusText = "Untag invisible failed: " + ex.Message;
                Log.Error("SceneAnalyzer UntagInvisible failed: " + ex);
            }
        }

        // Cleanup for the tag above. Needed because a degenerate border_soft hull used to make
        // every entity read as outside, tagging the whole scene with no way to undo it.
        public void ExecuteUntagOutsideBorder()
        {
            var inquiry = new InquiryData(
                "Remove all outside-border tags?",
                $"Removes the {LiveSceneChecks.OutsideBorderTag} tag from every entity in the scene that carries it. " +
                "Only touches that one tag - nothing else about the entities changes. Continue?",
                isAffirmativeOptionShown: true, isNegativeOptionShown: true,
                affirmativeText: "Remove Tags", negativeText: "Cancel",
                affirmativeAction: () =>
                {
                    try
                    {
                        if (!TryGetEntities(out _, out var all)) return;
                        var removed = LiveSceneChecks.UntagEntitiesOutsideBorder(all);
                        StatusText = removed == 0
                            ? $"No entities carried the {LiveSceneChecks.OutsideBorderTag} tag."
                            : $"Removed {LiveSceneChecks.OutsideBorderTag} from {removed} entit(y/ies).";
                    }
                    catch (Exception ex)
                    {
                        StatusText = "Untag outside border failed: " + ex.Message;
                        Log.Error("SceneAnalyzer UntagOutsideBorder failed: " + ex);
                    }
                },
                negativeAction: null);
            InformationManager.ShowInquiry(inquiry);
        }

        public void ExecuteRemovePhysicsOutsideBorder()
        {
            var inquiry = new InquiryData(
                "Remove physics shapes outside the soft border?",
                "Strips collision (RemovePhysics - same mechanism the game itself uses for props that stay visible but " +
                "lose their collider) from every entity currently outside the border_soft boundary (+10 unit margin). " +
                "The entities and their visible meshes are NOT deleted, only their collision. A backup is taken first. Continue?",
                isAffirmativeOptionShown: true, isNegativeOptionShown: true,
                affirmativeText: "Remove Physics", negativeText: "Cancel",
                affirmativeAction: RunRemovePhysicsOutsideBorder,
                negativeAction: null);
            InformationManager.ShowInquiry(inquiry);
        }

        private void RunRemovePhysicsOutsideBorder()
        {
            try
            {
                if (!TryGetEntities(out _, out var all)) return;
                BackupManager.BackupNow("before-apply");
                var (cleared, status) = LiveSceneChecks.RemovePhysicsOutsideBorder(all);
                StatusText = status != null ? status.Message : $"Remove physics outside border: {cleared} entit(y/ies) had collision removed.";
            }
            catch (Exception ex)
            {
                StatusText = "Remove physics outside border failed: " + ex.Message;
                Log.Error("SceneAnalyzer RemovePhysicsOutsideBorder failed: " + ex);
            }
        }

        private void RunMutatingAction(string label, Func<List<GameEntity>, int> action, string countSuffix)
        {
            try
            {
                if (!TryGetEntities(out _, out var all)) return;
                BackupManager.BackupNow("before-apply");
                var count = action(all);
                StatusText = $"{label}: {count} {countSuffix}.";
            }
            catch (Exception ex)
            {
                StatusText = $"{label} failed: " + ex.Message;
                Log.Error($"SceneAnalyzer '{label}' failed: " + ex);
            }
        }

        public void ExecuteApplyLodFixes()
        {
            var inquiry = new InquiryData(
                "Apply LOD substitutions?",
                "Instantly swaps known-inferior LOD entities for their better-LOD-distance replacements in the live scene, " +
                "preserving position/rotation. A backup is taken first. Non-default scale (if any) will not carry over. Continue?",
                isAffirmativeOptionShown: true, isNegativeOptionShown: true,
                affirmativeText: "Apply", negativeText: "Cancel",
                affirmativeAction: RunApplyLodFixes,
                negativeAction: null);
            InformationManager.ShowInquiry(inquiry);
        }

        private void RunApplyLodFixes()
        {
            try
            {
                if (!TryGetEntities(out var scene, out var all)) return;
                BackupManager.BackupNow("before-apply");
                var results = LiveSceneChecks.ApplyLodSubstitutions(scene, all);
                var okCount = results.Count(r => r.Success);
                var failCount = results.Count - okCount;
                StatusText = okCount == 0
                    ? "No known LOD substitutions applied - nothing matched."
                    : $"LOD fixes applied: {okCount} entity/entities swapped live." + (failCount > 0 ? $" ({failCount} failed.)" : "");
            }
            catch (Exception ex)
            {
                StatusText = "LOD fix failed: " + ex.Message;
                Log.Error("SceneAnalyzer LOD fix failed: " + ex);
            }
        }

        private void RunRequirementCheck(string setKey)
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }

                var items = SceneRequirementChecker.Check(setKey);
                if (items.Count == 0)
                {
                    StatusText = $"No requirement definitions found for '{setKey}'.";
                    return;
                }

                var lines = items.Select(i =>
                    i.Kind == "note" ? $"[i] {i.Label}" :
                    i.Found ? $"[OK] {i.Label} (found {i.Count})" : $"[MISSING] {i.Label}").ToList();

                var foundCount = items.Count(i => i.Kind != "note" && i.Found);
                var totalCheckable = items.Count(i => i.Kind != "note");
                var header = $"{SceneRequirementChecker.GetDisplayName(setKey)} - {foundCount}/{totalCheckable} requirements met";

                DiffPreviewLayer.Open(header, lines);
                StatusText = $"Checked '{SceneRequirementChecker.GetDisplayName(setKey)}' - see the results panel.";
            }
            catch (Exception ex)
            {
                StatusText = "Requirement check failed: " + ex.Message;
                Log.Error("SceneAnalyzer requirement check failed: " + ex);
            }
        }
    }
}
