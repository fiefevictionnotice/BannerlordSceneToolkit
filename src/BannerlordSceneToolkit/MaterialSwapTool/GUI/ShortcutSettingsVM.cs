using System;
using MaterialSwapTool.Core;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    // F9 -> "Keyboard Shortcuts". Switches for the optional shortcuts only.
    //
    // The F5-F9 panel hotkeys are deliberately absent: making the keys that OPEN the tools
    // disableable from inside a panel you can only reach with one of them is a way to lock
    // yourself out. tool_toggles.txt already covers switching whole tools off, before startup.
    //
    // No confirmation on these, unlike the notification switches. Turning a shortcut off cannot
    // hide a problem from you - the worst case is a key that stops working, which is obvious
    // immediately and fixed by pressing the same button again.
    public class ShortcutSettingsVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;
        private string _statusText = "";

        public ShortcutSettingsVM(Action closeAction, Action beginDragAction)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
        }

        [DataSourceProperty]
        public string StatusText
        {
            get => _statusText;
            set { if (value != _statusText) { _statusText = value; OnPropertyChangedWithValue(value, nameof(StatusText)); } }
        }

        [DataSourceProperty]
        public string ClearFieldLabel =>
            ShortcutSettings.Current.ClearFieldEnabled ? "Clear field: ON" : "Clear field: OFF";

        [DataSourceProperty]
        public string ClearFieldColor =>
            ShortcutSettings.Current.ClearFieldEnabled ? "#4fb96aFF" : "#c9484eFF";

        [DataSourceProperty]
        public string IsolateLabel =>
            ShortcutSettings.Current.IsolateEnabled ? "Isolate: ON" : "Isolate: OFF";

        [DataSourceProperty]
        public string IsolateColor =>
            ShortcutSettings.Current.IsolateEnabled ? "#4fb96aFF" : "#c9484eFF";

        [DataSourceProperty]
        public string RepeatLabel =>
            ShortcutSettings.Current.RepeatLastEnabled ? "Repeat last: ON" : "Repeat last: OFF";

        [DataSourceProperty]
        public string RepeatColor =>
            ShortcutSettings.Current.RepeatLastEnabled ? "#4fb96aFF" : "#c9484eFF";

        public void ExecuteToggleClearField()
        {
            var s = ShortcutSettings.Current;
            s.ClearFieldEnabled = !s.ClearFieldEnabled;
            s.Save();
            OnPropertyChanged(nameof(ClearFieldLabel));
            OnPropertyChanged(nameof(ClearFieldColor));
            StatusText = s.ClearFieldEnabled
                ? "Ctrl/Shift+Backspace clears the focused text field."
                : "Ctrl/Shift+Backspace off - Backspace behaves normally everywhere.";
        }

        public void ExecuteToggleIsolate()
        {
            var s = ShortcutSettings.Current;
            s.IsolateEnabled = !s.IsolateEnabled;
            s.Save();
            OnPropertyChanged(nameof(IsolateLabel));
            OnPropertyChanged(nameof(IsolateColor));

            // Leaving the scene isolated while switching the shortcut off would strand a hidden
            // scene with no key to bring it back.
            if (!s.IsolateEnabled && IsolationManager.IsActive)
                StatusText = "Isolate off. " + IsolationManager.Restore();
            else
                StatusText = s.IsolateEnabled
                    ? "Shift+O isolates the selection, and restores it on a second press."
                    : "Isolate off.";
        }

        [DataSourceProperty]
        public string IsolateHintLabel =>
            ShortcutSettings.Current.IsolateHintEnabled ? "Isolate first-use hint: ON" : "Isolate first-use hint: OFF";

        [DataSourceProperty]
        public string IsolateHintColor =>
            ShortcutSettings.Current.IsolateHintEnabled ? "#4fb96aFF" : "#c9484eFF";

        public void ExecuteToggleIsolateHint()
        {
            var s = ShortcutSettings.Current;
            s.IsolateHintEnabled = !s.IsolateHintEnabled;
            s.Save();
            OnPropertyChanged(nameof(IsolateHintLabel));
            OnPropertyChanged(nameof(IsolateHintColor));
            StatusText = s.IsolateHintEnabled
                ? "The first Isolate each session shows 'Shift+O Isolate mode active, Shift+O to reverse'."
                : "Isolate first-use hint off - Isolate toggles silently apart from its normal status line.";
        }

        public void ExecuteToggleRepeat()
        {
            var s = ShortcutSettings.Current;
            s.RepeatLastEnabled = !s.RepeatLastEnabled;
            s.Save();
            OnPropertyChanged(nameof(RepeatLabel));
            OnPropertyChanged(nameof(RepeatColor));
            StatusText = s.RepeatLastEnabled
                ? "Shift+R replays the last drag onto the current selection. It takes an undo step first."
                : "Repeat last off.";
        }

        [DataSourceProperty]
        public string NumericLabel =>
            ShortcutSettings.Current.NumericTransformEnabled ? "Numeric transform: ON" : "Numeric transform: OFF";

        [DataSourceProperty]
        public string NumericColor =>
            ShortcutSettings.Current.NumericTransformEnabled ? "#4fb96aFF" : "#c9484eFF";

        public void ExecuteToggleNumeric()
        {
            var s = ShortcutSettings.Current;
            s.NumericTransformEnabled = !s.NumericTransformEnabled;
            s.Save();
            OnPropertyChanged(nameof(NumericLabel));
            OnPropertyChanged(nameof(NumericColor));
            StatusText = s.NumericTransformEnabled
                ? "Ctrl+Shift+T opens numeric transform: type a number, Enter applies, Esc puts it back."
                : "Numeric transform off.";
        }

        [DataSourceProperty]
        public string SelectRootLabel =>
            ShortcutSettings.Current.SelectRootEnabled ? "Select root: ON" : "Select root: OFF";

        [DataSourceProperty]
        public string SelectRootColor =>
            ShortcutSettings.Current.SelectRootEnabled ? "#4fb96aFF" : "#c9484eFF";

        public void ExecuteToggleSelectRoot()
        {
            var s = ShortcutSettings.Current;
            s.SelectRootEnabled = !s.SelectRootEnabled;
            s.Save();
            OnPropertyChanged(nameof(SelectRootLabel));
            OnPropertyChanged(nameof(SelectRootColor));
            StatusText = s.SelectRootEnabled
                ? "Ctrl+Shift+P selects the top-level prefab(s) of whatever parts are selected."
                : "Select root off.";
        }

        [DataSourceProperty]
        public string SelectionGrowLabel =>
            ShortcutSettings.Current.SelectionGrowEnabled ? "Grow selection: ON" : "Grow selection: OFF";

        [DataSourceProperty]
        public string SelectionGrowColor =>
            ShortcutSettings.Current.SelectionGrowEnabled ? "#4fb96aFF" : "#c9484eFF";

        public void ExecuteToggleSelectionGrow()
        {
            var s = ShortcutSettings.Current;
            s.SelectionGrowEnabled = !s.SelectionGrowEnabled;
            s.Save();
            OnPropertyChanged(nameof(SelectionGrowLabel));
            OnPropertyChanged(nameof(SelectionGrowColor));
            StatusText = s.SelectionGrowEnabled
                ? "Ctrl+Numpad+ grows the selection by proximity; the radius is live until you press Enter."
                : "Grow selection off.";
        }

        // Performance warnings live here too - a permanent dismissal has to have somewhere to be
        // undone, or "stop asking" quietly becomes "never again" with no way back.
        [DataSourceProperty]
        public string PerformanceLabel =>
            PerformanceSettings.Current.WarningsEnabled ? "Stutter warnings: ON" : "Stutter warnings: OFF";

        [DataSourceProperty]
        public string PerformanceColor =>
            PerformanceSettings.Current.WarningsEnabled ? "#4fb96aFF" : "#c9484eFF";

        public void ExecuteTogglePerformance()
        {
            var p = PerformanceSettings.Current;
            p.WarningsEnabled = !p.WarningsEnabled;
            p.Save();
            OnPropertyChanged(nameof(PerformanceLabel));
            OnPropertyChanged(nameof(PerformanceColor));
            StatusText = p.WarningsEnabled
                ? PerformanceWatchdog.DescribeState()
                : "Stutter warnings off - nothing else changes about what the toolkit does.";
        }

        // Native cursor: keep the editor's Windows arrow while panels are open instead of the
        // engine's oversized Gauntlet cursor. Applied to every open panel on the spot, so if the
        // cursor ever vanishes in native mode the way back is one click, not a reopen.
        [DataSourceProperty]
        public string NativeCursorLabel =>
            ShortcutSettings.Current.NativeCursorEnabled ? "Native cursor: ON" : "Native cursor: OFF";

        [DataSourceProperty]
        public string NativeCursorColor =>
            ShortcutSettings.Current.NativeCursorEnabled ? "#4fb96aFF" : "#c9484eFF";

        public void ExecuteToggleNativeCursor()
        {
            var s = ShortcutSettings.Current;
            s.NativeCursorEnabled = !s.NativeCursorEnabled;
            s.Save();
            OnPropertyChanged(nameof(NativeCursorLabel));
            OnPropertyChanged(nameof(NativeCursorColor));
            int touched = BannerlordSceneToolkit.ToolkitCursor.ReapplyToOpenLayers();
            StatusText = s.NativeCursorEnabled
                ? $"Native cursor on - panels keep the editor's Windows cursor. Applied to {touched} open panel(s)."
                : $"Native cursor off - panels show the engine's own (large) cursor again. Applied to {touched} open panel(s).";
        }

        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();
    }
}
