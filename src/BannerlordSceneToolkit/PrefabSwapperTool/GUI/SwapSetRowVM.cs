using System;
using PrefabSwapperTool.Core;
using TaleWorlds.Library;

namespace PrefabSwapperTool.GUI
{
    // One card in the Swap Set browser - mirrors the Texture Override / Texture Set browser's own
    // card layout (name/Apply/Edit/Delete row, expandable detail showing the actual rules) rather
    // than a flat summary line, so you can see what a set actually does without opening Edit.
    public class SwapSetRowVM : ViewModel
    {
        private readonly Action<SwapSetRowVM> _onApply;
        private readonly Action<SwapSetRowVM> _onDelete;
        private readonly Action<SwapSetRowVM> _onEdit;
        private string _name;
        private string _summaryText;
        private bool _isExpanded;
        private string _expandButtonText = "Show Rules";
        private MBBindingList<SwapSetPairSummaryRowVM> _pairs;

        public SwapSet Set { get; }

        public SwapSetRowVM(SwapSet set, Action<SwapSetRowVM> onApply, Action<SwapSetRowVM> onDelete, Action<SwapSetRowVM> onEdit)
        {
            Set = set;
            _onApply = onApply;
            _onDelete = onDelete;
            _onEdit = onEdit;
            _name = set.Name;
            _summaryText = $"{set.Pairs.Count} rule(s)";
            // Rules are materialized into the BOUND list only while expanded (2026-08-23,
            // "Show or hide rules... does not work"): the old approach kept the list always
            // populated and toggled an IsVisible binding on the nested ListPanel, which never
            // took effect inside the row's item template. An empty CoverChildren list IS
            // collapsed - no visibility binding needed at all.
            _pairs = new MBBindingList<SwapSetPairSummaryRowVM>();
            foreach (var pair in set.Pairs)
                _allPairs.Add(new SwapSetPairSummaryRowVM(pair.OldPrefabName, pair.NewPrefabName, pair.PresetName));
        }

        private readonly System.Collections.Generic.List<SwapSetPairSummaryRowVM> _allPairs =
            new System.Collections.Generic.List<SwapSetPairSummaryRowVM>();

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
        public MBBindingList<SwapSetPairSummaryRowVM> Pairs
        {
            get => _pairs;
            set { if (value != _pairs) { _pairs = value; OnPropertyChangedWithValue(value, nameof(Pairs)); } }
        }

        public void ExecuteToggleExpand()
        {
            IsExpanded = !IsExpanded;
            ExpandButtonText = IsExpanded ? "Hide Rules" : "Show Rules";
            Pairs.Clear();
            if (IsExpanded)
                foreach (var pair in _allPairs) Pairs.Add(pair);
        }

        public void ExecuteApply() => _onApply?.Invoke(this);
        public void ExecuteDelete() => _onDelete?.Invoke(this);
        public void ExecuteEdit() => _onEdit?.Invoke(this);
    }

    // Read-only "A -> B (texture)" line shown when a SwapSetRowVM card is expanded.
    public class SwapSetPairSummaryRowVM : ViewModel
    {
        private readonly string _oldPrefabName;
        private readonly string _newPrefabName;
        private readonly string _presetName;

        public SwapSetPairSummaryRowVM(string oldPrefabName, string newPrefabName, string presetName)
        {
            _oldPrefabName = oldPrefabName;
            _newPrefabName = newPrefabName;
            _presetName = presetName;
        }

        [DataSourceProperty]
        public string RuleText
        {
            get => $"{_oldPrefabName} -> {_newPrefabName}" + (string.IsNullOrEmpty(_presetName) ? "" : $"  [{_presetName}]");
            set { }
        }
    }
}
