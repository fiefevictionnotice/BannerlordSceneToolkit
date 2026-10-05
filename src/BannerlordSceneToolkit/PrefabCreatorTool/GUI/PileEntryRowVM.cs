using System;
using TaleWorlds.Library;

namespace PrefabCreatorTool.GUI
{
    // One whitelisted row in the Pile Generator's entry list. List order IS layer order (see
    // PileGenerator.Generate) - Move Up/Down swap this row with its neighbor in the parent's
    // MBBindingList rather than exposing a typed order number, since GauntletUI has no native
    // drag-to-reorder and arrow buttons were the explicitly preferred control.
    public class PileEntryRowVM : ViewModel
    {
        private readonly Action<PileEntryRowVM> _onMoveUp;
        private readonly Action<PileEntryRowVM> _onMoveDown;
        private readonly Action<PileEntryRowVM> _onRemove;
        private readonly Action<PileEntryRowVM> _onCyclePreset;

        private string _prefabName;
        private string _countText;
        private string _presetName;
        private bool _snapToSurface;

        public PileEntryRowVM(string prefabName, int count, string presetName, bool snapToSurface,
            Action<PileEntryRowVM> onMoveUp, Action<PileEntryRowVM> onMoveDown, Action<PileEntryRowVM> onRemove, Action<PileEntryRowVM> onCyclePreset)
        {
            _prefabName = prefabName ?? "";
            _countText = Math.Max(1, count).ToString();
            _presetName = presetName;
            _snapToSurface = snapToSurface;
            _onMoveUp = onMoveUp;
            _onMoveDown = onMoveDown;
            _onRemove = onRemove;
            _onCyclePreset = onCyclePreset;
        }

        [DataSourceProperty]
        public string PrefabName
        {
            get => _prefabName;
            set { if (value != _prefabName) { _prefabName = value; OnPropertyChangedWithValue(value, nameof(PrefabName)); } }
        }

        [DataSourceProperty]
        public string CountText
        {
            get => _countText;
            set { if (value != _countText) { _countText = value; OnPropertyChangedWithValue(value, nameof(CountText)); } }
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

        // Sits between Texture and Snap on the row because that is the order the operations
        // actually happen in: pick the look, decide whether it settles, decide whether it collides.
        // Applied after the whole pile is generated - see PileGenerator.StripPhysics.
        public bool DeletePhysics
        {
            get => _deletePhysics;
            set { if (value != _deletePhysics) { _deletePhysics = value; OnPropertyChangedWithValue(PhysicsLabel, nameof(PhysicsLabel)); } }
        }
        private bool _deletePhysics;

        [DataSourceProperty]
        public string PhysicsLabel
        {
            get => _deletePhysics ? "Physics: Delete" : "Physics: Keep";
            set { }
        }

        public bool SnapToSurface
        {
            get => _snapToSurface;
            set { if (value != _snapToSurface) { _snapToSurface = value; OnPropertyChangedWithValue(SnapLabel, nameof(SnapLabel)); } }
        }

        [DataSourceProperty]
        public string SnapLabel
        {
            get => _snapToSurface ? "Snap: On" : "Snap: Off (flat scatter)";
            set { }
        }

        public int ParsedCount => int.TryParse(CountText, out var n) && n > 0 ? n : 1;

        public void ExecuteMoveUp() => _onMoveUp?.Invoke(this);
        public void ExecuteMoveDown() => _onMoveDown?.Invoke(this);
        public void ExecuteRemove() => _onRemove?.Invoke(this);
        public void ExecuteCyclePreset() => _onCyclePreset?.Invoke(this);
        public void ExecuteToggleSnap() => SnapToSurface = !SnapToSurface;
        public void ExecuteTogglePhysics() => DeletePhysics = !DeletePhysics;
    }
}
