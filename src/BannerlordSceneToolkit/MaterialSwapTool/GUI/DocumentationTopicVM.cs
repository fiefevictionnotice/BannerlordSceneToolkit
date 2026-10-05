using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    // One accordion entry: a title row that toggles its own body open/closed in place. Replaced
    // the old "3 buttons + one shared body pane below" layout, which didn't scale past a handful of
    // topics and looked like it was flying out to somewhere else when really it wasn't - this is a
    // single scrollable vertical list, each item expanding inline.
    public class DocumentationTopicVM : ViewModel
    {
        private string _title;
        private string _body;
        private bool _isExpanded;

        public DocumentationTopicVM(string title, string body)
        {
            _title = title;
            _body = body;
        }

        [DataSourceProperty]
        public string Title
        {
            get => _title;
            set { if (value != _title) { _title = value; OnPropertyChangedWithValue(value, nameof(Title)); } }
        }

        [DataSourceProperty]
        public string Body
        {
            get => _body;
            set { if (value != _body) { _body = value; OnPropertyChangedWithValue(value, nameof(Body)); } }
        }

        [DataSourceProperty]
        public bool IsExpanded
        {
            get => _isExpanded;
            set { if (value != _isExpanded) { _isExpanded = value; OnPropertyChangedWithValue(value, nameof(IsExpanded)); } }
        }

        public void ExecuteToggle() => IsExpanded = !IsExpanded;
    }
}
