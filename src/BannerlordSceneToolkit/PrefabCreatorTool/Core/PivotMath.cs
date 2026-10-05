using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace PrefabCreatorTool.Core
{
    public static class PivotMath
    {
        // Z is confirmed the vertical axis in this engine (Vec3.Up == (0, 0, 1), checked directly
        // against the shipped TaleWorlds.Library.dll). "Bottom" of a group means the lowest Z
        // across every selected entity's world-space bounding box, not just one entity's own
        // origin - a plain wooden plank's origin might already sit at its base, but a decorative
        // object authored with its origin at its visual center would throw off a single-entity
        // heuristic. X/Y are centered on the combined footprint.
        public static Vec3 ComputeBottomCenterPivot(List<GameEntity> entities)
        {
            var validEntities = entities.Where(EntitySelector.IsValidEntity).ToList();
            if (validEntities.Count == 0) return Vec3.Zero;

            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            bool any = false;

            foreach (var entity in validEntities)
            {
                BoundingBox box;
                try { box = entity.GetGlobalBoundingBox(); }
                catch { continue; }

                minX = System.Math.Min(minX, box.min.x);
                minY = System.Math.Min(minY, box.min.y);
                minZ = System.Math.Min(minZ, box.min.z);
                maxX = System.Math.Max(maxX, box.max.x);
                maxY = System.Math.Max(maxY, box.max.y);
                maxZ = System.Math.Max(maxZ, box.max.z);
                any = true;
            }

            if (!any)
            {
                // Bounding box read failed for every entity (shouldn't normally happen) - fall
                // back to the simpler "lowest entity's own origin" plan the user proposed as a
                // backup, using each entity's placed frame origin instead of geometry.
                var lowest = validEntities.OrderBy(e => e.GetGlobalFrame().origin.z).First();
                return lowest.GetGlobalFrame().origin;
            }

            return new Vec3((minX + maxX) / 2f, (minY + maxY) / 2f, minZ, 0f);
        }
    }
}
