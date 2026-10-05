using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;

namespace PrefabCreatorTool.Core
{
    // Same MBEditor._editorScene access pattern as MaterialSwapTool's own EntitySelector -
    // duplicated rather than shared, per the decision to keep this mod fully independent.
    public static class EntitySelector
    {
        public static Scene CurrentScene => MBEditor._editorScene;

        public static bool HasOpenScene => CurrentScene != null && CurrentScene.Pointer != UIntPtr.Zero;

        // Reads the editor's selection RIGHT NOW rather than from any cache. The cached selection
        // in this toolkit is only refreshed while certain panels are open, which is the wrong
        // assumption for anything driven by a button press in a different panel. One scene scan
        // per call, on a discrete user action - never per tick.
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

        public static bool IsValidEntity(GameEntity e) => e != null && e.Pointer != UIntPtr.Zero;

        // Refreshed from the tick patch on a click while a panel is open - same reasoning as
        // MaterialSwapTool's version: a live IsEntitySelected read inside a button's own click
        // handler risks reading state the same click just cleared, so this reads a snapshot taken
        // just before the click resolves instead.
        private static List<GameEntity> _cachedManualSelection = new List<GameEntity>();

        public static void RefreshManualSelectionCache()
        {
            if (!HasOpenScene) { _cachedManualSelection = new List<GameEntity>(); return; }
            var all = new List<GameEntity>();
            CurrentScene.GetEntities(ref all);
            _cachedManualSelection = all.Where(e => IsValidEntity(e) && MBEditor.IsEntitySelected(e)).ToList();
        }

        public static List<GameEntity> GetManualSelection() =>
            _cachedManualSelection.Where(IsValidEntity).ToList();

        public static List<GameEntity> CollectAll()
        {
            if (!HasOpenScene) return new List<GameEntity>();
            var all = new List<GameEntity>();
            CurrentScene.GetEntities(ref all);
            return all.Where(IsValidEntity).ToList();
        }
    }
}
