using System;
using PrefabSwapperTool.Core;
using TaleWorlds.Library;
using BannerlordSceneToolkit;

namespace PrefabSwapperTool.GUI
{
    // Ported from PrefabCreatorTool.GUI.TextureSetOverrideRowVM - one override line within a
    // texture set being browsed or edited.
    public class TextureSetOverrideRowVM : ViewModel
    {
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
