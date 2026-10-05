using System;
using System.Diagnostics;
using MaterialSwapTool.Core;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    public class BatchHistoryItemVM : ViewModel
    {
        private readonly Action<BatchHistoryItemVM> _onUndo;
        private readonly Action<BatchHistoryItemVM> _onRedo;
        private readonly string _screenshotPath;
        private string _summaryText;
        private string _rowStatusText = "";

        public string BatchId { get; }

        public BatchHistoryItemVM(BatchSummary summary, Action<BatchHistoryItemVM> onUndo, Action<BatchHistoryItemVM> onRedo)
        {
            BatchId = summary.BatchId;
            _onUndo = onUndo;
            _onRedo = onRedo;

            var when = summary.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            _summaryText = $"{when}   {summary.SceneName}   {summary.EntryCount} change(s)   [{summary.BatchId}]";

            // Embedding the screenshot as an in-panel thumbnail turned out to be a dead end -
            // Texture.CreateTextureFromPath only understands the engine's own native texture
            // format (confirmed via the native "Unknown texture format" error string in
            // rglTexture.cpp), not a plain PNG, and there's no PNG decoder available here to
            // convert it without pulling in a new dependency. Opening the file externally with
            // the OS's own image viewer sidesteps the problem entirely.
            _screenshotPath = ScreenshotManager.GetPathForBatch(BatchId);
        }

        [DataSourceProperty]
        public string SummaryText
        {
            get => _summaryText;
            set { if (value != _summaryText) { _summaryText = value; OnPropertyChangedWithValue(value, nameof(SummaryText)); } }
        }

        [DataSourceProperty]
        public string RowStatusText
        {
            get => _rowStatusText;
            set { if (value != _rowStatusText) { _rowStatusText = value; OnPropertyChangedWithValue(value, nameof(RowStatusText)); } }
        }

        public void ExecuteUndo() => _onUndo?.Invoke(this);
        public void ExecuteRedo() => _onRedo?.Invoke(this);

        // Only Apply and Revert to Normal capture a screenshot (see ScreenshotManager) - a batch
        // from before this existed, or from Continuous Recolor (deliberately excluded, see that
        // class), just has none, so this quietly no-ops rather than erroring.
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
