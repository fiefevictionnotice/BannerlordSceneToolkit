using System;
using PrefabCreatorTool.Core;
using TaleWorlds.Library;

namespace PrefabCreatorTool.GUI
{
    // One detected same-prefab-instance color/material variant (Mode 2b) in the results list.
    public class InstanceGroupRowVM : ViewModel
    {
        private readonly Action<InstanceGroupRowVM> _onRename;
        private readonly Action<InstanceGroupRowVM> _onSavePreset;
        private string _summary;
        private string _baseNameInput;

        public PrefabInstanceGroup Group { get; }

        public InstanceGroupRowVM(PrefabInstanceGroup group, string suggestedBaseName, Action<InstanceGroupRowVM> onRename, Action<InstanceGroupRowVM> onSavePreset)
        {
            Group = group;
            _onRename = onRename;
            _onSavePreset = onSavePreset;
            _baseNameInput = suggestedBaseName;
            _summary = $"'{group.Anchor.Name}' - {group.Variants.Count} color/material variant(s) detected";
        }

        [DataSourceProperty]
        public string Summary
        {
            get => _summary;
            set { if (value != _summary) { _summary = value; OnPropertyChangedWithValue(value, nameof(Summary)); } }
        }

        [DataSourceProperty]
        public string BaseNameInput
        {
            get => _baseNameInput;
            set { if (value != _baseNameInput) { _baseNameInput = value; OnPropertyChangedWithValue(value, nameof(BaseNameInput)); } }
        }

        public void ExecuteRename() => _onRename?.Invoke(this);
        public void ExecuteSavePreset() => _onSavePreset?.Invoke(this);
    }
}
