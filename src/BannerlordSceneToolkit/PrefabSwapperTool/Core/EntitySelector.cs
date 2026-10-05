using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;

namespace PrefabSwapperTool.Core
{
    public enum SelectionMode
    {
        Manual,
        Filtered,
        WholeScene
    }

    // Builds the target entity list for an operation. All three modes funnel through the same
    // MBEditor._editorScene reference - the live scene the plain Qt editor currently has open.
    public static class EntitySelector
    {
        public static Scene CurrentScene => MBEditor._editorScene;

        public static bool HasOpenScene => CurrentScene != null && CurrentScene.Pointer != UIntPtr.Zero;

        // Same fallback string the swap logger writes, so a comparison between a logged
        // batch and the open scene cannot fail merely because the two spell "no name" differently.
        public static string CurrentSceneName => CurrentScene?.GetName() ?? "(unknown scene)";

        public static bool IsValidEntity(GameEntity e) => e != null && e.Pointer != UIntPtr.Zero;

        // Continuously refreshed from the tick patch, every frame, regardless of whether the
        // panel is even open. Manual mode reads from this snapshot instead of scanning
        // IsEntitySelected live at click time - a click on the panel appears to also reach the
        // native editor's own click-to-select/deselect handling underneath (InputRestrictions is
        // a Mission-input concept and likely doesn't cover plain edit mode at all), so a live
        // read done inside a button's own click handler risks reading state the same click just
        // cleared. Reading a snapshot taken before the click sidesteps that regardless of which
        // button triggered it.
        private static List<GameEntity> _cachedManualSelection = new List<GameEntity>();

        // Temporary: settles whether the reported "deselection on click" is a real change to
        // IsEntitySelected's return value or purely a rendering artifact. Only logs on count
        // transitions (not every tick), so this is cheap and safe to leave running. Remove once
        // answered - grep tool.log for "[SelectionDiag]" and correlate against the button-click
        // timestamps logged from MaterialSwapVM.
        private static int _lastLoggedSelectionCount = -1;

        // Reads the editor's selection RIGHT NOW rather than from _cachedManualSelection.
        //
        // The cache is only refreshed while a panel is open - a deliberate optimisation, since
        // refreshing it is a full scene scan and doing that every tick was wasteful. But anything
        // driven by a HOTKEY runs with no panel open, which is exactly when the cache is stale or
        // empty. That mismatch is why numeric transform reported "nothing selected", the drag
        // watcher captured nothing, and repeat recorded nothing.
        //
        // One scan per call, and every caller is on a discrete event (a hotkey, a mouse-down),
        // never per tick - so the reason the cache exists does not apply to them.
        public static List<GameEntity> GetLiveManualSelection()
        {
            var selected = new List<GameEntity>();
            if (!HasOpenScene) return selected;

            // Utilities.GetSelectedEntities asks the editor for its selection DIRECTLY - one
            // native call. The obvious implementation (enumerate every entity, ask
            // MBEditor.IsEntitySelected about each) is fine once per click but ruinous at the
            // rate the drag watcher polls, since its cost scales with the whole scene rather
            // than with how much is selected.
            try
            {
                var live = new List<GameEntity>();
                Utilities.GetSelectedEntities(ref live);
                foreach (var e in live)
                    if (IsValidEntity(e)) selected.Add(e);

                // Only trusted when it actually returns something. If this native call is ever a
                // no-op in some editor state, silently returning "nothing is selected" would break
                // every feature downstream - falling through to the scan is the safe failure.
                if (selected.Count > 0) return selected;
            }
            catch (Exception ex)
            {
                Log.Warn("GetLiveManualSelection: fast path failed, falling back to a scan: " + ex.Message);
            }

            try
            {
                var all = new List<GameEntity>();
                CurrentScene.GetEntities(ref all);
                foreach (var e in all)
                    if (IsValidEntity(e) && MBEditor.IsEntitySelected(e)) selected.Add(e);
            }
            catch (Exception ex) { Log.Warn("GetLiveManualSelection failed: " + ex.Message); }
            return selected;
        }

