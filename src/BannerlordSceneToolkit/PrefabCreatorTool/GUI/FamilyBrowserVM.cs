using System;
using System.Linq;
using PrefabCreatorTool.Core;
using TaleWorlds.Library;

namespace PrefabCreatorTool.GUI
{
    // Family Browser - families (many-to-many "these base prefabs are color variants of each
    // other" groupings, PrefabFamilyStore) never had a browsable list, only a bare "type a family
    // name, Assign Selected" button in the Pairing Browser (still there, unchanged, as a quick
    // action). This is the same card-list/search/expand pattern as the Texture Set, Swap Set, and
    // Pile Recipe browsers, applied to families: see what exists, see its members, remove a
    // member, delete a family - instead of only ever being able to add blind.
    public class FamilyBrowserVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;

        private string _searchTerm = "";
        private MBBindingList<FamilyRowVM> _rows;
        private string _statusText = "";
        private string _newFamilyNameInput = "";

        public FamilyBrowserVM(Action closeAction, Action beginDragAction)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            _rows = new MBBindingList<FamilyRowVM>();
            Refresh();
        }

        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();
        public void ExecuteRefresh() => Refresh();

        [DataSourceProperty]
        public string SearchTerm
        {
            get => _searchTerm;
            set { if (value != _searchTerm) { _searchTerm = value; OnPropertyChangedWithValue(value, nameof(SearchTerm)); Refresh(); } }
        }

        [DataSourceProperty]
        public MBBindingList<FamilyRowVM> Rows
        {
            get => _rows;
            set { if (value != _rows) { _rows = value; OnPropertyChangedWithValue(value, nameof(Rows)); } }
        }

        [DataSourceProperty]
        public string StatusText
        {
            get => _statusText;
            set { if (value != _statusText) { _statusText = value; OnPropertyChangedWithValue(value, nameof(StatusText)); } }
        }

        [DataSourceProperty]
        public string NewFamilyNameInput
        {
            get => _newFamilyNameInput;
            set { if (value != _newFamilyNameInput) { _newFamilyNameInput = value; OnPropertyChangedWithValue(value, nameof(NewFamilyNameInput)); } }
        }

        private void Refresh()
        {
            Rows.Clear();
            var term = (SearchTerm ?? "").Trim();
            foreach (var name in PrefabFamilyStore.ListFamilyNames())
            {
                if (term.Length > 0 && name.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var members = PrefabFamilyStore.GetMembers(name);
                Rows.Add(new FamilyRowVM(name, members, RunDelete, RunRemoveMember));
            }
            StatusText = Rows.Count == 0 ? "No families yet." : $"{Rows.Count} famil{(Rows.Count == 1 ? "y" : "ies")}.";
        }

        // Same "select ONE entity, type/confirm a family name" convention as Pairing Browser's own
        // Assign Selected To Family - duplicated here rather than shared since it's a one-line call,
        // matching how infrastructure stays independent elsewhere in this codebase.
        public void ExecuteAddSelectedToFamily()
        {
            if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }
            if (string.IsNullOrWhiteSpace(NewFamilyNameInput)) { StatusText = "Type a family name first."; return; }
            var selection = EntitySelector.GetManualSelection();
            if (selection.Count != 1) { StatusText = $"Select exactly ONE placed entity (selected: {selection.Count})."; return; }

            PrefabFamilyStore.AddMemberToFamily(NewFamilyNameInput.Trim(), selection[0].Name);
            StatusText = $"Added '{selection[0].Name}' to family '{NewFamilyNameInput.Trim()}'.";
            Refresh();
        }

        private void RunRemoveMember(FamilyRowVM row, string basePrefabName)
        {
            PrefabFamilyStore.RemoveMemberFromFamily(row.FamilyName, basePrefabName);
            StatusText = $"Removed '{basePrefabName}' from family '{row.FamilyName}'.";
        }

        private void RunDelete(FamilyRowVM row)
        {
            var inquiry = new InquiryData(
                "Delete family?",
                $"'{row.FamilyName}' will be permanently deleted. This does not delete any pairings saved at family scope for it - those become orphaned data. This cannot be undone from here.",
                isAffirmativeOptionShown: true, isNegativeOptionShown: true,
                affirmativeText: "Delete", negativeText: "Cancel",
                affirmativeAction: () => { PrefabFamilyStore.DeleteFamily(row.FamilyName); Refresh(); },
                negativeAction: null);
            InformationManager.ShowInquiry(inquiry);
        }
    }
}
