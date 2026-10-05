using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;

namespace MaterialSwapTool.Core
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

        // What every ChangeLogEntry.SceneName is stamped with, and what batch history and
        // Undo/Redo scope themselves to. Single definition on purpose: the engines used to each
        // inline `CurrentScene?.GetName() ?? "(unknown scene)"`, so a batch logged with no scene
        // open and a batch filtered for one had to agree on that literal by coincidence.
        public const string UnknownSceneName = "(unknown scene)";

        public static string CurrentSceneName => CurrentScene?.GetName() ?? UnknownSceneName;

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

        // Reads the editor's selection RIGHT NOW, bypassing _cachedManualSelection entirely.
        //
        // WHY THIS HAD TO EXIST. The cache is only refreshed from the tick patch while the F8 or
        // Flora panel is open (a deliberate optimisation - it was a full scene scan 60x a second
        // otherwise). Everything driven by a HOTKEY rather than a panel therefore read a cache
        // that was empty or stale, because the normal way to use a hotkey is with no panel open.
        // That is the single reason numeric transform reported "nothing selected", the drag
        // watcher never captured anything, and repeat recorded nothing.
        //
        // Cost is ONE native call in the normal case (see the empty-result note below) - cheap
        // enough for the drag watcher's 0.06s poll, not just for discrete events.
        public static List<GameEntity> GetLiveManualSelection()
        {
            var selected = new List<GameEntity>();
            if (!HasOpenScene) return selected;

            // Utilities.GetSelectedEntities asks the editor for its selection DIRECTLY - one
            // native call. The obvious implementation (enumerate every entity, ask
            // MBEditor.IsEntitySelected about each) is fine once per click but ruinous at the
            // rate the drag watcher polls, since its cost scales with the whole scene rather
            // than with how much is selected.
            // AN EMPTY RESULT IS NOW TRUSTED. This used to fall through to the scan below
            // whenever the fast path came back with nothing, on the theory that an empty answer
            // might be the native call no-opping rather than a real empty selection. That cost
            // far more than it was worth, and it produced a CONFIRMED FREEZE (2026-08-22, user
            // report: "the editor freezes for a bit when I hit Ctrl+Shift+P").
            //
            // Why that path was hit constantly rather than rarely: the editor CLEARS the
            // selection on a keypress, before our Postfix runs - the whole reason SelectionMemory
            // exists. So every hotkey (Ctrl+Shift+P select-root, Ctrl+Shift+O isolate, Shift+R
            // repeat, Ctrl+Shift+T numeric) read an empty live selection by definition, fell
            // through, and paid for a full scene enumeration plus one native IsEntitySelected
            // per entity - 7,700 entities in CC_74_battle, 10,800 in CC_76_battle - synchronously,
            // inside the frame. That is the stutter. Worse, ManipulationWatcher polls this every
            // 0.06s while numeric transform is on, so simply having nothing selected was paying
            // that cost ~16 times a second.
            //
            // The fast path is trustworthy: Utilities.GetSelectedEntities is the same call
            // ApplyEditorSelectionNow reads the current selection back with in order to clear it,
            // and clearing demonstrably works. A throw still falls back to the scan, since that
            // is a real failure rather than a real "nothing is selected".
            try
            {
                var live = new List<GameEntity>();
                Utilities.GetSelectedEntities(ref live);
                foreach (var e in live)
                    if (IsValidEntity(e)) selected.Add(e);
                return selected;
            }
            catch (Exception ex)
            {
                Log.Warn("GetLiveManualSelection: fast path threw, falling back to a full scan: " + ex.Message);
            }

            try
            {
                var all = new List<GameEntity>();
                CurrentScene.GetEntities(ref all);
                foreach (var e in all)
                    if (IsValidEntity(e) && MBEditor.IsEntitySelected(e)) selected.Add(e);
                Log.Info($"[SelectionDiag] fallback scan used: {all.Count} entities examined, {selected.Count} selected.");
            }
            catch (Exception ex) { Log.Warn("GetLiveManualSelection failed: " + ex.Message); }
            return selected;
        }

        // Refreshed on left-click while the F8/Flora panel is open. Reads through
        // GetLiveManualSelection rather than doing its own full scene scan (which is what this
        // did until 2026-08-22): the scan cost one enumeration plus a native IsEntitySelected
        // per entity - ~10k of them on a real scene - on EVERY click, which is the same
        // per-frame-budget problem as the hotkey freeze documented above, just triggered by
        // clicking instead. The snapshot semantics are unchanged; only how it is read.
        public static void RefreshManualSelectionCache()
        {
            if (!HasOpenScene) { _cachedManualSelection = new List<GameEntity>(); return; }
            _cachedManualSelection = GetLiveManualSelection();

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

        // Flattens an entity plus every descendant (recursive, arbitrary depth) into one list.
        // Composite prefabs (a parent anchor entity with the actual visible mesh living on CHILD
        // entities - columns, greebles, and a lot of real furniture in this asset library) have
        // empty or near-empty mesh slots on the entity actually selected/targeted - confirmed live
        // as "the material swap tool doesn't work when there's a prefab with children," since every
        // mesh-slot-walking method in this mod used to only ever look at the target's OWN slots.
        // Shared here (not duplicated per engine) since every caller lives in this one mod - no
        // cross-mod independence concern the way FamilyAutoPlacer's duplication has.
        public static List<GameEntity> EnumerateSelfAndDescendants(GameEntity root)
        {
            var result = new List<GameEntity>();
            CollectSelfAndDescendants(root, result);
            return result;
        }

        private static void CollectSelfAndDescendants(GameEntity entity, List<GameEntity> result)
        {
            if (entity == null) return;
            result.Add(entity);

            IEnumerable<GameEntity> children;
            try { children = entity.GetChildren(); }
            catch { return; }

            foreach (var child in children)
                CollectSelfAndDescendants(child, result);
        }

        // Every distinct current material name across all MetaMesh/Mesh slots on the given
        // entities AND their descendants, in first-seen order. Feeds "Get Input Rules from
        // Selection" - same MultiMeshComponentCount/GetMetaMesh/MeshCount/GetMeshAtIndex walk
        // MaterialSwapEngine uses, just reading instead of writing.
        public static List<string> GetDistinctMaterialsOnEntities(IEnumerable<GameEntity> entities)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var target in entities)
            {
                if (!IsValidEntity(target)) continue;
                foreach (var entity in EnumerateSelfAndDescendants(target))
                {
                    if (!IsValidEntity(entity)) continue;
                    for (int m = 0; m < entity.MultiMeshComponentCount; m++)
                    {
                        var meta = entity.GetMetaMesh(m);
                        if (meta == null || !meta.IsValid) continue;
                        for (int i = 0; i < meta.MeshCount; i++)
                        {
                            // Normalized: an override-clone 'x(copy)' is still material x, and a
                            // From-rule seeded with the raw clone name could never match anything
                            // now that rule matching strips the suffix too.
                            var name = OverrideFillEngine.StripCopySuffix(meta.GetMeshAtIndex(i)?.GetMaterial()?.Name);
                            if (!string.IsNullOrEmpty(name) && seen.Add(name))
                                result.Add(name);
                        }
                    }
                }
            }
            return result;
        }

        // The inverse of EnumerateSelfAndDescendants: walk each selected entity UP to its
        // top-level root and hand back the deduplicated roots. This is what turns "I clicked a
        // column of the house" into "the whole house prefab is selected" - composite prefabs put
        // the visible meshes on child entities, so a viewport click routinely lands on a part
        // when the top-level prefab is what a swap, isolate, or transform actually wants. Works
        // just as well on a generated pile/distribution (the pieces' root is the anchor) and is
        // a no-op for an entity that is already top-level.
        // Single-entity form of PromoteToRoots' walk: the topmost valid parent, or the entity
        // itself when already top-level. MaterialSwapEngine anchors weighted-target rolls here
        // (2026-08-23, "the top parent should define the inheritance for those below").
        public static GameEntity TopMostParent(GameEntity entity)
        {
            if (!IsValidEntity(entity)) return entity;
            var current = entity;
            int guard = 0;
            while (guard++ < 64)
            {
                GameEntity parent;
                try { parent = current.Parent; } catch { break; }
                if (!IsValidEntity(parent)) break;
                current = parent;
            }
            return current;
        }

        public static List<GameEntity> PromoteToRoots(IEnumerable<GameEntity> entities)
        {
            var roots = new List<GameEntity>();
            var seen = new HashSet<UIntPtr>();
            if (entities == null) return roots;

            foreach (var entity in entities)
            {
                if (!IsValidEntity(entity)) continue;

                var current = entity;
                int guard = 0;
                while (guard++ < 64)
                {
                    GameEntity parent;
                    try { parent = current.Parent; } catch { break; }
                    if (!IsValidEntity(parent)) break;
                    current = parent;
                }

                if (IsValidEntity(current) && seen.Add(current.Pointer))
                    roots.Add(current);
            }
            return roots;
        }
    }
}
