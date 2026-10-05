using System;
using System.Linq;
using PrefabCreatorTool.Core;
using TaleWorlds.Library;

namespace PrefabCreatorTool.GUI
{
    // One card in the Pile Generator's Saved Recipes browser - same card style as the Texture Set
    // and Swap Set browsers (name/Load/Delete row, expandable detail) rather than the bare
    // type-a-name Save/Load/Delete row this used to be the only option.
    public class PileRecipeRowVM : ViewModel
    {
        private readonly Action<PileRecipeRowVM> _onLoad;
        private readonly Action<PileRecipeRowVM> _onDelete;
        private string _name;
        private string _summaryText;
        private bool _isExpanded;
        private string _expandButtonText = "Show Entries";
        private MBBindingList<PileRecipeEntrySummaryRowVM> _entrySummaries;

        public PileRecipe Recipe { get; }

        public PileRecipeRowVM(PileRecipe recipe, Action<PileRecipeRowVM> onLoad, Action<PileRecipeRowVM> onDelete)
        {
            Recipe = recipe;
            _onLoad = onLoad;
            _onDelete = onDelete;
            _name = recipe.Name;
            _summaryText = $"{recipe.Entries.Count} entry(ies), scatter radius {recipe.ScatterRadius:F2}";
            _entrySummaries = new MBBindingList<PileRecipeEntrySummaryRowVM>();
            foreach (var entry in recipe.Entries)
                _entrySummaries.Add(new PileRecipeEntrySummaryRowVM(entry));
        }

        [DataSourceProperty]
        public string Name
        {
            get => _name;
            set { if (value != _name) { _name = value; OnPropertyChangedWithValue(value, nameof(Name)); } }
        }

        [DataSourceProperty]
        public string SummaryText
        {
            get => _summaryText;
            set { if (value != _summaryText) { _summaryText = value; OnPropertyChangedWithValue(value, nameof(SummaryText)); } }
        }

        [DataSourceProperty]
        public string ExpandButtonText
        {
            get => _expandButtonText;
            set { if (value != _expandButtonText) { _expandButtonText = value; OnPropertyChangedWithValue(value, nameof(ExpandButtonText)); } }
        }

        [DataSourceProperty]
        public bool IsExpanded
        {
            get => _isExpanded;
            set { if (value != _isExpanded) { _isExpanded = value; OnPropertyChangedWithValue(value, nameof(IsExpanded)); } }
        }

        [DataSourceProperty]
        public MBBindingList<PileRecipeEntrySummaryRowVM> EntrySummaries
        {
            get => _entrySummaries;
            set { if (value != _entrySummaries) { _entrySummaries = value; OnPropertyChangedWithValue(value, nameof(EntrySummaries)); } }
        }

        public void ExecuteToggleExpand()
        {
            IsExpanded = !IsExpanded;
            ExpandButtonText = IsExpanded ? "Hide Entries" : "Show Entries";
        }

        public void ExecuteLoad() => _onLoad?.Invoke(this);
        public void ExecuteDelete() => _onDelete?.Invoke(this);
    }

    // Read-only "prefab x count [texture] (snap)" line shown when a PileRecipeRowVM is expanded -
    // listed in the same top-row-first order the recipe itself is authored in.
    public class PileRecipeEntrySummaryRowVM : ViewModel
    {
        private readonly PileEntry _entry;

        public PileRecipeEntrySummaryRowVM(PileEntry entry) => _entry = entry;

        [DataSourceProperty]
        public string EntryText
        {
            get
            {
                var texture = string.IsNullOrEmpty(_entry.PresetName) ? "" : $"  [{_entry.PresetName}]";
                var snap = _entry.SnapToSurface ? "" : "  (no snap)";
                return $"{_entry.PrefabName} x{_entry.Count}{texture}{snap}";
            }
            set { }
        }
    }
}
