using System;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;

namespace BannerlordSceneToolkit
{
    // Ctrl+Backspace / Shift+Backspace clears the whole focused text field.
    //
    // Beats a pair of per-field clear buttons on two counts: the rule rows are already crowded
    // (from, to, colour, invert, delete) and two more buttons per row would be four more widgets
    // on every row of a list that can run to hundreds; and a shortcut works in EVERY text field in
    // the toolkit rather than only the ones somebody remembered to put buttons on.
    //
    // Uses the GLOBAL TaleWorlds.InputSystem.Input rather than the layer's own input, matching
    // what MaterialSwapLayer.Tick already does for its click-outside-to-defocus fix. The layer's
    // input is gated by focus, which is precisely the state this needs to work in.
    //
    // Both modifiers are accepted because Ctrl+Backspace already means "delete previous word" in
    // most text fields and muscle memory splits between the two - there is no reason to make the
    // user guess which one this toolkit chose.
    public static class TextFieldShortcuts
    {
        public static void Tick(Widget rootWidget)
        {
            if (rootWidget == null) return;

            if (!MaterialSwapTool.Core.ShortcutSettings.Current.ClearFieldEnabled) return;
            if (!Input.IsKeyPressed(InputKey.BackSpace)) return;

            bool modifier = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl)
                         || Input.IsKeyDown(InputKey.LeftShift)   || Input.IsKeyDown(InputKey.RightShift);
            if (!modifier) return;

            EditableTextWidget field;
            try { field = rootWidget.EventManager?.FocusedWidget as EditableTextWidget; }
            catch { return; }
            if (field == null) return;

            try
            {
                // RealText is the property our XML binds (RealText="@Something"), so writing it is
                // what actually reaches the ViewModel - clearing only the visible text would look
                // right and leave the underlying value untouched.
                field.RealText = string.Empty;

                // ...and this resets the widget's own visible text, selection and cursor, which do
                // not necessarily follow from the property write alone.
                field.SetAllText(string.Empty);
            }
            catch (Exception ex)
            {
                MaterialSwapTool.Log.Warn("[TextFieldShortcuts] clear failed: " + ex.Message);
            }
        }
    }
}
