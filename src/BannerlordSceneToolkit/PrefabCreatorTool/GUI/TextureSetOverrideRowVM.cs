using System;
using PrefabCreatorTool.Core;
using TaleWorlds.Library;
using BannerlordSceneToolkit;

namespace PrefabCreatorTool.GUI
{
    // One override line within a texture set being browsed or edited - a part key (mesh slot name
    // or stripped child entity name) plus the material/color to apply to it.
    public class TextureSetOverrideRowVM : ViewModel
    {
        // Neutral placeholder for an override with no color set (Material-only override, or a
        // blank row not yet filled in) - distinguishable from any real captured color at a glance.
        // Fully qualified throughout this class since the "Color" DataSourceProperty below (a
        // string) shadows the TaleWorlds.Library.Color type name.
        private static readonly TaleWorlds.Library.Color NoColorSwatch = TaleWorlds.Library.Color.ConvertStringToColor("#4a4a4aFF");

        private readonly Action<TextureSetOverrideRowVM> _onRemove;
        private string _partKey;
        private string _material;
        private string _color;
        private TaleWorlds.Library.Color _swatchColor;

        public TextureSetOverrideRowVM(string partKey, string material, string color, Action<TextureSetOverrideRowVM> onRemove)
        {
            _partKey = partKey;
            _material = material ?? "";
            _color = color ?? "";
            _swatchColor = ResolveSwatch(_color);
            _onRemove = onRemove;
        }

        private static TaleWorlds.Library.Color ResolveSwatch(string hex) =>
            ColorHex.TryParse(hex, out var packed) && !string.IsNullOrWhiteSpace(hex) ? TaleWorlds.Library.Color.FromUint(packed) : NoColorSwatch;

        [DataSourceProperty]
        public string PartKey
        {
            get => _partKey;
            set { if (value != _partKey) { _partKey = value; OnPropertyChangedWithValue(value, nameof(PartKey)); } }
        }

        [DataSourceProperty]
        public string Material
        {
            get => _material;
            set { if (value != _material) { _material = value; OnPropertyChangedWithValue(value, nameof(Material)); } }
        }

        [DataSourceProperty]
        public string Color
        {
            get => _color;
            set
            {
                if (value == _color) return;
                _color = value;
                OnPropertyChangedWithValue(value, nameof(Color));
                SwatchColor = ResolveSwatch(_color);
            }
        }

        [DataSourceProperty]
        public TaleWorlds.Library.Color SwatchColor
        {
            get => _swatchColor;
            set { if (!value.Equals(_swatchColor)) { _swatchColor = value; OnPropertyChangedWithValue(value, nameof(SwatchColor)); } }
        }

        public void ExecuteRemove() => _onRemove?.Invoke(this);
    }
}
