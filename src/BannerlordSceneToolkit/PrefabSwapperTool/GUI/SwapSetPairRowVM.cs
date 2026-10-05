using System;
using TaleWorlds.Library;

namespace PrefabSwapperTool.GUI
{
    // One rule in a Swap Set's editor area: old prefab -> new prefab, plus an optional texture
    // override (a saved ColorPreset name, applied to the NEW entity right after the swap - see
    // Core/ColorPreset.cs). Cycled by repeated clicking like every other preset-picker in these
    // mods (GauntletUI has no native dropdown) - options are whatever's saved for NewPrefabName,
    // since the override describes what the RESULT of the swap should look like.
    public class SwapSetPairRowVM : ViewModel
    {
        private readonly Action<SwapSetPairRowVM> _onRemove;
        private readonly Action<SwapSetPairRowVM> _onCyclePreset;
        private string _oldPrefabName;
        private string _newPrefabName;
        private string _presetName;

        public SwapSetPairRowVM(string oldPrefabName, string newPrefabName, string presetName,
            Action<SwapSetPairRowVM> onRemove, Action<SwapSetPairRowVM> onCyclePreset)
        {
            _oldPrefabName = oldPrefabName ?? "";
            _newPrefabName = newPrefabName ?? "";
            _presetName = presetName;
            _onRemove = onRemove;
            _onCyclePreset = onCyclePreset;
        }

        [DataSourceProperty]
        public string OldPrefabName
        {
            get => _oldPrefabName;
            set { if (value != _oldPrefabName) { _oldPrefabName = value; OnPropertyChangedWithValue(value, nameof(OldPrefabName)); } }
        }

        [DataSourceProperty]
        public string NewPrefabName
        {
            get => _newPrefabName;
            set { if (value != _newPrefabName) { _newPrefabName = value; OnPropertyChangedWithValue(value, nameof(NewPrefabName)); } }
        }

        public string PresetName
        {
            get => _presetName;
            set { if (value != _presetName) { _presetName = value; OnPropertyChangedWithValue(PresetLabel, nameof(PresetLabel)); } }
        }

        [DataSourceProperty]
        public string PresetLabel
        {
            get => "Texture: " + (string.IsNullOrEmpty(_presetName) ? "None" : _presetName);
            set { }
        }

        public void ExecuteRemove() => _onRemove?.Invoke(this);
        public void ExecuteCyclePreset() => _onCyclePreset?.Invoke(this);
    }
}
