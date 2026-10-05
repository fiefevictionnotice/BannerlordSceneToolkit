using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    public class DiffPreviewVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;
        private string _headerText;
        private MBBindingList<DiffLineVM> _lines = new MBBindingList<DiffLineVM>();

        // Set only by the caller that can actually rebuild its list at a different length
        // (Preview). The Scene Analyzer's reports are complete as generated, so it passes null
        // and the whole limit row hides itself rather than offering a control that does nothing.
        private readonly Func<int, DiffPreviewContent> _regenerate;
        private string _limitInput;
        private bool _isLimitVisible;
        private string _limitStatus = "";
        private string _rowStatus = "";

        public const int LimitDefault = 200;
        public const int LimitMax = 5000;     // GauntletUI builds a widget per row, and rows now carry a
                                             // Go button as well as text - roughly four widgets each,
                                             // so the practical ceiling dropped from 20000.

        public DiffPreviewVM(Action closeAction, Action beginDragAction, string headerText)
            : this(closeAction, beginDragAction, headerText, 0, null) { }

        public DiffPreviewVM(Action closeAction, Action beginDragAction, string headerText,
                             int initialLimit, Func<int, DiffPreviewContent> regenerate)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            _headerText = headerText;
            _regenerate = regenerate;
            _isLimitVisible = regenerate != null;
            _limitInput = (initialLimit > 0 ? initialLimit : LimitDefault).ToString();
        }

        [DataSourceProperty]
        public string HeaderText
        {
            get => _headerText;
            set { if (value != _headerText) { _headerText = value; OnPropertyChangedWithValue(value, nameof(HeaderText)); } }
        }

        [DataSourceProperty]
        public MBBindingList<DiffLineVM> Lines
        {
            get => _lines;
            set { if (value != _lines) { _lines = value; OnPropertyChangedWithValue(value, nameof(Lines)); } }
        }

        [DataSourceProperty]
        public bool IsLimitVisible
        {
            get => _isLimitVisible;
            set { if (value != _isLimitVisible) { _isLimitVisible = value; OnPropertyChangedWithValue(value, nameof(IsLimitVisible)); } }
        }

        [DataSourceProperty]
        public string LimitInput
        {
            get => _limitInput;
            set { if (value != _limitInput) { _limitInput = value; OnPropertyChangedWithValue(value, nameof(LimitInput)); } }
        }

        [DataSourceProperty]
        public string LimitStatus
        {
            get => _limitStatus;
            set { if (value != _limitStatus) { _limitStatus = value; OnPropertyChangedWithValue(value, nameof(LimitStatus)); } }
        }

        // Where a row's Go button reports what it did. Separate from LimitStatus because that
        // one lives inside the limit row, which is hidden for callers with no regenerate
        // callback - and the Go buttons exist for those callers too.
        [DataSourceProperty]
        public string RowStatus
        {
            get => _rowStatus;
            set { if (value != _rowStatus) { _rowStatus = value; OnPropertyChangedWithValue(value, nameof(RowStatus)); } }
        }

        // Single place that builds rows, so every path gets the Go button wired up. Adding rows
        // directly to Lines would leave StatusSink null and the button silently mute.
        public void SetLines(IEnumerable<string> lines)
        {
            Lines.Clear();
            if (lines == null) return;
            foreach (var line in lines)
                Lines.Add(new DiffLineVM(line) { StatusSink = m => RowStatus = m });
        }

        // Rebuilds the list in place at the requested length. Deliberately does NOT re-run the
        // swap engine - the caller closes over the result it already computed, so showing more
        // rows costs nothing but the widgets.
        public void ExecuteApplyLimit()
        {
            if (_regenerate == null) return;

            var raw = (_limitInput ?? "").Trim();
            if (!int.TryParse(raw, out var n))
            {
                LimitStatus = $"'{raw}' is not a number.";
                return;
            }

            int clamped = n < 1 ? 1 : (n > LimitMax ? LimitMax : n);
            if (clamped != n)
            {
                LimitInput = clamped.ToString();
                LimitStatus = $"Clamped to {clamped} (allowed 1-{LimitMax}).";
            }
            else LimitStatus = "";

            try
            {
                var content = _regenerate(clamped);
                SetLines(content.Lines);
                HeaderText = content.Header;
                if (string.IsNullOrEmpty(LimitStatus))
                    LimitStatus = $"Showing up to {clamped}.";
            }
            catch (Exception ex)
            {
                LimitStatus = "Failed: " + ex.Message;
                Log.Warn("DiffPreview limit change failed: " + ex);
            }
        }

        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();
    }

    // Plain class rather than a tuple so the callback's two halves are named at every call site.
    public class DiffPreviewContent
    {
        public string Header;
        public List<string> Lines;

        public DiffPreviewContent(string header, List<string> lines)
        {
            Header = header;
            Lines = lines ?? new List<string>();
        }
    }
}
