using System;
using System.Globalization;
using System.Text.RegularExpressions;
using MaterialSwapTool.Core;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    public class DiffLineVM : ViewModel
    {
        private string _text;
        private bool _hasPosition;
        private float _x, _y, _z;

        // Matches the two coordinate formats these reports actually print:
        //   Preview:            name   (123.45, 67.89, 0.00)   2 slots: ...
        //   OOB findings:       [ERROR]   OOB: 'name' at [123.45, 67.89, 0.00]
        // Deliberately strict - two decimals and a bracketed triple - so ordinary prose in a
        // finding message can't be mistaken for a position and grow a Go button that lies.
        private static readonly Regex PositionPattern = new Regex(
            @"[\(\[]\s*(-?\d+\.\d{2})\s*,\s*(-?\d+\.\d{2})\s*,\s*(-?\d+\.\d{2})\s*[\)\]]",
            RegexOptions.Compiled);

        // Set by the layer so a row can report back what its Go press did.
        public Action<string> StatusSink;

        public DiffLineVM(string text)
        {
            _text = text;
            TryParsePosition(text);
        }

        // For callers that already hold the exact position and shouldn't have to round-trip it
        // through text formatting to get a button.
        public DiffLineVM(string text, float x, float y, float z)
        {
            _text = text;
            _x = x; _y = y; _z = z;
            _hasPosition = true;
        }

        private void TryParsePosition(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var m = PositionPattern.Match(text);
            if (!m.Success) return;
            if (float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
                float.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            {
                _x = x; _y = y; _z = z;
                _hasPosition = true;
            }
        }

        [DataSourceProperty]
        public string Text
        {
            get => _text;
            set { if (value != _text) { _text = value; OnPropertyChangedWithValue(value, nameof(Text)); } }
        }

        // Drives the Go button's visibility - summary and header lines carry no coordinates and
        // get no button.
        [DataSourceProperty]
        public bool HasPosition
        {
            get => _hasPosition;
            set { if (value != _hasPosition) { _hasPosition = value; OnPropertyChangedWithValue(value, nameof(HasPosition)); } }
        }

        // Same thing the editor's own scene list does on a double-click: move the camera there
        // and select what's there.
        public void ExecuteGo()
        {
            if (!_hasPosition) return;
            var message = EditorNavigation.GoTo(_x, _y, _z);
            StatusSink?.Invoke(message);
        }
    }
}
