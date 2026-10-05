using System;
using System.Collections.Generic;
using System.Linq;
using BannerlordSceneToolkit;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace PrefabCreatorTool.Core
{
    // Automatic origin repositioning: put a prefab's origin at a named point on its OWN bounding
    // box - bottom center, top center, -X center, and so on - instead of having to place an
    // anchor entity there by hand first (which is what Origin To Anchor above needs, and which
    // does not scale past one prefab).
    //
    // Each selected prefab is measured and moved INDEPENDENTLY: select five and each one's origin
    // lands on its own bottom center, not on a shared group point. That is the difference from
    // every other bulk transform in this toolkit, which deliberately work off a group pivot.
    //
    // THE NAMING RULE: the named axis is pinned to that end of the box, and the other two axes
    // are centered. "Bottom Center" = Z at the minimum, X and Y centered. "+X Center" = X at the
    // maximum, Y and Z centered. So the seven useful presets fall out of a plain axis + side
    // pair, which is what lets the UI be two cycle buttons rather than seven buttons. X/Center
    // and Y/Center both land on the box's true center, same as Z/Center - the same point named
    // three ways, harmless, and it keeps the two controls fully independent.
    //
    // WHY MESHED ENTITIES ARE SKIPPED, NOT MOVED. Moving an origin without moving the prefab
    // works by putting the CHILDREN back in world space after the root moves (see
    // OriginToAnchor). An entity's own mesh has no such offset to compensate with - it is drawn
    // at the entity's frame, so moving the frame moves the geometry. On a meshed entity this
    // operation cannot do what its name promises, so it declines and says so rather than
    // silently sliding real objects around. Composite prefabs - an empty root with the geometry
    // on children - are the case this is for, and are what New Prefab produces.
    public static class OriginPresets
    {
        public enum Axis { X, Y, Z }
        public enum Side { Min, Center, Max }

        public static string FriendlyName(Axis axis, Side side)
        {
            if (side == Side.Center) return "Middle Center";
            switch (axis)
            {
                case Axis.Z: return side == Side.Min ? "Bottom Center" : "Top Center";
                case Axis.X: return side == Side.Min ? "-X Center" : "+X Center";
                default:     return side == Side.Min ? "-Y Center" : "+Y Center";
            }
        }

        private static bool Alive(GameEntity e) => e != null && e.Pointer != UIntPtr.Zero;

        // The world-space point this preset names on the entity's own full-hierarchy box.
        // GetGlobalBoundingBox covers the entity AND its children (the same call
        // PrefabDistributor measures spacing with, and the reason it uses the global box rather
        // than the local one: a composite prefab's root has a near-zero box of its own).
        public static bool TryComputePoint(GameEntity root, Axis axis, Side side, out Vec3 point)
        {
            point = Vec3.Zero;
            if (!Alive(root)) return false;

            BoundingBox box;
            try { box = root.GetGlobalBoundingBox(); }
            catch { return false; }

            var center = new Vec3((box.min.x + box.max.x) / 2f,
                                  (box.min.y + box.max.y) / 2f,
                                  (box.min.z + box.max.z) / 2f, 0f);

            point = center;
            if (side == Side.Center) return true;

            bool useMin = side == Side.Min;
            switch (axis)
            {
                case Axis.X: point.x = useMin ? box.min.x : box.max.x; break;
                case Axis.Y: point.y = useMin ? box.min.y : box.max.y; break;
                default:     point.z = useMin ? box.min.z : box.max.z; break;
            }
            return true;
        }

        public class Result
        {
            public int Moved;
            public int Failed;
            public readonly List<string> SkippedMeshed = new List<string>();
            public readonly List<string> Problems = new List<string>();
        }

        // Every entity gets its own measurement and its own move. Undo is captured by the caller
        // (it needs the children too - see PrefabCreatorVM), and a backup is the caller's job as
        // well, so this stays a pure operation.
        public static Result ApplyToEach(IEnumerable<GameEntity> targets, Axis axis, Side side)
        {
            var result = new Result();
            if (targets == null) return result;

            // Deduplicated by native pointer: the same entity reaching this twice would measure
            // its box a second time AFTER its origin already moved. The box itself does not move
            // (geometry stays put), so the second pass would be a no-op - but paying for it, and
            // reporting it as a second success, would both be wrong.
            var seen = new HashSet<UIntPtr>();

            foreach (var entity in targets)
            {
                if (!Alive(entity) || !seen.Add(entity.Pointer)) continue;

                if (!OriginToAnchor.IsEmpty(entity, out var why))
                {
                    result.SkippedMeshed.Add($"'{entity.Name}' ({why})");
                    continue;
                }

                if (!TryComputePoint(entity, axis, side, out var point))
                {
                    result.Failed++;
                    result.Problems.Add($"'{entity.Name}': could not read its bounding box");
                    continue;
                }

                // force:false is safe here - IsEmpty was already checked above, so this never
                // trips the confirmation path.
                var moved = OriginToAnchor.MoveOriginTo(entity, point, force: false);
                if (moved.Success) result.Moved++;
                else
                {
                    result.Failed++;
                    result.Problems.Add($"'{entity.Name}': {moved.Message}");
                }
            }

            Log.Info($"[OriginPresets] {FriendlyName(axis, side)}: moved={result.Moved} " +
                     $"skippedMeshed={result.SkippedMeshed.Count} failed={result.Failed}");
            return result;
        }

        // Roots plus their direct children - what undo has to hold to put an origin move back
        // exactly. The children do not end up anywhere new (they are world-restored), but they
        // are written to, and capturing them makes the undo an exact inverse rather than an
        // almost-inverse.
        public static List<GameEntity> CollectForUndo(IEnumerable<GameEntity> targets)
        {
            var list = new List<GameEntity>();
            var seen = new HashSet<UIntPtr>();
            if (targets == null) return list;

            foreach (var entity in targets)
            {
                if (!Alive(entity) || !seen.Add(entity.Pointer)) continue;
                list.Add(entity);

                List<GameEntity> children;
                try { children = entity.GetChildren().Where(Alive).ToList(); }
                catch { continue; }

                foreach (var child in children)
                    if (seen.Add(child.Pointer)) list.Add(child);
            }
            return list;
        }
    }
}
