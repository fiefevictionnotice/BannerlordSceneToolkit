using System;
using System.Linq;
using PrefabCreatorTool.Core;
using TaleWorlds.Library;

namespace PrefabCreatorTool.GUI
{
    // One card in the Family Browser - same card style as the Texture Set/Swap Set/Pile Recipe
    // browsers (name/member-count row, expandable detail listing members with per-member Remove).
    public class FamilyRowVM : ViewModel
    {
        private readonly Action<FamilyRowVM> _onDelete;
        private readonly Action<FamilyRowVM, string> _onRemoveMember;
        private string _name;
        private string _summaryText;
        private bool _isExpanded;
        private string _expandButtonText = "Show Members";
        private MBBindingList<FamilyMemberRowVM> _members;

        public string FamilyName => _name;

        public FamilyRowVM(string familyName, System.Collections.Generic.List<string> members,
            Action<FamilyRowVM> onDelete, Action<FamilyRowVM, string> onRemoveMember)
        {
            _name = familyName;
            _onDelete = onDelete;
            _onRemoveMember = onRemoveMember;
            _summaryText = $"{members.Count} member(s)";
            _members = new MBBindingList<FamilyMemberRowVM>();
            foreach (var m in members)
                _members.Add(new FamilyMemberRowVM(m, RemoveMember));
        }

        [DataSourceProperty]
        public string Name
        {
            get => _name;
            set { if (value != _name) { _name = value; OnPropertyChangedWithValue(value, nameof(Name)); } }
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
        public MBBindingList<FamilyMemberRowVM> Members
        {
            get => _members;
            set { if (value != _members) { _members = value; OnPropertyChangedWithValue(value, nameof(Members)); } }
        }

        public void ExecuteToggleExpand()
        {
            IsExpanded = !IsExpanded;
            ExpandButtonText = IsExpanded ? "Hide Members" : "Show Members";
        }

        public void ExecuteDelete() => _onDelete?.Invoke(this);

        private void RemoveMember(string basePrefabName)
        {
            _onRemoveMember?.Invoke(this, basePrefabName);
            var row = Members.FirstOrDefault(m => string.Equals(m.PrefabName, basePrefabName, StringComparison.OrdinalIgnoreCase));
            if (row != null) Members.Remove(row);
            SummaryText = $"{Members.Count} member(s)";
        }
    }

    public class FamilyMemberRowVM : ViewModel
    {
        private readonly Action<string> _onRemove;

        public string PrefabName { get; }

        public FamilyMemberRowVM(string prefabName, Action<string> onRemove)
        {
            PrefabName = prefabName;
            _onRemove = onRemove;
        }

        [DataSourceProperty]
        public string DisplayName
        {
            get => PrefabName;
            set { }
        }

        public void ExecuteRemove() => _onRemove?.Invoke(PrefabName);
    }
}
