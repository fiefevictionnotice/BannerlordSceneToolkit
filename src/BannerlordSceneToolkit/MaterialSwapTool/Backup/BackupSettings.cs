using System;
using System.IO;
using Newtonsoft.Json;

namespace MaterialSwapTool.Backup
{
    // Runtime-editable backup settings, persisted next to the tool's other user data.
    //
    // These were compile-time constants (BackupsEnabled, AutoBackupIntervalSeconds,
    // MaxBackupsPerScene, and a hardcoded BackupRoot). That is the direct reason backups sat
    // switched off for three days: turning them back on was not a click, it was a source edit,
    // a rebuild and a redeploy - so during crash-hunting the only affordable move was "leave them
    // off", and nothing surfaced that the safety net was gone.
    //
    // PeriodicEnabled is deliberately separate from Enabled. The two have different risk profiles:
    // the interval timer copies files in the background whenever it fires, while before-apply
    // backups happen on an action you just took. When a background file-copy is suspect, the right
    // response is to disable the timer and KEEP before-apply - which the single old switch could
    // not express.
    public class BackupSettings
    {
        public bool Enabled { get; set; } = true;              // master: any scene backup at all
        public bool PeriodicEnabled { get; set; } = true;      // the interval timer specifically
        public int IntervalMinutes { get; set; } = 4;
        public int MaxPerScene { get; set; } = 50;
        public string CustomRoot { get; set; } = "";           // empty = default Documents location

        // On-screen notifications, separate from whether the backups themselves run. Both default
        // ON, and both keep writing to tool.log even when switched off - silencing the popup is
        // not the same as losing the record, and a backup that FAILED silently with no trace
        // anywhere would be the worst outcome this tool could produce.
        public bool BackupWarningsEnabled { get; set; } = true;   // "backup in progress", "backup FAILED"
        public bool SaveRemindersEnabled { get; set; } = true;    // no-save watchdog + post-apply "save the scene"

        private static BackupSettings _current;

        public static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "MaterialSwapTool", "backup_settings.json");

        public static BackupSettings Current
        {
            get
            {
                if (_current != null) return _current;
                try
                {
                    if (File.Exists(SettingsPath))
                    {
                        _current = JsonConvert.DeserializeObject<BackupSettings>(File.ReadAllText(SettingsPath));
                        if (_current != null) { Clamp(_current); return _current; }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("BackupSettings: failed to load, using defaults: " + ex.Message);
                }
                _current = new BackupSettings();
                return _current;
            }
        }

        // Guards against a hand-edited file setting something self-defeating - a 0-minute interval
        // would try to back up every tick, and 0 retention would delete a backup as soon as it
        // was written.
        private static void Clamp(BackupSettings s)
        {
            if (s.IntervalMinutes < 1) s.IntervalMinutes = 1;
            if (s.IntervalMinutes > 120) s.IntervalMinutes = 120;
            if (s.MaxPerScene < 1) s.MaxPerScene = 1;
            if (s.MaxPerScene > 500) s.MaxPerScene = 500;
        }

        public void Save()
        {
            try
            {
                Clamp(this);
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
                _current = this;
                Log.Info($"BackupSettings saved: enabled={Enabled} periodic={PeriodicEnabled} " +
                         $"interval={IntervalMinutes}m keep={MaxPerScene} root='{CustomRoot}' " +
                         $"backupWarnings={BackupWarningsEnabled} saveReminders={SaveRemindersEnabled}");
            }
            catch (Exception ex)
            {
                Log.Error("BackupSettings save failed: " + ex);
            }
        }
    }
}
