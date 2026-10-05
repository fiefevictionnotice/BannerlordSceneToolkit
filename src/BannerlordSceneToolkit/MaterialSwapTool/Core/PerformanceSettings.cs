using System;
using System.IO;
using Newtonsoft.Json;

namespace MaterialSwapTool.Core
{
    // Whether the stutter check runs at all. Its own file rather than a field on ShortcutSettings:
    // this is a "do you want to be told" preference, not an input binding, and mixing them would
    // mean a keybind change could clobber a dismissal made in the same session.
    public class PerformanceSettings
    {
        public bool WarningsEnabled { get; set; } = true;

        private static PerformanceSettings _current;

        public static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "MaterialSwapTool", "performance_settings.json");

        public static PerformanceSettings Current
        {
            get
            {
                if (_current != null) return _current;
                try
                {
                    if (File.Exists(SettingsPath))
                    {
                        _current = JsonConvert.DeserializeObject<PerformanceSettings>(File.ReadAllText(SettingsPath));
                        if (_current != null) return _current;
                    }
                }
                catch (Exception ex) { Log.Warn("PerformanceSettings: failed to load, using defaults: " + ex.Message); }
                _current = new PerformanceSettings();
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
                Log.Info($"PerformanceSettings saved: warnings={WarningsEnabled}");
            }
            catch (Exception ex) { Log.Error("PerformanceSettings save failed: " + ex); }
        }
    }
}
