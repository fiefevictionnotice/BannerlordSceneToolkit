using System;
using System.Collections.Generic;
using TaleWorlds.Engine;

namespace MaterialSwapTool.Core
{
    // WHY THIS EXISTS (2026-08-21).
    //
    // Every "select these in the editor" button reported the right count and left the editor
    // showing nothing selected. The engine call was never the problem: tool.log shows
    //     [SelectInEditor] selected=14 (matched=14)
    // with no disagreement from either Utilities.GetSelectedEntities or MBEditor.IsEntitySelected,
    // so at the instant our handler ran, 14 entities really were selected.
    //
    // The next line in the log is what gives it away - EntitySelector's per-tick SelectionDiag,
    // which counts MBEditor.IsEntitySelected across the whole scene every frame, never logged a
    // transition to 14. It stayed at 0. So the selection was set and then wiped again inside the
    // same frame, after our click handler returned.
    //
    // That matches a hazard EntitySelector already documented for a different symptom: a click on
    // one of our Gauntlet panels ALSO reaches the native editor's own click-to-select handling
    // underneath (InputRestrictions is a Mission-input concept and does not appear to cover plain
    // edit mode). The editor processes that same click after us and resets the selection to
    // "whatever the mouse is over", which is our panel - i.e. nothing.
    //
    // Fix: don't select during the click. Queue it and apply a couple of ticks later, once the
    // editor has finished handling that click. Selecting is idempotent, so a delay costs nothing.
    public static class DeferredSelection
    {
        private static List<GameEntity> _pending;
        private static int _ticksRemaining;

        // Two ticks, not one: one tick is enough for the editor's handling of THIS click, but a
        // click also produces a release event on the following frame, and a release over our
        // panel can clear the selection just as well as the press did.
        private const int DefaultDelayTicks = 2;

        public static void Request(List<GameEntity> entities)
        {
            _pending = entities ?? new List<GameEntity>();
            _ticksRemaining = DefaultDelayTicks;
        }

        // Cancels a queued selection. Called when a scene switch invalidates the entities, since
        // applying stale GameEntity pointers after a teardown is exactly the class of bug that
        // has crashed the editor before.
        public static void Cancel()
        {
            _pending = null;
            _ticksRemaining = 0;
        }

        public static void Tick()
        {
            if (_pending == null) return;
            if (_ticksRemaining-- > 0) return;

            var list = _pending;
            _pending = null;
            try { LiveSceneChecks.ApplyEditorSelectionNow(list); }
            catch (Exception ex) { Log.Warn("DeferredSelection.Tick failed: " + ex.Message); }
        }
    }
}
