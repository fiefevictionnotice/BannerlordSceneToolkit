using System;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    // One row in the category editor: a category name plus its Include/Exclude pattern lists,
    // shown as comma-separated text (matching how presets show tags) rather than a nested
    // per-pattern list - simpler to read and edit for what's usually a handful of short strings.
    public class CategoryEditItemVM : ViewModel
    {
        private readonly Action<CategoryEditItemVM> _onDelete;
        private string _name;
        private string _includeText;
        private string _excludeText;

        public CategoryEditItemVM(string name, string includeText, string excludeText, Action<CategoryEditItemVM> onDelete)
        {
            _name = name;
            _includeText = includeText;
            _excludeText = excludeText;
            _onDelete = onDelete;
        }

        [DataSourceProperty]
        public string Name
        {
            get => _name;
            set { if (value != _name) { _name = value; OnPropertyChangedWithValue(value, nameof(Name)); } }
        }

        [DataSourceProperty]
        public string IncludeText
        {
            get => _includeText;
            set { if (value != _includeText) { _includeText = value; OnPropertyChangedWithValue(value, nameof(IncludeText)); } }
        }

        [DataSourceProperty]
        public string ExcludeText
        {
            get => _excludeText;
            set { if (value != _excludeText) { _excludeText = value; OnPropertyChangedWithValue(value, nameof(ExcludeText)); } }
        }

        public void ExecuteDelete() => _onDelete?.Invoke(this);
    }
}
