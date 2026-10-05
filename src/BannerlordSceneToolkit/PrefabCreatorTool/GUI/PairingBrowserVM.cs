using System;
using System.Collections.Generic;
using System.Linq;
using PrefabCreatorTool.Backup;
using PrefabCreatorTool.Core;
using TaleWorlds.Library;

namespace PrefabCreatorTool.GUI
{
    // The pairing/combine flyout, broken out of the main PrefabCreatorPanel into its own popup
    // (mirrors TextureSetBrowserLayer's pattern) per the explicit ask for a dedicated flyout rather
    // than an inline section of the main panel: select a placed base, Display Paired Prefabs shows
    // everything already known to pair with it (direct pairings plus anything from a family it
    // belongs to), click to stage one or more, Combine places them. Add Pairing / Assign To Family
    // below are the authoring side - teaching the tool a new pairing or family membership.
    public class PairingBrowserVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;

        private string _newPairedPrefabNameInput = "";
        private string _combineStatus = "Select ONE placed entity, then Display Paired Prefabs.";
        private MBBindingList<ComboMemberRowVM> _pairedPrefabs;
        private string _currentComboBaseName;
        private string _familyNameInput = "";
        private bool _pairScopeIsFamily;
        private string _pairScopeLabel = "Pairing Scope: This Prefab Only";

