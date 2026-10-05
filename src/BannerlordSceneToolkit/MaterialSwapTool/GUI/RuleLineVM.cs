using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    // Read-only display line for one rule inside an expanded preset card - no editing, no
    // click handler, just text, so no removeAction/onChosen plumbing needed like the other VMs.
    public class RuleLineVM : ViewModel
    {
        private string _text;

        public RuleLineVM(string text) => _text = text;

        [DataSourceProperty]
        public string Text
        {
            get => _text;
            set { if (value != _text) { _text = value; OnPropertyChangedWithValue(value, nameof(Text)); } }
        }
    }
}
