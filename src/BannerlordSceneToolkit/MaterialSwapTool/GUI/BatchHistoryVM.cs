using System;
using MaterialSwapTool.Core;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    public class BatchHistoryVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;
        private MBBindingList<BatchHistoryItemVM> _batches = new MBBindingList<BatchHistoryItemVM>();
        private string _statusText = "";
        private string _scopeButtonText = "";
        // Off by default: the change log is one shared file, so an unscoped list shows batches
        // (and screenshots) belonging to scenes that aren't even open - which reads as the panel
        // being broken. Showing every scene is still available, just as a deliberate choice.
        private bool _showAllScenes;

        public BatchHistoryVM(Action closeAction, Action beginDragAction)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            Refresh();
        }

        [DataSourceProperty]
        public MBBindingList<BatchHistoryItemVM> Batches
        {
            get => _batches;
            set { if (value != _batches) { _batches = value; OnPropertyChangedWithValue(value, nameof(Batches)); } }
        }

        [DataSourceProperty]
        public string StatusText
        {
            get => _statusText;
            set { if (value != _statusText) { _statusText = value; OnPropertyChangedWithValue(value, nameof(StatusText)); } }
        }

        [DataSourceProperty]
        public string ScopeButtonText
        {
            get => _scopeButtonText;
            set { if (value != _scopeButtonText) { _scopeButtonText = value; OnPropertyChangedWithValue(value, nameof(ScopeButtonText)); } }
        }

        public void ExecuteRefresh() => Refresh();
        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();

        public void ExecuteToggleScope()
        {
            _showAllScenes = !_showAllScenes;
            Refresh();
        }

        private void Refresh()
        {
            try
            {
                Batches.Clear();
                var currentScene = EntitySelector.CurrentSceneName;
                var scopeFilter = _showAllScenes ? null : currentScene;

                foreach (var summary in ChangeLogger.ListRecentBatches(ChangeLogger.MaxHistoryDepth, scopeFilter))
                    Batches.Add(new BatchHistoryItemVM(summary, OnUndo, OnRedo));

                // Button says what clicking it will DO, not what's on screen now.
                ScopeButtonText = _showAllScenes ? "This Scene Only" : "All Scenes";

                if (Batches.Count == 0)
                {
                    StatusText = _showAllScenes
                        ? "No batches logged yet."
                        : $"No batches logged for '{currentScene}' yet - press All Scenes to see other scenes' history.";
                }
                else
                {
                    var scopeLabel = _showAllScenes ? "ALL scenes" : $"'{currentScene}'";
                    StatusText = $"{Batches.Count} batch(es) for {scopeLabel} (most recent first).";
                }
            }
            catch (Exception ex)
            {
                StatusText = "Failed to list batch history: " + ex.Message;
                Log.Error("BatchHistory refresh failed: " + ex);
            }
        }

        // Undo/Redo on an out-of-order (not-most-recent) batch is already safe: RevertOne/RedoOne
        // check the live material against what the log entry expects before touching anything, so
        // an entry a later batch already overwrote gets skipped with a warning instead of
        // corrupting state - see RevertManager. That's what makes "undo something from the middle
        // of history without disturbing later batches" work at all.
        private void OnUndo(BatchHistoryItemVM item)
        {
            try
            {
                var outcome = RevertManager.RevertBatch(item.BatchId, applyAutoReverts: true);
                item.RowStatusText = $"Undo: {outcome.AutoReverted.Count} reverted, {outcome.NeedsConfirmation.Count} need confirmation (see log), {outcome.NotFound.Count} not found.";
            }
            catch (Exception ex)
            {
                item.RowStatusText = "Undo failed: " + ex.Message;
                Log.Error("Batch history undo failed: " + ex);
            }
        }

        private void OnRedo(BatchHistoryItemVM item)
        {
            try
            {
                var outcome = RevertManager.RedoBatch(item.BatchId, applyAutoReverts: true);
                item.RowStatusText = $"Redo: {outcome.AutoReverted.Count} reapplied, {outcome.NeedsConfirmation.Count} need confirmation (see log), {outcome.NotFound.Count} not found.";
            }
            catch (Exception ex)
            {
                item.RowStatusText = "Redo failed: " + ex.Message;
                Log.Error("Batch history redo failed: " + ex);
            }
        }
    }
}
