using System;
using System.Diagnostics;
using PrefabSwapperTool.Core;
using TaleWorlds.Library;

namespace PrefabSwapperTool.GUI
{
    // One row in the Undo/Redo History flyout (PrefabHistoryLayer) - same shape as this mod's other
    // history flyout (BatchHistoryItemVM for material-swap changes): both Undo and Redo buttons are
    // always shown, and the executor methods themselves (PrefabSwapHistory.Undo/Redo) reject the
    // wrong direction with a clear per-row message rather than the row hiding/showing buttons.
    public class PrefabSwapHistoryItemVM : ViewModel
    {
        private readonly Action<PrefabSwapHistoryItemVM> _onUndo;
        private readonly Action<PrefabSwapHistoryItemVM> _onRedo;
        private readonly string _screenshotPath;
        private string _displayText;
        private string _rowStatusText = "";

        public string BatchId { get; }

        public PrefabSwapHistoryItemVM(PrefabSwapBatchSummary summary, Action<PrefabSwapHistoryItemVM> onUndo, Action<PrefabSwapHistoryItemVM> onRedo)
        {
            BatchId = summary.BatchId;
            _onUndo = onUndo;
            _onRedo = onRedo;
            var localTime = summary.TimestampUtc.ToLocalTime().ToString("HH:mm:ss");
            var undoneNote = summary.AllUndone ? " [undone]" : "";
            _displayText = $"{localTime}  {summary.Label}{undoneNote}";

            // No in-panel thumbnail - Texture.CreateTextureFromPath only understands the engine's
            // own native texture format, not a plain PNG, and there's no PNG decoder available here
            // (same dead end already hit building MaterialSwapTool's own Batch History). Opening the
            // file with the OS's own image viewer sidesteps that entirely.
            _screenshotPath = ScreenshotManager.GetPathForBatch(BatchId);
        }

        [DataSourceProperty]
        public string DisplayText
        {
            get => _displayText;
            set { if (value != _displayText) { _displayText = value; OnPropertyChangedWithValue(value, nameof(DisplayText)); } }
        }

        [DataSourceProperty]
        public string RowStatusText
        {
            get => _rowStatusText;
            set { if (value != _rowStatusText) { _rowStatusText = value; OnPropertyChangedWithValue(value, nameof(RowStatusText)); } }
        }

        public void ExecuteUndo() => _onUndo?.Invoke(this);
        public void ExecuteRedo() => _onRedo?.Invoke(this);

        // Only batches from RunSwap/RunSetSwap capture a screenshot - a batch from before this
        // existed just has none, and this quietly no-ops rather than erroring.
        public void ExecuteViewScreenshot()
        {
            if (string.IsNullOrEmpty(_screenshotPath))
            {
                RowStatusText = "No screenshot was captured for this batch.";
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(_screenshotPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                RowStatusText = "Couldn't open screenshot: " + ex.Message;
                Log.Warn("ExecuteViewScreenshot failed: " + ex);
            }
        }
    }
}