        public PairingBrowserVM(Action closeAction, Action beginDragAction)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            _pairedPrefabs = new MBBindingList<ComboMemberRowVM>();
        }

        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();
        public void ExecuteOpenFamilyBrowser() => FamilyBrowserLayer.Toggle();

        [DataSourceProperty]
        public string NewPairedPrefabNameInput
        {
            get => _newPairedPrefabNameInput;
            set { if (value != _newPairedPrefabNameInput) { _newPairedPrefabNameInput = value; OnPropertyChangedWithValue(value, nameof(NewPairedPrefabNameInput)); } }
        }

        [DataSourceProperty]
        public string CombineStatus
        {
            get => _combineStatus;
            set { if (value != _combineStatus) { _combineStatus = value; OnPropertyChangedWithValue(value, nameof(CombineStatus)); } }
        }

        [DataSourceProperty]
        public MBBindingList<ComboMemberRowVM> PairedPrefabs
        {
            get => _pairedPrefabs;
            set { if (value != _pairedPrefabs) { _pairedPrefabs = value; OnPropertyChangedWithValue(value, nameof(PairedPrefabs)); } }
        }

        [DataSourceProperty]
        public string FamilyNameInput
        {
            get => _familyNameInput;
            set { if (value != _familyNameInput) { _familyNameInput = value; OnPropertyChangedWithValue(value, nameof(FamilyNameInput)); } }
        }

        [DataSourceProperty]
        public string PairScopeLabel
        {
            get => _pairScopeLabel;
            set { if (value != _pairScopeLabel) { _pairScopeLabel = value; OnPropertyChangedWithValue(value, nameof(PairScopeLabel)); } }
        }

        public void ExecuteTogglePairScope()
        {
            _pairScopeIsFamily = !_pairScopeIsFamily;
            PairScopeLabel = _pairScopeIsFamily ? "Pairing Scope: Whole Family (named below)" : "Pairing Scope: This Prefab Only";
        }

        public void ExecuteAssignSelectedToFamily()
        {
            if (!EntitySelector.HasOpenScene) { CombineStatus = "No scene is currently open."; return; }
            if (string.IsNullOrWhiteSpace(FamilyNameInput)) { CombineStatus = "Type a family name first."; return; }
            var selection = EntitySelector.GetManualSelection();
            if (selection.Count != 1) { CombineStatus = $"Select exactly ONE placed entity (selected: {selection.Count})."; return; }

            PrefabFamilyStore.AddMemberToFamily(FamilyNameInput.Trim(), selection[0].Name);
            CombineStatus = $"'{selection[0].Name}' is now a member of family '{FamilyNameInput.Trim()}'.";
        }

        public void ExecuteDisplayPairedPrefabs()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { CombineStatus = "No scene is currently open."; return; }
                var selection = EntitySelector.GetManualSelection();
                if (selection.Count != 1) { CombineStatus = $"Select exactly ONE placed entity (selected: {selection.Count})."; return; }

                _currentComboBaseName = selection[0].Name;
                var effective = PrefabComboStore.LoadEffectiveMembers(_currentComboBaseName);

                PairedPrefabs.Clear();
                foreach (var member in effective)
                    PairedPrefabs.Add(MakeRow(member));

                var families = PrefabFamilyStore.GetFamiliesForBase(_currentComboBaseName);
                var familyNote = families.Count > 0 ? $" (member of famil{(families.Count == 1 ? "y" : "ies")}: {string.Join(", ", families)})" : "";
                CombineStatus = effective.Count == 0
                    ? $"No paired prefabs saved yet for '{_currentComboBaseName}'{familyNote}. Use Add Pairing below to define one."
                    : $"{effective.Count} paired prefab(s) for '{_currentComboBaseName}'{familyNote} - click to stage, then Combine.";
            }
            catch (Exception ex)
            {
                CombineStatus = "Display Paired Prefabs failed: " + ex.Message;
                Log.Error("ExecuteDisplayPairedPrefabs failed: " + ex);
            }
        }

        public void ExecuteAddPairing()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { CombineStatus = "No scene is currently open."; return; }
                var selection = EntitySelector.GetManualSelection();

                string pairedName;
                ComboMember offset = null;

                if (selection.Count == 2)
                {
                    var baseEntity = selection[0];
                    var pairedEntity = selection[1];
                    if (string.IsNullOrWhiteSpace(pairedEntity.Name)) { CombineStatus = "The second selected entity has no name - can't determine its prefab."; return; }

                    _currentComboBaseName = baseEntity.Name;
                    pairedName = pairedEntity.Name;
                    offset = PrefabCreatorEngine.CaptureOffset(baseEntity, pairedEntity);
                }
                else if (selection.Count == 1 && !string.IsNullOrWhiteSpace(NewPairedPrefabNameInput))
                {
                    _currentComboBaseName = selection[0].Name;
                    pairedName = NewPairedPrefabNameInput.Trim();
                }
                else
                {
                    CombineStatus = "Either select TWO entities (base, then the paired piece positioned correctly relative to it - offset gets captured exactly), " +
                        "or select ONE entity and type a prefab name below (old shared-origin behavior, no live instance needed).";
                    return;
                }

                // Only auto-fill the texture set when it's unambiguous (exactly one saved for this
                // secondary) - picking whichever one happened to sort first alphabetically when
                // several exist just silently wires in a texture the user never chose. With 2+
                // options the pairing is left untextured and CombineStatus says so below, so the
                // ambiguity is visible instead of resolved wrong.
                var existingPresets = ColorPresetStore.ListForBasePrefab(pairedName);
                var defaultPreset = existingPresets.Count == 1 ? existingPresets[0].Name : null;

                string pairTarget;
                if (_pairScopeIsFamily)
                {
                    if (string.IsNullOrWhiteSpace(FamilyNameInput)) { CombineStatus = "Pairing Scope is Whole Family, but no family name is typed."; return; }
                    pairTarget = FamilyNameInput.Trim();
                    PrefabComboStore.AddFamilyMember(pairTarget, pairedName, defaultPreset, offset);
                }
                else
                {
                    pairTarget = _currentComboBaseName;
                    PrefabComboStore.AddMember(pairTarget, pairedName, defaultPreset, offset);
                }
                NewPairedPrefabNameInput = "";

                var effective = PrefabComboStore.LoadEffectiveMembers(_currentComboBaseName);
                PairedPrefabs.Clear();
                foreach (var member in effective)
                    PairedPrefabs.Add(MakeRow(member));

                var offsetNote = offset != null ? " (exact relative offset captured)" : " (shared-origin - no offset captured)";
                var scopeNote = _pairScopeIsFamily ? $" (family '{pairTarget}')" : $" (this prefab '{pairTarget}')";
                string textureNote;
                if (defaultPreset != null) textureNote = $" - texture set: {defaultPreset}.";
                else if (existingPresets.Count > 1) textureNote = $" - {existingPresets.Count} texture sets exist for '{pairedName}'; click Preset on the row below to pick one.";
                else textureNote = " - no texture set saved for this secondary yet.";

                // Confirmed live confusion: pairing scope defaults to "This Prefab Only" even when
                // the base ALREADY belongs to a family, and family membership never retroactively
                // upgrades an existing direct pairing - so "fief_wine_cupboard_a" and "_br" being in
                // the same family did nothing to make a pairing added while scope was still "This
                // Prefab Only" show up for both. Warn right here, at the moment it matters, instead
                // of leaving it to be discovered later as "why didn't the other variant get this."
                string familyWarning = "";
                if (!_pairScopeIsFamily)
                {
                    var families = PrefabFamilyStore.GetFamiliesForBase(_currentComboBaseName);
                    if (families.Count > 0)
                        familyWarning = $" NOTE: '{_currentComboBaseName}' is also in famil{(families.Count == 1 ? "y" : "ies")} {string.Join(", ", families)} - this pairing only applies to '{_currentComboBaseName}' itself, not other family members. Toggle Pairing Scope to Whole Family first if you want it shared.";
                }

                CombineStatus = $"Paired '{pairedName}'{scopeNote}{offsetNote}{textureNote}{familyWarning}";
            }
            catch (Exception ex)
            {
                CombineStatus = "Add Pairing failed: " + ex.Message;
                Log.Error("ExecuteAddPairing failed: " + ex);
            }
        }

        private ComboMemberRowVM MakeRow(EffectiveComboMember member) =>
            new ComboMemberRowVM(member.StorageKey, member.Member.PrefabName, member.Member.PresetName,
                member.Member.HasOffset ? member.Member : null, member.SourceFamily, member.Member.AutoPlaceOnNewInstance,
                OnToggleStagedRow, OnCyclePresetRow, OnRemovePairingRow, OnToggleAutoPlaceRow);

        private void OnToggleAutoPlaceRow(ComboMemberRowVM row)
        {
            row.AutoPlace = !row.AutoPlace;
            PrefabComboStore.AddMember(row.BasePrefabName, row.PrefabName, row.PresetName, null, row.AutoPlace);
            CombineStatus = row.AutoPlace
                ? $"'{row.PrefabName}' will now be placed automatically whenever this base is swapped in or distributed, in Prefab Swapper/Distribution."
                : $"'{row.PrefabName}' will no longer be placed automatically.";
        }

        private void OnToggleStagedRow(ComboMemberRowVM row) => row.IsStaged = !row.IsStaged;

        private void OnCyclePresetRow(ComboMemberRowVM row)
        {
            var options = ColorPresetStore.ListForBasePrefab(row.PrefabName).Select(p => p.Name).ToList();
            if (options.Count == 0) { CombineStatus = $"No saved texture sets for '{row.PrefabName}' yet."; return; }

            var currentIdx = options.FindIndex(n => string.Equals(n, row.PresetName, StringComparison.OrdinalIgnoreCase));
            var next = options[(currentIdx + 1) % options.Count];
            row.PresetName = next;
            PrefabComboStore.AddMember(row.BasePrefabName, row.PrefabName, next);
        }

        private void OnRemovePairingRow(ComboMemberRowVM row)
        {
            PrefabComboStore.RemoveMember(row.BasePrefabName, row.PrefabName);
            PairedPrefabs.Remove(row);
            CombineStatus = $"Removed pairing '{row.PrefabName}'.";
        }

        public void ExecuteCombineAtOrigin()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { CombineStatus = "No scene is currently open."; return; }
                var selection = EntitySelector.GetManualSelection();
                if (selection.Count != 1) { CombineStatus = $"Select exactly ONE entity to use as the shared pivot (selected: {selection.Count})."; return; }

                var staged = PairedPrefabs.Where(r => r.IsStaged).ToList();
                if (staged.Count == 0) { CombineStatus = "Nothing staged - click paired prefab(s) in the list above first."; return; }

                BackupManager.BackupNow("before-apply");
                var referenceFrame = selection[0].GetGlobalFrame();
                var baseName = selection[0].Name;
                var members = staged.Select(r => new PrefabCreatorEngine.CombineMemberRequest
                {
                    PrefabName = r.PrefabName,
                    PresetName = r.PresetName,
                    Offset = r.Offset,
                }).ToList();

                var result = PrefabCreatorEngine.CombineAtSharedOrigin(EntitySelector.CurrentScene, referenceFrame, members, baseName);

                if (!result.Success) { CombineStatus = "Failed: " + result.Error; return; }

                var msg = $"Created '{result.AnchorEntity.Name}', parented {result.Instantiated.Count} of {staged.Count} staged prefab(s) beneath it.";
                if (result.Failed.Count > 0) msg += $" Failed: {string.Join("; ", result.Failed)}";
                CombineStatus = msg;

                foreach (var row in staged) row.IsStaged = false;
            }
            catch (Exception ex)
            {
                CombineStatus = "Failed: " + ex.Message;
                Log.Error("CombineAtOrigin failed: " + ex);
            }
        }

        public void ExecuteAutoCombineFromSelection()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { CombineStatus = "No scene is currently open."; return; }
                var selection = EntitySelector.GetManualSelection();

                BackupManager.BackupNow("before-apply");
                var result = PrefabCreatorEngine.AutoDetectHostAndCombine(selection, PrefabNaming.Prefix);

                if (!result.Success) { CombineStatus = "Auto-Combine failed: " + result.Error; return; }

                var msg = $"Auto-detected host '{result.Host.Name}' - parented {result.Parented.Count} piece(s) onto it and saved each as a paired prefab.";
                if (result.Warnings.Count > 0) msg += $" ({result.Warnings.Count} warning(s): {string.Join("; ", result.Warnings)})";
                CombineStatus = msg;

                if (string.Equals(_currentComboBaseName, result.Host.Name, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(_currentComboBaseName))
                {
                    _currentComboBaseName = result.Host.Name;
                    var effective = PrefabComboStore.LoadEffectiveMembers(_currentComboBaseName);
                    PairedPrefabs.Clear();
                    foreach (var member in effective)
                        PairedPrefabs.Add(MakeRow(member));
                }
            }
            catch (Exception ex)
            {
                CombineStatus = "Auto-Combine failed: " + ex.Message;
                Log.Error("ExecuteAutoCombineFromSelection failed: " + ex);
            }
        }
    }
}
