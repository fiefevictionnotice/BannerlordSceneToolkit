using System;
using System.Collections.Generic;
using System.Linq;
using MaterialSwapTool.Core;
using TaleWorlds.Library;
using BannerlordSceneToolkit;

namespace MaterialSwapTool.GUI
{
    // Live category-based recolor: toggle which categories are "in scope," set a color, and it
    // applies immediately to your current selection - then stays armed and re-applies to whatever
    // you select next, until you disarm it. Every re-application is triggered by a discrete event
    // (color edited, category toggled, or - from the tick patch - a click-triggered selection
    // change while armed), never a per-tick timer. See ContinuousRecolorEngine for why.
    public class ContinuousRecolorVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;
        private readonly List<CategoryToggleVM> _allCategories = new List<CategoryToggleVM>();
        private MBBindingList<CategoryToggleVM> _categoriesColumn1;
        private MBBindingList<CategoryToggleVM> _categoriesColumn2;
        private MBBindingList<CategoryToggleVM> _categoriesColumn3;
        private string _colorInput = "";
        private bool _isArmed;
        private string _statusText = "Toggle categories, set a color, then Arm.";
        private string _paletteNameInput = "";
        private MBBindingList<CulturePickItemVM> _savedPalettes;

        public ContinuousRecolorVM(Action closeAction, Action beginDragAction)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            _categoriesColumn1 = new MBBindingList<CategoryToggleVM>();
            _categoriesColumn2 = new MBBindingList<CategoryToggleVM>();
            _categoriesColumn3 = new MBBindingList<CategoryToggleVM>();
            _savedPalettes = new MBBindingList<CulturePickItemVM>();
            RebuildCategoryColumns();
            RefreshSavedPalettes();
        }

        // Re-populates the 3 columns from MaterialCategoryInference.AllCategories - called at
        // construction and again after the category editor saves changes, so a newly added/
        // removed/renamed category shows up immediately without reopening this panel.
        public void RebuildCategoryColumns()
        {
            var previouslyOn = new HashSet<string>(
                _allCategories.Where(c => c.IsOn).Select(c => c.Category), StringComparer.OrdinalIgnoreCase);

            _allCategories.Clear();
            CategoriesColumn1.Clear();
            CategoriesColumn2.Clear();
            CategoriesColumn3.Clear();

            // Round-robin across 3 columns instead of one long list - keeps the panel roughly a
            // third as tall regardless of how many categories exist, rather than growing linearly.
            var columns = new[] { CategoriesColumn1, CategoriesColumn2, CategoriesColumn3 };
            int index = 0;
            foreach (var category in MaterialCategoryInference.AllCategories)
            {
                var toggle = new CategoryToggleVM(category, ReapplyIfArmed) { IsOn = previouslyOn.Contains(category) };
                _allCategories.Add(toggle);
                columns[index % 3].Add(toggle);
                index++;
            }
        }

        [DataSourceProperty]
        public MBBindingList<CategoryToggleVM> CategoriesColumn1
        {
            get => _categoriesColumn1;
            set { if (value != _categoriesColumn1) { _categoriesColumn1 = value; OnPropertyChangedWithValue(value, nameof(CategoriesColumn1)); } }
        }

        [DataSourceProperty]
        public MBBindingList<CategoryToggleVM> CategoriesColumn2
        {
            get => _categoriesColumn2;
            set { if (value != _categoriesColumn2) { _categoriesColumn2 = value; OnPropertyChangedWithValue(value, nameof(CategoriesColumn2)); } }
        }

        [DataSourceProperty]
        public MBBindingList<CategoryToggleVM> CategoriesColumn3
        {
            get => _categoriesColumn3;
            set { if (value != _categoriesColumn3) { _categoriesColumn3 = value; OnPropertyChangedWithValue(value, nameof(CategoriesColumn3)); } }
        }

        // Typing here fills every currently-ON row's own color, silently - the original single-
        // shared-color workflow ("type a color, toggle categories, arm") still works, it's just a
        // bulk-fill into the per-row palette model now rather than a separate code path.
        //
        // Deliberately does NOT reapply on every keystroke anymore (it used to, and so did each
        // row's own box) - that meant every character typed re-scanned the whole selection and
        // rewrote every matched mesh's color, and with several categories armed each keystroke also
        // wrote into every one of their boxes first. That rapid, repeated full-scene mutation is the
        // confirmed cause of two in-editor crashes (access violation in Qt5Core.dll, the editor's
        // own Qt-based shell - not this mod's GauntletUI overlay - at the identical fault offset
        // both times). Reapply now only happens on discrete events: toggling a category, arming,
        // Apply Colors below, or selecting something new while armed.
        [DataSourceProperty]
        public string ColorInput
        {
            get => _colorInput;
            set
            {
                if (value != _colorInput)
                {
                    _colorInput = value;
                    OnPropertyChangedWithValue(value, nameof(ColorInput));
                    foreach (var row in _allCategories.Where(c => c.IsOn))
                        row.SetColorSilent(value);
                }
            }
        }

        // Explicit trigger for whatever's currently typed into the per-row/shared color boxes -
        // needed now that typing itself no longer reapplies (see ColorInput above).
        // SEEDED SAMPLING: "Get Input from Selection".
        //
        // The Material Swap tool's equivalent seeds RULES from a selection. This tool has no
        // rules - its inputs are which categories are on and what colour each one carries - so the
        // analogue is: switch on the categories present on what you selected, and preload each
        // one's colour from what is already painted there.
        //
        // WHY SEEDED RANDOM RATHER THAN "MOST COMMON". A category routinely has several different
        // colours across a selection (three stone materials, three tints). Always picking the
        // dominant one makes this button return the same answer forever, which is useless for
        // exploring a palette. Each press advances a seed and picks a different combination, so
        // pressing it four times gives four coherent sets rather than four copies of one. The seed
        // is shown, and can be typed back in, so a set you liked is reproducible instead of lost.
        private int _sampleSeed;

        [DataSourceProperty]
        public string SampleSeedInput
        {
            get => _sampleSeedInput;
            set { if (value != _sampleSeedInput) { _sampleSeedInput = value; OnPropertyChangedWithValue(value, nameof(SampleSeedInput)); } }
        }
        private string _sampleSeedInput = "";

        public void ExecuteSampleFromSelection()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }

                var selected = EntitySelector.GetTargets(SelectionMode.Manual);
                if (selected.Count == 0) { StatusText = "Nothing selected in the editor."; return; }

                var sample = ContinuousRecolorEngine.SampleSelection(selected);
                if (sample.SlotsByCategory.Count == 0)
                {
                    StatusText = sample.UncategorizedMaterials.Count > 0
                        ? $"None of the {sample.UncategorizedMaterials.Count} material(s) on that selection match any category. Add them in Edit Categories."
                        : "No categorized materials found on that selection.";
                    return;
                }

                // An explicitly typed seed reproduces a previous set; otherwise advance so each
                // press is a new combination.
                if (int.TryParse((_sampleSeedInput ?? "").Trim(), out var typed) && typed != _sampleSeed)
                    _sampleSeed = typed;
                else
                    _sampleSeed++;

                var rng = new Random(_sampleSeed);

                // Iterate in a fixed order so one seed always yields the same set - iterating a
                // Dictionary directly would let hash order vary the draw sequence and break that.
                var categories = sample.SlotsByCategory.Keys.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();

                int turnedOn = 0, colored = 0, hadChoice = 0;
                foreach (var category in categories)
                {
                    var toggle = _allCategories.FirstOrDefault(
                        t => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase));
                    if (toggle == null) continue;

                    if (!toggle.IsOn) { toggle.IsOn = true; turnedOn++; }

                    if (!sample.ColorsByCategory.TryGetValue(category, out var counts) || counts.Count == 0)
                        continue;   // category present but everything in it is untinted

                    // Uniform over DISTINCT colours, not weighted by slot count: weighting would
                    // keep returning the dominant tint and defeat the point of re-rolling.
                    var choices = counts.Keys.OrderBy(c => c).ToList();
                    if (choices.Count > 1) hadChoice++;
                    var picked = choices[rng.Next(choices.Count)];

                    // Silent: neither IsOn nor SetColorSilent triggers a reapply, so seeding many
                    // rows cannot fire the rapid back-to-back recolor passes that crashed the
                    // editor's Qt shell before (see CategoryToggleVM.ColorInput).
                    toggle.SetColorSilent(ColorHex.ToHex(picked));
                    colored++;
                }

                SampleSeedInput = _sampleSeed.ToString();

                var parts = new List<string>
                {
                    $"Seed {_sampleSeed}: {categories.Count} categor{(categories.Count == 1 ? "y" : "ies")} on this selection",
                };
                if (turnedOn > 0) parts.Add($"{turnedOn} newly switched on");
                parts.Add($"{colored} colour(s) seeded");
                if (hadChoice > 0) parts.Add($"{hadChoice} had multiple colours - press again to re-roll");
                if (sample.UncategorizedMaterials.Count > 0)
                    parts.Add($"{sample.UncategorizedMaterials.Count} material(s) matched no category");

                StatusText = string.Join("; ", parts) + ". Click Apply Colors when you like it.";
                Log.Info($"[SampleSelection] seed={_sampleSeed} categories={categories.Count} colored={colored} " +
                         $"multi={hadChoice} uncategorized={sample.UncategorizedMaterials.Count}");
            }
            catch (Exception ex)
            {
                StatusText = "Get Input from Selection failed: " + ex.Message;
                Log.Error("SampleFromSelection failed: " + ex);
            }
        }

        // ONE-SHOT apply, independent of the armed state (2026-08-23, "the apply color button
        // is pointless since you can't click it during the disarmed state?" - it used to route
        // through ReapplyIfArmed, whose disarmed early-return made the button dead exactly when
        // a manual button is useful; armed mode already recolors on selection by itself). Now:
        // armed = continuous, this button = recolor the current selection once, armed or not.
        public void ExecuteApplyColors() => ApplyToCurrentSelection();

        [DataSourceProperty]
        public string PaletteNameInput
        {
            get => _paletteNameInput;
            set { if (value != _paletteNameInput) { _paletteNameInput = value; OnPropertyChangedWithValue(value, nameof(PaletteNameInput)); } }
        }

        [DataSourceProperty]
        public MBBindingList<CulturePickItemVM> SavedPalettes
        {
            get => _savedPalettes;
            set { if (value != _savedPalettes) { _savedPalettes = value; OnPropertyChangedWithValue(value, nameof(SavedPalettes)); } }
        }

        [DataSourceProperty]
        public bool IsArmed
        {
            get => _isArmed;
            set { if (value != _isArmed) { _isArmed = value; OnPropertyChangedWithValue(value, nameof(IsArmed)); } }
        }

        [DataSourceProperty]
        public string StatusText
        {
            get => _statusText;
            set { if (value != _statusText) { _statusText = value; OnPropertyChangedWithValue(value, nameof(StatusText)); } }
        }

        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();
        public void ExecuteOpenCategoryEditor() => CategoryEditorLayer.Open(RebuildCategoryColumns);
        public void ExecuteOpenDocumentation() => DocumentationLayer.Toggle();

        public void ExecuteToggleArmed()
        {
            IsArmed = !IsArmed;
            if (IsArmed) ReapplyIfArmed();
            else StatusText = "Disarmed - selecting entities won't recolor them until you arm again.";
        }

        // Public so the tick patch can call it on a click-triggered selection change while armed -
        // same idempotent-safe pattern as everything else here, so calling it redundantly is
        // always harmless.
        //
        // Always goes through ApplyPalette now (Part B's engine) even for the plain single-color
        // workflow - each ON row supplies its OWN color rather than one shared color for every
        // armed category, which is a strict superset of the old single-color behavior (ColorInput's
        // setter above fills every ON row with the same value, so "one shared color" still works,
        // it's just no longer a separate code path).
        public void ReapplyIfArmed()
        {
            if (!IsArmed) return;
            ApplyToCurrentSelection();
        }

        // The actual apply, shared by the continuous (armed) path and the one-shot button.
        private void ApplyToCurrentSelection()
        {
            if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }

            var onRows = _allCategories.Where(c => c.IsOn).ToList();
            if (onRows.Count == 0)
            {
                StatusText = "Toggle at least one category first.";
                return;
            }

            var categoryColors = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            var badRows = new List<string>();
            foreach (var row in onRows)
            {
                if (ColorHex.TryParse(row.ColorInput, out var color)) categoryColors[row.Category] = color;
                else badRows.Add(row.Category);
            }

            if (categoryColors.Count == 0)
            {
                StatusText = "Enter a valid color first (#RRGGBB, #RRGGBBAA, or clear/white/none to reset).";
                return;
            }

            var targets = EntitySelector.GetTargets(Core.SelectionMode.Manual);
            if (targets.Count == 0)
            {
                StatusText = "Nothing selected.";
                return;
            }

            var batchId = ChangeLogger.NewBatchId();
            var entries = new List<ChangeLogEntry>();
            var sceneName = EntitySelector.CurrentSceneName;
            var result = ContinuousRecolorEngine.ApplyPalette(targets, categoryColors, batchId, entries, sceneName);

            if (entries.Count > 0)
                ChangeLogger.Append(entries);

            var skippedNote = badRows.Count > 0 ? $" ({badRows.Count} row(s) skipped - no valid color set: {string.Join("/", badRows)})" : "";

            // No more "mixed materials, skipped" case - each mesh slot is now recolored (or not)
            // independently via Mesh.Color, so a mixed building no longer blocks the slots that
            // DO match just because siblings in the same MetaMesh don't.
            StatusText = result.SlotsColored == 0
                ? $"{(IsArmed ? "Armed" : "One-shot")} - {targets.Count} entit{(targets.Count == 1 ? "y" : "ies")} selected, nothing matched {string.Join("/", categoryColors.Keys)}.{skippedNote}"
                : $"Recolored {result.SlotsColored} slot(s) across {targets.Count} entit{(targets.Count == 1 ? "y" : "ies")}.{skippedNote}";
        }

        // ============================================================
        // Part B: palette save/load - captures every currently-ON row's own color under a name,
        // and restoring it later resets the ON/OFF + color state of every row to match exactly
        // (rows not in the saved palette get switched OFF), so loading a palette is a full replace
        // of the current arm configuration, not a merge.
        // ============================================================

        private void RefreshSavedPalettes()
        {
            SavedPalettes.Clear();
            foreach (var name in RecolorPalette.ListNames())
                SavedPalettes.Add(new CulturePickItemVM(name, LoadPaletteByName));
        }

        // --- Palette import / export (same Exports folder as presets, Palettes subfolder) ---
        public void ExecuteExportPalettes()
        {
            try
            {
                var (exported, dir) = RecolorPalette.Export();
                StatusText = exported == 0 ? "No saved palettes to export." : $"Exported {exported} palette(s) to {dir}";
            }
            catch (Exception ex)
            {
                StatusText = "Palette export failed: " + ex.Message;
                Log.Error("ExportPalettes failed: " + ex);
            }
        }

        public void ExecuteImportPalettes()
        {
            try
            {
                var (imported, skipped, problems) = RecolorPalette.Import(overwriteExisting: false);
                foreach (var p in problems) Log.Warn("[PaletteIO] " + p);
                var note = problems.Count == 0 ? "" : "  Issues: " + string.Join("; ", problems.Take(3))
                           + (problems.Count > 3 ? $" (+{problems.Count - 3} more, see tool.log)" : "");
                StatusText = $"Imported {imported} palette(s), skipped {skipped}." + note;
                RefreshSavedPalettes();
            }
            catch (Exception ex)
            {
                StatusText = "Palette import failed: " + ex.Message;
                Log.Error("ImportPalettes failed: " + ex);
            }
        }

        public void ExecuteOpenPaletteFolder()
        {
            try { RecolorPalette.OpenExportFolder(); StatusText = "Opened " + RecolorPalette.ExportDir; }
            catch (Exception ex) { StatusText = "Couldn't open the folder: " + ex.Message; }
        }

        public void ExecuteSavePalette()
        {
            var name = (PaletteNameInput ?? "").Trim();
            if (name.Length == 0) { StatusText = "Enter a palette name first."; return; }

            var onRows = _allCategories.Where(c => c.IsOn && ColorHex.TryParse(c.ColorInput, out _)).ToList();
            if (onRows.Count == 0) { StatusText = "Toggle at least one category with a valid color first."; return; }

            var palette = new RecolorPalette { Name = name };
            foreach (var row in onRows)
                palette.CategoryColors[row.Category] = row.ColorInput;

            try
            {
                palette.Save();
                RefreshSavedPalettes();
                StatusText = $"Saved palette '{name}' ({onRows.Count} categor{(onRows.Count == 1 ? "y" : "ies")}).";
            }
            catch (Exception ex)
            {
                StatusText = "Save palette failed: " + ex.Message;
                Log.Error("ContinuousRecolor save palette failed: " + ex);
            }
        }

        private void LoadPaletteByName(string name)
        {
            try
            {
                var palette = RecolorPalette.Load(name);
                foreach (var row in _allCategories)
                {
                    if (palette.CategoryColors.TryGetValue(row.Category, out var hex))
                    {
                        row.IsOn = true;
                        row.SetColorSilent(hex);
                    }
                    else
                    {
                        row.IsOn = false;
                    }
                }
                PaletteNameInput = name;
                StatusText = $"Loaded palette '{name}' ({palette.CategoryColors.Count} categor{(palette.CategoryColors.Count == 1 ? "y" : "ies")}).";
                ReapplyIfArmed();
            }
            catch (Exception ex)
            {
                StatusText = "Load palette failed: " + ex.Message;
                Log.Error("ContinuousRecolor load palette failed: " + ex);
            }
        }

        public void ExecuteDeleteSelectedPalette()
        {
            var name = (PaletteNameInput ?? "").Trim();
            if (name.Length == 0) { StatusText = "Enter (or click) a palette name first."; return; }
            RecolorPalette.Delete(name);
            RefreshSavedPalettes();
            StatusText = $"Deleted palette '{name}'.";
        }
    }
}
