using System;
using System.Linq;
using PrefabCreatorTool.Core;
using TaleWorlds.Library;
using BannerlordSceneToolkit;

namespace PrefabCreatorTool.GUI
{
    // One card in the Texture Set browser - mirrors MaterialSwapTool's PresetBrowserPanel card
    // layout (Name/Choose/Delete row, expandable detail) since the user specifically asked for the
    // same visual style, not a fresh design.
    public class TextureSetRowVM : ViewModel
    {
        private readonly Action<TextureSetRowVM> _onApply;
        private readonly Action<TextureSetRowVM> _onDelete;
        private readonly Action<TextureSetRowVM> _onEdit;
        private string _name;
        private string _basePrefabName;
        private string _summaryText;
        private bool _isExpanded;
        private string _expandButtonText = "Show Overrides";
        private MBBindingList<TextureSetOverrideRowVM> _overrides;
        private Color _swatchColor;

        private static readonly Color NoColorSwatch = Color.ConvertStringToColor("#4a4a4aFF");

        public ColorPreset Preset { get; }

        public TextureSetRowVM(ColorPreset preset, Action<TextureSetRowVM> onApply, Action<TextureSetRowVM> onDelete, Action<TextureSetRowVM> onEdit)
        {
            Preset = preset;
            _onApply = onApply;
            _onDelete = onDelete;
            _onEdit = onEdit;
            _name = preset.Name;
            _basePrefabName = preset.BasePrefabName;
            _summaryText = $"{preset.Overrides.Count} override(s) for '{preset.BasePrefabName}'";
            _overrides = new MBBindingList<TextureSetOverrideRowVM>();
            foreach (var kv in preset.Overrides)
                _overrides.Add(new TextureSetOverrideRowVM(kv.Key, kv.Value.Material, kv.Value.Color, null));

            // Representative swatch for the whole card - first override that actually has a color
            // set, so a Material-only override at index 0 doesn't leave the card looking colorless.
            var firstColor = preset.Overrides.Values.Select(o => o.Color).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
            _swatchColor = firstColor != null && ColorHex.TryParse(firstColor, out var packed) ? Color.FromUint(packed) : NoColorSwatch;
        }

        [DataSourceProperty]
        public string Name
        {
            get => _name;
            set { if (value != _name) { _name = value; OnPropertyChangedWithValue(value, nameof(Name)); } }
        }

        [DataSourceProperty]
        public string BasePrefabName
        {
            get => _basePrefabName;
            set { if (value != _basePrefabName) { _basePrefabName = value; OnPropertyChangedWithValue(value, nameof(BasePrefabName)); } }
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
        public MBBindingList<TextureSetOverrideRowVM> Overrides
        {
            get => _overrides;
            set { if (value != _overrides) { _overrides = value; OnPropertyChangedWithValue(value, nameof(Overrides)); } }
        }

        [DataSourceProperty]
        public Color SwatchColor
        {
            get => _swatchColor;
            set { if (!value.Equals(_swatchColor)) { _swatchColor = value; OnPropertyChangedWithValue(value, nameof(SwatchColor)); } }
        }

        public void ExecuteToggleExpand()
        {
            IsExpanded = !IsExpanded;
            ExpandButtonText = IsExpanded ? "Hide Overrides" : "Show Overrides";
        }

        public void ExecuteApply() => _onApply?.Invoke(this);
        public void ExecuteDelete() => _onDelete?.Invoke(this);
        public void ExecuteEdit() => _onEdit?.Invoke(this);
    }
}
