using System;
using System.Collections.Generic;
using System.Linq;
using MaterialSwapTool.Core;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    // Lets you see and edit Continuous Recolor's material-category patterns live, from inside the
    // editor, instead of needing a code change + DLL redeploy every time a real material name
    // doesn't match the category it obviously should (the original ask that started this: empire_
    // wall_a not matching "stone"). Include/exclude patterns are comma-separated substrings,
    // matched case-insensitively against the material name - same rules MaterialCategoryInference
    // itself uses, so what you test here is exactly what Continuous Recolor will do.
    public class CategoryEditorVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;
        private readonly Action _onSaved;
        private MBBindingList<CategoryEditItemVM> _rows;
        private string _testMaterialInput = "";
        private string _testResultText = "";
        private string _statusText = "Edit patterns, Test a material name to check them, then Save.";

        public CategoryEditorVM(Action closeAction, Action beginDragAction, Action onSaved)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            _onSaved = onSaved;
            _rows = new MBBindingList<CategoryEditItemVM>();
            Refresh();
        }

        private void Refresh()
        {
            Rows.Clear();
            foreach (var kvp in MaterialCategoryInference.GetAllForEditing().OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                Rows.Add(new CategoryEditItemVM(kvp.Key, string.Join(", ", kvp.Value.Include), string.Join(", ", kvp.Value.Exclude), RemoveRow));
        }

        private void RemoveRow(CategoryEditItemVM row)
        {
            if (Rows.Contains(row)) Rows.Remove(row);
        }

        [DataSourceProperty]
        public MBBindingList<CategoryEditItemVM> Rows
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

        // Tests against the CURRENT UNSAVED rows, not the live-loaded categories - so you can
        // check whether an edit actually fixes what you think it fixes before committing it.
        private void RunTest()
        {
            var lower = (TestMaterialInput ?? "").Trim().ToLowerInvariant();
            if (lower.Length == 0) { TestResultText = ""; return; }

            var matches = new List<string>();
            foreach (var row in Rows)
            {
                var includes = SplitPatterns(row.IncludeText);
                var excludes = SplitPatterns(row.ExcludeText);
                bool included = includes.Any(p => lower.Contains(p));
                if (!included) continue;
                bool excluded = excludes.Any(p => lower.Contains(p));
                if (!excluded) matches.Add(row.Name);
            }
            TestResultText = matches.Count > 0 ? "Matches: " + string.Join(", ", matches) : "No category matches.";
        }

        private static List<string> SplitPatterns(string text) =>
            (text ?? "").Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).ToList();

        public void ExecuteAddCategory()
        {
            Rows.Add(new CategoryEditItemVM("new_category", "", "", RemoveRow));
        }

        public void ExecuteSave()
        {
            var dict = new Dictionary<string, CategoryDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in Rows)
            {
                var name = (row.Name ?? "").Trim();
                if (name.Length == 0) continue;
                dict[name] = new CategoryDefinition
                {
                    Include = SplitPatterns(row.IncludeText),
                    Exclude = SplitPatterns(row.ExcludeText),
                };
            }

            MaterialCategoryInference.Save(dict);
            MaterialCategoryInference.Reload();
            _onSaved?.Invoke();
            Refresh();
            StatusText = $"Saved {dict.Count} categories - Continuous Recolor's category list has been refreshed.";
        }

        // --- Import / export (v0.7) - MaterialSwapTool\Exports\Categories ---
        //
        // Export writes the SAVED categories, not the unsaved rows on screen - exporting a
        // half-edited state you might then discard would be a trap. Import merges (new
        // categories added, existing ones gain missing patterns, nothing removed - see
        // MaterialCategoryInference.Import) and then refreshes the rows, so unsaved edits in
        // the panel are replaced by the merged result; Save first if they matter.
        public void ExecuteExportCategories()
        {
            try
            {
                var (count, dir) = MaterialCategoryInference.Export();
                StatusText = count == 0 ? "No categories to export." : $"Exported {count} categories to {dir}";
            }
            catch (Exception ex)
            {
                StatusText = "Category export failed: " + ex.Message;
                Log.Error("ExportCategories failed: " + ex);
            }
        }

        public void ExecuteImportCategories()
        {
            try
            {
                var (categoriesAdded, patternsAdded, problems) = MaterialCategoryInference.Import();
                foreach (var p in problems) Log.Warn("[CategoryIO] " + p);
                _onSaved?.Invoke();
                Refresh();
                var note = problems.Count == 0 ? "" : "  Issues: " + string.Join("; ", problems.Take(3))
                           + (problems.Count > 3 ? $" (+{problems.Count - 3} more, see tool.log)" : "");
                StatusText = categoriesAdded == 0 && patternsAdded == 0 && problems.Count == 0
                    ? "Nothing new to import - every category and pattern was already present."
                    : $"Imported {categoriesAdded} new categories, {patternsAdded} new patterns (merged, nothing removed)." + note;
            }
            catch (Exception ex)
            {
                StatusText = "Category import failed: " + ex.Message;
                Log.Error("ImportCategories failed: " + ex);
            }
        }

        public void ExecuteOpenCategoryFolder()
        {
            try { MaterialCategoryInference.OpenExportFolder(); StatusText = "Opened " + MaterialCategoryInference.ExportDir; }
            catch (Exception ex) { StatusText = "Couldn't open the folder: " + ex.Message; }
        }

        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();
    }
}
