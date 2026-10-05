using PrefabSwapperTool.Core;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    // Read-only readout for the last-operation HUD. No commands - the layer it lives on is not a
    // focus layer, so it can never take a click or keystroke from the editor.
    //
    // VISIBILITY IS NOT TIED TO THE TAKEOVER WINDOW. It was, and that was wrong: the 30-second
    // window exists so a stray digit long after an operation does not hijack it, which is a rule
    // about INPUT. Reusing it for the display meant the readout vanished whenever you paused for
    // half a minute - it showed for a few operations in a row and then disappeared, which is
    // exactly what it looked like from the outside.
    //
    // The readout now persists until the next operation replaces it. The hint about typing is what
    // comes and goes, because that part really does expire.
    public class LastOperationHudVM : ViewModel
    {
        private string _text = "";
        private bool _visible;

        public void Refresh()
        {
            var label = ManipulationWatcher.LastOperationText ?? "";
            var canType = ManipulationWatcher.HasRecentOperation;
            var live = ManipulationWatcher.OperationInProgress;

            // Mid-drag the label is a RUNNING total measured from the drag's origin, refreshed
            // every watcher sample. Typing takes over a LIVE drag too, so that hint stays;
            // only the dismiss hint is held back until the operation is finished.
            var wantedText = string.IsNullOrEmpty(label)
                ? ""
                : live ? $"Now: {label}   -   type a number to edit"
                : canType ? $"Last: {label}   -   type a number to edit  |  click to dismiss"
                          : $"Last: {label}   -   click to dismiss";

            var wantedVisible = !string.IsNullOrEmpty(label);

            if (wantedVisible != _visible) { _visible = wantedVisible; OnPropertyChanged(nameof(IsVisible)); }
            if (wantedText != _text) { _text = wantedText; OnPropertyChanged(nameof(Text)); }
        }

        [DataSourceProperty]
        public bool IsVisible => _visible;

        [DataSourceProperty]
        public string Text => _text;
    }
}
