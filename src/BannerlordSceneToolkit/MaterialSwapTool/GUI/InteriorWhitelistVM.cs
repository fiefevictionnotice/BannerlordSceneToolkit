using System;
using System.Collections.Generic;
using System.Linq;
using MaterialSwapTool.Core;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    public class InteriorEntryVM : ViewModel
    {
        private readonly Action<InteriorEntryVM> _removeAction;
        private string _name;
        private bool _knownGood;
        private bool _isPattern;

        public InteriorEntryVM(string name, bool knownGood, bool isPattern, Action<InteriorEntryVM> removeAction)
        {
            _name = name;
            _knownGood = knownGood;
            _isPattern = isPattern;
            _removeAction = removeAction;
        }

        public bool KnownGood => _knownGood;
        public bool IsPattern => _isPattern;

        [DataSourceProperty]
        public string Name
        {
            get => _name;
            set { if (value != _name) { _name = value; OnPropertyChangedWithValue(value, nameof(Name)); } }
        }

        // Button label rather than a checkbox: this list is scanned to answer one question -
        // "is this protected?" - and a word states that far more directly than a tick box.
        [DataSourceProperty]
        public string ProtectedLabel => _knownGood ? "PROTECTED" : "deletable";

        [DataSourceProperty]
        public string ProtectedColor => _knownGood ? "#4fb96aFF" : "#b83227FF";

        [DataSourceProperty]
        public string PatternLabel => _isPattern ? "pattern" : "exact";

        public void ExecuteToggleProtected()
        {
            _knownGood = !_knownGood;
            OnPropertyChanged(nameof(ProtectedLabel));
            OnPropertyChanged(nameof(ProtectedColor));
        }

        public void ExecuteTogglePattern()
        {
            _isPattern = !_isPattern;
            OnPropertyChanged(nameof(PatternLabel));
        }

        public void ExecuteRemove() => _removeAction?.Invoke(this);
    }

    // Editor for Interior_Entities.txt - the list that decides which interior entities Delete
    // Interior Entities refuses to remove. Same shipped-default-plus-Documents-override
    // convention as the category and culture editors, and the same reason for existing: a
    // protection list you can only change by hand-editing a file in the module folder is one
    // nobody actually maintains.
    public class InteriorWhitelistVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;
        private readonly Action _onSaved;
        private MBBindingList<InteriorEntryVM> _entries = new MBBindingList<InteriorEntryVM>();
        private string _newEntryName = "";
        private string _filterTerm = "";
        private string _statusText = "";
        private List<InteriorEntryVM> _all = new List<InteriorEntryVM>();

        public InteriorWhitelistVM(Action closeAction, Action beginDragAction, Action onSaved)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            _onSaved = onSaved;
            Refresh();
        }

        private void Refresh()
        {
            _all = LiveSceneChecks.GetInteriorEntriesForEditing()
                .Select(e => new InteriorEntryVM(e.Name, e.KnownGood, e.IsPattern, RemoveRow))
                .ToList();
            ApplyFilter();
            StatusText = $"{_all.Count} entr(y/ies): {_all.Count(e => e.KnownGood)} protected, {_all.Count(e => !e.KnownGood)} deletable.";
        }

        // The list runs to ~50 entries and is scanned by eye, so a filter matters more here than
        // a scrollbar does.
        private void ApplyFilter()
        {
            Entries.Clear();
            var term = (_filterTerm ?? "").Trim();
            foreach (var e in _all)
            {
                if (term.Length > 0 && e.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0) continue;
                Entries.Add(e);
            }
        }

        private void RemoveRow(InteriorEntryVM row)
        {
            _all.Remove(row);
            if (Entries.Contains(row)) Entries.Remove(row);
        }

        [DataSourceProperty]
        public MBBindingList<InteriorEntryVM> Entries
        {
            get => _entries;
            set { if (value != _entries) { _entries = value; OnPropertyChangedWithValue(value, nameof(Entries)); } }
        }

        [DataSourceProperty]
        public string NewEntryName
        {
            get => _newEntryName;
            set { if (value != _newEntryName) { _newEntryName = value; OnPropertyChangedWithValue(value, nameof(NewEntryName)); } }
        }

        [DataSourceProperty]
        public string FilterTerm
        {
            get => _filterTerm;
            set { if (value != _filterTerm) { _filterTerm = value; OnPropertyChangedWithValue(value, nameof(FilterTerm)); ApplyFilter(); } }
        }

        [DataSourceProperty]
        public string StatusText
        {
            get => _statusText;
            set { if (value != _statusText) { _statusText = value; OnPropertyChangedWithValue(value, nameof(StatusText)); } }
        }

        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();

        // New entries default to PROTECTED - you open this panel to stop something being deleted
        // far more often than to mark something deletable.
        public void ExecuteAddEntry()
        {
            var name = (NewEntryName ?? "").Trim();
            if (name.Length == 0) { StatusText = "Type an entity name first."; return; }
            if (_all.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
            { StatusText = $"'{name}' is already in the list."; return; }

            var row = new InteriorEntryVM(name, true, false, RemoveRow);
            _all.Insert(0, row);
            ApplyFilter();
            NewEntryName = "";
            StatusText = $"Added '{name}' as PROTECTED. Not saved yet - press Save.";
        }

        // Pulls every interior entity in the open scene that isn't already listed, so the list can
        // be built from what a real scene actually contains instead of typed from memory.
        public void ExecuteAddFromScene()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }
                var all = LiveSceneChecks.CollectAll(EntitySelector.CurrentScene);
                var found = LiveSceneChecks.PreviewInteriorEntities(all);
                if (found.Count == 0) { StatusText = "No interior entities in this scene."; return; }

                int added = 0;
                foreach (var f in found)
                {
                    if (_all.Any(e => string.Equals(e.Name, f.Name, StringComparison.OrdinalIgnoreCase))) continue;
                    // Unlisted scene entities come in as DELETABLE: they are currently unprotected
                    // in practice, so listing them as protected would silently change behaviour
                    // just by opening this panel.
                    _all.Insert(0, new InteriorEntryVM(f.Name, false, false, RemoveRow));
                    added++;
                }
                ApplyFilter();
                StatusText = added == 0
                    ? $"All {found.Count} interior name(s) in this scene are already listed."
                    : $"Added {added} name(s) from the scene as 'deletable' - toggle any you want protected. Not saved yet.";
            }
            catch (Exception ex)
            {
                StatusText = "Add from scene failed: " + ex.Message;
                Log.Error("InteriorWhitelist AddFromScene failed: " + ex);
            }
        }

        public void ExecuteSave()
        {
            try
            {
                var entries = _all
                    .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                    .Select(e => new LiveSceneChecks.InteriorEntry { Name = e.Name.Trim(), IsPattern = e.IsPattern, KnownGood = e.KnownGood })
                    .ToList();
                LiveSceneChecks.SaveInteriorEntries(entries);
                StatusText = $"Saved {entries.Count} entr(y/ies) - {entries.Count(e => e.KnownGood)} protected. Takes effect immediately.";
                _onSaved?.Invoke();
            }
            catch (Exception ex)
            {
                StatusText = "Save failed: " + ex.Message;
                Log.Error("InteriorWhitelist save failed: " + ex);
            }
        }
    }
}
