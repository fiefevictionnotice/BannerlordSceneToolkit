using System;
using System.Collections.Generic;
using System.Linq;
using PrefabSwapperTool.Backup;
using PrefabSwapperTool.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace PrefabSwapperTool.GUI
{
    // Swap Sets - a named group of old-prefab -> new-prefab mappings applied together, so a
    // modular structure built from several distinct prefabs (a house's wall/roof/door/window
    // pieces, say) can be swapped to its matching variant in one action instead of one prefab
    // swap at a time. Apply-to-Selection matches each SELECTED entity's current name against the
    // set (not a scene-wide "every X becomes Y", which would also touch every OTHER house reusing
    // the same base pieces) - reuses LivePrefabSwapper.SwapMany and the exact same batch/undo
    // logging RunSwap already writes, so History/Undo Last Swap work on a set application for
    // free, with no separate undo plumbing needed.
    public class SwapSetBrowserVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;

        private string _searchTerm = "";
        private MBBindingList<SwapSetRowVM> _rows;
        private string _statusText = "";

        private string _editNameInput = "";
        private MBBindingList<SwapSetPairRowVM> _editPairs;
        private string _editStatus = "Type a name, Add Rule for each old-prefab -> new-prefab mapping, then Save.";

        public SwapSetBrowserVM(Action closeAction, Action beginDragAction)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            _rows = new MBBindingList<SwapSetRowVM>();
            _editPairs = new MBBindingList<SwapSetPairRowVM>();
            Refresh();
        }

        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();
        public void ExecuteRefresh() => Refresh();
        public void ExecuteOpenTextureOverrides() => TextureSetBrowserLayer.Toggle();

        [DataSourceProperty]
        public string SearchTerm
        {
            get => _searchTerm;
            set { if (value != _searchTerm) { _searchTerm = value; OnPropertyChangedWithValue(value, nameof(SearchTerm)); Refresh(); } }
        }

        [DataSourceProperty]
        public MBBindingList<SwapSetRowVM> Rows
        {
            get => _rows;
            set { if (value != _rows) { _rows = value; OnPropertyChangedWithValue(value, nameof(Rows)); } }
        }

        [DataSourceProperty]
        public string StatusText
        {
            get => _statusText;
            set { if (value != _statusText) { _statusText = value; OnPropertyChangedWithValue(value, nameof(StatusText)); } }
        }

        [DataSourceProperty]
        public string EditNameInput
        {
            get => _editNameInput;
            set { if (value != _editNameInput) { _editNameInput = value; OnPropertyChangedWithValue(value, nameof(EditNameInput)); } }
        }

        [DataSourceProperty]
        public MBBindingList<SwapSetPairRowVM> EditPairs
        {
            get => _editPairs;
            set { if (value != _editPairs) { _editPairs = value; OnPropertyChangedWithValue(value, nameof(EditPairs)); } }
        }

        [DataSourceProperty]
        public string EditStatus
        {
            get => _editStatus;
            set { if (value != _editStatus) { _editStatus = value; OnPropertyChangedWithValue(value, nameof(EditStatus)); } }
        }

        // --- SwapSet import / export ---
        public void ExecuteExportAll()
        {
            try
            {
                var (exported, dir) = SwapSetStore.Export();
                StatusText = exported == 0 ? "Nothing saved to export." : $"Exported {exported} to {dir}";
            }
            catch (Exception ex) { StatusText = "Export failed: " + ex.Message; Log.Error("Export failed: " + ex); }
        }

        public void ExecuteImportAll()
        {
            try
            {
                var (imported, skipped, problems) = SwapSetStore.Import(overwriteExisting: false);
                foreach (var p in problems) Log.Warn("[SwapSet IO] " + p);
                var note = problems.Count == 0 ? "" : "  Issues: " + string.Join("; ", problems.Take(3))
                           + (problems.Count > 3 ? $" (+{problems.Count - 3} more, see tool.log)" : "");
                StatusText = $"Imported {imported}, skipped {skipped}." + note;
                Refresh();
            }
            catch (Exception ex) { StatusText = "Import failed: " + ex.Message; Log.Error("Import failed: " + ex); }
        }

        public void ExecuteOpenExportFolder()
        {
            try { SwapSetStore.OpenExportFolder(); StatusText = "Opened " + SwapSetStore.ExportDir; }
            catch (Exception ex) { StatusText = "Couldn't open the folder: " + ex.Message; }
        }

        private void Refresh()
        {
            Rows.Clear();
            var term = (SearchTerm ?? "").Trim();
            foreach (var name in SwapSetStore.ListNames())
            {
                SwapSet set;
                try { set = SwapSetStore.Load(name); }
                catch { continue; }
                if (term.Length > 0 && set.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0) continue;

                Rows.Add(new SwapSetRowVM(set, RunApply, RunDelete, RunEdit));
            }
            StatusText = Rows.Count == 0 ? "No saved swap sets yet." : $"{Rows.Count} set(s).";
        }

        // Rewritten per direct request: a Swap Set is a plain rule table (old prefab -> new prefab,
        // plus an optional texture override), typed directly - NOT captured by selecting entities
        // in the scene. The earlier "Capture Pair" (select 2 entities, read their names) was left
        // in as a convenience the first time this was built despite being asked to remove it in
        // favor of the rule-table model; this replaces it outright rather than coexisting with it.
        public void ExecuteAddRule() => EditPairs.Add(new SwapSetPairRowVM("", "", null, RemovePairRow, OnCyclePreset));

        private void RemovePairRow(SwapSetPairRowVM row) => EditPairs.Remove(row);

        // Cycles through whatever texture overrides exist for the row's NEW prefab (what the swap
        // actually produces) - one click past the last real option clears it back to None, same
        // convention as every other preset-cycle button in these mods.
        private void OnCyclePreset(SwapSetPairRowVM row)
        {
            var options = ColorPresetStore.ListForBasePrefab(row.NewPrefabName).Select(p => p.Name).ToList();
            if (options.Count == 0) { EditStatus = $"No saved texture overrides for '{row.NewPrefabName}' yet - use the Texture Overrides browser to add one."; return; }

            var currentIdx = options.FindIndex(n => string.Equals(n, row.PresetName, StringComparison.OrdinalIgnoreCase));
            row.PresetName = currentIdx + 1 >= options.Count ? null : options[currentIdx + 1];
        }

        public void ExecuteSaveSet()
        {
            if (string.IsNullOrWhiteSpace(EditNameInput)) { EditStatus = "Enter a name first."; return; }
            var pairs = EditPairs
                .Where(p => !string.IsNullOrWhiteSpace(p.OldPrefabName) && !string.IsNullOrWhiteSpace(p.NewPrefabName))
                .Select(p => new SwapSetPair { OldPrefabName = p.OldPrefabName.Trim(), NewPrefabName = p.NewPrefabName.Trim(), PresetName = p.PresetName })
                .ToList();
            if (pairs.Count == 0) { EditStatus = "Add at least one complete rule (both names filled in) first."; return; }

            var set = new SwapSet { Name = EditNameInput.Trim(), Pairs = pairs };
            SwapSetStore.Save(set);
            EditStatus = $"Saved '{set.Name}' ({set.Pairs.Count} rule(s)).";
            Refresh();
        }

        public void ExecuteClearEditor()
        {
            EditNameInput = "";
            EditPairs.Clear();
            EditStatus = "Cleared - type a name, Add Rule for each mapping, then Save.";
        }

        private void RunEdit(SwapSetRowVM row)
        {
            EditNameInput = row.Name;
            EditPairs.Clear();
            foreach (var pair in row.Set.Pairs)
                EditPairs.Add(new SwapSetPairRowVM(pair.OldPrefabName, pair.NewPrefabName, pair.PresetName, RemovePairRow, OnCyclePreset));
            EditStatus = $"Editing '{row.Name}' - change anything below, then Save (overwrites this same set).";
        }

        private void RunDelete(SwapSetRowVM row)
        {
            var inquiry = new InquiryData(
                "Delete swap set?",
                $"'{row.Name}' will be permanently deleted. This cannot be undone from here.",
                isAffirmativeOptionShown: true, isNegativeOptionShown: true,
                affirmativeText: "Delete", negativeText: "Cancel",
                affirmativeAction: () => { SwapSetStore.Delete(row.Name); Refresh(); },
                negativeAction: null);
            InformationManager.ShowInquiry(inquiry);
        }

        // The actual "change the whole house at once" action: every currently SELECTED entity
        // whose current name matches this set's OldPrefabName gets swapped to the matching
        // NewPrefabName. Entities not covered by the set are left alone and reported, not treated
        // as an error - a selection that includes one extra unrelated entity, or is missing a
        // piece, shouldn't block swapping everything that DOES match.
        private void RunApply(SwapSetRowVM row)
        {
            if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }
            var selection = EntitySelector.GetTargets(SelectionMode.Manual);
            if (selection.Count == 0) { StatusText = "Nothing selected - select the pieces to change first."; return; }

            // FIXED 2026-08-22: this matched on entity.Name ONLY - the display name - which the
            // editor changes behind your back (duplicates get _2/_3, Material Swap renames append
            // _mst, recolor naming appends the material). An instance of exactly the prefab a
            // pair covers failed the match because its display name had drifted ("none of the 1
            // selected entities match a prefab name" on a selected instance of the set's own old
            // prefab - confirmed report). The REAL prefab name (GetPrefabName) is tried first,
            // the raw display name second, and the display name with the editor's trailing
            // _<digits> duplicate suffix stripped as the last resort.
            var matched = new List<(GameEntity entity, string newPrefab, string presetName)>();
            var unmatched = 0;
            foreach (var entity in selection)
            {
                var candidates = new List<string>(3);
                try { var pn = entity.GetPrefabName(); if (!string.IsNullOrWhiteSpace(pn)) candidates.Add(pn.Trim()); } catch { }
                var displayName = entity.Name ?? "";
                if (displayName.Length > 0) candidates.Add(displayName);
                var stripped = System.Text.RegularExpressions.Regex.Replace(displayName, @"_\d+$", "");
                if (stripped.Length > 0 && stripped != displayName) candidates.Add(stripped);

                var pair = row.Set.Pairs.FirstOrDefault(p =>
                    candidates.Any(c => string.Equals(p.OldPrefabName, c, StringComparison.OrdinalIgnoreCase)));
                if (pair != null) matched.Add((entity, pair.NewPrefabName, pair.PresetName));
                else unmatched++;
            }

            if (matched.Count == 0)
            {
                var firstNames = string.Join(", ", selection.Take(3).Select(e =>
                {
                    string pn = null; try { pn = e.GetPrefabName(); } catch { }
                    return string.IsNullOrWhiteSpace(pn) ? $"'{e.Name}' (no prefab)" : $"'{e.Name}' (prefab '{pn}')";
                }));
                StatusText = $"None of the {selection.Count} selected entit(y/ies) match an OLD prefab name in '{row.Name}'. " +
                             $"Selected: {firstNames}. The set's rules map OLD prefab -> NEW prefab; the selection must contain the OLD ones.";
                return;
            }

            bool addMode = LivePrefabSwapper.AddModeKeepOriginals;
            var inquiry = new InquiryData(
                addMode ? "Apply swap set (ADD mode)?" : "Apply swap set?",
                $"'{row.Name}' will {(addMode ? "ADD the mapped prefab at" : "swap")} {matched.Count} of {selection.Count} selected entit(y/ies)" +
                (unmatched > 0 ? $" ({unmatched} selected don't match this set and will be left alone)" : "") +
                (addMode
                    ? ". ADD mode: originals are KEPT and will overlap the new pieces until you delete them - no \"break prefab?\" dialogs. A backup is taken first."
                    : ". Position/rotation are preserved. A backup is taken first."),
                isAffirmativeOptionShown: true, isNegativeOptionShown: true,
                affirmativeText: addMode ? "Add" : "Apply", negativeText: "Cancel",
                affirmativeAction: () => RunSetSwap(row.Name, matched),
                negativeAction: null);
            InformationManager.ShowInquiry(inquiry);
        }

        // Mirrors PrefabSwapperVM.RunSwap's batching/logging exactly (same PrefabSwapHistory +
        // PrefabSwapLogger calls) so a set application shows up in, and can be undone from, the
        // same History flyout / Undo Last Swap button as any other swap - generalized only in that
        // each target gets its OWN new prefab name instead of one shared name for the whole batch.
        // Texture overrides are applied AFTER LivePrefabSwapper.SwapMany, matched back to each
        // result by index (SwapMany processes pairs strictly in order, one result per pair) since
        // the swap engine itself knows nothing about textures - same layering as FamilyAutoPlacer
        // applying a paired secondary's texture only after it's actually been placed.
        private void RunSetSwap(string setName, List<(GameEntity entity, string newPrefab, string presetName)> pairs)
        {
            try
            {
                BackupManager.BackupNow("before-apply");
                var scene = EntitySelector.CurrentScene;
                var sceneName = scene?.GetName() ?? "(unknown scene)";
                var results = LivePrefabSwapper.SwapMany(scene, pairs.Select(p => (p.entity, p.newPrefab)).ToList());

                var textureApplied = 0;
                var textureFailed = 0;
                for (int i = 0; i < results.Count; i++)
                {
                    if (!results[i].Success) continue;
                    var presetName = pairs[i].presetName;
                    if (string.IsNullOrWhiteSpace(presetName)) continue;
                    try
                    {
                        var preset = ColorPresetStore.Load(presetName);
                        ColorPresetApplier.Apply(results[i].NewEntity, preset);
                        textureApplied++;
                    }
                    catch (Exception ex)
                    {
                        textureFailed++;
                        Log.Warn($"SwapSetBrowserVM: texture override '{presetName}' failed for '{pairs[i].newPrefab}': {ex.Message}");
                    }
                }

                var okResults = results.Where(r => r.Success).ToList();
                var failCount = results.Count - okResults.Count;
                var notRemovedCount = okResults.Count(r => r.OriginalNotRemoved);
                var autoPlacedCount = okResults.Sum(r => r.AutoPlacedSecondaries.Count);
                var autoPlaceFailedCount = okResults.Sum(r => r.AutoPlaceFailed.Count);
                var autoPlaceTextureFailedCount = okResults.Sum(r => r.AutoPlaceTextureFailed.Count);

                // Add mode: no removals happened, so swap history's re-instantiate-old undo model
                // doesn't apply - additions go on the created-entities undo, like a distribute run.
                if (LivePrefabSwapper.AddModeKeepOriginals)
                {
                    if (okResults.Count > 0)
                        BannerlordSceneToolkit.EditUndo.CaptureCreated($"Apply swap set '{setName}' - add ({okResults.Count})",
                            okResults.Select(r => r.NewEntity));
                    var addMsg = $"'{setName}': ADDED {okResults.Count} piece(s) at the matched entit(y/ies)' spots - originals KEPT.";
                    if (failCount > 0) addMsg += $" {failCount} failed.";
                    if (textureApplied > 0) addMsg += $" {textureApplied} texture override(s) applied.";
                    if (textureFailed > 0) addMsg += $" {textureFailed} texture override(s) failed.";
                    StatusText = addMsg;
                    return;
                }

                if (okResults.Count > 0)
                {
                    var batchId = PrefabSwapLogger.NewBatchId();
                    PrefabSwapHistory.RecordSessionBatch(batchId, okResults);

                    var logEntries = okResults.Select(r => new PrefabSwapLogEntry
                    {
                        BatchId = batchId,
                        TimestampUtc = DateTime.UtcNow,
                        SceneName = sceneName,
                        OldPrefabName = r.OldPrefabName,
                        NewPrefabName = r.NewPrefabName,
                        PosX = r.Frame.origin.x,
                        PosY = r.Frame.origin.y,
                        PosZ = r.Frame.origin.z,
                        RotForwardX = r.Frame.rotation.f.x,
                        RotForwardY = r.Frame.rotation.f.y,
                        RotForwardZ = r.Frame.rotation.f.z,
                    }).ToList();
                    PrefabSwapLogger.Append(logEntries);
                    ScreenshotManager.CaptureForBatch(batchId);
                }

                var msg = $"Applied '{setName}': swapped {okResults.Count} entit{(okResults.Count == 1 ? "y" : "ies")}.";
                if (notRemovedCount > 0) msg += $" WARNING: {notRemovedCount} original(s) could NOT be removed and still overlap - delete by hand, or use ADD mode.";
                if (failCount > 0) msg += $" {failCount} failed.";
                if (textureApplied > 0) msg += $" Applied texture override(s) to {textureApplied}.";
                if (textureFailed > 0) msg += $" {textureFailed} texture override(s) failed to apply.";
                if (autoPlacedCount > 0) msg += $" Auto-placed {autoPlacedCount} paired secondary(ies).";
                if (autoPlaceFailedCount > 0) msg += $" {autoPlaceFailedCount} auto-place secondary(ies) failed.";
                if (autoPlaceTextureFailedCount > 0) msg += $" {autoPlaceTextureFailedCount} auto-placed secondary(ies) placed but its texture set failed to apply.";
                StatusText = msg;
            }
            catch (Exception ex)
            {
                StatusText = "Apply failed: " + ex.Message;
                Log.Error("SwapSetBrowserVM.RunSetSwap failed: " + ex);
            }
        }
    }
}
