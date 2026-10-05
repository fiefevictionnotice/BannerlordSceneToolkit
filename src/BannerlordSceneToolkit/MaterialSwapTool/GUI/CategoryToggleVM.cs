using System;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    public class CategoryToggleVM : ViewModel
    {
        private readonly Action _onToggled;
        private bool _isOn;
        private string _label;
        private string _colorInput = "";

        public string Category { get; }

        public CategoryToggleVM(string category, Action onToggled)
        {
            Category = category;
            _onToggled = onToggled;
            _label = BuildLabel();
        }

        private string BuildLabel() =>
            (_isOn ? "[X] " : "[ ] ") + char.ToUpperInvariant(Category[0]) + Category.Substring(1);

        [DataSourceProperty]
        public string Label
        {
            get => _label;
            set { if (value != _label) { _label = value; OnPropertyChangedWithValue(value, nameof(Label)); } }
        }

        // Plain C# property, not a two-way XML binding target - see ExecuteToggle for why.
        // Settable directly (not just via ExecuteToggle) so RebuildCategoryColumns can restore a
        // previously-armed category's state after the category editor saves changes, without
        // going through the click path or re-triggering ReapplyIfArmed.
        public bool IsOn
        {
            get => _isOn;
            set
            {
                _isOn = value;
                Label = BuildLabel();
            }
        }

        // Per-row color for palette mode (part B) - lets each armed category carry its own color
        // instead of the whole arm sharing one. The top-level ColorInput field in
        // ContinuousRecolorVM still exists as a "bulk-fill all ON rows" convenience that writes
        // into this field rather than a separate code path, so the original single-color workflow
        // (type one color, toggle categories, arm) still works exactly as before.
        //
        // Deliberately does NOT call _onToggled on every keystroke anymore (it used to) - with one
        // color box that meant one full-scene reapply per character typed; with a color box PER
        // CATEGORY (this feature), typing while several categories are armed fired many rapid,
        // back-to-back full-selection recolor passes. That's the confirmed cause of two real
        // in-editor crashes (both an access violation in Qt5Core.dll - the editor's own Qt-based
        // shell, not this mod's GauntletUI overlay - at the identical fault offset both times,
        // consistent with a native UI toolkit choking on rapid concurrent mutation of the entity
        // data it's simultaneously trying to read/redraw). Typing now just stores the value; see
        // ContinuousRecolorVM.ExecuteApplyColors for the explicit trigger.
        [DataSourceProperty]
        public string ColorInput
        {
            get => _colorInput;
            set { if (value != _colorInput) { _colorInput = value; OnPropertyChangedWithValue(value, nameof(ColorInput)); } }
        }

        // For bulk operations (load palette, fill-to-ON-rows) that touch many rows at once -
        // identical to the setter above now that it's already silent, kept as its own method so the
        // call sites stay self-documenting about which case they're in.
        public void SetColorSilent(string value)
        {
            if (value == _colorInput) return;
            _colorInput = value;
            OnPropertyChangedWithValue(value, nameof(ColorInput));
        }

        // Plain button + Command.Click, not ButtonType="Toggle" + a two-way "@IsOn" binding - that
        // pattern was live-tested elsewhere in this tool (TagChanged/RenameChanged/AssignUid) and
        // found to get stuck ON with no way to click it off, despite looking correct in the
        // decompiled ButtonWidget source. Using the proven plain-button pattern here instead of
        // risking the same bug, rather than waiting for a bug report on this specific checkbox.
        public void ExecuteToggle()
        {
            IsOn = !IsOn;
            _onToggled?.Invoke();
        }
    }
}
