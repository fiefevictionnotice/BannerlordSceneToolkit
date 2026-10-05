using System;
using MaterialSwapTool.Backup;
using MaterialSwapTool.Core;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    // F9 - Backups. Replaces the retired Flora Swap tool's slot.
    //
    // Exists because every backup control was previously either a compile-time constant or absent
    // entirely: you could not change the interval, the location, or whether backups ran at all
    // without editing source and redeploying. That is the direct cause of the three-day gap where
    // backups were off and nothing said so.
    public class BackupPanelVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;

        private string _statusText = "";
        private string _intervalInput;
        private string _keepInput;
        private string _rootInput;

        public BackupPanelVM(Action closeAction, Action beginDragAction)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            var s = BackupSettings.Current;
            _intervalInput = s.IntervalMinutes.ToString();
            _keepInput = s.MaxPerScene.ToString();
            _rootInput = s.CustomRoot ?? "";
            RefreshStats();
        }

        // ---- live status ----
        private string _summaryText = "";
        private string _lastResultText = "";
        private string _stalenessText = "";
        private string _stalenessColor = "#cfc7b8FF";

        [DataSourceProperty]
        public string SummaryText
        {
            get => _summaryText;
            set { if (value != _summaryText) { _summaryText = value; OnPropertyChangedWithValue(value, nameof(SummaryText)); } }
        }

        // Verified outcome of the last backup - the async copy means a queued path is not proof
        // anything landed, so this is what actually happened.
        [DataSourceProperty]
        public string LastResultText
        {
            get => _lastResultText;
            set { if (value != _lastResultText) { _lastResultText = value; OnPropertyChangedWithValue(value, nameof(LastResultText)); } }
        }

        [DataSourceProperty]
        public string StalenessText
        {
            get => _stalenessText;
            set { if (value != _stalenessText) { _stalenessText = value; OnPropertyChangedWithValue(value, nameof(StalenessText)); } }
        }

        [DataSourceProperty]
        public string StalenessColor
        {
            get => _stalenessColor;
            set { if (value != _stalenessColor) { _stalenessColor = value; OnPropertyChangedWithValue(value, nameof(StalenessColor)); } }
        }

        // Size of the backup folder on disk. Separate from SummaryText because the per-scene
        // figure there is what retention manages; this is what the folder actually costs,
        // _ToolData and all, plus the room left on that drive.
        private string _folderSizeText = "";

        [DataSourceProperty]
        public string FolderSizeText
        {
            get => _folderSizeText;
            set { if (value != _folderSizeText) { _folderSizeText = value; OnPropertyChangedWithValue(value, nameof(FolderSizeText)); } }
        }

        public void RefreshStats()
        {
            var st = BackupManager.GetStats();
            LastResultText = BackupManager.DescribeLastResult();
            SummaryText = st.BackupCount == 0
                ? $"No backups yet.   Location: {BackupManager.BackupRootPath}"
                : $"{st.BackupCount} backup(s) across {st.SceneCount} scene(s), {BackupManager.FormatBytes(st.TotalBytes)}.   Location: {BackupManager.BackupRootPath}";

            if (st.FolderBytes == 0 && st.BackupCount == 0)
                FolderSizeText = "Backups folder: empty.";
            else
            {
                var text = $"Backups folder on disk: {BackupManager.FormatBytes(st.FolderBytes)}" +
                           $"   (scene backups {BackupManager.FormatBytes(st.TotalBytes)}, tool data {BackupManager.FormatBytes(st.ToolDataBytes)})";
                if (st.DriveFreeBytes >= 0)
                    text += $"   Free on drive: {BackupManager.FormatBytes(st.DriveFreeBytes)}";
                FolderSizeText = text;
            }

            if (!BackupSettings.Current.Enabled)
            {
                StalenessText = "BACKUPS ARE OFF - nothing is being written.";
                StalenessColor = "#c9484eFF";
            }
            else if (st.Newest == null)
            {
                StalenessText = "No backup has been written yet.";
                StalenessColor = "#c9782fFF";
            }
            else
            {
                // Colour-graded rather than a bare timestamp: the failure mode this panel exists to
                // prevent is a stale safety net going unnoticed, and a date alone does not shout.
                var age = DateTime.Now - st.Newest.Value;
                string ageText = age.TotalMinutes < 1 ? "just now"
                    : age.TotalMinutes < 60 ? $"{(int)age.TotalMinutes} min ago"
                    : age.TotalHours < 48 ? $"{(int)age.TotalHours} hours ago"
                    : $"{(int)age.TotalDays} DAYS ago";
                StalenessText = $"Last backup: {ageText}   ({st.NewestLabel})";
                StalenessColor = age.TotalHours < 1 ? "#4fb96aFF" : age.TotalHours < 24 ? "#c9782fFF" : "#c9484eFF";
            }
        }

        // ---- toggles ----
        [DataSourceProperty]
        public string EnabledLabel => BackupSettings.Current.Enabled ? "Backups: ON" : "Backups: OFF";

        [DataSourceProperty]
        public string EnabledColor => BackupSettings.Current.Enabled ? "#4fb96aFF" : "#c9484eFF";

        [DataSourceProperty]
        public string PeriodicLabel => BackupSettings.Current.PeriodicEnabled ? "Timer: ON" : "Timer: OFF";

        [DataSourceProperty]
        public string PeriodicColor => BackupSettings.Current.PeriodicEnabled ? "#4fb96aFF" : "#c9782fFF";

        public void ExecuteToggleEnabled()
        {
            var s = BackupSettings.Current;
            s.Enabled = !s.Enabled;
            s.Save();
            OnPropertyChanged(nameof(EnabledLabel));
            OnPropertyChanged(nameof(EnabledColor));
            RefreshStats();
            StatusText = s.Enabled
                ? "Backups enabled - before-apply and scene-switch backups will run."
                : "Backups OFF. Nothing will be written, including before destructive operations.";
        }

        public void ExecuteTogglePeriodic()
        {
            var s = BackupSettings.Current;
            s.PeriodicEnabled = !s.PeriodicEnabled;
            s.Save();
            OnPropertyChanged(nameof(PeriodicLabel));
            OnPropertyChanged(nameof(PeriodicColor));
            StatusText = s.PeriodicEnabled
                ? $"Interval timer on - a backup every {s.IntervalMinutes} minute(s)."
                : "Interval timer off. Before-apply and scene-switch backups still run - this only stops the background timer.";
        }

        // ---- editable settings ----
        [DataSourceProperty]
        public string IntervalInput
        {
            get => _intervalInput;
            set { if (value != _intervalInput) { _intervalInput = value; OnPropertyChangedWithValue(value, nameof(IntervalInput)); } }
        }

        [DataSourceProperty]
        public string KeepInput
        {
            get => _keepInput;
            set { if (value != _keepInput) { _keepInput = value; OnPropertyChangedWithValue(value, nameof(KeepInput)); } }
        }

        [DataSourceProperty]
        public string RootInput
        {
            get => _rootInput;
            set { if (value != _rootInput) { _rootInput = value; OnPropertyChangedWithValue(value, nameof(RootInput)); } }
        }

        [DataSourceProperty]
        public string StatusText
        {
            get => _statusText;
            set { if (value != _statusText) { _statusText = value; OnPropertyChangedWithValue(value, nameof(StatusText)); } }
        }

        public void ExecuteSaveSettings()
        {
            var s = BackupSettings.Current;

            if (!int.TryParse((IntervalInput ?? "").Trim(), out var mins))
            { StatusText = $"Interval '{IntervalInput}' isn't a number - unchanged."; return; }
            if (!int.TryParse((KeepInput ?? "").Trim(), out var keep))
            { StatusText = $"Keep '{KeepInput}' isn't a number - unchanged."; return; }

            var root = (RootInput ?? "").Trim();
            if (root.Length > 0)
            {
                // Checked before saving, not on first use: a bad path would otherwise turn every
                // future backup into a silent failure, which is the exact class of problem this
                // panel is meant to make impossible.
                try
                {
                    System.IO.Directory.CreateDirectory(root);
                }
                catch (Exception ex)
                {
                    StatusText = $"Can't use '{root}': {ex.Message} - location unchanged.";
                    return;
                }
            }

            s.IntervalMinutes = mins;
            s.MaxPerScene = keep;
            s.CustomRoot = root;
            s.Save();

            IntervalInput = s.IntervalMinutes.ToString();   // reflect clamping back to the boxes
            KeepInput = s.MaxPerScene.ToString();
            RefreshStats();
            StatusText = $"Saved. Every {s.IntervalMinutes} min, keep {s.MaxPerScene} per scene, "
                         + (root.Length == 0 ? "default location." : $"location: {root}");
        }

        // ---- actions ----
        public void ExecuteBackupNow()
        {
            var path = BackupManager.BackupNow("manual");
            RefreshStats();
            StatusText = path != null
                ? $"Backed up to {path}"
                : (BackupManager.BackupsEnabled ? "Backup failed - see tool.log."
                                                : "Backups are OFF - nothing was written. Turn them on above.");
        }

        // Deliberately manual-only: presets/palettes/categories change when you change them, not
        // continuously like a scene, so there is nothing for a timer to catch.
        public void ExecuteBackupToolData()
        {
            try
            {
                var dest = BackupManager.BackupToolData();
                RefreshStats();
                StatusText = dest != null
                    ? $"Tool data (presets, palettes, categories, cultures, whitelist) backed up to {dest}"
                    : "Nothing to back up - no tool data files found.";
            }
            catch (Exception ex)
            {
                StatusText = "Tool data backup failed: " + ex.Message;
                Log.Error("BackupToolData failed: " + ex);
            }
        }

        public void ExecuteOpenFolder()
        {
            try { StatusText = "Opened " + BackupManager.OpenBackupFolder(); }
            catch (Exception ex) { StatusText = "Couldn't open the folder: " + ex.Message; }
        }

        // Notifications live behind a flyout because switching them off makes the tool quieter
        // about things going wrong - see NotificationSettingsVM. The label here reports the
        // current state so a silenced warning is never invisible from the panel that owns it.
        [DataSourceProperty]
        public string NotificationsLabel
        {
            get
            {
                var s = BackupSettings.Current;
                int off = (s.BackupWarningsEnabled ? 0 : 1) + (s.SaveRemindersEnabled ? 0 : 1);
                return off == 0 ? "Notifications..." : $"Notifications ({off} OFF)...";
            }
        }

        [DataSourceProperty]
        public string NotificationsColor
        {
            get
            {
                var s = BackupSettings.Current;
                return s.BackupWarningsEnabled && s.SaveRemindersEnabled ? "#3ba1c9FF" : "#c9484eFF";
            }
        }

        public void ExecuteOpenNotifications()
        {
            NotificationSettingsLayer.Open(() =>
            {
                OnPropertyChanged(nameof(NotificationsLabel));
                OnPropertyChanged(nameof(NotificationsColor));
            });
        }

        public void ExecuteOpenShortcuts() => ShortcutSettingsLayer.Open();

        // Engineering history & confirmed-bug reference - the same accordion window as the User
        // Guide, built with the technical topic set (see DocumentationVM's technical flag).
        public void ExecuteOpenTechnicalDocs() => TechnicalDocsLayer.Toggle();

        public void ExecuteRefresh()
 { RefreshStats(); StatusText = "Refreshed."; }
        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();
    }
}
