using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using BannerlordSceneToolkit;

namespace PrefabCreatorTool.Core
{
    // "Match Secondary Origin to Primary": re-origins a secondary anchor's pivot to match a
    // primary anchor's pivot, WITHOUT moving the secondary's own visible content (its children).
    // No unparent/move/re-parent needed - GameEntity.SetGlobalFrame is a genuine world-space
    // transform setter, independent of parenting, so the trick is: record each child's current
    // WORLD position before touching anything, move the parent, then set each child's world
    // position back to what was recorded. The engine recomputes each child's local-to-parent
    // offset to make that world position true again - the children never actually move, only
    // their local offset relative to the now-moved parent changes. Only direct children need
    // handling, not the whole recursive subtree: a grandchild's world position is anchored
    // relative to ITS OWN immediate parent (one of these direct children), and since that
    // immediate parent's world frame is being restored exactly, anything further down the
    // hierarchy comes along for free without needing its own explicit restore.
    public static class PivotAligner
    {
        public class MatchOriginResult
        {
            public bool Success;
            public string Error;
            public string Warning;
            public int ChildrenRepositioned;
        }

        public static MatchOriginResult MatchSecondaryOriginToPrimary(GameEntity primaryAnchor, GameEntity secondaryAnchor)
        {
            if (primaryAnchor == null || secondaryAnchor == null)
                return new MatchOriginResult { Success = false, Error = "Missing primary or secondary entity." };
            if (!EntitySelector.IsValidEntity(primaryAnchor) || !EntitySelector.IsValidEntity(secondaryAnchor))
                return new MatchOriginResult { Success = false, Error = "Primary or secondary entity is no longer valid." };
            if (primaryAnchor.Pointer == secondaryAnchor.Pointer)
                return new MatchOriginResult { Success = false, Error = "Primary and secondary are the same entity." };

            List<GameEntity> children;
            try { children = secondaryAnchor.GetChildren().Where(EntitySelector.IsValidEntity).ToList(); }
            catch { children = new List<GameEntity>(); }

            // Captured BEFORE the parent moves - these are the world positions to restore.
            var childFrames = new List<(GameEntity Entity, MatrixFrame Frame)>();
            foreach (var child in children)
                childFrames.Add((child, child.GetGlobalFrame()));

            // CONFIRMED BUG, fixed 2026-08-20: this used to hand the primary's ENTIRE frame to
            // SetGlobalFrame - and a MatrixFrame carries rotation AND scale in its `rotation` Mat3,
            // not just position. So "match origin" also stamped the primary's rotation and scale
            // onto the secondary, which is exactly the reported "it moves AND resizes the prefab".
            // Only the origin should change; the secondary keeps its own orientation and scale.
            var secondaryBefore = secondaryAnchor.GetGlobalFrame();
            var primaryOrigin = primaryAnchor.GetGlobalFrame().origin;
            Log.Info($"[PivotAlign] primary.origin=({primaryOrigin.x:F3},{primaryOrigin.y:F3},{primaryOrigin.z:F3}) " +
                     $"secondary.origin=({secondaryBefore.origin.x:F3},{secondaryBefore.origin.y:F3},{secondaryBefore.origin.z:F3}) " +
                     $"children={childFrames.Count}");

            try
            {
                var target = secondaryBefore;   // keep secondary's own rotation/scale
                target.origin = primaryOrigin;  // change ONLY the pivot position
                secondaryAnchor.SetGlobalFrame(ref target, true);
                EditorFrameSync.Sync(secondaryAnchor);
            }
            catch (Exception ex)
            {
                return new MatchOriginResult { Success = false, Error = $"Failed to move the secondary's origin: {ex.Message}" };
            }

            var secondaryAfter = secondaryAnchor.GetGlobalFrame();
            Log.Info($"[PivotAlign] secondary.origin AFTER=({secondaryAfter.origin.x:F3},{secondaryAfter.origin.y:F3},{secondaryAfter.origin.z:F3})");

            int repositioned = 0;
            foreach (var (child, originalFrame) in childFrames)
            {
                try
                {
                    var before = child.GetGlobalFrame().origin;
                    var frame = originalFrame;
                    child.SetGlobalFrame(ref frame, true);
                    EditorFrameSync.Sync(child);
                    var after = child.GetGlobalFrame().origin;

                    // Diagnostic for the reported "offset DOUBLES" case. If the restore worked,
                    // `after` equals originalFrame.origin. If it lands somewhere else - especially
                    // at twice the delta - the frame is not being applied in the space assumed
                    // here, and this line says so outright instead of leaving it to guesswork.
                    var dx = after.x - originalFrame.origin.x;
                    var dy = after.y - originalFrame.origin.y;
                    var dz = after.z - originalFrame.origin.z;
                    var err = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (err > 0.01)
                    {
                        Log.Warn($"[PivotAlign] child '{child.Name}' did NOT hold position: " +
                                 $"wanted=({originalFrame.origin.x:F3},{originalFrame.origin.y:F3},{originalFrame.origin.z:F3}) " +
                                 $"gotBeforeRestore=({before.x:F3},{before.y:F3},{before.z:F3}) " +
                                 $"gotAfterRestore=({after.x:F3},{after.y:F3},{after.z:F3}) error={err:F3}");
                    }
                    repositioned++;
                }
                catch (Exception ex)
                {
                    Log.Warn($"PivotAligner: failed to restore world position for child '{child.Name}': {ex.Message}");
                }
            }

            // An entity's own meshes hang off its origin, so re-origining necessarily drags them
            // along - only CHILD entities can be held in place. If the secondary carries its own
            // mesh, the visible content moves and there is no way around it short of reparenting
            // that mesh into a child first. Say so rather than reporting a clean success.
            var ownMeshes = 0;
            try { ownMeshes = secondaryAnchor.MultiMeshComponentCount; } catch { }

            var result = new MatchOriginResult { Success = true, ChildrenRepositioned = repositioned };
            if (ownMeshes > 0)
                result.Warning = $"'{secondaryAnchor.Name}' has {ownMeshes} mesh component(s) of its own, which move with the origin. " +
                                 "Only child entities can hold their position - put the mesh on a child if it must stay put.";
            if (childFrames.Count == 0 && ownMeshes == 0)
                result.Warning = $"'{secondaryAnchor.Name}' has no children and no meshes - the origin moved but there was no content to hold in place.";
            return result;
        }
    }
}
