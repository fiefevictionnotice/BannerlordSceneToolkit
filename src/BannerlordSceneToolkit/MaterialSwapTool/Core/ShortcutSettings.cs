using System;
using System.IO;
using Newtonsoft.Json;

namespace MaterialSwapTool.Core
{
    // Which optional keyboard shortcuts are live, persisted so a shortcut you switched off stays
    // off. Separate file from BackupSettings: these are input bindings, not backup policy, and
    // mixing them would mean a backup-settings write could clobber a keybind change made in the
    // same session.
    //
    // The F5-F9 panel hotkeys are NOT here. Those are the tools' entry points - making them
    // disableable from inside a panel you can only reach with one of them is a way to lock
    // yourself out. tool_toggles.txt already covers switching whole tools off.
    public class ShortcutSettings
    {
        // Ctrl/Shift+Backspace clears the focused text field. Harmless and universally wanted, so
        // it defaults on, but it is listed for completeness and in case it ever fights with
        // something the editor wants Backspace for.
        public bool ClearFieldEnabled { get; set; } = true;

        // Hide everything except the selection, and restore. Non-destructive - visibility only.
        public bool IsolateEnabled { get; set; } = true;

        // One-time-per-session reminder shown (editor warning + log) the FIRST time Isolate
        // activates: "Shift+O Isolate mode active, Shift+O to reverse". Requested after the
        // binding moved twice in one evening (bare-I stamped entities; a native Ctrl+Shift+O
        // hid the selection instead) - a mode this easy to enter by accident deserves to
        // announce its exit key.
        public bool IsolateHintEnabled { get; set; } = true;

        // Re-apply the last transform to the CURRENT selection. Deliberately behind a
        // three-key combo AND capturable by undo, because repeating a transform onto the wrong
        // selection is the easiest way to make a mess with this toolkit.
        public bool RepeatLastEnabled { get; set; } = true;

        // Ctrl+Shift+T opens the numeric transform modal. Non-destructive by construction -
        // Esc puts everything back and discards its own undo step - so it defaults on.
        public bool NumericTransformEnabled { get; set; } = true;

        // Ctrl+Shift+P promotes the selection to its top-level prefab roots - click a child
        // part, press it, the whole prefab is selected. Selection-only, nothing is modified,
        // so it defaults on.
        public bool SelectRootEnabled { get; set; } = true;

        // Ctrl+Numpad+ opens the proximity grow/shrink modal. Selection-only, and Esc puts the
        // original selection back, so it defaults on.
        public bool SelectionGrowEnabled { get; set; } = true;

        // Keep the editor's Windows cursor while toolkit panels are open, instead of letting the
        // engine swap in its oversized Gauntlet cursor. Not a key binding, but it is a toolkit-wide
        // input preference and this is the one settings flyout every panel can reach. Defaults on;
        // see Core/ToolkitCursor for what it changes and why it is switchable.
        public bool NativeCursorEnabled { get; set; } = true;

        private static ShortcutSettings _current;

        public static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "MaterialSwapTool", "shortcut_settings.json");

        public static ShortcutSettings Current
        {
            get
            {
                if (_current != null) return _current;
                try
                {
                    if (File.Exists(SettingsPath))
                    {
                        _current = JsonConvert.DeserializeObject<ShortcutSettings>(File.ReadAllText(SettingsPath));
                        if (_current != null) return _current;
                    }
                }
                catch (Exception ex) { Log.Warn("ShortcutSettings: failed to load, using defaults: " + ex.Message); }
                _current = new ShortcutSettings();
                return _current;
            }
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
                _current = this;
                Log.Info($"ShortcutSettings saved: clearField={ClearFieldEnabled} isolate={IsolateEnabled} repeat={RepeatLastEnabled} numeric={NumericTransformEnabled} selectRoot={SelectRootEnabled} grow={SelectionGrowEnabled} nativeCursor={NativeCursorEnabled}");
            }
            catch (Exception ex) { Log.Error("ShortcutSettings save failed: " + ex); }
        }
    }
}
