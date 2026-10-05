using System;
using PrefabCreatorTool.Core;
using TaleWorlds.Library;

namespace PrefabCreatorTool.GUI
{
    // One row in the "Display Paired Prefabs" flyout - a prefab already known to pair with
    // whatever's selected. Clicking the row stages/unstages it for the next Combine; clicking
    // "Preset" cycles through that specific prefab's own saved ColorPresets (if any exist) since
    // GauntletUI has no native dropdown widget - repeated-click cycling is the closest equivalent
    // to "its own dropdown window" the engine's list-based widgets can actually do. Clicking
    // "Remove" deletes this pairing from the saved combo entirely (not just unstages it).
    public class ComboMemberRowVM : ViewModel
    {
        private readonly Action<ComboMemberRowVM> _onToggleStaged;
        private readonly Action<ComboMemberRowVM> _onCyclePreset;
        private readonly Action<ComboMemberRowVM> _onRemove;
        private readonly Action<ComboMemberRowVM> _onToggleAutoPlace;
        private string _prefabName;
        private string _presetName;
        private bool _isStaged;
        private bool _autoPlace;

        // The combo file this row's edits (cycle preset / remove) actually write back to - the
        // literal base prefab name for a direct pairing, or "family__<name>" for one inherited
        // from a family this base belongs to (see PrefabComboStore.LoadEffectiveMembers).
        public string BasePrefabName { get; }
        public string SourceFamily { get; }
        // Locked relative position/rotation captured at Add Pairing time (null/HasOffset=false =
        // old shared-origin behavior) - carried through unchanged to Combine at Shared Origin.
        public ComboMember Offset { get; }

        public ComboMemberRowVM(string basePrefabName, string prefabName, string presetName, ComboMember offset, string sourceFamily, bool autoPlace,
            Action<ComboMemberRowVM> onToggleStaged, Action<ComboMemberRowVM> onCyclePreset, Action<ComboMemberRowVM> onRemove, Action<ComboMemberRowVM> onToggleAutoPlace)
        {
            BasePrefabName = basePrefabName;
            _prefabName = prefabName;
            _presetName = presetName;
            Offset = offset;
            SourceFamily = sourceFamily;
            _autoPlace = autoPlace;
            _onToggleStaged = onToggleStaged;
            _onCyclePreset = onCyclePreset;
            _onRemove = onRemove;
            _onToggleAutoPlace = onToggleAutoPlace;
        }

        public string PrefabName => _prefabName;
        public string PresetName
        {
            get => _presetName;
            set { if (value != _presetName) { _presetName = value; OnPropertyChangedWithValue(PresetLabel, nameof(PresetLabel)); } }
        }

        [DataSourceProperty]
        public string RowLabel
        {
            get => (_isStaged ? "[Staged] " : "") + _prefabName + (SourceFamily != null ? $" (family: {SourceFamily})" : "");
            set { }
        }

        [DataSourceProperty]
        public string PresetLabel
        {
            get => "Texture set: " + (string.IsNullOrEmpty(_presetName) ? "None" : _presetName);
            set { }
        }

        public bool IsStaged
        {
            get => _isStaged;
            set { if (value != _isStaged) { _isStaged = value; OnPropertyChangedWithValue(RowLabel, nameof(RowLabel)); } }
        }

        // Whether MaterialSwapTool's own placement tools (Swap Selected/All Matching, Distribute)
        // auto-place this secondary alongside any NEW instance of the base/family they place -
        // separate from IsStaged, which only affects the manual Combine flow in this panel.
        //
        // CONFIRMED LIVE CRASH: this setter used to call OnPropertyChangedWithValue(value, ...) -
        // value is the new bool, but the property being notified (AutoPlaceLabel) is a string.
        // GauntletUI's reflection-based binding tried to convert the bool to a string and threw an
        // unhandled System.ArgumentException ("Object of type 'System.Boolean' cannot be converted
        // to type 'System.String'"), crashing the whole process the instant Auto-Place was clicked -
        // confirmed via a full WER dump (CLR_EXCEPTION_System.ArgumentException, mscorlib!
        // RuntimeType.TryChangeType). Must pass the freshly-computed STRING value of the property
        // actually being notified, not the raw setter argument.
        public bool AutoPlace
        {
            get => _autoPlace;
            set { if (value != _autoPlace) { _autoPlace = value; OnPropertyChangedWithValue(AutoPlaceLabel, nameof(AutoPlaceLabel)); } }
        }

        [DataSourceProperty]
        public string AutoPlaceLabel
        {
            get => _autoPlace
                ? "Auto-Place: On (spawns with every new copy of the base elsewhere)"
                : "Auto-Place: Off";
            set { }
        }

        public void ExecuteToggleStaged() => _onToggleStaged?.Invoke(this);
        public void ExecuteCyclePreset() => _onCyclePreset?.Invoke(this);
        public void ExecuteRemove() => _onRemove?.Invoke(this);
        public void ExecuteToggleAutoPlace() => _onToggleAutoPlace?.Invoke(this);
    }
}
