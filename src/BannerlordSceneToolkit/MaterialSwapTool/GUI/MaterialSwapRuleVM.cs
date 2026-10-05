using System;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    public class MaterialSwapRuleVM : ViewModel
    {
        private readonly Action<MaterialSwapRuleVM> _removeAction;
        private string _fromMaterial;
        private string _toMaterial;
        private string _colorFactor;

        public MaterialSwapRuleVM(Action<MaterialSwapRuleVM> removeAction, string from = "", string to = "", string colorFactor = "")
        {
            _removeAction = removeAction;
            _fromMaterial = from;
            _toMaterial = to;
            _colorFactor = colorFactor ?? "";
        }

        [DataSourceProperty]
        public string FromMaterial
        {
            get => _fromMaterial;
            set { if (value != _fromMaterial) { _fromMaterial = value; OnPropertyChangedWithValue(value, nameof(FromMaterial)); } }
        }

        [DataSourceProperty]
        public string ToMaterial
        {
            get => _toMaterial;
            set { if (value != _toMaterial) { _toMaterial = value; OnPropertyChangedWithValue(value, nameof(ToMaterial)); } }
        }

        // Optional - empty means "don't touch the mesh's color". RRGGBB or RRGGBBAA hex.
        [DataSourceProperty]
        public string ColorFactor
        {
            get => _colorFactor;
            set { if (value != _colorFactor) { _colorFactor = value; OnPropertyChangedWithValue(value, nameof(ColorFactor)); } }
        }

        // Command.Click inside a ListPanel's ItemTemplate resolves against the item's own view
        // model, not the parent - so the remove button has to live here and delegate back up,
        // the same way the item VMs in the game's own list-based screens do.
        public void ExecuteRemoveRule() => _removeAction?.Invoke(this);

        // Per-row invert. Same swap ExecuteInvertRules does to the whole list, but scoped to this
        // one rule - needs no delegate back to the parent because it only touches its own state.
        // Blank-on-both rows are left alone so clicking a placeholder row does nothing surprising.
        public void ExecuteInvertRule()
        {
            if (string.IsNullOrWhiteSpace(FromMaterial) && string.IsNullOrWhiteSpace(ToMaterial)) return;
            var from = FromMaterial;
            FromMaterial = ToMaterial;
            ToMaterial = from;
        }
    }
}
