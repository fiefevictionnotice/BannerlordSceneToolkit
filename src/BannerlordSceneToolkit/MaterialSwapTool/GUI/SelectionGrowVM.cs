using System;
using MaterialSwapTool.Core;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    // Readout for the grow/shrink modal. Everything shown is derived from SelectionGrow's own
    // state and refreshed once per tick (same pattern as NumericTransformVM) - the keys are
    // polled by the layer rather than bound to widgets, because Gauntlet has no keypress event
    // for arbitrary keys and the whole point of this panel is a number that changes as you
    // press things.
    public class SelectionGrowVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;

        public SelectionGrowVM(Action closeAction, Action beginDragAction)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
        }

        public void RefreshFromState()
        {
            OnPropertyChanged(nameof(RadiusText));
            OnPropertyChanged(nameof(CountText));
            OnPropertyChanged(nameof(CapWarningText));
        }

        [DataSourceProperty]
        public string HeaderText => "Grow Selection";

        [DataSourceProperty]
        public string RadiusText => SelectionGrow.RadiusText + " units";

        [DataSourceProperty]
        public string CountText =>
            $"{SelectionGrow.BaseCount} selected  ->  {SelectionGrow.ResultCount} within radius";

        // Only non-empty when the cap actually bit, so the panel does not carry a permanent
        // warning about a limit nobody is near.
        [DataSourceProperty]
        public string CapWarningText =>
            SelectionGrow.WasCapped ? "Capped at 2000 - use a smaller radius." : "";

        // Spells out the step sizes rather than leaving them to be discovered - the whole point
        // of the modal is that you drive it by feel while watching the count.
        [DataSourceProperty]
        public string HintText =>
            "+/-  (numpad or keyboard)  or  Up/Down     step 1      hold Shift  step 5      hold Ctrl  step 0.25\n" +
            "type a number for an exact radius      Enter applies      Esc restores the original selection";

        public void ExecuteCommit() { SelectionGrow.Commit(); _closeAction?.Invoke(); }
        public void ExecuteCancel() { SelectionGrow.Cancel(); _closeAction?.Invoke(); }

        public void ExecuteGrow() { SelectionGrow.Step(1f); RefreshFromState(); }
        public void ExecuteShrink() { SelectionGrow.Step(-1f); RefreshFromState(); }

        public void ExecuteDragStart() => _beginDragAction?.Invoke();
    }
}
