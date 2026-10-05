using System;
using System.Collections.Generic;
using System.Linq;
using MaterialSwapTool.Backup;
using MaterialSwapTool.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using BannerlordSceneToolkit;

namespace MaterialSwapTool.GUI
{
    public class MaterialSwapVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;

        private MBBindingList<MaterialSwapRuleVM> _rules;
        private string _filterTerm = "";
        private string _selectionModeLabel = "Manual (editor selection)";
        private string _presetName = "";
        private string _presetTagsInput = "";
        private string _entityColorFactorInput = "";
        private string _statusText = "";
        private bool _tagChanged = true;
        private string _tagChangedLabel = "Tag changed: On";
        private bool _renameChanged;
        private string _renameChangedLabel = "Rename changed: Off";
        private bool _assignUid = true;
        private string _assignUidLabel = "Track by UID: On";
        private int _lastMatchCount;

        // "Fill In Overrides" - see OverrideFillEngine for the full design writeup. A reference
        // entity (A) captured explicitly via its own button, held across clicks the same way
        // _lastLodMismatches carries state between Check and Fix.
        private GameEntity _overrideReference;
        private string _overrideReferenceLabel = "Reference (A): none set";

        public MaterialSwapVM(Action closeAction, Action beginDragAction)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            _rules = new MBBindingList<MaterialSwapRuleVM>();
            _rules.Add(NewRuleVM());
            RefreshValues();
        }

        public void ExecuteDragStart() => _beginDragAction?.Invoke();

        private MaterialSwapRuleVM NewRuleVM(string from = "", string to = "", string colorFactor = "") =>
            new MaterialSwapRuleVM(RemoveRule, from, to, colorFactor);

        private void RemoveRule(MaterialSwapRuleVM rule)
        {
            if (rule != null && Rules.Contains(rule)) Rules.Remove(rule);
        }

        [DataSourceProperty]
        public MBBindingList<MaterialSwapRuleVM> Rules
        {
            get => _rules;
            set { if (value != _rules) { _rules = value; OnPropertyChangedWithValue(value, nameof(Rules)); } }
        }

        [DataSourceProperty]
        public string FilterTerm
        {
            get => _filterTerm;
            set { if (value != _filterTerm) { _filterTerm = value; OnPropertyChangedWithValue(value, nameof(FilterTerm)); } }
        }

        [DataSourceProperty]
        public string SelectionModeLabel
        {
            get => _selectionModeLabel;
            set { if (value != _selectionModeLabel) { _selectionModeLabel = value; OnPropertyChangedWithValue(value, nameof(SelectionModeLabel)); } }
        }

        [DataSourceProperty]
        public string PresetName
        {
            get => _presetName;
            set { if (value != _presetName) { _presetName = value; OnPropertyChangedWithValue(value, nameof(PresetName)); } }
        }

        [DataSourceProperty]
        public string PresetTagsInput
        {
            get => _presetTagsInput;
            set { if (value != _presetTagsInput) { _presetTagsInput = value; OnPropertyChangedWithValue(value, nameof(PresetTagsInput)); } }
        }

        // Entity-wide (GameEntity.SetFactorColor), not tied to any from/to rule - separate from a
        // rule row's own ColorFactor, which only tints one LOD slot (MetaMesh.SetFactor1). Kept
        // as its own clearly-labeled field rather than an "empty rule + color" convention, which
        // would work but be too easy to forget existed.
        [DataSourceProperty]
        public string EntityColorFactorInput
        {
            get => _entityColorFactorInput;
            set { if (value != _entityColorFactorInput) { _entityColorFactorInput = value; OnPropertyChangedWithValue(value, nameof(EntityColorFactorInput)); } }
        }

        // Blank/space/# don't work as "clear the tint" because blank specifically means "don't
        // touch entity-wide color at all" (see hasEntityColor in RunApply) - there was no way to
        // say "explicitly reset this to no tint" at all. #FFFFFFFF (white) is the multiplicative
        // identity for a color factor - same "no-op" convention the engine already uses for
        // Widget.ColorFactor's own default of 1.0 - so setting it here and then hitting
        // Apply/Dry Run runs through the exact same entity-wide color path as any other value,
        // no special-casing needed downstream.
        public void ExecuteClearEntityColor() => EntityColorFactorInput = "#FFFFFFFF";

        [DataSourceProperty]
        public string StatusText
        {
            get => _statusText;
            set { if (value != _statusText) { _statusText = value; OnPropertyChangedWithValue(value, nameof(StatusText)); } }
        }

        [DataSourceProperty]
        public bool TagChanged
        {
            get => _tagChanged;
            set
            {
                if (value != _tagChanged)
                {
                    _tagChanged = value;
                    OnPropertyChangedWithValue(value, nameof(TagChanged));
                    Log.Info($"[CheckboxDiag] TagChanged -> {value}");
                }
            }
        }

        [DataSourceProperty]
        public string TagChangedLabel
        {
            get => _tagChangedLabel;
            set { if (value != _tagChangedLabel) { _tagChangedLabel = value; OnPropertyChangedWithValue(value, nameof(TagChangedLabel)); } }
        }

        // Plain button + Command.Click, matching the pattern already proven to work for
        // Mode/Invert in the preset browser - see the XML comment for why the ButtonType="Toggle"
        // version of these three was abandoned despite the decompile saying it should work.
        public void ExecuteToggleTagChanged()
        {
            TagChanged = !TagChanged;
            TagChangedLabel = TagChanged ? "Tag changed: On" : "Tag changed: Off";
        }

        [DataSourceProperty]
        public bool RenameChanged
        {
            get => _renameChanged;
            set
            {
                if (value != _renameChanged)
                {
                    _renameChanged = value;
                    OnPropertyChangedWithValue(value, nameof(RenameChanged));
                    Log.Info($"[CheckboxDiag] RenameChanged -> {value}");
                }
            }
        }

        [DataSourceProperty]
        public string RenameChangedLabel
        {
            get => _renameChangedLabel;
            set { if (value != _renameChangedLabel) { _renameChangedLabel = value; OnPropertyChangedWithValue(value, nameof(RenameChangedLabel)); } }
        }

        public void ExecuteToggleRenameChanged()
        {
            RenameChanged = !RenameChanged;
            RenameChangedLabel = RenameChanged ? "Rename changed: On" : "Rename changed: Off";
        }

        [DataSourceProperty]
        public bool AssignUid
        {
            get => _assignUid;
            set
            {
                if (value != _assignUid)
                {
                    _assignUid = value;
                    OnPropertyChangedWithValue(value, nameof(AssignUid));
                    Log.Info($"[CheckboxDiag] AssignUid -> {value}");
                }
            }
        }

        [DataSourceProperty]
        public string AssignUidLabel
        {
            get => _assignUidLabel;
            set { if (value != _assignUidLabel) { _assignUidLabel = value; OnPropertyChangedWithValue(value, nameof(AssignUidLabel)); } }
        }

        public void ExecuteToggleAssignUid()
        {
            AssignUid = !AssignUid;
            AssignUidLabel = AssignUid ? "Track by UID: On" : "Track by UID: Off";
        }

        [DataSourceProperty]
        public string OverrideReferenceLabel
        {
            get => _overrideReferenceLabel;
            set { if (value != _overrideReferenceLabel) { _overrideReferenceLabel = value; OnPropertyChangedWithValue(value, nameof(OverrideReferenceLabel)); } }
        }

        private SelectionMode _mode = SelectionMode.Manual;

        public void ExecuteSelectManual() { Log.Info("[SelectionDiag] Manual button clicked"); _mode = SelectionMode.Manual; SelectionModeLabel = "Manual (editor selection)"; RefreshPreview(); }
        public void ExecuteSelectFiltered() { _mode = SelectionMode.Filtered; SelectionModeLabel = $"Filtered: \"{FilterTerm}\""; RefreshPreview(); }
        public void ExecuteSelectWholeScene() { _mode = SelectionMode.WholeScene; SelectionModeLabel = "Whole scene"; RefreshPreview(); }

        public void ExecuteOpenDocumentation() => DocumentationLayer.Toggle();
        public void ExecuteOpenContinuousRecolor() => ContinuousRecolorLayer.Open();

        public void ExecuteAddRule() => Rules.Add(NewRuleVM());

        // "No right-side" covers both a genuinely empty row (added via + Add Rule, or the leftover
        // default row, never filled in) and a row where only FromMaterial got typed - either way
        // there's nothing there for Apply to act on, so both count as blank for this purpose.
        public void ExecuteDeleteBlankRules()
        {
            var blank = Rules.Where(r => string.IsNullOrWhiteSpace(r.ToMaterial)).ToList();
            foreach (var r in blank) Rules.Remove(r);
            if (Rules.Count == 0) Rules.Add(NewRuleVM());
            StatusText = blank.Count == 0 ? "No blank rules to delete." : $"Deleted {blank.Count} blank rule(s).";
        }

        // Flips FromMaterial/ToMaterial on every rule currently in the list, in place - the
        // Preset Browser's own Invert only flips as a preset LOADS, which doesn't help a rule set
        // you've built or hand-edited without going through a preset at all.
        // SELECT IN EDITOR - shows you what the rules actually touch, before you Apply.
        //
        // "Select Rule Matches" answers the question Dry Run can only answer as a number: WHICH
        // entities. Selecting them puts the answer in the viewport instead of the status line.
        // Matching mirrors MaterialSwapEngine's own rule lookup (ruleMap keyed by FromMaterial,
        // case-insensitive, against each mesh's current material) so the selection is exactly the
        // set Apply would touch - not an approximation of it.
        public void ExecuteSelectRuleMatches()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }

                var froms = new System.Collections.Generic.HashSet<string>(
                    Rules.Select(r => Norm(r.FromMaterial)).Where(s => !string.IsNullOrWhiteSpace(s)),
                    StringComparer.OrdinalIgnoreCase);
                if (froms.Count == 0) { StatusText = "No rules with a From material to match."; return; }

                var all = new List<GameEntity>();
                EntitySelector.CurrentScene.GetEntities(ref all);
                var n = LiveSceneChecks.SelectInEditor(all, e => EntityUsesAnyMaterial(e, froms));
                StatusText = n == 0
                    ? $"Nothing in the scene uses any of the {froms.Count} From material(s)."
                    : $"Selected {n} entit(y/ies) matching {froms.Count} From material(s) - this is what Apply would touch.";
            }
            catch (Exception ex)
            {
                StatusText = "Select rule matches failed: " + ex.Message;
                Log.Error("SelectRuleMatches failed: " + ex);
            }
        }

        // Selects everything using the material typed in the filter box - the fastest way to answer
        // "where is this material actually used?" without writing a rule for it first.
        public void ExecuteSelectByFilterMaterial()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }
                var term = Norm(FilterTerm);
                if (string.IsNullOrWhiteSpace(term)) { StatusText = "Type a material name in the filter box first."; return; }

                var all = new List<GameEntity>();
                EntitySelector.CurrentScene.GetEntities(ref all);
                // Substring, not exact: the filter box is used for partial names everywhere else in
                // this panel, and "stone_wall" finding every stone_wall_* is the useful behaviour.
                var n = LiveSceneChecks.SelectInEditor(all, e => EntityUsesMaterialLike(e, term));
                StatusText = n == 0
                    ? $"No entity uses a material matching '{term}'."
                    : $"Selected {n} entit(y/ies) using a material matching '{term}'.";
            }
            catch (Exception ex)
            {
                StatusText = "Select by material failed: " + ex.Message;
                Log.Error("SelectByFilterMaterial failed: " + ex);
            }
        }

        private static bool EntityUsesAnyMaterial(GameEntity e, System.Collections.Generic.HashSet<string> materials)
        {
            for (int m = 0; m < e.MultiMeshComponentCount; m++)
            {
                var meta = e.GetMetaMesh(m);
                if (meta == null || !meta.IsValid) continue;
                for (int i = 0; i < meta.MeshCount; i++)
                {
                    var name = meta.GetMeshAtIndex(i)?.GetMaterial()?.Name;
                    if (name != null && materials.Contains(name)) return true;
                }
            }
            return false;
        }

        private static bool EntityUsesMaterialLike(GameEntity e, string term)
        {
            for (int m = 0; m < e.MultiMeshComponentCount; m++)
            {
                var meta = e.GetMetaMesh(m);
                if (meta == null || !meta.IsValid) continue;
                for (int i = 0; i < meta.MeshCount; i++)
                {
                    var name = meta.GetMeshAtIndex(i)?.GetMaterial()?.Name;
                    if (name != null && name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            return false;
        }

        // Destructive and easy to hit by accident next to Fill Rules From Entity, so it confirms
        // first and reports the count it's about to discard rather than just emptying the list.
        // Clears ONE SIDE of every rule, leaving the rows and the other side intact - distinct
        // from Clear All Rules, which removes the rows entirely.
        //
        // Clearing the OUTPUT side is the useful one in practice: it produces exactly the "blank
        // rule" state (FromMaterial set, ToMaterial empty) that Fill Rules From Entity and Fill In
        // Overrides consume, so a preset can be stripped back to its input list and re-targeted at
        // a different prefab without retyping every FROM material.
        //
        // Clearing the INPUT side is the mirror of that, for re-pointing a set of targets at
        // different sources. Both confirm first, because a full rule list is a lot of typing to
        // lose and there is no undo for the rules list (EditUndo covers scene entities, not this).
        public void ExecuteClearInputMaterials() => ClearOneSide(input: true);

        public void ExecuteClearOutputMaterials() => ClearOneSide(input: false);

        private void ClearOneSide(bool input)
        {
            var side = input ? "input (FROM)" : "output (TO)";
            var count = input
                ? Rules.Count(r => !string.IsNullOrWhiteSpace(r.FromMaterial))
                : Rules.Count(r => !string.IsNullOrWhiteSpace(r.ToMaterial));

            if (count == 0) { StatusText = $"No {side} materials to clear."; return; }

            var body = input
                ? $"This blanks the FROM material on {count} rule(s), keeping the rows and their TO materials. " +
                  "Presets already saved to disk are not affected."
                : $"This blanks the TO material on {count} rule(s), keeping the rows and their FROM materials - " +
                  "the same state Fill Rules From Entity and Fill In Overrides expect. " +
                  "Presets already saved to disk are not affected.";

            InformationManager.ShowInquiry(new InquiryData(
                $"Clear all {side} materials?",
                body,
                isAffirmativeOptionShown: true,
                isNegativeOptionShown: true,
                affirmativeText: "Clear",
                negativeText: "Cancel",
                affirmativeAction: () =>
                {
                    foreach (var rule in Rules)
                    {
                        if (input) rule.FromMaterial = "";
                        else rule.ToMaterial = "";
                    }
                    StatusText = $"Cleared the {side} material on {count} rule(s).";
                    Log.Info($"[Rules] cleared {side} on {count} rule(s).");
                },
                negativeAction: null));
        }

        public void ExecuteClearAllRules()
        {
            var count = Rules.Count(r => !string.IsNullOrWhiteSpace(r.FromMaterial) || !string.IsNullOrWhiteSpace(r.ToMaterial));
            if (count == 0) { StatusText = "No rules to clear."; return; }

            InformationManager.ShowInquiry(new InquiryData(
                "Clear all rules?",
                $"This removes all {count} rule(s) from the list. Presets already saved to disk are not affected.",
                isAffirmativeOptionShown: true,
                isNegativeOptionShown: true,
                affirmativeText: "Clear All",
                negativeText: "Cancel",
                affirmativeAction: () =>
                {
                    Rules.Clear();
                    Rules.Add(NewRuleVM());
                    StatusText = $"Cleared {count} rule(s).";
                },
                negativeAction: null));
        }

        public void ExecuteInvertRules()
        {
            int count = 0;
            foreach (var r in Rules)
            {
                if (string.IsNullOrWhiteSpace(r.FromMaterial) && string.IsNullOrWhiteSpace(r.ToMaterial)) continue;
                (r.FromMaterial, r.ToMaterial) = (r.ToMaterial, r.FromMaterial);
                count++;
            }
            StatusText = count == 0 ? "No rules to invert." : $"Inverted {count} rule(s).";
        }

        // TWO dedupe flavors (2026-08-23, settled after two corrections - by example:
        // "roman_roof:5, timber_frame_c:5 modifies to 'roman_roof' after Dedupe (Multi-Output)"):
        //
        // Dedupe (Rows) - one ROW per input material: the first row with a given FromMaterial
        // wins, every later row with that input is deleted, whatever kind of row it is.
        public void ExecuteDedupeRows()
        {
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var extras = new List<MaterialSwapRuleVM>();
            foreach (var r in Rules)
            {
                if (string.IsNullOrWhiteSpace(r.FromMaterial)) continue;
                if (!seen.Add(Norm(r.FromMaterial))) extras.Add(r);
            }
            foreach (var r in extras) Rules.Remove(r);
            if (Rules.Count == 0) Rules.Add(NewRuleVM());
            StatusText = extras.Count == 0
                ? "No repeated input materials found."
                : $"Removed {extras.Count} row(s) - one row per input material, first kept.";
        }

        // Dedupe (Multi-Output) - collapses each WEIGHTED spec to its FIRST output:
        // "roman_roof:5, timber_frame_c:5" becomes "roman_roof". No rows are added or removed;
        // the alternatives inside each rule are what get deduped.
        public void ExecuteDedupeMultiOutput()
        {
            int collapsed = 0;
            foreach (var r in Rules)
            {
                if (!Core.WeightedTarget.IsWeighted(r.ToMaterial)) continue;
                var options = Core.WeightedTarget.Parse(r.ToMaterial);
                if (options.Count == 0) continue;
                r.ToMaterial = options[0].Material;
                collapsed++;
            }
            StatusText = collapsed == 0
                ? "No weighted (multi-output) rules found."
                : $"Collapsed {collapsed} weighted rule(s) to their first output.";
        }

        // Reads whatever's currently selected in the editor viewport (independent of the
        // Manual/Filtered/WholeScene mode toggle above, which only governs Apply/Dry Run's
        // target set) and seeds one new blank-TO rule row per distinct material found, skipping
        // any material that already has a rule (by FromMaterial, case-insensitive).
        public void ExecuteGetInputFromSelection()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }

                var selected = EntitySelector.GetTargets(SelectionMode.Manual);
                if (selected.Count == 0) { StatusText = "Nothing selected in the editor."; return; }

                var existing = new System.Collections.Generic.HashSet<string>(
                    Rules.Select(r => Norm(r.FromMaterial)), StringComparer.OrdinalIgnoreCase);

                int added = 0;
                foreach (var mat in EntitySelector.GetDistinctMaterialsOnEntities(selected))
                {
                    if (!existing.Add(Norm(mat))) continue;
                    Rules.Add(NewRuleVM(mat));
                    added++;
                }

                StatusText = added == 0
                    ? $"No new materials on {selected.Count} selected entity(ies) - already covered by existing rules."
                    : $"Added {added} rule(s) from {selected.Count} selected entity(ies).";
            }
            catch (Exception ex)
            {
                StatusText = "Get Input Rules failed: " + ex.Message;
                Log.Error("GetInputFromSelection failed: " + ex);
            }
        }

        // Captures whatever's currently manually selected (must be exactly one entity) as the
        // reference (A) for Fill In Overrides below. Held until replaced or the panel is closed.
        public void ExecuteSetOverrideReference()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }

                var selected = EntitySelector.GetTargets(SelectionMode.Manual);
                if (selected.Count != 1)
                {
                    StatusText = $"Select exactly one entity to use as the reference (A) - {selected.Count} selected.";
                    return;
                }

                _overrideReference = selected[0];
                OverrideReferenceLabel = $"Reference (A): {_overrideReference.Name}";
                StatusText = $"Reference set to '{_overrideReference.Name}'. Now select a different entity (B) and click Fill In Overrides.";
            }
            catch (Exception ex)
            {
                StatusText = "Set Reference failed: " + ex.Message;
                Log.Error("SetOverrideReference failed: " + ex);
            }
        }

        // Reads whatever's currently manually selected as the target (B), finds every mesh slot on
        // B (and its children) whose CURRENT material matches a material found somewhere on the
        // reference (A), and stages B's color for that slot to match A's - see OverrideFillEngine.
        public void ExecuteFillInOverrides()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }
                if (_overrideReference == null || !EntitySelector.IsValidEntity(_overrideReference))
                {
                    StatusText = "Set a reference entity (A) first.";
                    return;
                }

                var selected = EntitySelector.GetTargets(SelectionMode.Manual);
                if (selected.Count != 1)
                {
                    StatusText = $"Select exactly one target entity (B) - {selected.Count} selected.";
                    return;
                }

                var target = selected[0];
                if (target.Pointer == _overrideReference.Pointer)
                {
                    StatusText = "Target (B) is the same entity as the reference (A) - select a different entity.";
                    return;
                }

                var changes = OverrideFillEngine.PreviewColorFill(_overrideReference, target);
                if (changes.Count == 0)
                {
                    StatusText = $"No matching materials with a different color between '{_overrideReference.Name}' (A) and '{target.Name}' (B).";
                    return;
                }

                var refName = _overrideReference.Name;
                var targetName = target.Name;
                var inquiry = new InquiryData(
                    "Fill in overrides?",
                    $"This will set {changes.Count} mesh slot color(s) on '{targetName}' (and its children) to match " +
                    $"'{refName}' wherever the material already matches. A backup is taken first. Continue?",
                    isAffirmativeOptionShown: true,
                    isNegativeOptionShown: true,
                    affirmativeText: "Fill In",
                    negativeText: "Cancel",
                    affirmativeAction: () => RunFillInOverrides(changes, refName, targetName),
                    negativeAction: null);
                InformationManager.ShowInquiry(inquiry);
            }
            catch (Exception ex)
            {
                StatusText = "Fill In Overrides failed: " + ex.Message;
                Log.Error("FillInOverrides failed: " + ex);
            }
        }

        // Infer Material from A (Matching Prefab). A is the input entity; whatever is selected now
        // is the matching prefab B. Produces material->material RULES from a slot-for-slot walk of
        // both hierarchies - it does not touch the scene. Nothing applies until Apply.
        public void ExecuteInferMaterialRules()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }
                if (_overrideReference == null || !EntitySelector.IsValidEntity(_overrideReference))
                {
                    StatusText = "Set an input entity (A) first.";
                    return;
                }

                var selected = EntitySelector.GetTargets(SelectionMode.Manual);
                if (selected.Count != 1)
                {
                    StatusText = $"Select exactly one matching prefab (B) - {selected.Count} selected.";
                    return;
                }

                var target = selected[0];
                if (target.Pointer == _overrideReference.Pointer)
                {
                    StatusText = "B is the same entity as A - select the other prefab.";
                    return;
                }

                var inferred = OverrideFillEngine.InferMaterialRules(_overrideReference, target);
                if (inferred.Rules.Count == 0)
                {
                    StatusText = inferred.SlotsCompared == 0
                        ? $"No comparable mesh slots between '{_overrideReference.Name}' (A) and '{target.Name}' (B)."
                        : $"A and B already use identical materials on all {inferred.SlotsCompared} compared slot(s) - nothing to infer.";
                    return;
                }

                var existing = new System.Collections.Generic.HashSet<string>(
                    Rules.Select(r => Norm(r.FromMaterial)), StringComparer.OrdinalIgnoreCase);

                int added = 0, skipped = 0;
                foreach (var rule in inferred.Rules)
                {
                    if (!existing.Add(Norm(rule.FromMaterial))) { skipped++; continue; }
                    Rules.Add(NewRuleVM(rule.FromMaterial, rule.ToMaterial, rule.ColorFactor));
                    added++;
                }

                var notes = new System.Collections.Generic.List<string>();
                if (skipped > 0) notes.Add($"{skipped} already had a rule");
                if (inferred.Weighted.Count > 0) notes.Add($"{inferred.Weighted.Count} weighted (same material mapped several ways)");
                if (inferred.StructureMismatch) notes.Add("STRUCTURE MISMATCH - " + inferred.MismatchNote);

                StatusText = $"Inferred {added} material rule(s) from {inferred.SlotsCompared} slot(s) across {inferred.EntitiesPaired} entity(ies)"
                             + (notes.Count == 0 ? "." : " (" + string.Join("; ", notes) + ").");
            }
            catch (Exception ex)
            {
                StatusText = "Infer Material failed: " + ex.Message;
                Log.Error("InferMaterialRules failed: " + ex);
            }
        }

        // SLOT-EXACT sibling of Infer Material (2026-08-23, "shouldn't it be overriding stuff
        // more selectively?"): same A + selected-B pairing, but instead of producing name rules
        // (which collapse "one material used four different ways" into a weighted dice roll) it
        // directly sets each of A's mesh slots to the material B carries in the same slot.
        // Changes A in the scene - confirmed first, backed up, and undoable like any batch.
        public void ExecuteApplyMaterialLayout()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }
                if (_overrideReference == null || !EntitySelector.IsValidEntity(_overrideReference))
                {
                    StatusText = "Set an input entity (A) first.";
                    return;
                }

                var selected = EntitySelector.GetTargets(SelectionMode.Manual);
                if (selected.Count != 1)
                {
                    StatusText = $"Select exactly one matching prefab (B) - {selected.Count} selected.";
                    return;
                }

                var target = selected[0];
                if (target.Pointer == _overrideReference.Pointer)
                {
                    StatusText = "B is the same entity as A - select the other prefab.";
                    return;
                }

                // Dry pass first so the confirmation can say exactly what it will do.
                var preview = OverrideFillEngine.ApplyMaterialLayout(_overrideReference, target,
                    batchId: "", sceneName: "", dryRun: true);
                if (preview.SlotsChanged == 0)
                {
                    StatusText = preview.SlotsCompared == 0
                        ? $"No comparable mesh slots between '{_overrideReference.Name}' (A) and '{target.Name}' (B)."
                        : $"All {preview.SlotsCompared} compared slot(s) already match - nothing to change.";
                    return;
                }

                var a = _overrideReference;
                var mismatchNote = preview.StructureMismatch ? $"\n\nWARNING: {preview.MismatchNote}" : "";
                var inquiry = new InquiryData(
                    "Apply B's material layout to A?",
                    $"This will set {preview.SlotsChanged} mesh slot(s) on '{a.Name}' (A, and its children) to the material " +
                    $"'{target.Name}' (B) carries in the same slot - slot-exact, no weighted guessing. " +
                    $"A backup is taken first and the batch is undoable.{mismatchNote}",
                    isAffirmativeOptionShown: true,
                    isNegativeOptionShown: true,
                    affirmativeText: "Apply Layout",
                    negativeText: "Cancel",
                    affirmativeAction: () => RunApplyMaterialLayout(a, target),
                    negativeAction: null);
                InformationManager.ShowInquiry(inquiry);
            }
            catch (Exception ex)
            {
                StatusText = "Apply Layout failed: " + ex.Message;
                Log.Error("ApplyMaterialLayout failed: " + ex);
            }
        }

        private void RunApplyMaterialLayout(GameEntity a, GameEntity b)
        {
            try
            {
                BackupManager.BackupNow("before-apply");
                var batchId = ChangeLogger.NewBatchId();
                var sceneName = EntitySelector.CurrentSceneName;
                var result = OverrideFillEngine.ApplyMaterialLayout(a, b, batchId, sceneName, dryRun: false);
                if (result.Entries.Count > 0) ChangeLogger.Append(result.Entries);

                StatusText = $"Applied B's layout: {result.SlotsChanged} slot(s) on '{a.Name}' set to '{b.Name}''s materials"
                             + (result.StructureMismatch ? $" (WARNING: {result.MismatchNote})" : ".");
                if (result.SlotsChanged > 0)
                {
                    BackupManager.ArmSaveReminder();
                    ScreenshotManager.CaptureForBatch(batchId);
                }
            }
            catch (Exception ex)
            {
                StatusText = "Apply Layout failed: " + ex.Message;
                Log.Error("RunApplyMaterialLayout failed: " + ex);
            }
        }

        // GENERATE-RULES mode for the same Reference (A): instead of writing colors onto one
        // target entity, turn A's per-material colors into rule rows. See
        // OverrideFillEngine.GenerateColorRules for why this is the more useful mode for bulk
        // work - the output is a preset you can Apply to the whole scene, not a one-off edit.
        // Existing rules are never overwritten: a material that already has a row is skipped, so
        // this can be run on top of a rule set you've already started.
        public void ExecuteGenerateRulesFromReference()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }
                if (_overrideReference == null || !EntitySelector.IsValidEntity(_overrideReference))
                {
                    StatusText = "Set a reference entity (A) first.";
                    return;
                }

                var generated = OverrideFillEngine.GenerateColorRules(_overrideReference);
                if (generated.Count == 0)
                {
                    StatusText = $"'{_overrideReference.Name}' has no non-white material colors to turn into rules.";
                    return;
                }

                var existing = new System.Collections.Generic.HashSet<string>(
                    Rules.Select(r => Norm(r.FromMaterial)), StringComparer.OrdinalIgnoreCase);

                int added = 0, skipped = 0;
                foreach (var rule in generated)
                {
                    if (!existing.Add(Norm(rule.FromMaterial))) { skipped++; continue; }
                    Rules.Add(NewRuleVM(rule.FromMaterial, rule.ToMaterial, rule.ColorFactor));
                    added++;
                }

                StatusText = skipped == 0
                    ? $"Generated {added} color rule(s) from '{_overrideReference.Name}' (A)."
                    : $"Generated {added} color rule(s) from '{_overrideReference.Name}' (A); skipped {skipped} material(s) that already had a rule.";
            }
            catch (Exception ex)
            {
                StatusText = "Generate Rules failed: " + ex.Message;
                Log.Error("GenerateRulesFromReference failed: " + ex);
            }
        }

        private void RunFillInOverrides(List<OverrideFillEngine.ColorFillChange> changes, string refName, string targetName)
        {
            try
            {
                BackupManager.BackupNow("before-apply");
                var batchId = ChangeLogger.NewBatchId();
                var entries = new List<ChangeLogEntry>();
                var sceneName = EntitySelector.CurrentSceneName;
                var applied = OverrideFillEngine.ApplyColorFill(changes, batchId, entries, sceneName);
                if (entries.Count > 0) ChangeLogger.Append(entries);

                StatusText = $"Filled in {applied} override(s) on '{targetName}' from '{refName}'.";
                if (applied > 0)
                {
                    BackupManager.ArmSaveReminder();
                    ScreenshotManager.CaptureForBatch(batchId);
                }
            }
            catch (Exception ex)
            {
                StatusText = "Fill In Overrides failed: " + ex.Message;
                Log.Error("RunFillInOverrides failed: " + ex);
            }
        }

        // Alternate mode of Get Input Rules from Selection, for prefabs where some copies of a
        // repeated part were already manually swapped and others weren't - see OverrideFillEngine.
        // PreviewRuleFill. Only fills the ToMaterial of rules already blank; never adds new rows,
        // never overwrites a row that already has a target.
        public void ExecuteFillRulesFromEntity()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }

                var blankFrom = Rules
                    .Where(r => !string.IsNullOrWhiteSpace(r.FromMaterial) && string.IsNullOrWhiteSpace(r.ToMaterial))
                    .Select(r => r.FromMaterial.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (blankFrom.Count == 0)
                {
                    StatusText = "No blank rules to fill - run Get Input Rules from Selection first.";
                    return;
                }

                var selected = EntitySelector.GetTargets(SelectionMode.Manual);
                if (selected.Count == 0) { StatusText = "Nothing selected - select the prefab to search within."; return; }

                var result = OverrideFillEngine.PreviewRuleFill(selected, blankFrom);

                int filled = 0;
                foreach (var rowVm in Rules)
                {
                    if (!string.IsNullOrWhiteSpace(rowVm.ToMaterial)) continue;
                    if (string.IsNullOrWhiteSpace(rowVm.FromMaterial)) continue;
                    if (!result.Fills.TryGetValue(rowVm.FromMaterial.Trim(), out var to)) continue;
                    rowVm.ToMaterial = to;
                    filled++;
                }

                var ambiguousNote = result.Ambiguous.Count > 0
                    ? $" ({result.Ambiguous.Count} ambiguous - multiple different materials found for: {string.Join(", ", result.Ambiguous)})"
                    : "";
                StatusText = filled == 0
                    ? $"No matching name+tags group with a different material found for any blank rule.{ambiguousNote}"
                    : $"Filled {filled} rule(s) from matching entities in the selected prefab(s).{ambiguousNote}";
            }
            catch (Exception ex)
            {
                StatusText = "Fill Rules From Entity failed: " + ex.Message;
                Log.Error("FillRulesFromEntity failed: " + ex);
            }
        }

        // Diagnostic only: logs exact per-slot data (MetaMeshIndex, MeshIndex, prefab/node name,
        // current material, LOD mask) for whatever's currently selected, to tool.log. Built
        // specifically to verify whether our MultiMeshComponentCount/MeshCount loop's (m, i)
        // indices correspond to mesh_slot_map.csv's (Lod, SlotIndex) columns before trusting that
        // mapping for the LOD Mismatch Checker or Revert to Normal - cross-reference the logged
        // rows against the CSV for the same prefab name.
        public void ExecuteDumpSlotInfo()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }

                var targets = EntitySelector.GetTargets(SelectionMode.Manual);
                if (targets.Count == 0) { StatusText = "Nothing selected to dump."; return; }

                foreach (var entity in targets)
                {
                    Log.Info($"[SlotDump] entity='{entity.Name}' multiMeshComponentCount={entity.MultiMeshComponentCount} " +
                             $"entityFactorColor={ColorHex.ToHex(entity.GetFactorColor())}");
                    for (int m = 0; m < entity.MultiMeshComponentCount; m++)
                    {
                        var meta = entity.GetMetaMesh(m);
                        if (meta == null || !meta.IsValid) continue;
                        var metaName = meta.GetName();
                        Log.Info($"[SlotDump] m={m} metaMeshName='{metaName}' metaMeshFactor1={ColorHex.ToHex(meta.GetFactor1())}");
                        for (int i = 0; i < meta.MeshCount; i++)
                        {
                            var mesh = meta.GetMeshAtIndex(i);
                            if (mesh == null) continue;
                            var lodMask = meta.GetLodMaskForMeshAtIndex(i);
                            Log.Info($"[SlotDump]   m={m} i={i} meshName='{mesh.Name}' " +
                                     $"material='{mesh.GetMaterial()?.Name}' lodMask={lodMask} " +
                                     $"meshColor={mesh.Color:X8} meshColor2={mesh.Color2:X8}");
                        }
                    }
                }

                StatusText = $"Dumped slot info for {targets.Count} entit{(targets.Count == 1 ? "y" : "ies")} to tool.log - search for [SlotDump].";
            }
            catch (Exception ex)
            {
                StatusText = "Dump Slot Info failed: " + ex.Message;
                Log.Error("DumpSlotInfo failed: " + ex);
            }
        }

        // Compares each entity's full-detail mesh slots against their LOD5 counterparts (matched
        // by name, not index - see LodMismatchChecker) and flags any pair whose materials
        // disagree. A slot with no counterpart in the other tier is normal LOD simplification,
        // not a mismatch, and is never flagged.
        private List<LodMismatchChecker.Mismatch> _lastLodMismatches;

        public void ExecuteCheckLodMismatches()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }

                var targets = EntitySelector.GetTargets(_mode, FilterTerm);
                if (targets.Count == 0) { StatusText = "Nothing in scope to check."; return; }

                _lastLodMismatches = LodMismatchChecker.Check(targets);
                if (_lastLodMismatches.Count == 0)
                {
                    StatusText = $"No LOD mismatches found across {targets.Count} entit{(targets.Count == 1 ? "y" : "ies")}.";
                    return;
                }

                var lines = _lastLodMismatches.Select(mm => mm.ColorFull.HasValue
                    ? $"{mm.EntityName}: [COLOR] {mm.FullDetailMeshName}={ColorHex.ToHex(mm.ColorFull.Value)}  vs  {mm.LodMeshName}={ColorHex.ToHex(mm.ColorLod.Value)}"
                    : $"{mm.EntityName}: [MATERIAL] {mm.FullDetailMeshName}='{mm.MaterialFull}'  vs  {mm.LodMeshName}='{mm.MaterialLod}'").ToList();

                var colorCount = _lastLodMismatches.Count(mm => mm.ColorFull.HasValue);
                DiffPreviewLayer.Open(
                    $"{_lastLodMismatches.Count} LOD mismatch(es) across {targets.Count} entit{(targets.Count == 1 ? "y" : "ies")} " +
                    $"({colorCount} color, {_lastLodMismatches.Count - colorCount} material):",
                    lines);
                StatusText = colorCount > 0
                    ? $"Found {_lastLodMismatches.Count} LOD mismatch(es), {colorCount} of them color - click Fix Color Mismatches to sync LOD5 to the full-detail color."
                    : $"Found {_lastLodMismatches.Count} LOD mismatch(es) - see the list.";
            }
            catch (Exception ex)
            {
                StatusText = "LOD mismatch check failed: " + ex.Message;
                Log.Error("CheckLodMismatches failed: " + ex);
            }
        }

        // Both Fix buttons used to refuse outright unless Check LOD Mismatches had been pressed
        // first, purely because they read the list Check happened to leave behind. There was
        // never a technical reason - the scan is the same scan, and it is cheap. They now run it
        // themselves when nothing is queued, and say so. Check remains useful for REVIEWING the
        // list before fixing, which is a choice rather than a prerequisite.
        private bool EnsureLodMismatches(out string error)
        {
            error = null;
            if (_lastLodMismatches != null && _lastLodMismatches.Count > 0) return true;

            if (!EntitySelector.HasOpenScene) { error = "No scene is currently open."; return false; }

            var targets = EntitySelector.GetTargets(_mode, FilterTerm);
            if (targets.Count == 0) { error = "Nothing in scope to check."; return false; }

            _lastLodMismatches = LodMismatchChecker.Check(targets);
            if (_lastLodMismatches.Count == 0)
            {
                error = $"No LOD mismatches found across {targets.Count} entit{(targets.Count == 1 ? "y" : "ies")} - nothing to fix.";
                return false;
            }
            return true;
        }

        // Syncs each LOD5 slot's color to match its full-detail counterpart, since that's what
        // you actually see up close - a color mismatch is never intentional, so this just runs.
        public void ExecuteFixColorMismatches()
        {
            if (!EnsureLodMismatches(out var scanError)) { StatusText = scanError; return; }
            var colorMismatches = _lastLodMismatches.Where(mm => mm.ColorFull.HasValue).ToList();
            if (colorMismatches.Count == 0)
            {
                StatusText = "No color mismatches in the last check - nothing to fix.";
                return;
            }

            var inquiry = new InquiryData(
                "Fix LOD color mismatches?",
                $"This will set {colorMismatches.Count} LOD5 mesh slot(s)' color to match their full-detail counterpart, " +
                "so the color doesn't visibly change when the LOD switches. A backup is taken first. Continue?",
                isAffirmativeOptionShown: true,
                isNegativeOptionShown: true,
                affirmativeText: "Fix",
                negativeText: "Cancel",
                affirmativeAction: () => RunFixColorMismatches(colorMismatches),
                negativeAction: null);
            InformationManager.ShowInquiry(inquiry);
        }

        private void RunFixColorMismatches(List<LodMismatchChecker.Mismatch> colorMismatches)
        {
            try
            {
                BackupManager.BackupNow("before-apply");
                var batchId = ChangeLogger.NewBatchId();
                var entries = new List<ChangeLogEntry>();
                var sceneName = EntitySelector.CurrentSceneName;
                var fixedCount = LodMismatchChecker.FixColorMismatches(colorMismatches, batchId, entries, sceneName);
                if (entries.Count > 0) ChangeLogger.Append(entries);

                StatusText = $"Fixed {fixedCount} LOD color mismatch(es).";
                if (fixedCount > 0)
                {
                    BackupManager.ArmSaveReminder();
                    ScreenshotManager.CaptureForBatch(batchId);
                }
                // Only drop the color entries just handled, not the whole list - a subsequent
                // Fix Material Mismatches click should still see whatever material mismatches were
                // found in the same check.
                _lastLodMismatches = _lastLodMismatches?.Where(mm => !colorMismatches.Contains(mm)).ToList();
            }
            catch (Exception ex)
            {
                StatusText = "Fix color mismatches failed: " + ex.Message;
                Log.Error("FixColorMismatches failed: " + ex);
            }
        }

        // Syncs each LOD5 slot's material to match its full-detail counterpart. Confirm-gated with
        // an explicit warning (unlike color's fix above) - see LodMismatchChecker.
        // FixMaterialMismatches for why: a LOD tier's material can legitimately be a deliberate
        // cheaper substitute, not necessarily a bug, so this needs a human's go-ahead.
        public void ExecuteFixMaterialMismatches()
        {
            if (!EnsureLodMismatches(out var scanError)) { StatusText = scanError; return; }
            var materialMismatches = _lastLodMismatches.Where(mm => mm.MaterialFull != null).ToList();
            if (materialMismatches.Count == 0)
            {
                StatusText = "No material mismatches in the last check - nothing to fix.";
                return;
            }

            var inquiry = new InquiryData(
                "Fix LOD material mismatches?",
                $"This will set {materialMismatches.Count} LOD5 mesh slot(s)' material to match their full-detail " +
                "counterpart. CAUTION: unlike a color mismatch, a LOD tier using a different material can be " +
                "intentional (a deliberately cheaper substitute at distance) rather than a bug - review the list " +
                "before confirming if you're not sure. A backup is taken first. Continue?",
                isAffirmativeOptionShown: true,
                isNegativeOptionShown: true,
                affirmativeText: "Fix",
                negativeText: "Cancel",
                affirmativeAction: () => RunFixMaterialMismatches(materialMismatches),
                negativeAction: null);
            InformationManager.ShowInquiry(inquiry);
        }

        private void RunFixMaterialMismatches(List<LodMismatchChecker.Mismatch> materialMismatches)
        {
            try
            {
                BackupManager.BackupNow("before-apply");
                var batchId = ChangeLogger.NewBatchId();
                var entries = new List<ChangeLogEntry>();
                var sceneName = EntitySelector.CurrentSceneName;
                var fixedCount = LodMismatchChecker.FixMaterialMismatches(materialMismatches, batchId, entries, sceneName);
                if (entries.Count > 0) ChangeLogger.Append(entries);

                StatusText = $"Fixed {fixedCount} LOD material mismatch(es).";
                if (fixedCount > 0)
                {
                    BackupManager.ArmSaveReminder();
                    ScreenshotManager.CaptureForBatch(batchId);
                }
                _lastLodMismatches = _lastLodMismatches?.Where(mm => !materialMismatches.Contains(mm)).ToList();
            }
            catch (Exception ex)
            {
                StatusText = "Fix material mismatches failed: " + ex.Message;
                Log.Error("FixMaterialMismatches failed: " + ex);
            }
        }

        // Resets whatever's currently selected back to vanilla materials via MeshDefaults -
        // independent of this tool's own change log, so it works even on an entity we never
        // touched (someone else's edit, a different tool, or a scene you didn't build). Scoped to
        // Manual selection specifically, not the general Filtered/WholeScene mode toggle - "a
        // specific selected building," not a scene-wide operation, per how this was asked for.
        public void ExecuteRevertToNormal()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }

                var targets = EntitySelector.GetTargets(SelectionMode.Manual);
                if (targets.Count == 0) { StatusText = "Nothing selected."; return; }

                var preview = RevertToNormalEngine.Apply(targets, dryRun: true);
                if (preview.MaterialsReverted == 0 && preview.ColorsReverted == 0)
                {
                    StatusText = preview.SlotsNotInReference > 0
                        ? $"Already normal, or {preview.SlotsNotInReference} slot(s) aren't in the reference data (not a recognized native prefab part)."
                        : "Already normal - nothing to revert.";
                    return;
                }

                var inquiry = new InquiryData(
                    "Revert to normal?",
                    $"This will reset {preview.MaterialsReverted} material slot(s) and {preview.ColorsReverted} color tint(s) " +
                    $"(entity-wide, per-LOD, and per-mesh) across {preview.EntitiesTouched} " +
                    $"entit{(preview.EntitiesTouched == 1 ? "y" : "ies")} to their vanilla defaults, regardless of how or when " +
                    "they were changed - not limited to what this tool's own history knows about. Continue?",
                    isAffirmativeOptionShown: true,
                    isNegativeOptionShown: true,
                    affirmativeText: "Revert",
                    negativeText: "Cancel",
                    affirmativeAction: () => RunRevertToNormal(targets),
                    negativeAction: null);
                InformationManager.ShowInquiry(inquiry);
            }
            catch (Exception ex)
            {
                StatusText = "Revert to Normal failed: " + ex.Message;
                Log.Error("RevertToNormal failed: " + ex);
            }
        }

        private void RunRevertToNormal(List<GameEntity> targets)
        {
            try
            {
                BackupManager.BackupNow("before-apply");
                var result = RevertToNormalEngine.Apply(targets, dryRun: false);
                StatusText = $"Reverted {result.MaterialsReverted} material slot(s) and {result.ColorsReverted} color tint(s) " +
                             $"across {result.EntitiesTouched} entit{(result.EntitiesTouched == 1 ? "y" : "ies")} to vanilla defaults.";
                if (result.MaterialsReverted > 0 || result.ColorsReverted > 0)
                {
                    BackupManager.ArmSaveReminder();
                    ScreenshotManager.CaptureForBatch(result.BatchId);
                }
            }
            catch (Exception ex)
            {
                StatusText = "Revert to Normal failed: " + ex.Message;
                Log.Error("RunRevertToNormal failed: " + ex);
            }
        }

        // "Preview" (rich, non-committing) vs "Dry Run" (runs the actual engine in DryRun mode
        // and reports the authoritative summary). Both compute the same way under the hood - this
        // just also renders a full breakdown instead of a one-line count - but Preview is the one
        // meant to be safe to click freely while you're still writing rules.
        public void ExecuteRefreshPreview()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }

                var targets = EntitySelector.GetTargets(_mode, FilterTerm);
                var rules = Rules
                    .Where(r => !string.IsNullOrWhiteSpace(r.FromMaterial) && !string.IsNullOrWhiteSpace(r.ToMaterial))
                    .Select(r => new MaterialSwapRule(r.FromMaterial, r.ToMaterial, r.ColorFactor))
                    .ToList();

                // IsNullOrEmpty, not IsNullOrWhiteSpace - a bare space is a valid "clear the
                // tint" sentinel now (see ColorHex.TryParse), and IsNullOrWhiteSpace would filter
                // it out before it ever got the chance to be recognized as one.
                bool hasEntityColor = !string.IsNullOrEmpty(EntityColorFactorInput) &&
                    ColorHex.TryParse(EntityColorFactorInput, out _);

                if (rules.Count == 0 && !hasEntityColor)
                {
                    StatusText = $"{targets.Count} entity(ies) match current selection. Add rules or an entity-wide color to preview changes.";
                    return;
                }

                var options = new ApplyOptions
                {
                    DryRun = true,
                    // Preview never assigns tracking UIDs regardless of the checkbox - it's meant
                    // to be safe to click repeatedly while still editing rules, and tagging is a
                    // real mutation even in dry-run mode (see the DryRun guard added to
                    // EnsureTrackingUid in MaterialSwapEngine - this was quietly true of Dry Run
                    // too until now).
                    AssignTrackingUid = false,
                    EntityColorFactor = EntityColorFactorInput,
                };

                var result = MaterialSwapEngine.Apply(targets, rules, options);

                // One builder used both for the first render and for every later "Show first N"
                // press on the flyout. The engine result is captured, not re-run - changing the
                // list length must never re-evaluate the scene, or the numbers could drift from
                // what the header already claimed.
                int scopeCount = targets.Count;
                Func<int, DiffPreviewContent> build = cap =>
                {
                    _previewEntityCap = cap;
                    var built = BuildDiffLines(result, cap);
                    var h = $"{scopeCount} entity(ies) in scope - {result.MaterialsSwapped} material slot(s), " +
                            $"{result.EntityColorFactorsApplied} entity-wide color(s) would change. Positions are world coordinates:";
                    return new DiffPreviewContent(h, built);
                };

                var initial = build(_previewEntityCap);
                if (initial.Lines.Count == 0)
                {
                    StatusText = $"{targets.Count} entity(ies) in scope, but nothing matches your current rules.";
                    return;
                }

                DiffPreviewLayer.Open(initial.Header, initial.Lines, _previewEntityCap, build);

                var sel = SelectAffected(result);
                StatusText = $"Preview: {result.EntitiesTouched} entit{(result.EntitiesTouched == 1 ? "y" : "ies")} would change.{sel}";
            }
            catch (Exception ex)
            {
                StatusText = "Preview failed: " + ex.Message;
                Log.Error("Preview failed: " + ex);
            }
        }

        // How many entities Preview lists individually before switching to a "...and N more"
        // line. Lives on the flyout, not on the F8 panel - it only means anything once you are
        // looking at the list, and the F8 row it used to occupy was already full. The flyout
        // writes the chosen value back here so the next Preview reopens at the same length.
        private int _previewEntityCap = DiffPreviewVM.LimitDefault;

        // Selects exactly the entities a dry run / preview said it would touch, so the answer
        // lands in the viewport instead of only in a status line.
        //
        // DELIBERATELY SKIPPED IN MANUAL MODE. Manual mode's target set IS the current editor
        // selection, so re-selecting the matched subset would feed a narrower input into the next
        // run - press Preview twice and your scope silently shrinks each time. Non-obvious enough
        // that it says so rather than quietly doing nothing.
        private string SelectAffected(ApplyResult result)
        {
            try
            {
                var names = new HashSet<string>(
                    result.Entries.Select(e => e.EntityName).Where(n => !string.IsNullOrEmpty(n)),
                    StringComparer.OrdinalIgnoreCase);
                if (names.Count == 0) return "";

                if (_mode == SelectionMode.Manual)
                    return "  (not auto-selected: Manual mode uses your selection as the input - selecting the matches would shrink it next run.)";

                var all = new List<GameEntity>();
                EntitySelector.CurrentScene.GetEntities(ref all);

                // Match on name AND position: a scene has many entities sharing a prefab name, and
                // selecting all of them would over-select well beyond what the run reported.
                var wanted = new HashSet<string>(
                    result.Entries
                        .Where(e => !string.IsNullOrEmpty(e.EntityName))
                        .Select(e => $"{e.EntityName}|{e.PosX:F2}|{e.PosY:F2}|{e.PosZ:F2}"),
                    StringComparer.OrdinalIgnoreCase);

                var n = LiveSceneChecks.SelectInEditor(all, e =>
                {
                    var p = e.GetGlobalFrame().origin;
                    return wanted.Contains($"{e.Name}|{p.x:F2}|{p.y:F2}|{p.z:F2}");
                });
                return n > 0 ? $"  Selected {n} in the editor." : "";
            }
            catch (Exception ex)
            {
                Log.Warn("SelectAffected failed: " + ex.Message);
                return "";
            }
        }

        // Preview used to show exactly what Dry Run already said, only grouped by material pair -
        // "stone_wall_a -> stone_wall_b (12 entities, 30 slots)". Two features answering the same
        // question ("how much would change") and neither answering the one the summary can't:
        // WHICH entities, and where they are. Dry Run keeps the counts; Preview now lists the
        // entities with their world positions so a match can actually be found in the viewport.
        //
        // Grouped per ENTITY rather than per slot - one building changing five slots is one line,
        // not five - so the 200 cap counts things you can go look at.
        private List<string> BuildDiffLines(ApplyResult result, int cap)
        {
            var lines = new List<string>();

            // Short summary first: still the fastest way to see the shape of a change, and it is
            // cheap to keep now that it is a header rather than the whole content.
            var materialGroups = result.Entries
                .Where(e => !e.IsEntityWideColor)
                .GroupBy(e => (e.OldMaterial, e.NewMaterial))
                .OrderByDescending(g => g.Count())
                .ToList();

            foreach (var g in materialGroups)
            {
                int entityCount = g.Select(e => e.EntityName ?? "").Distinct().Count();
                lines.Add($"{g.Key.OldMaterial} -> {g.Key.NewMaterial}   " +
                          $"({entityCount} entit{(entityCount == 1 ? "y" : "ies")}, {g.Count()} slot{(g.Count() == 1 ? "" : "s")})");
            }

            var colorGroups = result.Entries.Where(e => e.IsEntityWideColor).GroupBy(e => e.NewColorFactor).ToList();
            foreach (var g in colorGroups)
            {
                int count = g.Count();
                lines.Add($"Entity-wide color -> {g.Key}   ({count} entit{(count == 1 ? "y" : "ies")})");
            }

            // Per-entity detail with positions.
            var perEntity = result.Entries
                .GroupBy(e => new { Name = e.EntityName ?? "(unnamed)", e.PosX, e.PosY, e.PosZ })
                .ToList();

            if (perEntity.Count > 0)
            {
                lines.Add("");
                lines.Add($"--- {perEntity.Count} entit{(perEntity.Count == 1 ? "y" : "ies")} affected "
                          + (perEntity.Count > cap ? $"(first {cap} listed) ---" : "---"));
            }

            foreach (var g in perEntity.Take(cap))
            {
                var slots = g.Count();
                // Name the actual change on the line, not just a count - "3 slots" alone does not
                // tell you whether this is the entity you were looking for.
                var what = string.Join(", ", g
                    .Where(e => !e.IsEntityWideColor && !string.IsNullOrEmpty(e.OldMaterial))
                    .Select(e => $"{e.OldMaterial}->{e.NewMaterial}")
                    .Distinct()
                    .Take(3));
                if (g.Any(e => e.IsEntityWideColor))
                    what = string.IsNullOrEmpty(what) ? "entity-wide color" : what + ", entity-wide color";

                lines.Add($"{g.Key.Name}   ({g.Key.PosX:F2}, {g.Key.PosY:F2}, {g.Key.PosZ:F2})   "
                          + $"{slots} slot{(slots == 1 ? "" : "s")}: {what}");
            }

            if (perEntity.Count > cap)
                lines.Add($"...and {perEntity.Count - cap} more entit(y/ies) not listed.");

            return lines;
        }

        public void ExecuteApply() => RunApply(dryRun: false);
        public void ExecuteDryRun() => RunApply(dryRun: true);

        public void ExecuteSavePreset()
        {
            if (string.IsNullOrWhiteSpace(PresetName)) { StatusText = "Enter a preset name first."; return; }
            try
            {
                var rules = Rules.Select(r => new MaterialSwapRule(r.FromMaterial, r.ToMaterial, r.ColorFactor)).ToList();

                // Automatic culture inference, merged with whatever tags were typed manually -
                // inference handles culture, the manual field is still there for anything else
                // (like "rocks", which isn't a culture and can't be inferred).
                var inferred = CultureMaterialInference.InferCultureTags(rules.Select(r => r.FromMaterial));
                var manual = ParseTags(PresetTagsInput);
                var tags = manual.Concat(inferred)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var preset = new MaterialSwapPreset
                {
                    Name = PresetName,
                    Rules = rules,
                    Tags = tags,
                };
                preset.Save();
                StatusText = inferred.Count == 0
                    ? $"Saved preset '{PresetName}'."
                    : $"Saved preset '{PresetName}' (auto-tagged: {string.Join(", ", inferred)}).";
            }
            catch (Exception ex)
            {
                StatusText = "Save failed: " + ex.Message;
                Log.Error("SavePreset failed: " + ex);
            }
        }

        public void ExecuteBrowsePresets() =>
            PresetBrowserLayer.Open((chosenName, mode, invert) =>
            {
                PresetName = chosenName;
                LoadPresetByName(chosenName, mode, invert);
            });

        public void ExecuteLoadPreset()
        {
            if (string.IsNullOrWhiteSpace(PresetName)) { StatusText = "Enter a preset name first."; return; }
            LoadPresetByName(PresetName, PresetLoadMode.Overwrite, invert: false);
        }

        // Shared tail of Add-mode loading. skippedConflicts is non-null only when the user chose
        // to skip them, so the status line can say what was left out rather than silently
        // dropping rules - the whole point of asking in the first place.
        private void CommitAddedRules(string name, string invertNote, List<MaterialSwapRule> toAdd,
                                      List<MaterialSwapRule> skippedConflicts, int exactDupes)
        {
            foreach (var r in toAdd)
                Rules.Add(NewRuleVM(r.FromMaterial, r.ToMaterial, r.ColorFactor));
            if (Rules.Count == 0) Rules.Add(NewRuleVM());

            var notes = new List<string>();
            if (exactDupes > 0) notes.Add($"{exactDupes} identical duplicate(s) skipped");
            if (skippedConflicts != null && skippedConflicts.Count > 0)
                notes.Add($"{skippedConflicts.Count} conflicting rule(s) skipped");

            StatusText = notes.Count == 0
                ? $"Added {toAdd.Count} rule(s) from preset '{name}'{invertNote}."
                : $"Added {toAdd.Count} rule(s) from preset '{name}'{invertNote} ({string.Join(", ", notes)}).";
        }

        private void LoadPresetByName(string name, PresetLoadMode mode, bool invert)
        {
            try
            {
                var preset = MaterialSwapPreset.Load(name);

                if (invert)
                {
                    foreach (var r in preset.Rules)
                        (r.FromMaterial, r.ToMaterial) = (r.ToMaterial, r.FromMaterial);
                }

                if (mode == PresetLoadMode.Merge)
                {
                    LoadPresetMerge(name, preset);
                    return;
                }

                if (mode == PresetLoadMode.Overwrite) Rules.Clear();

                var invertNote = invert ? " (inverted)" : "";

                if (mode != PresetLoadMode.Add)
                {
                    foreach (var r in preset.Rules)
                        Rules.Add(NewRuleVM(r.FromMaterial, r.ToMaterial, r.ColorFactor));
                    if (Rules.Count == 0) Rules.Add(NewRuleVM());
                    StatusText = $"Loaded preset '{name}'{invertNote} ({preset.Rules.Count} rule(s)).";
                    return;
                }

                // Add mode sorts incoming rules into three buckets, because "duplicate" was
                // previously judged on the whole (from, to, colorFactor) TRIPLE - which only ever
                // caught byte-identical rows. The rows that actually cause trouble are ones
                // sharing a FromMaterial with a DIFFERENT target or color: those aren't identical,
                // so they were added silently, and MaterialSwapEngine then keys ruleMap by
                // FromMaterial alone and keeps g.First() - quietly discarding every later rule for
                // that material. The rule was in your list, visible, and did nothing.
                //
                //   exact    - identical triple already present. Always skipped, never worth asking.
                //   conflict - same FromMaterial, different target/color. This is the ambiguous one.
                //   fresh    - material not currently ruled at all.
                var existingTriples = new System.Collections.Generic.HashSet<(string, string, string)>(
                    Rules.Select(r => (Norm(r.FromMaterial), Norm(r.ToMaterial), Norm(r.ColorFactor))));
                var existingFrom = new System.Collections.Generic.HashSet<string>(
                    Rules.Select(r => Norm(r.FromMaterial)), StringComparer.OrdinalIgnoreCase);

                var fresh = new List<MaterialSwapRule>();
                var conflicts = new List<MaterialSwapRule>();
                int exactDupes = 0;

                foreach (var r in preset.Rules)
                {
                    if (!existingTriples.Add((Norm(r.FromMaterial), Norm(r.ToMaterial), Norm(r.ColorFactor))))
                    {
                        exactDupes++;
                        continue;
                    }
                    if (!existingFrom.Add(Norm(r.FromMaterial)))
                    {
                        conflicts.Add(r);
                        continue;
                    }
                    fresh.Add(r);
                }

                if (conflicts.Count == 0)
                {
                    CommitAddedRules(name, invertNote, fresh, null, exactDupes);
                    return;
                }

                var conflictNames = string.Join(", ", conflicts
                    .Select(c => c.FromMaterial)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(6));
                if (conflicts.Select(c => Norm(c.FromMaterial)).Distinct().Count() > 6) conflictNames += ", ...";

                var inquiry = new InquiryData(
                    "Conflicting rules in this preset",
                    $"'{name}' has {conflicts.Count} rule(s) for material(s) your list already covers, but with a " +
                    $"DIFFERENT target or color: {conflictNames}.\n\n" +
                    "Only one rule per material can ever take effect - Apply keeps the first and ignores the rest. " +
                    "Adding them anyway will leave rows in your list that silently do nothing.\n\n" +
                    $"Skip Duplicates adds only the {fresh.Count} non-conflicting rule(s).",
                    isAffirmativeOptionShown: true,
                    isNegativeOptionShown: true,
                    affirmativeText: "Skip Duplicates",
                    negativeText: "Add Anyway",
                    affirmativeAction: () => CommitAddedRules(name, invertNote, fresh, conflicts, exactDupes),
                    negativeAction: () => CommitAddedRules(name, invertNote, fresh.Concat(conflicts).ToList(), null, exactDupes));
                InformationManager.ShowInquiry(inquiry);
                return;
            }
            catch (Exception ex)
            {
                StatusText = "Load failed: " + ex.Message;
            }
        }

        // Fills in ONLY the blank right side of your existing rules - never adds a row, never
        // overwrites a row that already has a target. Built for "Get Input Rules from Selection"
        // output (FromMaterial filled in, ToMaterial blank) plus an existing preset as the
        // answer key.
        private void LoadPresetMerge(string name, MaterialSwapPreset preset)
        {
            var presetByFrom = preset.Rules
                .Where(r => !string.IsNullOrWhiteSpace(r.FromMaterial) && !string.IsNullOrWhiteSpace(r.ToMaterial))
                .GroupBy(r => Norm(r.FromMaterial), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            int filled = 0;
            foreach (var rowVm in Rules)
            {
                if (!string.IsNullOrWhiteSpace(rowVm.ToMaterial)) continue; // already has a target - leave it
                if (string.IsNullOrWhiteSpace(rowVm.FromMaterial)) continue; // nothing to match on

                if (!presetByFrom.TryGetValue(Norm(rowVm.FromMaterial), out var match)) continue;

                rowVm.ToMaterial = match.ToMaterial;
                if (string.IsNullOrWhiteSpace(rowVm.ColorFactor) && !string.IsNullOrWhiteSpace(match.ColorFactor))
                    rowVm.ColorFactor = match.ColorFactor;
                filled++;
            }

            StatusText = filled == 0
                ? $"Merge: nothing in '{name}' matched a blank rule's FromMaterial."
                : $"Merge: filled in {filled} rule(s) from '{name}'.";
        }

        private static string Norm(string s) => (s ?? "").Trim();

        private static System.Collections.Generic.List<string> ParseTags(string input) =>
            (input ?? "").Split(',')
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();

        public void ExecuteOpenBatchHistory() => BatchHistoryLayer.Open();

        public void ExecuteUndoLastBatch()
        {
            try
            {
                var outcome = RevertManager.RevertLastBatch(applyAutoReverts: true);
                StatusText = $"Undo: {outcome.AutoReverted.Count} reverted automatically, " +
                             $"{outcome.NeedsConfirmation.Count} need manual confirmation (see log), " +
                             $"{outcome.NotFound.Count} not found.";
                if (outcome.NeedsConfirmation.Count > 0)
                    Log.Warn($"Undo produced {outcome.NeedsConfirmation.Count} low-confidence match(es) - " +
                             "not applied automatically. Review tool.log for entity/position details.");
            }
            catch (Exception ex)
            {
                StatusText = "Undo failed: " + ex.Message;
                Log.Error("Undo failed: " + ex);
            }
        }

        public void ExecuteSolidify()
        {
            var inquiry = new InquiryData(
                "Solidify tracking tags?",
                "This applies to the WHOLE SCENE, not just your current selection. Every entity this " +
                "tool has ever UID-tagged gets its tracking downgraded from exact tag-based to " +
                "position-based, and the tag is REMOVED from the scene. This is a one-way step: after " +
                "solidifying, revert/undo can no longer tell an entity apart from a clone at the same " +
                "spot with full confidence - if it moves later, tracking for it is lost. A new log file " +
                "is written; the original tag-based log is left untouched. Do this once you're done " +
                "rearranging and just want to shed the tag clutter, not while you're still actively " +
                "moving things around.",
                isAffirmativeOptionShown: true,
                isNegativeOptionShown: true,
                affirmativeText: "Solidify",
                negativeText: "Cancel",
                affirmativeAction: RunSolidify,
                negativeAction: null);
            InformationManager.ShowInquiry(inquiry);
        }

        private void RunSolidify()
        {
            try
            {
                var report = SolidifyManager.Run();
                StatusText = $"Solidify: {report.EntitiesSolidified} entities converted to position tracking, " +
                             $"{report.EntitiesAmbiguousSkipped} left tagged (ambiguous), " +
                             $"{report.EntriesDroppedMissing} stale entries dropped.";
            }
            catch (Exception ex)
            {
                StatusText = "Solidify failed: " + ex.Message;
                Log.Error("Solidify failed: " + ex);
            }
        }

        public void ExecuteBackupNow()
        {
            var path = BackupManager.BackupNow("manual");
            // "Backup failed" used to be shown even when backups were simply switched off, which
            // was actively misleading. Name the actual reason.
            // "Backed up" would be a claim about a copy that has not run yet - BackupNow returns
            // as soon as the background task is queued. Report it as started, and let the F9 panel
            // (or tool.log) carry the verified outcome.
            StatusText = path != null
                ? $"Backup started -> {path}   (F9 shows the verified result)"
                : (BackupManager.BackupsEnabled
                    ? "Nothing backed up - scene unchanged, or no scene folder found. See tool.log."
                    : "Backups are OFF - nothing was written.");
        }

        public void ExecuteOpenBackups()
        {
            try
            {
                var opened = BackupManager.OpenBackupFolder();
                var latest = BackupManager.DescribeLatestBackup();
                StatusText = latest != null
                    ? $"Opened {opened}   (latest backup: {latest})"
                    : $"Opened {opened} - no backups written yet.";
            }
            catch (Exception ex)
            {
                StatusText = "Couldn't open the backup folder: " + ex.Message;
                Log.Error("OpenBackups failed: " + ex);
            }
        }

        public void ExecuteClose() => _closeAction?.Invoke();

        private void RunApply(bool dryRun)
        {
            Log.Info($"[SelectionDiag] {(dryRun ? "DryRun" : "Apply")} button clicked");
            try
            {
                if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }

                var targets = EntitySelector.GetTargets(_mode, FilterTerm);
                var rules = Rules
                    .Where(r => !string.IsNullOrWhiteSpace(r.FromMaterial) && !string.IsNullOrWhiteSpace(r.ToMaterial))
                    .Select(r => new MaterialSwapRule(r.FromMaterial, r.ToMaterial, r.ColorFactor))
                    .ToList();

                // IsNullOrEmpty, not IsNullOrWhiteSpace - see the comment in ExecuteRefreshPreview.
                bool hasEntityColor = !string.IsNullOrEmpty(EntityColorFactorInput);
                if (rules.Count == 0 && !hasEntityColor)
                {
                    StatusText = "Add at least one from/to material rule, or set an entity-wide color factor, first.";
                    return;
                }
                if (hasEntityColor && !ColorHex.TryParse(EntityColorFactorInput, out _))
                {
                    StatusText = $"'{EntityColorFactorInput}' isn't a valid color (expected #RRGGBB or #RRGGBBAA).";
                    return;
                }

                // Dry Run never touches an entity, so a large target count there is harmless -
                // only a real Apply warrants stopping to confirm.
                if (!dryRun && targets.Count > LargeBatchWarningThreshold)
                {
                    var inquiry = new InquiryData(
                        "Large batch",
                        $"This will apply to {targets.Count} entities. Continue?",
                        isAffirmativeOptionShown: true,
                        isNegativeOptionShown: true,
                        affirmativeText: "Apply Anyway",
                        negativeText: "Cancel",
                        affirmativeAction: () => ContinueApply(targets, rules, dryRun),
                        negativeAction: () => { StatusText = $"Apply cancelled - {targets.Count} entities exceeds the {LargeBatchWarningThreshold}-entity warning threshold."; });
                    InformationManager.ShowInquiry(inquiry);
                    return;
                }

                ContinueApply(targets, rules, dryRun);
            }
            catch (Exception ex)
            {
                StatusText = "Apply failed: " + ex.Message;
                Log.Error("Apply failed: " + ex);
            }
        }

        private const int LargeBatchWarningThreshold = 50;

        private void ContinueApply(List<GameEntity> targets, List<MaterialSwapRule> rules, bool dryRun)
        {
            try
            {
                var invalid = FindInvalidMaterials(rules);

                // Dry Run never touches an entity either way (MaterialSwapEngine no-ops
                // SetMaterial/SetFactor1/SetFactorColor when DryRun is set), so there's nothing
                // dangerous to interrupt for - unrecognized materials just ride along as a note
                // in the summary instead of an interrupting dialog. Only real Apply blocks.
                if (invalid.Count == 0 || dryRun)
                {
                    RunEngineApply(targets, rules, dryRun, invalid);
                    return;
                }

                PromptAutoCorrect(targets, rules, invalid);
            }
            catch (Exception ex)
            {
                StatusText = "Apply failed: " + ex.Message;
                Log.Error("Apply failed: " + ex);
            }
        }

        // Every FromMaterial/ToMaterial that doesn't match anything in KnownMaterials, deduped.
        private static List<string> FindInvalidMaterials(List<MaterialSwapRule> rules)
        {
            var invalid = new List<string>();

            void AddIfUnknown(string material)
            {
                if (!string.IsNullOrWhiteSpace(material) && !KnownMaterials.IsKnown(material) &&
                    !invalid.Contains(material, StringComparer.OrdinalIgnoreCase))
                    invalid.Add(material);
            }

            foreach (var r in rules)
            {
                AddIfUnknown(r.FromMaterial);

                // A weighted ToMaterial ("matA:4, matB:1") is checked option-by-option - the raw
                // string itself is never a real material name, so validating it whole would always
                // fail.
                if (WeightedTarget.IsWeighted(r.ToMaterial))
                {
                    foreach (var option in WeightedTarget.Parse(r.ToMaterial))
                        AddIfUnknown(option.Material);
                }
                else
                {
                    AddIfUnknown(r.ToMaterial);
                }
            }
            return invalid;
        }

        // A weighted rule is only "clean" if every one of its options is known-good; a plain rule
        // just checks its single ToMaterial. Used both to filter what's safe to skip-and-apply and
        // (implicitly, by NOT matching here) what auto-correct should leave alone.
        private static bool RuleHasInvalidMaterial(MaterialSwapRule r, HashSet<string> invalidSet)
        {
            if (invalidSet.Contains(r.FromMaterial ?? "")) return true;
            if (WeightedTarget.IsWeighted(r.ToMaterial))
                return WeightedTarget.Parse(r.ToMaterial).Any(o => invalidSet.Contains(o.Material));
            return invalidSet.Contains(r.ToMaterial ?? "");
        }

        private void PromptAutoCorrect(List<GameEntity> targets, List<MaterialSwapRule> rules, List<string> invalid)
        {
            var list = string.Join(", ", invalid);
            var inquiry = new InquiryData(
                "Unrecognized materials",
                $"{invalid.Count} material name(s) don't match anything in the known-materials list: {list}. " +
                "Auto-Correct tries to fix typos; Apply Anyway uses them exactly as typed - the right call when it's a " +
                "genuine material the shipped list just doesn't cover yet (e.g. a new DLC/War Sails asset).",
                isAffirmativeOptionShown: true,
                isNegativeOptionShown: true,
                affirmativeText: "Try Auto-Correct",
                negativeText: "Apply Anyway / Cancel...",
                affirmativeAction: () => TryAutoCorrectAndContinue(targets, rules, invalid),
                negativeAction: () => PromptApplyAnyway(targets, rules, invalid));
            InformationManager.ShowInquiry(inquiry);
        }

        // The escape hatch the user asked for: the known-materials list is a shipped snapshot and
        // will always lag new content, so "unrecognized" must never be a hard wall. This second step
        // keeps a deliberate confirm (a real typo here renders broken/missing) without ever forcing
        // the auto-correct-or-cancel choice.
        private void PromptApplyAnyway(List<GameEntity> targets, List<MaterialSwapRule> rules, List<string> invalid)
        {
            var list = string.Join(", ", invalid);
            var inquiry = new InquiryData(
                "Apply as typed?",
                $"Apply the rules using {invalid.Count} unrecognized material name(s) exactly as typed: {list}. " +
                "If any of these is actually a typo, that material will render broken/missing wherever it's used.",
                isAffirmativeOptionShown: true,
                isNegativeOptionShown: true,
                affirmativeText: "Apply Anyway",
                negativeText: "Cancel",
                affirmativeAction: () =>
                {
                    StatusText = $"Applying with {invalid.Count} unrecognized material(s) as typed: {list}";
                    RunEngineApply(targets, rules, dryRun: false, invalid);
                },
                negativeAction: () => { StatusText = $"Apply cancelled - {invalid.Count} unrecognized material(s): {list}"; });
            InformationManager.ShowInquiry(inquiry);
        }

        private void TryAutoCorrectAndContinue(List<GameEntity> targets, List<MaterialSwapRule> rules, List<string> invalid)
        {
            var corrections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in invalid)
            {
                var corrected = KnownMaterials.TryAutoCorrect(name);
                if (corrected != null) corrections[name] = corrected;
            }

            // Applied to both the engine-bound snapshot AND the live rule rows, so the fix is
            // visible on screen and gets saved if this is later saved as a preset - not a silent
            // one-off substitution the user never sees. Weighted ToMaterial fields are skipped
            // here deliberately: blindly overwriting "matA:4, matB:1" with a single corrected name
            // would silently collapse the whole weighted list down to one option. A weighted rule
            // with an invalid option just falls through to the skip-or-cancel step instead.
            foreach (var r in rules)
            {
                if (corrections.TryGetValue(r.FromMaterial ?? "", out var newFrom)) r.FromMaterial = newFrom;
                if (!WeightedTarget.IsWeighted(r.ToMaterial) && corrections.TryGetValue(r.ToMaterial ?? "", out var newTo))
                    r.ToMaterial = newTo;
            }
            foreach (var rowVm in Rules)
            {
                if (corrections.TryGetValue(rowVm.FromMaterial ?? "", out var newFrom)) rowVm.FromMaterial = newFrom;
                if (!WeightedTarget.IsWeighted(rowVm.ToMaterial) && corrections.TryGetValue(rowVm.ToMaterial ?? "", out var newTo))
                    rowVm.ToMaterial = newTo;
            }

            var stillInvalid = invalid.Where(n => !corrections.ContainsKey(n)).ToList();

            if (stillInvalid.Count == 0)
            {
                StatusText = $"Auto-corrected {corrections.Count} material name(s): " +
                             string.Join(", ", corrections.Select(kv => $"{kv.Key} -> {kv.Value}"));
                RunEngineApply(targets, rules, dryRun: false, stillInvalid);
                return;
            }

            PromptSkipOrCancel(targets, rules, corrections, stillInvalid);
        }

        private void PromptSkipOrCancel(List<GameEntity> targets, List<MaterialSwapRule> rules,
            Dictionary<string, string> corrections, List<string> stillInvalid)
        {
            var list = string.Join(", ", stillInvalid);
            var correctedNote = corrections.Count > 0 ? $" ({corrections.Count} other(s) were auto-corrected.)" : "";
            var inquiry = new InquiryData(
                "Still unrecognized",
                $"{stillInvalid.Count} material name(s) couldn't be auto-corrected (no single confident match): " +
                $"{list}.{correctedNote} Skip the rules that reference them and apply everything else, or keep them as typed?",
                isAffirmativeOptionShown: true,
                isNegativeOptionShown: true,
                affirmativeText: "Skip & Apply Rest",
                negativeText: "Apply Anyway / Cancel...",
                affirmativeAction: () =>
                {
                    var stillInvalidSet = new HashSet<string>(stillInvalid, StringComparer.OrdinalIgnoreCase);
                    var usable = rules.Where(r => !RuleHasInvalidMaterial(r, stillInvalidSet)).ToList();
                    int skipped = rules.Count - usable.Count;
                    StatusText = $"Skipped {skipped} rule(s) with unrecognized materials.";
                    RunEngineApply(targets, usable, dryRun: false, stillInvalid);
                },
                negativeAction: () => PromptApplyAnyway(targets, rules, stillInvalid));
            InformationManager.ShowInquiry(inquiry);
        }

        private void RunEngineApply(List<GameEntity> targets, List<MaterialSwapRule> rules, bool dryRun, List<string> stillInvalid)
        {
            if (!dryRun)
                BackupManager.BackupNow("before-apply");

            var options = new ApplyOptions
            {
                DryRun = dryRun,
                TagChangedEntities = TagChanged,
                RenameChangedEntities = RenameChanged,
                AssignTrackingUid = AssignUid,
                EntityColorFactor = EntityColorFactorInput,
            };

            var result = MaterialSwapEngine.Apply(targets, rules, options);

            var invalidNote = stillInvalid.Count > 0
                ? $" ({stillInvalid.Count} unrecognized material(s) still in the rules: {string.Join(", ", stillInvalid)})"
                : "";

            var selNote = dryRun ? SelectAffected(result) : "";

            // A typed color that doesn't parse gets shouted about, on screen AND as an editor
            // warning (2026-08-23, "#ffwe193"): the engine skips an unparseable tint but still
            // swaps the material, which looks exactly like "the color factor is broken".
            var badColorNote = result.InvalidColorFactors.Count > 0
                ? $"  WARNING - invalid color factor(s), tint NOT applied: {string.Join("; ", result.InvalidColorFactors)}"
                : "";
            if (badColorNote.Length > 0)
                TaleWorlds.MountAndBlade.MBEditor.AddEditorWarning(
                    $"Material Swap: {result.InvalidColorFactors.Count} color factor(s) don't parse and were skipped - check the panel status line.");

            StatusText = (dryRun
                ? $"Dry run: would touch {result.EntitiesTouched} entities, {result.MaterialsSwapped} material slots, {result.EntityColorFactorsApplied} entity-wide color(s).{selNote}"
                : $"Applied: {result.EntitiesTouched} entities, {result.MaterialsSwapped} material slots swapped, {result.EntityColorFactorsApplied} entity-wide color(s) set.")
                + invalidNote + badColorNote;

            if (!dryRun && result.MaterialsSwapped > 0)
            {
                BackupManager.ArmSaveReminder();
                ScreenshotManager.CaptureForBatch(result.BatchId);
            }
        }

        private void RefreshPreview()
        {
            try
            {
                _lastMatchCount = EntitySelector.HasOpenScene
                    ? EntitySelector.GetTargets(_mode, FilterTerm).Count
                    : 0;
                StatusText = $"{_lastMatchCount} entity(ies) match current selection.";
            }
            catch (Exception ex)
            {
                StatusText = "Preview failed: " + ex.Message;
            }
        }

        public override void RefreshValues()
        {
            base.RefreshValues();
            StatusText = "Ready.";
        }
    }
}
