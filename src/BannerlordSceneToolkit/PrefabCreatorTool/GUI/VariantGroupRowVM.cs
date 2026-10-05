using System;
using PrefabCreatorTool.Core;
using TaleWorlds.Library;

namespace PrefabCreatorTool.GUI
{
    // One detected variant group in the "Identify Variations" results list - its own base-name box
    // (pre-filled with a guess) and an Apply button, since each group needs its own name and you'll
    // usually want to review/rename before committing.
    public class VariantGroupRowVM : ViewModel
    {
        private readonly Action<VariantGroupRowVM> _onApply;
        private readonly Action<VariantGroupRowVM> _onSavePreset;
        private string _summary;
        private string _baseNameInput;

        public VariantGroup Group { get; }

        public VariantGroupRowVM(VariantGroup group, string suggestedBaseName, Action<VariantGroupRowVM> onApply, Action<VariantGroupRowVM> onSavePreset)
        {
            Group = group;
            _onApply = onApply;
            _onSavePreset = onSavePreset;
            _baseNameInput = suggestedBaseName;
            _summary = $"{group.Anchor.Members.Count} part(s), {group.Variants.Count} variant(s) detected";
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

        public void ExecuteApply() => _onApply?.Invoke(this);
        public void ExecuteSavePreset() => _onSavePreset?.Invoke(this);
    }
}
