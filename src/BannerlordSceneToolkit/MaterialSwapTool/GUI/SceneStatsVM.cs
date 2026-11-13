using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MaterialSwapTool.Core;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    public enum SceneStatsTab { Prefabs, Meshes, Lights }

    // One row of any of the three tables. Holds text and a position only - see SceneStatistics
    // for why no GameEntity is kept.
    public class SceneStatRowVM : ViewModel
    {
        private readonly Action<SceneStatRowVM> _onSelect;
        private readonly Action<SceneStatRowVM> _onGo;
        private string _text;
        private string _rowColor;
        private bool _hasSelect, _hasGo;

        public string Key;              // prefab name or mesh key, for Select
        public float X, Y, Z;

        public const string NormalColor = "#00000055";
        public const string WarningColor = "#8a3a1a99";

        public SceneStatRowVM(string text, string key, bool hasSelect, bool hasGo, bool warning,
                              Action<SceneStatRowVM> onSelect, Action<SceneStatRowVM> onGo)
        {
            _text = text;
            Key = key;
            _hasSelect = hasSelect;
            _hasGo = hasGo;
            _rowColor = warning ? WarningColor : NormalColor;
            _onSelect = onSelect;
            _onGo = onGo;
        }

        [DataSourceProperty]
        public string Text
        {
            get => _text;
            set { if (value != _text) { _text = value; OnPropertyChangedWithValue(value, nameof(Text)); } }
        }

        [DataSourceProperty]
        public string RowColor
        {
            get => _rowColor;
            set { if (value != _rowColor) { _rowColor = value; OnPropertyChangedWithValue(value, nameof(RowColor)); } }
        }

        [DataSourceProperty]
        public bool HasSelect
        {
            get => _hasSelect;
            set { if (value != _hasSelect) { _hasSelect = value; OnPropertyChangedWithValue(value, nameof(HasSelect)); } }
        }

        [DataSourceProperty]
        public bool HasGo
        {
            get => _hasGo;
            set { if (value != _hasGo) { _hasGo = value; OnPropertyChangedWithValue(value, nameof(HasGo)); } }
        }

        public void ExecuteSelect() => _onSelect?.Invoke(this);
        public void ExecuteGo() => _onGo?.Invoke(this);
    }

    // F7 -> Statistics / Lights. One fly-out, three tables (most-used prefabs, heaviest
    // meshes, lights) over one shared row list, so F7 gains entry buttons and not panels.
    //
    // Everything here runs on a button press. Opening the panel or pressing Refresh scans the
    // scene once; switching tabs re-uses that scan (the Heaviest tab adds its own heavier pass
    // the first time it is opened). Nothing is recomputed on tick.
    public class SceneStatsVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;

        private SceneStatistics.Snapshot _stats;
        private SceneStatistics.MeshSnapshot _meshes;
        private SceneStatsTab _tab;
        private bool _lightsByRadius;
        private int _limit = PageSize;

        // Gauntlet builds real widgets for every row (no virtualized lists), and a row here is
        // a background, two buttons and a text - so the tables page instead of listing a few
        // hundred prefabs at once.
        public const int PageSize = 60;
        public const int LimitMax = 600;

        private const string TabOnColor = "#4fb96aFF";
        private const string TabOffColor = "#3ba1c9FF";

        private MBBindingList<SceneStatRowVM> _rows = new MBBindingList<SceneStatRowVM>();
        private string _totalsText = "";
        private string _headerText = "";
        private string _statusText = "";
        private string _sortButtonText = "";
        private string _showMoreText = "";
        private bool _isLightsTab, _isShowMoreVisible;
        private string _prefabsTabColor = TabOnColor, _meshesTabColor = TabOffColor, _lightsTabColor = TabOffColor;

        public SceneStatsVM(Action closeAction, Action beginDragAction, SceneStatsTab initialTab)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            _tab = initialTab;
            Rescan();
        }

        [DataSourceProperty]
        public MBBindingList<SceneStatRowVM> Rows
        {
            get => _rows;
            set { if (value != _rows) { _rows = value; OnPropertyChangedWithValue(value, nameof(Rows)); } }
        }

        [DataSourceProperty]
        public string TotalsText
        {
            get => _totalsText;
            set { if (value != _totalsText) { _totalsText = value; OnPropertyChangedWithValue(value, nameof(TotalsText)); } }
        }

        [DataSourceProperty]
        public string HeaderText
        {
            get => _headerText;
            set { if (value != _headerText) { _headerText = value; OnPropertyChangedWithValue(value, nameof(HeaderText)); } }
        }

        [DataSourceProperty]
        public string StatusText
        {
            get => _statusText;
            set { if (value != _statusText) { _statusText = value; OnPropertyChangedWithValue(value, nameof(StatusText)); } }
        }

        [DataSourceProperty]
        public string SortButtonText
        {
            get => _sortButtonText;
            set { if (value != _sortButtonText) { _sortButtonText = value; OnPropertyChangedWithValue(value, nameof(SortButtonText)); } }
        }

        [DataSourceProperty]
        public string ShowMoreText
        {
            get => _showMoreText;
            set { if (value != _showMoreText) { _showMoreText = value; OnPropertyChangedWithValue(value, nameof(ShowMoreText)); } }
        }

        [DataSourceProperty]
        public bool IsLightsTab
        {
            get => _isLightsTab;
            set { if (value != _isLightsTab) { _isLightsTab = value; OnPropertyChangedWithValue(value, nameof(IsLightsTab)); } }
        }

        [DataSourceProperty]
        public bool IsShowMoreVisible
        {
            get => _isShowMoreVisible;
            set { if (value != _isShowMoreVisible) { _isShowMoreVisible = value; OnPropertyChangedWithValue(value, nameof(IsShowMoreVisible)); } }
        }

        [DataSourceProperty]
        public string PrefabsTabColor
        {
            get => _prefabsTabColor;
            set { if (value != _prefabsTabColor) { _prefabsTabColor = value; OnPropertyChangedWithValue(value, nameof(PrefabsTabColor)); } }
        }

        [DataSourceProperty]
        public string MeshesTabColor
        {
            get => _meshesTabColor;
            set { if (value != _meshesTabColor) { _meshesTabColor = value; OnPropertyChangedWithValue(value, nameof(MeshesTabColor)); } }
        }

        [DataSourceProperty]
        public string LightsTabColor
        {
            get => _lightsTabColor;
            set { if (value != _lightsTabColor) { _lightsTabColor = value; OnPropertyChangedWithValue(value, nameof(LightsTabColor)); } }
        }

        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();

        public void ExecuteShowPrefabs() => ShowTab(SceneStatsTab.Prefabs);
        public void ExecuteShowMeshes() => ShowTab(SceneStatsTab.Meshes);
        public void ExecuteShowLights() => ShowTab(SceneStatsTab.Lights);

        public void ExecuteRefresh() => Rescan();

        public void ExecuteToggleSort()
        {
            _lightsByRadius = !_lightsByRadius;
            RebuildRows();
        }

        public void ExecuteShowMore()
        {
            _limit = Math.Min(LimitMax, _limit + PageSize);
            RebuildRows();
        }

        // Called by the layer when F7's other entry button is pressed while the panel is open.
        public void ShowTab(SceneStatsTab tab)
        {
            _tab = tab;
            _limit = PageSize;
            if (tab == SceneStatsTab.Meshes && _meshes == null && _stats != null) ScanMeshes();
            RebuildRows();
        }

        private static string N(long v) => v.ToString("N0", CultureInfo.InvariantCulture);
        private static string F(float v, string format = "0.##") => v.ToString(format, CultureInfo.InvariantCulture);

        private void Rescan()
        {
            try
            {
                if (!EntitySelector.HasOpenScene)
                {
                    _stats = null; _meshes = null;
                    Rows.Clear();
                    TotalsText = "";
                    HeaderText = "";
                    IsShowMoreVisible = false;
                    StatusText = "No scene is currently open.";
                    return;
                }

                _stats = SceneStatistics.Collect(EntitySelector.CurrentScene);
                // A stale triangle table next to fresh totals would be worse than none; it is
                // recomputed now if that tab is showing, otherwise the next time it is opened.
                _meshes = null;
                if (_tab == SceneStatsTab.Meshes) ScanMeshes();

                TotalsText = BuildTotals();
                StatusText = $"Scanned {N(_stats.Entities)} entities in {_stats.ElapsedMs} ms. This is the LIVE scene (unsaved changes included); press Refresh after editing."
                             + (_stats.Unreadable > 0 ? $" {_stats.Unreadable} entit(y/ies) could not be read and were skipped." : "")
                             + (_stats.Skipped.Count > 0 ? $" {_stats.Skipped.Count} count(s) are switched off - see NOT COUNTED above." : "");
                RebuildRows();
            }
            catch (Exception ex)
            {
                StatusText = "Statistics scan failed: " + ex.Message;
                Log.Error("SceneStats scan failed: " + ex);
            }
        }

        private void ScanMeshes()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) return;
                _meshes = SceneStatistics.CollectMeshes(EntitySelector.CurrentScene);
                if (_meshes == null)
                    StatusText = "Triangle counts are switched off: that scan crashed the editor in an earlier session. "
                               + "Delete native_probe_ledger.txt in Documents\\Mount and Blade II Bannerlord\\MaterialSwapTool to try it again.";
                TotalsText = BuildTotals();
            }
            catch (Exception ex)
            {
                _meshes = null;
                StatusText = "Triangle count failed: " + ex.Message;
                Log.Error("SceneStats mesh scan failed: " + ex);
            }
        }

        private string BuildTotals()
        {
            var s = _stats;
            if (s == null) return "";
            var top = s.TopLevel >= 0 && s.TopLevel <= s.Entities
                ? $" ({N(s.TopLevel)} top-level, {N(s.Entities - s.TopLevel)} children)"
                : "";
            var lines = new List<string>
            {
                $"Scene '{s.SceneName}':  {N(s.Entities)} entities{top}",
                $"Prefabs:  {N(s.Prefabs.Count)} distinct across {N(s.WithPrefab)} entities ({N(s.LinkedPrefabs)} live links, {N(s.WithPrefab - s.LinkedPrefabs)} baked copies matched by name);  {N(s.NoPrefab)} entities match no prefab (sub-parts, hand-built)",
                $"Meshes:  {N(s.MeshComponents)} mesh components on {N(s.MeshEntities)} entities;  cloth simulators: {N(s.ClothComponents)}",
                $"Lights:  {N(s.LightComponents)} ({N(s.ShadowLights)} shadow-casting, {N(s.LightWarnings)} flagged);  particle systems: {N(s.ParticleComponents)};  decals: {N(s.DecalComponents)}",
                $"Scripts:  {N(s.ScriptComponents)} script components on {N(s.ScriptedEntities)} entities",
            };
            if (_meshes != null)
                lines.Add($"Triangles (LOD0, all placed meshes):  {N(_meshes.TotalTris)} across {N(_meshes.Rows.Count)} distinct meshes");
            // A 0 above must not be read as "none" when the count behind it was skipped.
            if (s.Skipped.Count > 0)
                lines.Add("NOT COUNTED (crashed the editor before, now skipped):  " + string.Join(", ", s.Skipped));
            return string.Join("\n", lines);
        }

        private void RebuildRows()
        {
            Rows.Clear();
            IsLightsTab = _tab == SceneStatsTab.Lights;
            PrefabsTabColor = _tab == SceneStatsTab.Prefabs ? TabOnColor : TabOffColor;
            MeshesTabColor = _tab == SceneStatsTab.Meshes ? TabOnColor : TabOffColor;
            LightsTabColor = _tab == SceneStatsTab.Lights ? TabOnColor : TabOffColor;
            // The button names the order a click switches TO.
            SortButtonText = _lightsByRadius ? "Sort: Shadow Cost" : "Sort: Radius";

            if (_stats == null) { HeaderText = ""; IsShowMoreVisible = false; return; }

            int total;
            switch (_tab)
            {
                case SceneStatsTab.Prefabs:
                    total = _stats.Prefabs.Count;
                    foreach (var p in _stats.Prefabs.Take(_limit))
                    {
                        var text = $"{N(p.Count)} x   {p.Name}"
                                   + (p.Linked == p.Count ? "" : p.Linked == 0 ? "   (baked)" : $"   ({N(p.Linked)} linked, {N(p.Count - p.Linked)} baked)");
                        Rows.Add(new SceneStatRowVM(text, p.Name, hasSelect: true, hasGo: p.HasPos, warning: false, OnSelectPrefab, OnGo) { X = p.X, Y = p.Y, Z = p.Z });
                    }
                    HeaderText = total == 0
                        ? "No entity in this scene carries a prefab name."
                        : $"Most-used prefabs - showing {Math.Min(_limit, total)} of {N(total)}. Select picks every instance; Go jumps to the first.";
                    break;

                case SceneStatsTab.Meshes:
                    if (_meshes == null)
                    {
                        total = 0;
                        HeaderText = "Triangle counts are not available - see the status line.";
                        break;
                    }
                    total = _meshes.Rows.Count;
                    foreach (var m in _meshes.Rows.Take(_limit))
                    {
                        var text = $"{N(m.Total)} tris   =   {N(m.Instances)} x {N(m.TrisEach)}   {m.Name}";
                        Rows.Add(new SceneStatRowVM(text, m.Name, hasSelect: true, hasGo: m.HasPos, warning: false, OnSelectMesh, OnGo) { X = m.X, Y = m.Y, Z = m.Z });
                    }
                    HeaderText = total == 0
                        ? "No meshes found."
                        : $"Heaviest meshes (LOD0 triangles x instances) - showing {Math.Min(_limit, total)} of {N(total)}."
                          + (_meshes.TotalTris == 0 ? " Every face count read back as 0 - the engine is not exposing them for these meshes."
                             : _meshes.ZeroFaceMeshes > 0 ? $" {N(_meshes.ZeroFaceMeshes)} mesh(es) read as 0 faces." : "");
                    break;

                default:
                    total = _stats.Lights.Count;
                    var ordered = _lightsByRadius
                        ? _stats.Lights.OrderByDescending(l => l.Radius).ThenByDescending(l => l.ShadowCost)
                        : _stats.Lights.OrderByDescending(l => l.ShadowCost).ThenByDescending(l => l.Radius);
                    foreach (var l in ordered.Take(_limit))
                        Rows.Add(new SceneStatRowVM(LightText(l), null, hasSelect: false, hasGo: true, warning: l.HasWarning, null, OnGo) { X = l.X, Y = l.Y, Z = l.Z });
                    HeaderText = !_stats.LightsAvailable
                        ? "Lights could not be listed: counting them crashed the editor in an earlier session (see the totals above)."
                        : total == 0
                        ? "No lights in this scene."
                        : $"{N(total)} light(s): {N(_stats.ShadowLights)} shadow-casting, {N(_stats.LightWarnings)} flagged [!]. "
                          + (_lightsByRadius ? "Sorted by radius." : "Sorted by shadow cost (shadow-casting first, largest radius first).")
                          + (total > _limit ? $" Showing {_limit}." : "");
                    break;
            }

            int remaining = total - Math.Min(_limit, total);
            IsShowMoreVisible = remaining > 0 && _limit < LimitMax;
            ShowMoreText = remaining > 0 ? $"Show More (+{Math.Min(PageSize, remaining)})" : "";
        }

        private static string LightText(SceneStatistics.LightRow l)
        {
            var owner = l.Root != null ? $"'{l.Owner}' in '{l.Root}'" : $"'{l.Owner}'";
            if (l.ReadFailed) return $"[!] {owner}   - light could not be read";
            var text = (l.HasWarning ? "[!] " : "")
                       + $"radius {F(l.Radius)}   {(l.Shadow ? "SHADOW" : "no shadow")}   intensity {F(l.Intensity)}   "
                       + $"rgb {F(l.R, "0.00")} {F(l.G, "0.00")} {F(l.B, "0.00")}   {owner}"
                       + (l.Hidden ? "   (hidden)" : "");
            if (l.HasWarning) text += "   - " + string.Join("; ", l.Warnings);
            return text;
        }

        private void OnGo(SceneStatRowVM row)
        {
            try { StatusText = EditorNavigation.GoTo(row.X, row.Y, row.Z); }
            catch (Exception ex)
            {
                StatusText = "Go failed: " + ex.Message;
                Log.Error("SceneStats Go failed: " + ex);
            }
        }

        private void OnSelectPrefab(SceneStatRowVM row) =>
            SelectFound(row, scene => SceneStatistics.FindPrefabInstances(scene, row.Key), "instance(s) of prefab");

        private void OnSelectMesh(SceneStatRowVM row) =>
            SelectFound(row, scene => SceneStatistics.FindMeshUsers(scene, row.Key), "entit(y/ies) using mesh");

        // Re-finds the entities NOW rather than trusting anything from the scan, then hands
        // them to the deferred selection queue - selecting inside a click handler is wiped by
        // the editor's own handling of the same click (see DeferredSelection).
        private void SelectFound(SceneStatRowVM row, Func<TaleWorlds.Engine.Scene, List<TaleWorlds.Engine.GameEntity>> find, string what)
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }
                var found = find(EntitySelector.CurrentScene);
                if (found.Count == 0)
                {
                    StatusText = $"Nothing to select - no {what} '{row.Key}' is in the scene any more. Press Refresh.";
                    return;
                }
                LiveSceneChecks.SetEditorSelection(found);
                StatusText = $"Selected {N(found.Count)} {what} '{row.Key}'.";
            }
            catch (Exception ex)
            {
                StatusText = "Select failed: " + ex.Message;
                Log.Error("SceneStats Select failed: " + ex);
            }
        }

        public void ExecuteWriteReport()
        {
            try
            {
                if (_stats == null) { StatusText = "Nothing to write - no scan has run. Press Refresh."; return; }
                var s = _stats;
                var lines = new List<string>
                {
                    "Scene statistics - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    "Live scene as open in the editor (unsaved changes included).",
                    "",
                };
                lines.AddRange(BuildTotals().Split('\n'));

                lines.Add("");
                lines.Add($"=== Prefabs by instance count ({s.Prefabs.Count}) ===");
                foreach (var p in s.Prefabs)
                    lines.Add($"{p.Count,7}  {p.Name}" + (p.Linked == p.Count ? "" : $"  ({p.Linked} linked, {p.Count - p.Linked} baked)"));

                lines.Add("");
                lines.Add($"=== Lights by shadow cost ({s.Lights.Count}) ===");
                foreach (var l in s.Lights.OrderByDescending(x => x.ShadowCost).ThenByDescending(x => x.Radius))
                    lines.Add(LightText(l) + $"   at ({F(l.X, "0.00")}, {F(l.Y, "0.00")}, {F(l.Z, "0.00")})");

                lines.Add("");
                if (_meshes == null)
                    lines.Add("=== Heaviest meshes: not computed (open the Heaviest tab, then write the report again) ===");
                else
                {
                    lines.Add($"=== Meshes by LOD0 triangles x instances ({_meshes.Rows.Count}) ===");
                    foreach (var m in _meshes.Rows)
                        lines.Add($"{m.Total,12}  = {m.Instances,6} x {m.TrisEach,8}  {m.Name}");
                }

                var path = SceneSnapshots.WriteReport("scene-stats", s.SceneName, lines);
                StatusText = "Report written: " + path;
            }
            catch (Exception ex)
            {
                StatusText = "Writing the report failed: " + ex.Message;
                Log.Error("SceneStats report failed: " + ex);
            }
        }
    }
}
