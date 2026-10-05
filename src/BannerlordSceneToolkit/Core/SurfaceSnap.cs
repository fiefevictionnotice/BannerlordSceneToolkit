using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace BannerlordSceneToolkit
{
    // Drop a point onto whatever is beneath it, and work out which way that surface faces.
    //
    // A DELIBERATE PORT OF PILE GENERATOR'S IMPLEMENTATION, not a fresh one. This project has
    // two raycast-placement implementations in its history and they did not fare the same:
    // PrefabSwapperTool's RaycastPlacement (Snap to Surface / Snap Into Pile) was retired after
    // several rounds of real fixes still left it "not working, really", while PileGenerator's
    // own snapping was never reported broken and is still in daily use. So this is the working
    // one's maths, copied verbatim rather than re-derived - including the two details that were
    // learned the hard way:
    //
    //   * The cast STARTS ABOVE the point, never at it. Starting at the point lets a prefab's
    //     own collision swallow the ray, which reads as "snap does nothing".
    //   * AlignUpToNormal preserves each basis vector's LENGTH. This engine encodes scale as the
    //     magnitude of the rotation matrix's basis vectors, so rebuilding them as unit vectors
    //     silently resets the entity's scale to 1,1,1 - a bug that shipped once already.
    //
    // PileGenerator keeps its own copy rather than calling this: it works today, and the point
    // of sharing code is to stop new callers re-deriving fragile maths, not to refactor a
    // feature that is not broken.
    public static class SurfaceSnap
    {
        // How far above the point to start, and how far down to look. A column being placed on a
        // hillside can be a long way above or below the reference height, so this is generous.
        public const float MaxCastDistance = 200f;

        public class Hit
        {
            public bool Found;
            public Vec3 Point;
            public Vec3 Normal;     // world up when the surface could not be measured
        }

        // How many times a filtered cast will push past something that is not the target before
        // giving up. Enough to get through a bit of clutter (a rock, a bush, a prop sitting on
        // the floor you actually meant), not so many that a dense pile turns into a long loop.
        private const int MaxPassThroughAttempts = 6;

        // Casts straight down onto the first entity or terrain beneath the point.
        //
        // targetNames, when given, restricts what counts as a surface: only a hit on an entity of
        // one of those names - or on one of its children, since a prefab's collision usually lives
        // on child entities - is accepted. Anything else is treated as clutter in the way and the
        // cast RESUMES JUST BELOW IT rather than failing, so a stray prop lying on the floor does
        // not punch a hole in the grid. Without target names the first thing hit wins, which is
        // the plain "drop onto whatever is beneath" behaviour.
        //
        // SEVERAL names are accepted, comma-separated ("terrace_a, terrace_b, rock_shelf") - added
        // so one run can span multiple selected surfaces at once, e.g. a grid crossing two terrace
        // pieces and the rocks between them. The string is parsed ONCE per cast, not per attempt.
        public static Hit CastDown(Scene scene, Vec3 point, string targetNames = null, float maxDistance = MaxCastDistance)
        {
            var result = new Hit { Normal = new Vec3(0f, 0f, 1f, 0f) };
            if (scene == null) return result;

            var down = new Vec3(0f, 0f, -1f, 0f);
            var castFrom = point + new Vec3(0f, 0f, maxDistance * 0.25f, 0f);
            float remaining = maxDistance;
            var targets = ParseTargetNames(targetNames);
            bool filtered = targets != null;

            for (int attempt = 0; attempt < (filtered ? MaxPassThroughAttempts : 1); attempt++)
            {
                if (remaining <= 0.01f) return result;
                if (!TryCast(scene, castFrom, down, remaining, out var hitPoint, out var hitEntity)) return result;

                if (!filtered || Matches(hitEntity, targets))
                {
                    result.Found = true;
                    result.Point = hitPoint;
                    result.Normal = EstimateNormal(scene, hitPoint, targets)
                                    ?? new Vec3(0f, 0f, 1f, 0f);
                    return result;
                }

                // Not the target - drop below what we hit and keep looking downward.
                remaining -= Math.Max(0.02f, castFrom.z - hitPoint.z);
                castFrom = new Vec3(hitPoint.x, hitPoint.y, hitPoint.z - 0.02f, 0f);
            }

            return result;
        }

        // "a, b, c" -> ["a","b","c"]; null when there is no usable name at all, which is the
        // "unfiltered" signal the callers test for. Names with embedded commas are the one thing
        // this cannot express - entity names practically never contain them.
        private static string[] ParseTargetNames(string names)
        {
            if (string.IsNullOrWhiteSpace(names)) return null;

            var parts = names.Split(',');
            var cleaned = new System.Collections.Generic.List<string>(parts.Length);
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0) cleaned.Add(trimmed);
            }
            return cleaned.Count == 0 ? null : cleaned.ToArray();
        }

        // A hit counts as a target if the entity itself, or the root of the prefab it belongs
        // to, carries ANY of the names - collision on a composite prefab normally sits on its
        // children, so matching only the exact entity would reject the very surfaces this is
        // aimed at.
        private static bool Matches(WeakGameEntity hit, string[] targetNames)
        {
            try
            {
                if (!hit.IsValid) return false;

                var name = hit.Name;
                string rootName = null;
                var root = hit.Root;
                if (root != null && root.Pointer != UIntPtr.Zero) rootName = root.Name;

                foreach (var target in targetNames)
                {
                    if (string.Equals(name, target, StringComparison.OrdinalIgnoreCase)) return true;
                    if (rootName != null && string.Equals(rootName, target, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }
            return false;
        }

        // A normal-estimation sample, filtered the same way as the main cast (see EstimateNormal).
        private static bool TrySampleCast(Scene scene, Vec3 origin, Vec3 direction, float maxDistance,
                                          string[] targetNames, out Vec3 hitPoint)
        {
            hitPoint = default(Vec3);
            var from = origin;
            float remaining = maxDistance;
            bool filtered = targetNames != null;

            for (int attempt = 0; attempt < (filtered ? MaxPassThroughAttempts : 1); attempt++)
            {
                if (remaining <= 0.01f) return false;
                if (!TryCast(scene, from, direction, remaining, out hitPoint, out var hitEntity)) return false;
                if (!filtered || Matches(hitEntity, targetNames)) return true;

                remaining -= Math.Max(0.02f, from.z - hitPoint.z);
                from = new Vec3(hitPoint.x, hitPoint.y, hitPoint.z - 0.02f, 0f);
            }
            return false;
        }

        private static bool TryCast(Scene scene, Vec3 origin, Vec3 direction, float maxDistance,
                                    out Vec3 hitPoint, out WeakGameEntity hitEntity)
        {
            hitPoint = default(Vec3);
            var rayBegin = origin - direction * 0.05f;
            var rayEnd = origin + direction * maxDistance;
            return scene.RayCastForClosestEntityOrTerrain(rayBegin, rayEnd, out float distance, out hitPoint, out hitEntity, 0.02f);
        }

        // Three nearby downward casts make a triangle; its cross product is the surface normal.
        // The engine exposes no normal from the ray itself, so this is measured rather than read.
        //
        // The two sample casts honour the same target filter as the main one. Without that, a
        // sample landing on a nearby prop instead of the target surface would tilt the measured
        // normal toward that prop. A sample that finds nothing returns null here, and the caller
        // falls back to world up - a safe failure rather than a wrong angle.
        private static Vec3? EstimateNormal(Scene scene, Vec3 primaryHit, string[] targetNames = null)
        {
            const float sampleOffset = 0.15f;
            var dir = new Vec3(0f, 0f, -1f, 0f);
            var tangentA = new Vec3(1f, 0f, 0f, 0f);
            var tangentB = new Vec3(0f, 1f, 0f, 0f);

            if (!TrySampleCast(scene, primaryHit - dir * 10f + tangentA * sampleOffset, dir, 20f, targetNames, out var hitA)) return null;
            if (!TrySampleCast(scene, primaryHit - dir * 10f + tangentB * sampleOffset, dir, 20f, targetNames, out var hitB)) return null;

            var edgeA = hitA - primaryHit;
            var edgeB = hitB - primaryHit;
            var normal = Cross(edgeA, edgeB);
            var length = Length(normal);
            if (length < 0.0001f) return null;
            normal = normal / length;

            if (Vec3.DotProduct(normal, dir) > 0f) normal = normal * -1f;
            return normal;
        }

        // Tilts a rotation so its up axis follows the surface, keeping the facing as close to the
        // original as the new up allows - and keeping every basis vector's LENGTH, which is this
        // engine's encoding of scale (see the class comment).
        public static Mat3 AlignUpToNormal(Mat3 originalRotation, Vec3 normal)
        {
            var scaleSide = Length(originalRotation.s);
            var scaleForward = Length(originalRotation.f);
            var scaleUp = Length(originalRotation.u);

            var up = normal;
            var forward = originalRotation.f;
            var projectedForward = forward - up * Vec3.DotProduct(forward, up);
            var pLen = Length(projectedForward);
            if (pLen < 0.01f)
            {
                // Facing was almost straight up/down, so it says nothing about heading - fall
                // back to the side axis to get a usable reference direction.
                projectedForward = originalRotation.s - up * Vec3.DotProduct(originalRotation.s, up);
                pLen = Math.Max(0.0001f, Length(projectedForward));
            }

            var newForward = projectedForward / pLen;
            var newSide = Cross(newForward, up);
            var sLen = Math.Max(0.0001f, Length(newSide));
            newSide = newSide / sLen;

            return new Mat3(newSide * scaleSide, newForward * scaleForward, up * scaleUp);
        }

        private static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
            a.y * b.z - a.z * b.y,
            a.z * b.x - a.x * b.z,
            a.x * b.y - a.y * b.x, 0f);

        private static float Length(Vec3 v) => (float)Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
    }
}
