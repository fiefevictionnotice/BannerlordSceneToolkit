using System;
using System.Linq;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    // One row in the culture editor: a culture name plus its Unique (inference) and Common
    // (override/stub-generation) material lists, shown as comma-separated text.
    //
    // Common is rendered collapsed by default (a short summary + "Edit" button) rather than always
    // live as an EditableTextWidget. A real culture's Common list runs 60-145+ entries (empirically
    // scraped from mesh_slot_map.csv, see CultureMaterialInference) - joined into one comma-
    // separated string that's 1000-2000+ characters on a single unwrapped line, that's what was
    // tanking performance with this panel open: 6 such widgets laid out simultaneously, versus the
    // Category Editor's Include/Exclude fields which are only ever a handful of short substrings.
    // Collapsing means only the row actually being edited ever pays that cost.
    public class CultureEditItemVM : ViewModel
    {
        private readonly Action<CultureEditItemVM> _onDelete;
        private string _name;
        private string _uniqueText;
        private string _commonText;
        private bool _isCommonExpanded;

        public CultureEditItemVM(string name, string uniqueText, string commonText, Action<CultureEditItemVM> onDelete)
        {
            _name = name;
            _uniqueText = uniqueText;
            _commonText = commonText;
            _onDelete = onDelete;
            RefreshSummary();
        }

        [DataSourceProperty]
        public string Name
        {
            get => _name;
            set { if (value != _name) { _name = value; OnPropertyChangedWithValue(value, nameof(Name)); } }
        }

        [DataSourceProperty]
        public string UniqueText
        {
            get => _uniqueText;
            set { if (value != _uniqueText) { _uniqueText = value; OnPropertyChangedWithValue(value, nameof(UniqueText)); } }
        }

        [DataSourceProperty]
        public string CommonText
        {
            get => _commonText;
            set
            {
                if (value != _commonText)
                {
                    _commonText = value;
                    OnPropertyChangedWithValue(value, nameof(CommonText));
                    RefreshSummary();
                }
            }
        }

        private string _commonSummaryText;
        [DataSourceProperty]
        public string CommonSummaryText
        {
            get => _commonSummaryText;
            set { if (value != _commonSummaryText) { _commonSummaryText = value; OnPropertyChangedWithValue(value, nameof(CommonSummaryText)); } }
        }

        [DataSourceProperty]
        public bool IsCommonExpanded
        {
            get => _isCommonExpanded;
            set
            {
                if (value != _isCommonExpanded)
                {
                    _isCommonExpanded = value;
                    OnPropertyChangedWithValue(value, nameof(IsCommonExpanded));
                    IsCommonCollapsed = !value;
                }
            }
        }

        private bool _isCommonCollapsed = true;
        [DataSourceProperty]
        public bool IsCommonCollapsed
        {
            get => _isCommonCollapsed;
            set { if (value != _isCommonCollapsed) { _isCommonCollapsed = value; OnPropertyChangedWithValue(value, nameof(IsCommonCollapsed)); } }
        }

        private void RefreshSummary()
        {
            var count = (_commonText ?? "").Split(',').Select(s => s.Trim()).Count(s => s.Length > 0);
            CommonSummaryText = count == 0 ? "(empty)" : $"{count} material(s) - click Edit to view/change";
        }

        public void ExecuteDelete() => _onDelete?.Invoke(this);
        public void ExecuteToggleCommonExpanded() => IsCommonExpanded = !IsCommonExpanded;
    }
}
