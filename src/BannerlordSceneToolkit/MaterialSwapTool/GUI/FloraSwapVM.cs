using System;
using System.Linq;
using MaterialSwapTool.Core;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    // Deliberately its own separate tool (F8, own panel, own identity) rather than a mode bolted
    // onto Material Swap Tool - reuses the exact same proven engine/selection mechanics
    // (MaterialSwapEngine.Apply, EntitySelector) since both are material-agnostic already, but
    // scoped down to just selection + rules + dry run/apply. Not attempting true geometry/prefab
    // replacement (a temperate tree literally becoming a different desert mesh) - that needs
    // engine APIs that haven't been verified yet. This is the material-swap mechanism only,
    // safe and already proven, just branded and hotkeyed for flora.
    public class FloraSwapVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;
        private MBBindingList<MaterialSwapRuleVM> _rules;
        private string _filterTerm = "";
        private string _selectionModeLabel = "Manual (editor selection)";
        private string _statusText = "";
        private SelectionMode _mode = SelectionMode.Manual;

        public FloraSwapVM(Action closeAction, Action beginDragAction)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            _rules = new MBBindingList<MaterialSwapRuleVM>();
            _rules.Add(NewRuleVM());
        }

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
        public string StatusText
        {
            get => _statusText;
            set { if (value != _statusText) { _statusText = value; OnPropertyChangedWithValue(value, nameof(StatusText)); } }
        }

        public void ExecuteSelectManual() { _mode = SelectionMode.Manual; SelectionModeLabel = "Manual (editor selection)"; }
        public void ExecuteSelectFiltered() { _mode = SelectionMode.Filtered; SelectionModeLabel = $"Filtered: \"{FilterTerm}\""; }
        public void ExecuteSelectWholeScene() { _mode = SelectionMode.WholeScene; SelectionModeLabel = "Whole scene"; }

        public void ExecuteAddRule() => Rules.Add(NewRuleVM());
        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();
        public void ExecuteOpenDocumentation() => DocumentationLayer.Toggle();

        public void ExecuteDryRun() => RunApply(dryRun: true);
        public void ExecuteApply() => RunApply(dryRun: false);

        private void RunApply(bool dryRun)
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }

                var targets = EntitySelector.GetTargets(_mode, FilterTerm);
                var rules = Rules
                    .Where(r => !string.IsNullOrWhiteSpace(r.FromMaterial) && !string.IsNullOrWhiteSpace(r.ToMaterial))
                    .Select(r => new MaterialSwapRule(r.FromMaterial, r.ToMaterial, r.ColorFactor))
                    .ToList();

                if (rules.Count == 0) { StatusText = "Add at least one from/to material rule first."; return; }

                var options = new ApplyOptions { DryRun = dryRun };
                var result = MaterialSwapEngine.Apply(targets, rules, options);

                StatusText = dryRun
                    ? $"Dry run: would touch {result.EntitiesTouched} entities, {result.MaterialsSwapped} material slots."
                    : $"Applied: {result.EntitiesTouched} entities, {result.MaterialsSwapped} material slots swapped.";
            }
            catch (Exception ex)
            {
                StatusText = "Apply failed: " + ex.Message;
                Log.Error("FloraSwap apply failed: " + ex);
            }
        }
    }
}
