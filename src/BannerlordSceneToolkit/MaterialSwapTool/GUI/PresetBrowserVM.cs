using System;
using System.Collections.Generic;
using System.Linq;
using MaterialSwapTool.Core;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    public class PresetBrowserVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;
        private readonly Action<string, PresetLoadMode, bool> _onChosen;
        private List<PresetSummary> _allSummaries = new List<PresetSummary>();
        private MBBindingList<PresetBrowserItemVM> _presets;
        private string _statusText;
        private string _searchTerm = "";
        private string _tagFilterTerm = "";
        private string _materialFilterTerm = "";
        private PresetLoadMode _loadMode = PresetLoadMode.Overwrite;
        private string _modeLabel = "Mode: Overwrite";
        private bool _invertMode;
        private string _invertLabel = "Invert: Off";

        public PresetBrowserVM(Action closeAction, Action beginDragAction, Action<string, PresetLoadMode, bool> onChosen)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            _onChosen = onChosen;
            _presets = new MBBindingList<PresetBrowserItemVM>();
            Refresh();
        }

        [DataSourceProperty]
        public MBBindingList<PresetBrowserItemVM> Presets
        {
            get => _presets;
            set { if (value != _presets) { _presets = value; OnPropertyChangedWithValue(value, nameof(Presets)); } }
        }

        [DataSourceProperty]
        public string StatusText
        {
            get => _statusText;
            set { if (value != _statusText) { _statusText = value; OnPropertyChangedWithValue(value, nameof(StatusText)); } }
        }

        // Matches preset NAME only now - tags and materials each got their own dedicated field
        // below, so every box asks exactly one question instead of overlapping.
        [DataSourceProperty]
        public string SearchTerm
        {
            get => _searchTerm;
            set
            {
                if (value != _searchTerm)
                {
                    _searchTerm = value;
                    OnPropertyChangedWithValue(value, nameof(SearchTerm));
                    ApplyFilter();
                }
            }
        }

        // Matches only tags (manual + inferred, both live in the same stored Tags list).
        [DataSourceProperty]
        public string TagFilterTerm
        {
            get => _tagFilterTerm;
            set
            {
                if (value != _tagFilterTerm)
                {
                    _tagFilterTerm = value;
                    OnPropertyChangedWithValue(value, nameof(TagFilterTerm));
                    ApplyFilter();
                }
            }
        }

        // Matches only the FROM side of a preset's rules - "which presets touch adobe_wall_2".
        // All three boxes combine via AND: every one that's filled in has to match.
        [DataSourceProperty]
        public string MaterialFilterTerm
        {
            get => _materialFilterTerm;
            set
            {
                if (value != _materialFilterTerm)
                {
                    _materialFilterTerm = value;
                    OnPropertyChangedWithValue(value, nameof(MaterialFilterTerm));
                    ApplyFilter();
                }
            }
        }

        [DataSourceProperty]
        public string ModeLabel
        {
            get => _modeLabel;
            set { if (value != _modeLabel) { _modeLabel = value; OnPropertyChangedWithValue(value, nameof(ModeLabel)); } }
        }

        // Flips FromMaterial/ToMaterial on every rule as it's loaded - built for reversing a
        // culture-swap preset's direction (e.g. choosing "Vlandia to Empire" with Invert on loads
        // it as "Empire to Vlandia") without needing a second preset saved for the opposite
        // direction. Composes with all three load modes rather than needing special-casing per
        // mode - see MaterialSwapVM.LoadPresetByName, which swaps columns before applying
        // whichever mode's own logic.
        [DataSourceProperty]
        public string InvertLabel
        {
            get => _invertLabel;
            set { if (value != _invertLabel) { _invertLabel = value; OnPropertyChangedWithValue(value, nameof(InvertLabel)); } }
        }

        public void ExecuteRefresh() => Refresh();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();
        public void ExecuteClose() => _closeAction?.Invoke();

        public void ExecuteToggleMode()
        {
            _loadMode = _loadMode switch
            {
                PresetLoadMode.Overwrite => PresetLoadMode.Add,
                PresetLoadMode.Add => PresetLoadMode.Merge,
                _ => PresetLoadMode.Overwrite,
            };
            ModeLabel = "Mode: " + _loadMode;
        }

        public void ExecuteToggleInvert()
        {
            _invertMode = !_invertMode;
            InvertLabel = _invertMode ? "Invert: On" : "Invert: Off";
        }

        private void Refresh()
        {
            try
            {
                _allSummaries = MaterialSwapPreset.ListPresetSummaries()
                    .OrderByDescending(s => s.ModifiedUtc)
                    .ToList();
                ApplyFilter();
            }
            catch (Exception ex)
            {
                StatusText = "Failed to list presets: " + ex.Message;
                Log.Error("PresetBrowser refresh failed: " + ex);
            }
        }

        private void ApplyFilter()
        {
            var nameTerm = (SearchTerm ?? "").Trim();
            var tagTerm = (TagFilterTerm ?? "").Trim();
            var materialTerm = (MaterialFilterTerm ?? "").Trim();
            IEnumerable<PresetSummary> matches = _allSummaries;

            if (nameTerm.Length > 0)
            {
                matches = matches.Where(s => s.Name.IndexOf(nameTerm, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (tagTerm.Length > 0)
            {
                matches = matches.Where(s => s.Tags.Any(t => t.IndexOf(tagTerm, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            if (materialTerm.Length > 0)
            {
                matches = matches.Where(s =>
                    s.Rules.Any(r => (r.FromMaterial ?? "").IndexOf(materialTerm, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            Presets.Clear();
            foreach (var summary in matches)
                Presets.Add(new PresetBrowserItemVM(summary, Choose, DeleteItem, _onChosen));

            StatusText = _allSummaries.Count == 0
                ? "No saved presets yet."
                : $"{Presets.Count} of {_allSummaries.Count} preset(s).";
        }

        private void Choose(string name)
        {
            _onChosen?.Invoke(name, _loadMode, _invertMode);
            _closeAction?.Invoke();
        }

        public void ExecuteOpenCultureGenerator() => CulturePresetGeneratorLayer.Open();

        // --- Import / Export (via the Exports folder; GauntletUI has no file dialog) ---
        public void ExecuteExportAll()
        {
            try
            {
                var (exported, dir) = MaterialSwapPreset.Export();
                StatusText = exported == 0
                    ? "No personal presets to export (built-ins aren't exported - they ship with the mod)."
                    : $"Exported {exported} preset(s) to {dir}";
            }
            catch (Exception ex)
            {
                StatusText = "Export failed: " + ex.Message;
                Log.Error("PresetBrowser ExportAll failed: " + ex);
            }
        }

        public void ExecuteImport()
        {
            var inquiry = new InquiryData(
                "Import presets?",
                $"Reads every .json in:\n{MaterialSwapPreset.ExportDir}\n\n"
                    + "Each file is checked before being accepted - anything that isn't a real preset is skipped and reported. "
                    + "Presets whose name already exists are KEPT AS YOURS and skipped; the previous version is archived either way.\n\n"
                    + "Use Open Folder first if you want to drop files in.",
                isAffirmativeOptionShown: true, isNegativeOptionShown: true,
                affirmativeText: "Import", negativeText: "Cancel",
                affirmativeAction: () =>
                {
                    try
                    {
                        var (imported, skipped, problems) = MaterialSwapPreset.Import(overwriteExisting: false);
                        var note = problems.Count == 0 ? "" : "  Issues: " + string.Join("; ", problems.Take(4))
                                   + (problems.Count > 4 ? $" (+{problems.Count - 4} more, see tool.log)" : "");
                        foreach (var p in problems) Log.Warn("[PresetIO] " + p);
                        StatusText = $"Imported {imported} preset(s), skipped {skipped}." + note;
                        Refresh();
                    }
                    catch (Exception ex)
                    {
                        StatusText = "Import failed: " + ex.Message;
                        Log.Error("PresetBrowser Import failed: " + ex);
                    }
                },
                negativeAction: null);
            InformationManager.ShowInquiry(inquiry);
        }

        public void ExecuteOpenExportFolder()
        {
            try
            {
                MaterialSwapPreset.OpenExportFolder();
                StatusText = "Opened " + MaterialSwapPreset.ExportDir;
            }
            catch (Exception ex)
            {
                StatusText = "Couldn't open the folder: " + ex.Message;
                Log.Error("PresetBrowser OpenExportFolder failed: " + ex);
            }
        }

        private void DeleteItem(PresetBrowserItemVM item)
        {
            if (item == null) return;

            if (!item.IsDeletable)
            {
                StatusText = $"'{item.Name}' is a BSA .txt template, not managed here - delete the file directly if you want it gone.";
                return;
            }

            var inquiry = new InquiryData(
                "Delete preset?",
                $"'{item.Name}' will be permanently deleted. This cannot be undone from here.",
                isAffirmativeOptionShown: true,
                isNegativeOptionShown: true,
                affirmativeText: "Delete",
                negativeText: "Cancel",
                affirmativeAction: () => ConfirmDelete(item.Name),
                negativeAction: null);
            InformationManager.ShowInquiry(inquiry);
        }

        private void ConfirmDelete(string name)
        {
            try
            {
                MaterialSwapPreset.Delete(name);
                Refresh();
            }
            catch (Exception ex)
            {
                StatusText = "Delete failed: " + ex.Message;
                Log.Error("Preset delete failed: " + ex);
            }
        }
    }
}
