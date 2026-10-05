using System;
using PrefabSwapperTool.Core;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    // Display-only ViewModel for the numeric transform modal. All the state lives in
    // NumericTransform (a static, because the key handling runs from the tick patch rather than
    // from widget events); this just mirrors it for the panel and offers buttons for the same
    // things the keys do, since a Cancel you can click was explicitly asked for.
    public class NumericTransformVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;

        public NumericTransformVM(Action closeAction, Action beginDragAction)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
        }

        // Called every tick by the layer so typing shows up without widget events.
        public void RefreshFromState()
        {
            OnPropertyChanged(nameof(TypedText));
            OnPropertyChanged(nameof(ModeText));
            OnPropertyChanged(nameof(AxisText));
            OnPropertyChanged(nameof(HeaderText));
            OnPropertyChanged(nameof(SliderMin));
            OnPropertyChanged(nameof(SliderMax));
            OnPropertyChanged(nameof(SliderValue));
            OnPropertyChanged(nameof(AxesModeText));
            OnPropertyChanged(nameof(RangeText));
        }

        [DataSourceProperty]
        public string HeaderText =>
            $"{NumericTransform.CapturedCount} entity(ies) captured - editor deselection will not affect them";

        [DataSourceProperty]
        public string ModeText =>
            NumericTransform.CurrentMode == NumericTransform.Mode.Rotate
                ? "Mode: ROTATE (degrees)   [R = rotate, G = move]"
                : "Mode: MOVE (metres)   [R = rotate, G = move]";

        [DataSourceProperty]
        public string AxisText => $"Axis: {NumericTransform.CurrentAxis}   [X / Y (or C) / Z - Z is height]";

        [DataSourceProperty]
        public string TypedText => string.IsNullOrEmpty(NumericTransform.Typed) ? "_" : NumericTransform.Typed;

        // SLIDER. Drives the same value the keyboard does - they are two ways of setting one
        // number, not two numbers. Typing updates the slider position and dragging the slider
        // rewrites the typed text, so whichever you touch last is what applies.
        //
        // The range follows the mode, because degrees and metres are not the same scale: a
        // rotation wants -180..180, a move wants something in the order of the things you are
        // nudging. Both are symmetric about zero so the slider can push in either direction.
        [DataSourceProperty]
        public float SliderMin => NumericTransform.CurrentMode == NumericTransform.Mode.Rotate ? -360f : -NumericTransform.MoveSliderRange;

        [DataSourceProperty]
        public float SliderMax => NumericTransform.CurrentMode == NumericTransform.Mode.Rotate ? 360f : NumericTransform.MoveSliderRange;

        [DataSourceProperty]
        public float SliderValue
        {
            get => NumericTransform.TryGetValue(out var v) ? v : 0f;
            set
            {
                // Rounded before it is written back: a raw float from a slider produces
                // "37.41999816894531" in the box, which is unreadable and useless to retype.
                var snapped = NumericTransform.CurrentMode == NumericTransform.Mode.Rotate
                    ? (float)Math.Round(value, 1)
                    : (float)Math.Round(value, 2);

                // The layer refreshes this property every tick so that TYPING moves the slider.
                // That refresh can echo straight back into this setter, and without this guard
                // the slider and the keyboard would overwrite each other every frame.
                var current = NumericTransform.TryGetValue(out var c) ? c : 0f;
                if (Math.Abs(snapped - current) < 0.0001f) return;

                // CONFIRMED BUG, fixed 2026-08-23 ("I literally cannot type higher than 20"): a
                // typed value beyond the slider's range pegs the SliderWidget at its end, and
                // that CLAMPED value echoed back through this setter, overwriting the typed
                // number with the cap. A pegged-slider echo while the typed value is out of
                // range is not a user drag - ignore it. Dragging the handle to the very end
                // still works: at that moment the typed value is within range.
                if (Math.Abs(current) > SliderMax + 0.0001f && Math.Abs(snapped) >= SliderMax - 0.0001f) return;

                NumericTransform.SetTypedValue(snapped);
                OnPropertyChangedWithValue(snapped, nameof(SliderValue));
                RefreshFromState();
            }
        }

        [DataSourceProperty]
        public string AxesModeText => NumericTransform.UseLocalAxes ? "Axes: LOCAL (first entity)" : "Axes: WORLD";

        [DataSourceProperty]
        public string RangeText => $"Slider range: +/-{NumericTransform.MoveSliderRange:0}";

        public void ExecuteToggleAxes() { NumericTransform.ToggleAxesSpace(); RefreshFromState(); }
        public void ExecuteCycleRange() { NumericTransform.CycleMoveRange(); RefreshFromState(); }

        public void ExecuteCommit() { NumericTransform.Commit(); _closeAction?.Invoke(); }
        public void ExecuteCancel() { NumericTransform.Cancel(); _closeAction?.Invoke(); }

        public void ExecuteModeRotate() { NumericTransform.SetMode(NumericTransform.Mode.Rotate); RefreshFromState(); }
        public void ExecuteModeMove() { NumericTransform.SetMode(NumericTransform.Mode.Translate); RefreshFromState(); }
        public void ExecuteAxisX() { NumericTransform.SetAxis(NumericTransform.Axis.X); RefreshFromState(); }
        public void ExecuteAxisY() { NumericTransform.SetAxis(NumericTransform.Axis.Y); RefreshFromState(); }
        public void ExecuteAxisZ() { NumericTransform.SetAxis(NumericTransform.Axis.Z); RefreshFromState(); }

        public void ExecuteDragStart() => _beginDragAction?.Invoke();
    }
}
