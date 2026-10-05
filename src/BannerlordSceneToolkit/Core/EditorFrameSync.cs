using System;
using TaleWorlds.Engine;

namespace BannerlordSceneToolkit
{
    // Refreshes the EDITOR'S transform manipulator (the "triad") after an entity is moved from
    // code.
    //
    // THE BUG THIS FIXES: SetGlobalFrame moves the entity, but the editor keeps its own cached
    // frame for the manipulator gizmo and does not notice. For an entity just made with
    // CreateEmpty that cached frame is world origin - the corner of the map. The result is an
    // anchor whose Transform panel reads correctly (that reads the real frame) while its gizmo
    // sits at the map corner, and the instant you drag it the editor recomputes from the stale
    // gizmo and the entity teleports. Same story after mirror/rotate/distribute: the entity is
    // where it should be, the manipulator is not.
    //
    // GameEntity.UpdateTriadFrameForEditor / UpdateTriadFrameForEditorForAllChildren are real
    // members of TaleWorlds.Engine.GameEntity (verified by reflecting the shipped assembly), and
    // were never called anywhere in this codebase.
    //
    // Children get their own refresh because a composed prefab is an anchor plus children, and
    // dragging a child with a stale triad has the same failure independently of its parent.
    public static class EditorFrameSync
    {
        public static void Sync(GameEntity entity)
        {
            if (entity == null || entity.Pointer == UIntPtr.Zero) return;
            try
            {
                entity.UpdateTriadFrameForEditor();
                entity.UpdateTriadFrameForEditorForAllChildren();
            }
            catch (Exception ex)
            {
                Log.Warn($"EditorFrameSync: triad refresh failed for '{entity.Name}': {ex.Message}");
            }
        }
    }
}
