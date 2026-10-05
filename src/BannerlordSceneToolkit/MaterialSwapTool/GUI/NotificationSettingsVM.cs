using System;
using MaterialSwapTool.Backup;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    // Opened from F9 -> "Notifications...". Deliberately behind a flyout rather than sitting on
    // the Backup panel itself: these switches make the tool quieter about things going wrong, so
    // the cost of hitting one by accident is that a real problem stops announcing itself. Two
    // deterrents, both asked for: it takes a deliberate trip into a sub-panel to reach them, and
    // switching one OFF requires confirming a dialog that spells out what stops appearing.
    //
    // Turning one back ON is not confirmed - restoring a warning is never the risky direction.
    public class NotificationSettingsVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;
        private readonly Action _onChanged;
        private string _statusText = "";

        public NotificationSettingsVM(Action closeAction, Action beginDragAction, Action onChanged)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            _onChanged = onChanged;
        }

        [DataSourceProperty]
        public string StatusText
        {
            get => _statusText;
            set { if (value != _statusText) { _statusText = value; OnPropertyChangedWithValue(value, nameof(StatusText)); } }
        }

        [DataSourceProperty]
        public string BackupWarningsLabel =>
            BackupSettings.Current.BackupWarningsEnabled ? "Backup warnings: ON" : "Backup warnings: OFF";

        [DataSourceProperty]
        public string BackupWarningsColor =>
            BackupSettings.Current.BackupWarningsEnabled ? "#4fb96aFF" : "#c9484eFF";

        [DataSourceProperty]
        public string SaveRemindersLabel =>
            BackupSettings.Current.SaveRemindersEnabled ? "Save reminders: ON" : "Save reminders: OFF";

        [DataSourceProperty]
        public string SaveRemindersColor =>
            BackupSettings.Current.SaveRemindersEnabled ? "#4fb96aFF" : "#c9484eFF";

        public void ExecuteToggleBackupWarnings()
        {
            var s = BackupSettings.Current;
            if (!s.BackupWarningsEnabled) { ApplyBackupWarnings(true); return; }

            Confirm(
                "Turn off backup warnings?",
                "You will stop seeing on-screen messages when a backup is running, when a backup does " +
                "NOT complete, and when a backup FAILS. Backups themselves keep running, and every one " +
                "of those messages still goes to tool.log - but nothing will interrupt you about a " +
                "failed backup until you come looking. Turn off?",
                () => ApplyBackupWarnings(false));
        }

        public void ExecuteToggleSaveReminders()
        {
            var s = BackupSettings.Current;
            if (!s.SaveRemindersEnabled) { ApplySaveReminders(true); return; }

            Confirm(
                "Turn off save reminders?",
                "You will stop seeing the 15-minute no-save warning and the reminder to save after an " +
                "apply. This matters because backups copy the SAVED file: while the scene goes unsaved, " +
                "the newest backup is just as out of date as the file is, and nothing will point that " +
                "out. Still logged to tool.log. Turn off?",
                () => ApplySaveReminders(false));
        }

        private void ApplyBackupWarnings(bool on)
        {
            var s = BackupSettings.Current;
            s.BackupWarningsEnabled = on;
            s.Save();
            OnPropertyChanged(nameof(BackupWarningsLabel));
            OnPropertyChanged(nameof(BackupWarningsColor));
            StatusText = on
                ? "Backup warnings on."
                : "Backup warnings OFF - failures will only appear in tool.log.";
            _onChanged?.Invoke();
        }

        private void ApplySaveReminders(bool on)
        {
            var s = BackupSettings.Current;
            s.SaveRemindersEnabled = on;
            s.Save();
            OnPropertyChanged(nameof(SaveRemindersLabel));
            OnPropertyChanged(nameof(SaveRemindersColor));
            StatusText = on
                ? "Save reminders on."
                : "Save reminders OFF - nothing will warn you about a long unsaved stretch.";
            _onChanged?.Invoke();
        }

        // Cancel is the default-looking choice, and the affirmative says what it does rather than
        // "OK", so a reflexive click on the wrong one is less likely to be the destructive one.
        private static void Confirm(string title, string body, Action onYes)
        {
            InformationManager.ShowInquiry(new InquiryData(
                title, body,
                isAffirmativeOptionShown: true,
                isNegativeOptionShown: true,
                affirmativeText: "Turn off",
                negativeText: "Keep them on",
                affirmativeAction: onYes,
                negativeAction: null));
        }

        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();
    }
}