        public static void RefreshManualSelectionCache()
        {
            if (!HasOpenScene) { _cachedManualSelection = new List<GameEntity>(); return; }
            var all = new List<GameEntity>();
            CurrentScene.GetEntities(ref all);
            _cachedManualSelection = all.Where(e => IsValidEntity(e) && MBEditor.IsEntitySelected(e)).ToList();

            if (_cachedManualSelection.Count != _lastLoggedSelectionCount)
            {
                Log.Info($"[SelectionDiag] native selection count changed: {_lastLoggedSelectionCount} -> {_cachedManualSelection.Count}");
                _lastLoggedSelectionCount = _cachedManualSelection.Count;
            }
        }

        public static List<GameEntity> GetTargets(SelectionMode mode, string filterTerm = null)
        {
            if (!HasOpenScene)
                throw new InvalidOperationException("No scene is currently open in the editor.");

            var all = new List<GameEntity>();
            CurrentScene.GetEntities(ref all);

            switch (mode)
            {
                case SelectionMode.Manual:
                    // FIXED 2026-08-22 (confirmed in CC_76): the cache is refreshed only by the
                    // F6 panel's own layer hooks - but since the v0.7 move this selector also
                    // serves the swap section EMBEDDED IN F5 and the browsers launched from it.
                    // With F6 closed, every F5 swap action read whatever was selected the last
                    // time F6 was open (the user's mirror anchor, for a whole evening: fills
                    // returned the wrong prefab, Set Live Reference grabbed the mirror anchor,
                    // swap-set Apply tested the wrong entity). Live read first - one native call,
                    // and every caller here is a discrete button click, never per-tick - with the
                    // cache kept only as the fallback for the click-clears-selection case it
                    // originally existed for.
                    var liveSelection = GetLiveManualSelection();
                    if (liveSelection.Count > 0) return liveSelection;
                    return _cachedManualSelection.Where(IsValidEntity).ToList();

                case SelectionMode.Filtered:
                    if (string.IsNullOrWhiteSpace(filterTerm))
                        return new List<GameEntity>();
                    var term = filterTerm.Trim();
                    return all.Where(e => IsValidEntity(e) && MatchesFilter(e, term)).ToList();

                case SelectionMode.WholeScene:
                    return all.Where(IsValidEntity).ToList();

                default:
                    return new List<GameEntity>();
            }
        }

        private static bool MatchesFilter(GameEntity entity, string term)
        {
            var name = entity.Name ?? string.Empty;
            if (name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // Catalog objects carry no name, only a prefab reference - check both so a term like
            // "aserai" matches whichever attribute the entity actually has.
            var meta = TryGetFirstMetaMesh(entity);
            var prefabName = meta?.GetName() ?? string.Empty;
            return prefabName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static MetaMesh TryGetFirstMetaMesh(GameEntity entity)
        {
            if (entity.MultiMeshComponentCount <= 0) return null;
            return entity.GetMetaMesh(0);
        }

        // Every distinct current material name across all MetaMesh/Mesh slots on the given
        // entities, in first-seen order. Feeds "Get Input Rules from Selection" - same
        // MultiMeshComponentCount/GetMetaMesh/MeshCount/GetMeshAtIndex walk MaterialSwapEngine
        // uses, just reading instead of writing.
        public static List<string> GetDistinctMaterialsOnEntities(IEnumerable<GameEntity> entities)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var entity in entities)
            {
                if (!IsValidEntity(entity)) continue;
                for (int m = 0; m < entity.MultiMeshComponentCount; m++)
                {
                    var meta = entity.GetMetaMesh(m);
                    if (meta == null || !meta.IsValid) continue;
                    for (int i = 0; i < meta.MeshCount; i++)
                    {
                        var name = meta.GetMeshAtIndex(i)?.GetMaterial()?.Name;
                        if (!string.IsNullOrEmpty(name) && seen.Add(name))
                            result.Add(name);
                    }
                }
            }
            return result;
        }
    }
}
