using System;
using System.Collections.Generic;
using System.Linq;
using MaterialSwapTool.Core;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    // Lets you see and edit culture definitions live - both the strict "Unique" markers tag
    // inference uses, and the broader "Common" material list the Culture Preset Generator's
    // stub-generation feature uses. Also where a brand new custom culture gets added (just type
    // a name into a new row) - not limited to the 6 built-in ones.
    public class CultureEditorVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;
        private readonly Action _onSaved;
        private MBBindingList<CultureEditItemVM> _rows;
        private string _testMaterialInput = "";
        private string _testResultText = "";
        private string _statusText = "Edit patterns/materials, Test a material name to check Unique matches, then Save.";

        public CultureEditorVM(Action closeAction, Action beginDragAction, Action onSaved)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            _onSaved = onSaved;
            _rows = new MBBindingList<CultureEditItemVM>();
            Refresh();
        }

        // --- Culture import / export ---
        public void ExecuteExportAll()
        {
            try
            {
                var (exported, dir) = CultureMaterialInference.Export();
                StatusText = exported == 0 ? "Nothing saved to export." : $"Exported {exported} to {dir}";
            }
            catch (Exception ex) { StatusText = "Export failed: " + ex.Message; Log.Error("Export failed: " + ex); }
        }

        public void ExecuteImportAll()
        {
            try
            {
                var (imported, skipped, problems) = CultureMaterialInference.Import(overwriteExisting: false);
                foreach (var p in problems) Log.Warn("[Culture IO] " + p);
                var note = problems.Count == 0 ? "" : "  Issues: " + string.Join("; ", problems.Take(3))
                           + (problems.Count > 3 ? $" (+{problems.Count - 3} more, see tool.log)" : "");
                StatusText = $"Imported {imported}, skipped {skipped}." + note;
                CultureMaterialInference.Reload(); Refresh();
            }
            catch (Exception ex) { StatusText = "Import failed: " + ex.Message; Log.Error("Import failed: " + ex); }
        }

        public void ExecuteOpenExportFolder()
        {
            try { CultureMaterialInference.OpenExportFolder(); StatusText = "Opened " + CultureMaterialInference.ExportDir; }
            catch (Exception ex) { StatusText = "Couldn't open the folder: " + ex.Message; }
        }

        private void Refresh()
        {
            Rows.Clear();
            foreach (var kvp in CultureMaterialInference.GetAllForEditing().OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                Rows.Add(new CultureEditItemVM(kvp.Key, string.Join(", ", kvp.Value.Unique), string.Join(", ", kvp.Value.Common), RemoveRow));
        }

        private void RemoveRow(CultureEditItemVM row)
        {
            if (Rows.Contains(row)) Rows.Remove(row);
        }

        [DataSourceProperty]
        public MBBindingList<CultureEditItemVM> Rows
        {
            get => _rows;
            set { if (value != _rows) { _rows = value; OnPropertyChangedWithValue(value, nameof(Rows)); } }
        }

        [DataSourceProperty]
        public string TestMaterialInput
        {
            get => _testMaterialInput;
            set
            {
                if (value != _testMaterialInput)
                {
                    _testMaterialInput = value;
                    OnPropertyChangedWithValue(value, nameof(TestMaterialInput));
                    RunTest();
                }
            }
        }

        [DataSourceProperty]
        public string TestResultText
        {
            get => _testResultText;
            set { if (value != _testResultText) { _testResultText = value; OnPropertyChangedWithValue(value, nameof(TestResultText)); } }
        }

        [DataSourceProperty]
        public string StatusText
        {
            get => _statusText;
            set { if (value != _statusText) { _statusText = value; OnPropertyChangedWithValue(value, nameof(StatusText)); } }
        }

        // Tests against the CURRENT UNSAVED rows' Unique lists only (Common isn't used for
        // inference) - same reasoning as the category editor's live test field.
        private void RunTest()
        {
            var lower = (TestMaterialInput ?? "").Trim().ToLowerInvariant();
            if (lower.Length == 0) { TestResultText = ""; return; }

            var matches = new List<string>();
            foreach (var row in Rows)
            {
                var patterns = SplitList(row.UniqueText);
                if (patterns.Any(p => lower.Contains(p)))
                    matches.Add(row.Name);
            }
            TestResultText = matches.Count > 0 ? "Unique match: " + string.Join(", ", matches) : "No culture's Unique list matches.";
        }

        private static List<string> SplitList(string text) =>
            (text ?? "").Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).ToList();

        // --- Import a culture's material set from a preset ---
        //
        // Type part of a preset's name, pick from the matches, and its LEFT side (FromMaterial)
        // is merged into the target culture's Common list. Left side because that is "the
        // materials this preset operates on" - for a Culture Generator stub (CultureMaterial ->
        // blank) that IS the culture's own set. Right side is offered too, because a built-in is
        // authored Empire -> Culture, where the culture's palette is the TARGET side; importing
        // the left of one of those would give you Empire's materials instead.
        private string _presetSearchTerm = "";
        private string _importTargetCulture = "";
        private bool _importRightSide;
        private MBBindingList<CulturePickItemVM> _presetMatches = new MBBindingList<CulturePickItemVM>();

        [DataSourceProperty]
        public string PresetSearchTerm
        {
            get => _presetSearchTerm;
            set
            {
                if (value != _presetSearchTerm)
                {
                    _presetSearchTerm = value;
                    OnPropertyChangedWithValue(value, nameof(PresetSearchTerm));
                    RefreshPresetMatches();
                }
            }
        }

        [DataSourceProperty]
        public string ImportTargetCulture
        {
            get => _importTargetCulture;
            set { if (value != _importTargetCulture) { _importTargetCulture = value; OnPropertyChangedWithValue(value, nameof(ImportTargetCulture)); } }
        }

        [DataSourceProperty]
        public MBBindingList<CulturePickItemVM> PresetMatches
        {
            get => _presetMatches;
            set { if (value != _presetMatches) { _presetMatches = value; OnPropertyChangedWithValue(value, nameof(PresetMatches)); } }
        }

        [DataSourceProperty]
        public string ImportSideLabel => _importRightSide ? "Side: RIGHT (to-material)" : "Side: LEFT (from-material)";

        public void ExecuteToggleImportSide()
        {
            _importRightSide = !_importRightSide;
            OnPropertyChanged(nameof(ImportSideLabel));
            StatusText = _importRightSide
                ? "Importing the TO side - right for a built-in authored as Empire -> Culture."
                : "Importing the FROM side - right for a Culture Generator stub (Culture -> blank).";
        }

        private void RefreshPresetMatches()
        {
            PresetMatches.Clear();
            var term = (_presetSearchTerm ?? "").Trim();
            if (term.Length == 0) return;
            foreach (var p in MaterialSwapPreset.ListPresetSummaries()
                         .Where(p => p.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                         .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                         .Take(20))
            {
                PresetMatches.Add(new CulturePickItemVM(p.Name, ImportFromPreset));
            }
        }

        private void ImportFromPreset(string presetName)
        {
            try
            {
                var target = (ImportTargetCulture ?? "").Trim();
                if (target.Length == 0) { StatusText = "Type which culture to import INTO first."; return; }

                var row = Rows.FirstOrDefault(r => string.Equals((r.Name ?? "").Trim(), target, StringComparison.OrdinalIgnoreCase));
                if (row == null) { StatusText = $"No culture named '{target}' in the list - add it first, or check the spelling."; return; }

                var mats = CulturePresetGenerator.ExtractPresetSide(presetName, _importRightSide);
                if (mats.Count == 0) { StatusText = $"'{presetName}' has no materials on that side."; return; }

                // Merge, don't replace - importing a second preset should build the set up rather
                // than discard what a previous import or hand-editing already put there.
                var existing = SplitList(row.CommonText);
                var added = mats.Where(m => !existing.Contains(m.ToLowerInvariant())).ToList();
                var merged = SplitList(row.CommonText).Count == 0
                    ? mats
                    : row.CommonText.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Concat(added).ToList();

                row.CommonText = string.Join(", ", merged);
                StatusText = $"Imported {added.Count} new material(s) into '{target}' from '{presetName}' ({(_importRightSide ? "right" : "left")} side). " +
                             $"{mats.Count - added.Count} already present. Not saved yet - press Save.";
            }
            catch (Exception ex)
            {
                StatusText = "Import failed: " + ex.Message;
                Log.Error("CultureEditor ImportFromPreset failed: " + ex);
            }
        }

        public void ExecuteAddCulture()
        {
            Rows.Add(new CultureEditItemVM("new_culture", "", "", RemoveRow));
        }

        public void ExecuteSave()
        {
            var dict = new Dictionary<string, CultureDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in Rows)
            {
                var name = (row.Name ?? "").Trim();
                if (name.Length == 0) continue;
                dict[name] = new CultureDefinition
                {
                    Unique = SplitList(row.UniqueText),
                    // Common is exact material names, not lowercased patterns - keep original casing.
                    Common = (row.CommonText ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList(),
                };
            }

            CultureMaterialInference.Save(dict);
            CultureMaterialInference.Reload();
            _onSaved?.Invoke();
            Refresh();
            StatusText = $"Saved {dict.Count} culture(s) - the stub generator's culture list has been refreshed.";
        }

        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();
    }
}
