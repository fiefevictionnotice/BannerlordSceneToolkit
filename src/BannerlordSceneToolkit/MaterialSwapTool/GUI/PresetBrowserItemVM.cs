using System;
using System.Linq;
using MaterialSwapTool.Core;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    public class PresetBrowserItemVM : ViewModel
    {
        private readonly Action<string> _onChosen;
        private readonly Action<PresetBrowserItemVM> _onDelete;
        private readonly Action<string, PresetLoadMode, bool> _onLoadIntoCurrent;
        private string _name;
        private string _typeText;
        private string _dateText;
        private string _tagsText;
        private string _rule1Text;
        private string _rule2Text;
        private string _inferredTagsText;
        private bool _isDeletable;
        private bool _isExpanded;
        private string _expandButtonText;
        private MBBindingList<RuleLineVM> _extraRuleLines = new MBBindingList<RuleLineVM>();
        private readonly int _extraRuleCount;
        private Color _accentColor;
        private Color _cardBackgroundColor;

        // Built-in: warm gold. Personal: cool teal. Chosen to read clearly against the existing
        // dark panel background without needing a separate icon/badge widget.
        private static readonly Color BuiltInAccent = Color.ConvertStringToColor("#c9a13bFF");
        private static readonly Color BuiltInCardBg = Color.ConvertStringToColor("#c9a13b33");
        private static readonly Color PersonalAccent = Color.ConvertStringToColor("#3ba1c9FF");
        private static readonly Color PersonalCardBg = Color.ConvertStringToColor("#3ba1c933");

        public PresetSummary Summary { get; }

        public PresetBrowserItemVM(PresetSummary summary, Action<string> onChosen, Action<PresetBrowserItemVM> onDelete,
            Action<string, PresetLoadMode, bool> onLoadIntoCurrent)
        {
            Summary = summary;
            _onChosen = onChosen;
            _onDelete = onDelete;
            _onLoadIntoCurrent = onLoadIntoCurrent;
            _isDeletable = summary.IsDeletable;
            _accentColor = summary.IsBuiltIn ? BuiltInAccent : PersonalAccent;
            _cardBackgroundColor = summary.IsBuiltIn ? BuiltInCardBg : PersonalCardBg;

            _dateText = summary.ModifiedUtc == DateTime.MinValue
                ? "unknown date"
                : summary.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            _typeText = summary.IsBuiltIn ? "Built-in" : "Personal";

            _tagsText = summary.Tags.Count == 0 ? "Tags: none" : "Tags: " + string.Join(", ", summary.Tags);

            var rules = summary.Rules;

            // Culture direction, e.g. "(Empire -> Aserai)" - inferred separately for the FROM and
            // TO sides of the rule set. FROM defaults to "Empire" when nothing matches (nearly
            // every template starts from generic empire/roman base materials, which mostly don't
            // carry a culture-distinctive name themselves). No arrow shown at all if TO doesn't
            // resolve to a clear culture, or if it resolves to the same one as FROM.
            var fromCulture = CultureMaterialInference.InferPrimaryCulture(rules.Select(r => r.FromMaterial)) ?? "Empire";
            var toCulture = CultureMaterialInference.InferPrimaryCulture(rules.Select(r => r.ToMaterial));
            var direction = toCulture != null && !string.Equals(toCulture, fromCulture, StringComparison.OrdinalIgnoreCase)
                ? $" ({Capitalize(fromCulture)} -> {Capitalize(toCulture)})"
                : "";

            // Just the name + culture direction now - Type and Date moved to their own labels
            // next to the Expand button instead of crowding the top row.
            _name = $"{summary.Name}{direction}";

            _rule1Text = rules.Count > 0 ? $"{rules[0].FromMaterial} -> {rules[0].ToMaterial}" : "(no rules)";
            _rule2Text = rules.Count > 1 ? $"{rules[1].FromMaterial} -> {rules[1].ToMaterial}" : "";
            _extraRuleCount = System.Math.Max(0, rules.Count - 2);
            _expandButtonText = _extraRuleCount > 0 ? $"Expand (+{_extraRuleCount})" : "Expand";

            // Recomputed fresh from the saved rules rather than stored - inference is cheap and
            // deterministic, so this works retroactively on presets saved before this existed,
            // and always reflects the current pattern table even if it's tuned later.
            var inferred = CultureMaterialInference.InferCultureTags(rules.Select(r => r.FromMaterial));
            _inferredTagsText = inferred.Count == 0 ? "Inferred: none" : "Inferred: " + string.Join(", ", inferred);
        }

        [DataSourceProperty]
        public string Name
        {
            get => _name;
            set { if (value != _name) { _name = value; OnPropertyChangedWithValue(value, nameof(Name)); } }
        }

        [DataSourceProperty]
        public string TypeText
        {
            get => _typeText;
            set { if (value != _typeText) { _typeText = value; OnPropertyChangedWithValue(value, nameof(TypeText)); } }
        }

        [DataSourceProperty]
        public string DateText
        {
            get => _dateText;
            set { if (value != _dateText) { _dateText = value; OnPropertyChangedWithValue(value, nameof(DateText)); } }
        }

        [DataSourceProperty]
        public string TagsText
        {
            get => _tagsText;
            set { if (value != _tagsText) { _tagsText = value; OnPropertyChangedWithValue(value, nameof(TagsText)); } }
        }

        [DataSourceProperty]
        public string Rule1Text
        {
            get => _rule1Text;
            set { if (value != _rule1Text) { _rule1Text = value; OnPropertyChangedWithValue(value, nameof(Rule1Text)); } }
        }

        [DataSourceProperty]
        public string Rule2Text
        {
            get => _rule2Text;
            set { if (value != _rule2Text) { _rule2Text = value; OnPropertyChangedWithValue(value, nameof(Rule2Text)); } }
        }

        [DataSourceProperty]
        public string InferredTagsText
        {
            get => _inferredTagsText;
            set { if (value != _inferredTagsText) { _inferredTagsText = value; OnPropertyChangedWithValue(value, nameof(InferredTagsText)); } }
        }

        [DataSourceProperty]
        public Color AccentColor
        {
            get => _accentColor;
            set { if (value != _accentColor) { _accentColor = value; OnPropertyChangedWithValue(value, nameof(AccentColor)); } }
        }

        [DataSourceProperty]
        public Color CardBackgroundColor
        {
            get => _cardBackgroundColor;
            set { if (value != _cardBackgroundColor) { _cardBackgroundColor = value; OnPropertyChangedWithValue(value, nameof(CardBackgroundColor)); } }
        }

        [DataSourceProperty]
        public bool IsDeletable
        {
            get => _isDeletable;
            set { if (value != _isDeletable) { _isDeletable = value; OnPropertyChangedWithValue(value, nameof(IsDeletable)); } }
        }

        [DataSourceProperty]
        public string ExpandButtonText
        {
            get => _expandButtonText;
            set { if (value != _expandButtonText) { _expandButtonText = value; OnPropertyChangedWithValue(value, nameof(ExpandButtonText)); } }
        }

        // Empty when collapsed - the card's outer Widget uses HeightSizePolicy="CoverChildren"
        // so an empty list means the card stays small, and populating it grows the card (and the
        // whole scroll list beneath it) automatically, no manual height math needed.
        [DataSourceProperty]
        public MBBindingList<RuleLineVM> ExtraRuleLines
        {
            get => _extraRuleLines;
            set { if (value != _extraRuleLines) { _extraRuleLines = value; OnPropertyChangedWithValue(value, nameof(ExtraRuleLines)); } }
        }

        private static string Capitalize(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        // Command.Click inside an ItemTemplate resolves against the item's own VM, not the
        // parent - same reason MaterialSwapRuleVM carries its own remove handler.
        public void ExecuteChoose() => _onChosen?.Invoke(Summary.Name);
        public void ExecuteDelete() => _onDelete?.Invoke(this);

        // Only meaningful for personal presets - a built-in is never overwritten by this tool
        // (Save() always targets the personal folder), so it never has any history to show.
        public void ExecuteOpenHistory() => PresetHistoryLayer.Open(Summary.Name, _onLoadIntoCurrent);

        public void ExecuteToggleExpand()
        {
            _isExpanded = !_isExpanded;
            if (_isExpanded)
            {
                ExtraRuleLines.Clear();
                // Rules beyond the first two, which are already shown via Rule1Text/Rule2Text -
                // avoids showing the same rule twice.
                foreach (var r in Summary.Rules.Skip(2))
                    ExtraRuleLines.Add(new RuleLineVM($"{r.FromMaterial} -> {r.ToMaterial}"));
                ExpandButtonText = "Collapse";
            }
            else
            {
                ExtraRuleLines.Clear();
                ExpandButtonText = _extraRuleCount > 0 ? $"Expand (+{_extraRuleCount})" : "Expand";
            }
        }
    }
}
