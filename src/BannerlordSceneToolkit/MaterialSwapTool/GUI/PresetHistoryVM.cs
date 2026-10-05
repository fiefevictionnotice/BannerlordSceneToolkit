using System;
using MaterialSwapTool.Core;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    // Per-preset version history, opened from a personal preset's "History" button in Browse
    // Presets - same shape as Batch History (a list of timestamped snapshots with per-row
    // actions), scoped to just the one preset instead of the whole session's batches.
    public class PresetHistoryVM : ViewModel
    {
        private readonly string _presetName;
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;
        private readonly Action<string, PresetLoadMode, bool> _onLoadIntoCurrent;
        private MBBindingList<PresetHistoryItemVM> _versions = new MBBindingList<PresetHistoryItemVM>();
        private string _titleText;
        private string _statusText = "";
        private bool _alsoLoadIntoCurrent;
        private string _alsoLoadLabel = "Restore: saved preset only";

        public PresetHistoryVM(string presetName, Action closeAction, Action beginDragAction,
            Action<string, PresetLoadMode, bool> onLoadIntoCurrent)
        {
            _presetName = presetName;
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            _onLoadIntoCurrent = onLoadIntoCurrent;
            _titleText = $"History: {presetName}";
            Refresh();
        }

        [DataSourceProperty]
        public string TitleText
        {
            get => _titleText;
            set { if (value != _titleText) { _titleText = value; OnPropertyChangedWithValue(value, nameof(TitleText)); } }
        }

        [DataSourceProperty]
        public MBBindingList<PresetHistoryItemVM> Versions
        {
            get => _versions;
            set { if (value != _versions) { _versions = value; OnPropertyChangedWithValue(value, nameof(Versions)); } }
        }

        [DataSourceProperty]
        public string StatusText
        {
            get => _statusText;
            set { if (value != _statusText) { _statusText = value; OnPropertyChangedWithValue(value, nameof(StatusText)); } }
        }

        // Restore always overwrites the saved preset file - this only controls whether it ALSO
        // pushes into your currently-loaded rules. Off by default: restoring an old version of a
        // preset you're not even working with right now shouldn't silently blow away whatever
        // rules are currently on screen.
        [DataSourceProperty]
        public string AlsoLoadLabel
        {
            get => _alsoLoadLabel;
            set { if (value != _alsoLoadLabel) { _alsoLoadLabel = value; OnPropertyChangedWithValue(value, nameof(AlsoLoadLabel)); } }
        }

        public void ExecuteToggleAlsoLoad()
        {
            _alsoLoadIntoCurrent = !_alsoLoadIntoCurrent;
            AlsoLoadLabel = _alsoLoadIntoCurrent ? "Restore: saved preset AND current rules" : "Restore: saved preset only";
        }

        public void ExecuteRefresh() => Refresh();
        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();

        private void Refresh()
        {
            try
            {
                Versions.Clear();
                foreach (var info in PresetHistoryManager.ListVersions(_presetName))
                    Versions.Add(new PresetHistoryItemVM(info, OnRestore));
                StatusText = Versions.Count == 0
                    ? "No earlier versions yet - only saved when you overwrite this preset."
                    : $"{Versions.Count} earlier version(s), most recent first.";
            }
            catch (Exception ex)
            {
                StatusText = "Failed to list preset history: " + ex.Message;
                Log.Error("PresetHistory refresh failed: " + ex);
            }
        }

        private void OnRestore(PresetHistoryItemVM item)
        {
            try
            {
                PresetHistoryManager.RestoreVersion(_presetName, item.FilePath);

                if (_alsoLoadIntoCurrent && _onLoadIntoCurrent != null)
                {
                    _onLoadIntoCurrent(_presetName, PresetLoadMode.Overwrite, false);
                    StatusText = $"Restored the version from {item.DateText} - saved preset AND your current rules were updated.";
                }
                else
                {
                    StatusText = $"Restored the version from {item.DateText} to the SAVED preset only - " +
                                 "your currently loaded rules are unchanged. Reopen Browse Presets to load it, " +
                                 "or turn on the toggle above to update both at once next time.";
                }
                Refresh();
            }
            catch (Exception ex)
            {
                StatusText = "Restore failed: " + ex.Message;
                Log.Error("Preset history restore failed: " + ex);
            }
        }
    }
}
