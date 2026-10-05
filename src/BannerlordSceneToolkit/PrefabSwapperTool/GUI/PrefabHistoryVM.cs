using System;
using PrefabSwapperTool.Core;
using TaleWorlds.Library;

namespace PrefabSwapperTool.GUI
{
    // Prefab Swapper's full Undo/Redo history, broken out into its own flyout (same move as
    // BatchHistoryLayer for material-swap changes) so the main Prefab Swapper panel only needs a
    // single "Undo Last Swap" button instead of an always-visible scrollable list eating vertical
    // space it doesn't usually need.
    public class PrefabHistoryVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;
        private MBBindingList<PrefabSwapHistoryItemVM> _batches = new MBBindingList<PrefabSwapHistoryItemVM>();
        private string _statusText = "";

        public PrefabHistoryVM(Action closeAction, Action beginDragAction)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            Refresh();
        }

        [DataSourceProperty]
        public MBBindingList<PrefabSwapHistoryItemVM> Batches
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

        public void ExecuteRefresh() => Refresh();
        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();

        private void Refresh()
        {
            try
            {
                Batches.Clear();
                foreach (var summary in PrefabSwapLogger.ListRecentBatches(PrefabSwapLogger.MaxHistoryDepth, EntitySelector.CurrentSceneName))
                    Batches.Add(new PrefabSwapHistoryItemVM(summary, OnUndo, OnRedo));
                StatusText = Batches.Count == 0
                    ? $"No prefab swaps logged for '{EntitySelector.CurrentSceneName}' yet."
                    : $"{Batches.Count} batch(es) for '{EntitySelector.CurrentSceneName}' (most recent first).";
            }
            catch (Exception ex)
            {
                StatusText = "Failed to list swap history: " + ex.Message;
                Log.Error("PrefabHistory refresh failed: " + ex);
            }
        }

        // Deliberately doesn't Refresh() after - same as BatchHistoryVM's equivalent methods, this
        // just updates the one row's own status text in place. Click Refresh (or reopen) to pick
        // up the "[undone]" label change on the row itself.
        private void OnUndo(PrefabSwapHistoryItemVM item)
        {
            if (!EntitySelector.HasOpenScene) { item.RowStatusText = "No scene is currently open."; return; }
            var result = PrefabSwapHistory.Undo(EntitySelector.CurrentScene, item.BatchId);
            item.RowStatusText = result.Message;
        }

        private void OnRedo(PrefabSwapHistoryItemVM item)
        {
            if (!EntitySelector.HasOpenScene) { item.RowStatusText = "No scene is currently open."; return; }
            var result = PrefabSwapHistory.Redo(EntitySelector.CurrentScene, item.BatchId);
            item.RowStatusText = result.Message;
        }
    }
}
