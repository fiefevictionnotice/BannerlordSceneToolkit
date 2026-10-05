using System;
using System.Linq;
using MaterialSwapTool.Core;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    // A single clickable row in the built-in preset picker list. Deliberately just a name + a
    // click callback - no second list, no drag-drop - the parent VM decides whether a click fills
    // From or To based on an alternating "which field is next" flag, so one list serves both
    // fields without duplicating the UI.
    public class CulturePickItemVM : ViewModel
    {
        // Row tints for the culture lists: built-in (shipped) cultures gold, custom ones blue -
        // the color carries the distinction so the Name text stays clean for HandlePick to parse.
        public const string BuiltInColor = "#c9a23bFF";
        public const string CustomColor = "#3ba1c9FF";

        private readonly Action<string> _onClick;
        private string _name;
        private readonly string _rowColor;

        public CulturePickItemVM(string name, Action<string> onClick, string rowColor = CustomColor)
        {
            _name = name;
            _onClick = onClick;
            _rowColor = rowColor;
        }

        [DataSourceProperty]
        public string Name
        {
            get => _name;
            set { if (value != _name) { _name = value; OnPropertyChangedWithValue(value, nameof(Name)); } }
        }

        [DataSourceProperty]
        public string RowColor => _rowColor;

        public void ExecuteChoose() => _onClick?.Invoke(_name);
    }

    public class CulturePresetGeneratorVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;
        private string _fromCultureInput = "";
        private string _toCultureInput = "";
        private string _outputNameInput = "";
        private string _statusText = "";
        private string _nextFillLabel = "Click a preset below to fill: From";
        private bool _nextFillsFrom = true;
        private MBBindingList<CulturePickItemVM> _builtInPresets;
        private MBBindingList<CulturePickItemVM> _cultureStubList;

        public CulturePresetGeneratorVM(Action closeAction, Action beginDragAction)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            _builtInPresets = new MBBindingList<CulturePickItemVM>();
            RebuildPresetPickList();

            _cultureStubList = new MBBindingList<CulturePickItemVM>();
            RebuildCultureStubList();
        }

        // Built-ins first, then Personal (which includes anything the stub generator just wrote and
        // any hand-made custom-culture preset). This list used to be built-ins ONLY, which meant a
        // custom culture could be scaffolded here and then never picked as a bridge side - the
        // generator could create input it refused to accept. Personal entries are suffixed so the
        // two are still distinguishable at a glance; HandlePick strips the suffix before filling
        // the field, since the suffix isn't part of the preset's real name.
        // Only presets that can actually STAND FOR A CULTURE belong in this picker.
        //
        // Listing every personal preset (which is what fixing "custom cultures can't be picked"
        // originally did) dumped things like "Adobe to Empire Wall" and "Desert Whitewash" into a
        // list labelled as cultures, which is nonsense - they're plain material rule sets with no
        // culture identity at all. A preset qualifies if it is tagged as a culture swap or bridge,
        // is a generated stub, or carries a tag naming a known culture.
        //
        // Worth being clear about what these entries ARE: preset names, not culture names. That
        // was always true - "Vlandia Feudal" is a preset - but it only became confusing once
        // personal presets joined the list. The separate Cultures list below is the real
        // culture list, sourced from culture_definitions.json.
        private static bool IsCulturePreset(PresetSummary p)
        {
            if (p.Tags == null) return false;
            var cultures = CultureMaterialInference.AllCultures;
            foreach (var t in p.Tags)
            {
                if (string.IsNullOrWhiteSpace(t)) continue;
                if (t.Equals("culture-swap", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("culture-bridge", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("stub", StringComparison.OrdinalIgnoreCase)) return true;
                if (cultures.Any(c => c.Equals(t, StringComparison.OrdinalIgnoreCase))) return true;
            }
            return false;
        }

        // The picker now lists CULTURES, not presets. A culture is one distinctive material set
        // (its Common list in culture_definitions.json); the generator's whole job is combining
        // two sets into a conversion. Listing presets here was the original design and was the
        // root of every problem in this panel: a bridge preset is itself a conversion, so it can
        // never be a valid input, and no tag filter cleanly separates "one palette" from
        // "a conversion". Custom cultures now work by existing in the Culture Editor - nothing to
        // author first, nothing to filter.
        public void RebuildPresetPickList()
        {
            // Built-ins pinned to the top and tinted gold, customs after in blue - a growing
            // set of custom cultures should never bury the six standard ones in an
            // alphabetical mix.
            BuiltInPresets.Clear();
            foreach (var culture in CultureMaterialInference.AllCulturesBuiltInFirst)
            {
                var count = CultureMaterialInference.GetCommonMaterials(culture).Count;
                var color = CultureMaterialInference.IsBuiltInCulture(culture)
                    ? CulturePickItemVM.BuiltInColor : CulturePickItemVM.CustomColor;
                BuiltInPresets.Add(new CulturePickItemVM($"{culture}   ({count} materials)", HandlePick, color));
            }
        }

        private const string PersonalSuffix = "   [personal]";

        // Re-populates from CultureMaterialInference.AllCultures - called at construction and
        // again after the Culture Editor saves changes (a newly added custom culture should show
        // up here immediately without reopening this panel).
        public void RebuildCultureStubList()
        {
            // Same built-ins-first ordering and gold/blue tinting as the picker above.
            CultureStubList.Clear();
            foreach (var culture in CultureMaterialInference.AllCulturesBuiltInFirst)
            {
                var color = CultureMaterialInference.IsBuiltInCulture(culture)
                    ? CulturePickItemVM.BuiltInColor : CulturePickItemVM.CustomColor;
                CultureStubList.Add(new CulturePickItemVM(culture, GenerateStubForCulture, color));
            }
        }

        [DataSourceProperty]
        public MBBindingList<CulturePickItemVM> CultureStubList
        {
            get => _cultureStubList;
            set { if (value != _cultureStubList) { _cultureStubList = value; OnPropertyChangedWithValue(value, nameof(CultureStubList)); } }
        }

        // Every material this culture's real buildings are empirically known to use (see
        // CultureMaterialInference's Common list, sourced from mesh_slot_map.csv), scaffolded as
        // blank "material -> " rules ready to fill in - a starting point for a culture that has no
        // built-in preset yet, or a more complete one than the hand-authored templates cover.
        private void GenerateStubForCulture(string culture)
        {
            try
            {
                var materials = CultureMaterialInference.GetCommonMaterials(culture);
                if (materials.Count == 0)
                {
                    StatusText = $"No common materials defined for '{culture}' yet - add some via Edit Cultures.";
                    return;
                }

                var name = char.ToUpperInvariant(culture[0]) + culture.Substring(1) + " Materials (stub)";
                var preset = new MaterialSwapPreset { Name = name };
                foreach (var mat in materials.Distinct(StringComparer.OrdinalIgnoreCase))
                    preset.Rules.Add(new MaterialSwapRule(mat, ""));
                preset.Tags.Add(culture);
                preset.Tags.Add("stub");
                preset.Save();

                RebuildPresetPickList();
                StatusText = $"Generated '{name}' with {preset.Rules.Count} blank rule(s). It is selectable in the list above straight away, " +
                             "and appears under Personal in Browse Presets if you want to fill in the -> targets by hand.";
            }
            catch (Exception ex)
            {
                StatusText = "Generate stub failed: " + ex.Message;
                Log.Error("GenerateStubForCulture failed: " + ex);
            }
        }

        public void ExecuteOpenCultureEditor() => CultureEditorLayer.Open(RebuildCultureStubList);

        // Role matching runs on the material categories, so a material that bridges to nothing
        // (timber_frame's self-referential category, 2026-08-23) is fixed here - in its
        // category's Include patterns - without leaving the generator. Nothing to rebuild on
        // save: categories are re-read on the next Generate.
        public void ExecuteOpenCategoryEditor() => CategoryEditorLayer.Open(() => { });

        [DataSourceProperty]
        public MBBindingList<CulturePickItemVM> BuiltInPresets
        {
            get => _builtInPresets;
            set { if (value != _builtInPresets) { _builtInPresets = value; OnPropertyChangedWithValue(value, nameof(BuiltInPresets)); } }
        }

        [DataSourceProperty]
        public string NextFillLabel
        {
            get => _nextFillLabel;
            set { if (value != _nextFillLabel) { _nextFillLabel = value; OnPropertyChangedWithValue(value, nameof(NextFillLabel)); } }
        }

        // Alternates which field a click fills: 1st click -> From, 2nd -> To, 3rd -> From again
        // (overwriting the earlier pick), and so on. The text boxes stay editable too, so typing
        // still works exactly as before - this list is just a faster way to fill them.
        private void HandlePick(string name)
        {
            // Strip the "(N materials)" display suffix so the field holds the bare culture name.
            var paren = name?.IndexOf("   (", StringComparison.Ordinal) ?? -1;
            if (paren > 0) name = name.Substring(0, paren);
            name = name?.Trim();

            if (_nextFillsFrom)
            {
                FromCultureInput = name;
                NextFillLabel = "Click a preset below to fill: To";
            }
            else
            {
                ToCultureInput = name;
                NextFillLabel = "Click a preset below to fill: From";
            }
            _nextFillsFrom = !_nextFillsFrom;
        }

        [DataSourceProperty]
        public string FromCultureInput
        {
            get => _fromCultureInput;
            set { if (value != _fromCultureInput) { _fromCultureInput = value; OnPropertyChangedWithValue(value, nameof(FromCultureInput)); } }
        }

        [DataSourceProperty]
        public string ToCultureInput
        {
            get => _toCultureInput;
            set { if (value != _toCultureInput) { _toCultureInput = value; OnPropertyChangedWithValue(value, nameof(ToCultureInput)); } }
        }

        [DataSourceProperty]
        public string OutputNameInput
        {
            get => _outputNameInput;
            set { if (value != _outputNameInput) { _outputNameInput = value; OnPropertyChangedWithValue(value, nameof(OutputNameInput)); } }
        }

        [DataSourceProperty]
        public string StatusText
        {
            get => _statusText;
            set { if (value != _statusText) { _statusText = value; OnPropertyChangedWithValue(value, nameof(StatusText)); } }
        }

        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();

        public void ExecuteGenerate()
        {
            try
            {
                var fromCulture = (FromCultureInput ?? "").Trim();
                var toCulture = (ToCultureInput ?? "").Trim();
                var known = CultureMaterialInference.AllCultures;

                if (!known.Any(c => c.Equals(fromCulture, StringComparison.OrdinalIgnoreCase)))
                { StatusText = $"'{fromCulture}' isn't a known culture. Pick one from the list, or add it in Edit Cultures. Known: {string.Join(", ", known)}"; return; }
                if (!known.Any(c => c.Equals(toCulture, StringComparison.OrdinalIgnoreCase)))
                { StatusText = $"'{toCulture}' isn't a known culture. Pick one from the list, or add it in Edit Cultures. Known: {string.Join(", ", known)}"; return; }
                if (fromCulture.Equals(toCulture, StringComparison.OrdinalIgnoreCase))
                { StatusText = "From and To are the same culture - nothing to convert."; return; }

                var fromCount = CultureMaterialInference.GetCommonMaterials(fromCulture).Count;
                var toCount = CultureMaterialInference.GetCommonMaterials(toCulture).Count;
                if (fromCount == 0) { StatusText = $"'{fromCulture}' has no materials in its Common list - add some in Edit Cultures, or import them from a preset."; return; }
                if (toCount == 0) { StatusText = $"'{toCulture}' has no materials in its Common list - add some in Edit Cultures, or import them from a preset."; return; }

                var outputName = string.IsNullOrWhiteSpace(OutputNameInput)
                    ? $"{fromCulture} to {toCulture}"
                    : OutputNameInput.Trim();

                // Role matching is the only mechanism here, and that's deliberate: the old
                // Empire-pivot trick only worked between presets that happened to be authored
                // against the same Empire list, which is exactly the coincidence that made custom
                // cultures impossible. Matching material sets by role works for any pair.
                var bridge = CulturePresetGenerator.GenerateBridgeBetweenCultures(fromCulture, toCulture, outputName);

                if (bridge.Rules.Count == 0)
                {
                    StatusText = $"Couldn't bridge '{fromCulture}' ({fromCount} materials) and '{toCulture}' ({toCount}): no material categories in common. " +
                                 "Both sides need categorised materials - check Continuous Recolor > Edit Categories.";
                    return;
                }

                var weighted = bridge.Rules.Count(r => WeightedTarget.IsWeighted(r.ToMaterial));
                bridge.Save();
                StatusText = $"Generated '{outputName}': {bridge.Rules.Count} rule(s) from {fromCount} -> {toCount} materials, matched by material role. "
                             + (weighted > 0 ? $"{weighted} are weighted (several equally good targets). " : "")
                             + "These are INFERRED - review before applying. Find it under Personal in Browse Presets.";
            }
            catch (Exception ex)
            {
                StatusText = "Generate failed: " + ex.Message;
                Log.Error("CulturePresetGenerator failed: " + ex);
            }
        }
    }
}
