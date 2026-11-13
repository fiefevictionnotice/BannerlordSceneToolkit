using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MaterialSwapTool.Core;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    // One selectable scene.xscene in the Scene Diff list (the saved file or a backup).
    public class SceneDiffSnapshotVM : ViewModel
    {
        private readonly Action<SceneDiffSnapshotVM> _onFrom;
        private readonly Action<SceneDiffSnapshotVM> _onTo;
        private string _label;
        private string _roleText = "";
        private string _rowColor = NormalColor;

        public readonly SceneSnapshots.Snapshot Snapshot;

        public const string NormalColor = "#00000055";
        public const string FromColor = "#8a5fc988";
        public const string ToColor = "#4fb96a88";
        public const string BothColor = "#c9782f88";

        public SceneDiffSnapshotVM(SceneSnapshots.Snapshot snapshot, Action<SceneDiffSnapshotVM> onFrom, Action<SceneDiffSnapshotVM> onTo)
        {
            Snapshot = snapshot;
            _label = snapshot.Label;
            _onFrom = onFrom;
            _onTo = onTo;
        }

        [DataSourceProperty]
        public string Label
        {
            get => _label;
            set { if (value != _label) { _label = value; OnPropertyChangedWithValue(value, nameof(Label)); } }
        }

        // "FROM", "TO", "FROM+TO" or empty - the row's part in the next compare.
        [DataSourceProperty]
        public string RoleText
        {
            get => _roleText;
            set { if (value != _roleText) { _roleText = value; OnPropertyChangedWithValue(value, nameof(RoleText)); } }
        }

        [DataSourceProperty]
        public string RowColor
        {
            get => _rowColor;
            set { if (value != _rowColor) { _rowColor = value; OnPropertyChangedWithValue(value, nameof(RowColor)); } }
        }

        public void ExecutePickFrom() => _onFrom?.Invoke(this);
        public void ExecutePickTo() => _onTo?.Invoke(this);
    }

    // F7 -> Scene Diff. Pick two saved states of the open scene, compare, read the result in
    // the shared results window (Go buttons included), optionally write it to a text file.
    //
    // THREADING. Both files are parsed and compared on a Task - they are 6-8 MB each and the
    // editor must not hitch while that runs. The task touches only the two file paths and
    // SceneFileDiff (pure managed code, no engine objects). Its outcome is parked in _finished
    // and picked up by Poll(), which the layer calls from the editor tick: opening a Gauntlet
    // layer is main-thread work. Poll is a single reference read when nothing is pending.
    public class SceneDiffVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;

        private MBBindingList<SceneDiffSnapshotVM> _snapshots = new MBBindingList<SceneDiffSnapshotVM>();
        private string _statusText = "";
        private string _fromText = "";
        private string _toText = "";
        private string _compareButtonText = "Compare";

        private SceneDiffSnapshotVM _from, _to;
        private string _sceneName;

        private sealed class Outcome
        {
            public int RunId;
            public SceneFileDiff.Result Result;
            public string Error;
        }

        private volatile Outcome _finished;
        private int _runId;
        private bool _busy;
        private bool _reversed;
        private SceneFileDiff.Result _lastResult;

        public const int InitialShown = 200;

        public SceneDiffVM(Action closeAction, Action beginDragAction)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            Reload(keepStatus: false);
        }

        [DataSourceProperty]
        public MBBindingList<SceneDiffSnapshotVM> Snapshots
        {
            get => _snapshots;
            set { if (value != _snapshots) { _snapshots = value; OnPropertyChangedWithValue(value, nameof(Snapshots)); } }
        }

        [DataSourceProperty]
        public string StatusText
        {
            get => _statusText;
            set { if (value != _statusText) { _statusText = value; OnPropertyChangedWithValue(value, nameof(StatusText)); } }
        }

        [DataSourceProperty]
        public string FromText
        {
            get => _fromText;
            set { if (value != _fromText) { _fromText = value; OnPropertyChangedWithValue(value, nameof(FromText)); } }
        }

        [DataSourceProperty]
        public string ToText
        {
            get => _toText;
            set { if (value != _toText) { _toText = value; OnPropertyChangedWithValue(value, nameof(ToText)); } }
        }

        [DataSourceProperty]
        public string CompareButtonText
        {
            get => _compareButtonText;
            set { if (value != _compareButtonText) { _compareButtonText = value; OnPropertyChangedWithValue(value, nameof(CompareButtonText)); } }
        }

        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();
        public void ExecuteRefresh() => Reload(keepStatus: false);

        // Rebuilds the list from disk and re-applies the default pair:
        //   TO   = the saved file
        //   FROM = the newest backup that is NOT a copy of that same save
        // The newest backup very often IS a copy of the current save (that is what a backup
        // is), and comparing those two can only ever say "no differences" - so the default
        // skips past them to the newest backup that can actually differ.
        private void Reload(bool keepStatus)
        {
            try
            {
                Snapshots.Clear();
                _from = _to = null;

                var list = SceneSnapshots.List(out _sceneName, out var problem, out var older);
                foreach (var s in list)
                    Snapshots.Add(new SceneDiffSnapshotVM(s, OnPickFrom, OnPickTo));

                if (list.Count == 0)
                {
                    UpdateRoles();
                    StatusText = problem ?? "Nothing to compare.";
                    return;
                }

                _to = Snapshots.FirstOrDefault(s => s.Snapshot.IsCurrent) ?? Snapshots[0];
                _from = Snapshots.FirstOrDefault(s => !s.Snapshot.IsCurrent && !s.Snapshot.SameAsCurrent && s != _to)
                        ?? Snapshots.FirstOrDefault(s => s != _to);
                UpdateRoles();

                if (keepStatus) return;
                int backups = list.Count(s => !s.IsCurrent);
                var current = list.FirstOrDefault(s => s.IsCurrent);
                var parts = new List<string>
                {
                    backups == 0
                        ? $"No backups of '{_sceneName}' yet - nothing to compare the saved file with."
                        : $"{backups} backup(s) of '{_sceneName}'" + (older > 0 ? $" ({older} older not listed)" : "") + ".",
                };
                if (current == null) parts.Add("No saved scene.xscene found - backups can still be compared with each other.");
                else
                {
                    var age = DateTime.Now - current.SavedAt;
                    parts.Add("The diff reads the SAVED file (last saved " + Ago(age) + " ago), not unsaved editor changes - save first to include them.");
                }
                StatusText = string.Join(" ", parts);
            }
            catch (Exception ex)
            {
                StatusText = "Listing snapshots failed: " + ex.Message;
                Log.Error("SceneDiff reload failed: " + ex);
            }
        }

        private static string Ago(TimeSpan t)
        {
            if (t.TotalSeconds < 90) return Math.Max(0, (int)t.TotalSeconds) + " s";
            if (t.TotalMinutes < 90) return (int)t.TotalMinutes + " min";
            if (t.TotalHours < 48) return t.TotalHours.ToString("0.#", CultureInfo.InvariantCulture) + " h";
            return (int)t.TotalDays + " days";
        }

        private void OnPickFrom(SceneDiffSnapshotVM row) { _from = row; UpdateRoles(); }
        private void OnPickTo(SceneDiffSnapshotVM row) { _to = row; UpdateRoles(); }

        private void UpdateRoles()
        {
            foreach (var s in Snapshots)
            {
                bool isFrom = s == _from, isTo = s == _to;
                s.RoleText = isFrom && isTo ? "FROM+TO" : isFrom ? "FROM" : isTo ? "TO" : "";
                s.RowColor = isFrom && isTo ? SceneDiffSnapshotVM.BothColor
                           : isFrom ? SceneDiffSnapshotVM.FromColor
                           : isTo ? SceneDiffSnapshotVM.ToColor
                           : SceneDiffSnapshotVM.NormalColor;
            }
            FromText = "FROM (older):  " + (_from?.Snapshot.ShortLabel ?? "- pick one -");
            ToText = "TO (newer):  " + (_to?.Snapshot.ShortLabel ?? "- pick one -");
        }

        public void ExecuteCompare()
        {
            if (_busy) { StatusText = "A comparison is already running."; return; }
            if (_from == null || _to == null) { StatusText = "Pick a FROM and a TO snapshot first."; return; }
            if (_from == _to) { StatusText = "FROM and TO are the same snapshot - pick two different ones."; return; }

            var from = _from.Snapshot;
            var to = _to.Snapshot;
            int runId = ++_runId;
            // Nothing stops FROM being the later save (backup-vs-backup picks are free-form);
            // the result is still correct, it just reads in reverse, and the status says so.
            _reversed = from.SavedAt > to.SavedAt;
            _busy = true;
            CompareButtonText = "Comparing...";
            StatusText = $"Comparing {from.ShortLabel}  ->  {to.ShortLabel} in the background...";

            Task.Run(() =>
            {
                var outcome = new Outcome { RunId = runId };
                try
                {
                    var a = SceneFileDiff.Parse(from.Path);
                    var b = SceneFileDiff.Parse(to.Path);
                    var result = SceneFileDiff.Compare(a, b);
                    result.FromLabel = from.ShortLabel + "   " + from.Path;
                    result.ToLabel = to.ShortLabel + "   " + to.Path;
                    outcome.Result = result;
                }
                catch (System.Xml.XmlException ex)
                {
                    outcome.Error = "A scene file could not be parsed (" + ex.Message + "). If the editor was saving just now, try again.";
                }
                catch (Exception ex)
                {
                    outcome.Error = ex.Message;
                }
                _finished = outcome;
            });
        }

        // Main thread, once per tick while the panel is open. Cheap when idle.
        public void Poll()
        {
            var outcome = _finished;
            if (outcome == null) return;
            _finished = null;
            if (outcome.RunId != _runId) return;   // superseded

            _busy = false;
            CompareButtonText = "Compare";

            if (outcome.Error != null)
            {
                StatusText = "Compare failed: " + outcome.Error;
                Log.Warn("[SceneDiff] compare failed: " + outcome.Error);
                return;
            }

            var result = outcome.Result;
            _lastResult = result;
            Log.Info($"[SceneDiff] {result.Summary} | from={result.FromLabel} | to={result.ToLabel} | {result.ElapsedMs}ms compare.");

            try
            {
                // The regenerate callback closes over the finished result, so "Show first N"
                // in the results window re-slices it without re-reading either file.
                DiffPreviewLayer.Open("Scene Diff - " + result.Summary,
                    SceneFileDiff.BuildLines(result, InitialShown),
                    InitialShown,
                    limit => new DiffPreviewContent("Scene Diff - " + result.Summary, SceneFileDiff.BuildLines(result, limit)));
            }
            catch (Exception ex)
            {
                Log.Error("SceneDiff results window failed: " + ex);
            }

            StatusText = "Done: " + result.Summary + "  Saved files only - unsaved editor changes are not in this diff."
                         + (_reversed ? "  NOTE: FROM is the newer save, so this reads backwards (ADDED = gone since)." : "")
                         + (result.HasChanges ? "  Write Report saves the full list." : "");
        }

        public void ExecuteWriteReport()
        {
            try
            {
                if (_lastResult == null) { StatusText = "Nothing to write yet - run Compare first."; return; }
                var lines = new List<string>
                {
                    "Scene Diff - " + (_sceneName ?? "scene") + " - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                };
                lines.AddRange(SceneFileDiff.BuildLines(_lastResult, 0));
                var path = SceneSnapshots.WriteReport("scene-diff", _sceneName, lines);
                StatusText = "Report written: " + path;
            }
            catch (Exception ex)
            {
                StatusText = "Writing the report failed: " + ex.Message;
                Log.Error("SceneDiff report failed: " + ex);
            }
        }
    }
}
