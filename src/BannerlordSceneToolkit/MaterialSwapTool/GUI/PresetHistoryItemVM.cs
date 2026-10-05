using System;
using System.Collections.Generic;
using MaterialSwapTool.Core;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    public class PresetHistoryItemVM : ViewModel
    {
        private readonly Action<PresetHistoryItemVM> _onRestore;
        private readonly MaterialSwapPreset _preset;
        private string _dateText;
        private string _ruleSummaryText;
        private bool _isExpanded;
        private string _expandButtonText;
        private MBBindingList<RuleLineVM> _ruleLines = new MBBindingList<RuleLineVM>();

        public string FilePath { get; }

        public PresetHistoryItemVM(PresetHistoryManager.VersionInfo info, Action<PresetHistoryItemVM> onRestore)
        {
            FilePath = info.FilePath;
            _onRestore = onRestore;
            _dateText = info.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

            try { _preset = PresetHistoryManager.LoadVersion(info.FilePath); }
            catch (Exception ex) { Log.Warn($"PresetHistoryItemVM: failed to load '{info.FilePath}': {ex.Message}"); }

            var count = _preset?.Rules.Count ?? 0;
            _ruleSummaryText = $"{count} rule(s)";
            _expandButtonText = "Expand";
        }

        [DataSourceProperty]
        public string DateText
        {
            get => _dateText;
            set { if (value != _dateText) { _dateText = value; OnPropertyChangedWithValue(value, nameof(DateText)); } }
        }

        [DataSourceProperty]
        public string RuleSummaryText
        {
            get => _ruleSummaryText;
            set { if (value != _ruleSummaryText) { _ruleSummaryText = value; OnPropertyChangedWithValue(value, nameof(RuleSummaryText)); } }
        }

        [DataSourceProperty]
        public string ExpandButtonText
        {
            get => _expandButtonText;
            set { if (value != _expandButtonText) { _expandButtonText = value; OnPropertyChangedWithValue(value, nameof(ExpandButtonText)); } }
        }

        [DataSourceProperty]
        public MBBindingList<RuleLineVM> RuleLines
        {
            get => _ruleLines;
            set { if (value != _ruleLines) { _ruleLines = value; OnPropertyChangedWithValue(value, nameof(RuleLines)); } }
        }

        public void ExecuteToggleExpand()
        {
            _isExpanded = !_isExpanded;
            if (_isExpanded)
            {
                RuleLines.Clear();
                foreach (var r in _preset?.Rules ?? new List<MaterialSwapRule>())
                    RuleLines.Add(new RuleLineVM($"{r.FromMaterial} -> {r.ToMaterial}"));
                ExpandButtonText = "Collapse";
            }
            else
            {
                RuleLines.Clear();
                ExpandButtonText = "Expand";
            }
        }

        public void ExecuteRestore() => _onRestore?.Invoke(this);
    }
}
