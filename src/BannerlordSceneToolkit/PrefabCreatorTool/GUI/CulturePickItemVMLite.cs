using System;
using TaleWorlds.Library;

namespace PrefabCreatorTool.GUI
{
    // Generic "name + click callback" row, used for the saved color presets list. Named to match
    // MaterialSwapTool's own CulturePickItemVM shape/purpose (same pattern, independent copy - no
    // cross-mod reference).
    public class CulturePickItemVMLite : ViewModel
    {
        private readonly Action<string> _onClick;
        private string _name;

        public CulturePickItemVMLite(string name, Action<string> onClick)
        {
            _name = name;
            _onClick = onClick;
        }

        [DataSourceProperty]
        public string Name
        {
            get => _name;
            set { if (value != _name) { _name = value; OnPropertyChangedWithValue(value, nameof(Name)); } }
        }

        public void ExecuteChoose() => _onClick?.Invoke(_name);
    }
}
