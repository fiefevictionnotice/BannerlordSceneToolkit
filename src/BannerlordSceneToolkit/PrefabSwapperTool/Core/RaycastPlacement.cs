using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using BannerlordSceneToolkit;

namespace PrefabSwapperTool.Core
{
    // RETIRED FROM THE UI (still compiles, just unreachable - no button calls SnapToSurface/
    // SnapIntoPile anymore, see PrefabSwapperPanel.xml). Despite real fixes this session (self-
    // collision on the cast start point, bottom-up processing order, per-entity vs shared-centroid
    // jitter, scale reset in AlignUpToNormal), it still didn't hold up well enough live. Left intact
    // rather than deleted so a future revival has something to start from. Tracked as a future
    // feature, lowest priority - do not re-wire into the UI without being asked.
    //
    // "Shrinkwrap" placement - drop selected entities onto whatever's directly below them (terrain
    // or another entity) and orient them to sit flush on that surface, instead of only being able
    // to move an entity's ORIGIN to a point (the editor's native placement) with no way to also
    // snap its rotation to match a sloped/uneven landing spot.
    //
    // Built around Scene.RayCastForClosestEntityOrTerrain - the same native raycast the engine's
    // own systems (ballistics, AI ground checks) use, confirmed to exist by compiling directly
    // against the shipped TaleWorlds.Engine.dll rather than a guessed reflection call. What is NOT
    // independently confirmed is its exact runtime behavior against this specific ~17,000-entity
    // scene under repeated calls - test on one or two entities before trusting a big selection,
    // same caution as Mirror mode's rollout.
    //
    // "From camera POV" was asked for but isn't included - this codebase has no existing, confirmed
    // way to read the live editor viewport camera's frame (unlike GameEntity/Scene placement calls,
    // which are used all over both mods already). Guessing at a new native subsystem blind, mid an
    // unresolved native-crash investigation, was judged not worth the risk. "Straight down" and
    // "along the entity's own current forward" (both using APIs already proven throughout this
    // codebase) cover the same core need - drop it onto what's below - without that risk.
    public static class RaycastPlacement
    {
        // "Camera POV" was tried and rejected at compile time (Scene has no GetCameraFrame) - the
        // plain Qt scene editor's free camera runs on the native Qt+CoreCLR layer identified in
        // the crash-dump investigation (WotsMainNativeCoreCLR), architecturally separate from the
        // Mono runtime this mod runs on. Not just an unconfirmed member name - genuinely
        // unreachable from here. StraightDown and EntityForward cover the same core need.
        public enum CastDirection { StraightDown, EntityForward }

        public class SnapResult
        {
            public bool Success;
            public string Error;
            public int Snapped;
            public List<string> Failed = new List<string>();
        }

        // Small lateral offset used to sample two extra points near the main hit, so a surface
        // normal can be estimated from three real hit points (cross product of the two edge
        // vectors) rather than trusting an assumed extra output parameter on the raycast call that
        // isn't confirmed to exist on this overload.
        private const float NormalSampleOffset = 0.15f;

        public static SnapResult SnapToSurface(Scene scene, List<GameEntity> selection, CastDirection direction, float maxCastDistance = 300f)
        {
            var result = new SnapResult();
            if (scene == null || selection == null || selection.Count == 0)
            {
                result.Error = "Nothing selected to snap.";
                return result;
            }

            // Bottom-up (current lowest Z first) - confirmed live bug otherwise: with an arbitrary
            // processing order, a piece sitting ABOVE another selected piece could get corrected
            // first, land on that lower piece's STALE (not-yet-corrected) position, and then be
            // left floating once the lower piece got its own turn and moved out from under it.
            // Processing the true lowest pieces first means anything above always settles onto an
            // already-finalized surface.
            var ordered = selection.Where(EntitySelector.IsValidEntity)
                .OrderBy(e => e.GetGlobalFrame().origin.z)
                .ToList();

            foreach (var entity in ordered)
            {
                try
                {
                    var frame = entity.GetGlobalFrame();
                    Vec3 castDir = direction == CastDirection.StraightDown
                        ? new Vec3(0f, 0f, -1f, 0f)
                        : frame.rotation.f;

                    // Start the ray outside the entity's OWN bounding box, not a fixed 0.05 offset
                    // from its origin - RayCastForClosestEntityOrTerrain has no "ignore this entity"
                    // parameter, so a start point that small was well inside most props' own
                    // collision, meaning the ray immediately re-hit the very entity being moved
                    // instead of whatever's actually below it. Confirmed live as "doesn't fully snap
                    // down" / stays floating near its own prior position.
                    var castStart = ResolveCastStart(entity, frame.origin, castDir);

                    if (!TryCast(scene, castStart, castDir, maxCastDistance, entity, out var hitPoint))
                    {
                        result.Failed.Add($"'{entity.Name}': nothing hit within {maxCastDistance}m.");
                        continue;
                    }

                    var normal = EstimateNormal(scene, hitPoint, castDir, entity) ?? new Vec3(0f, 0f, 1f, 0f);
                    var newFrame = new MatrixFrame(AlignUpToNormal(frame.rotation, normal), hitPoint);

                    entity.SetGlobalFrame(ref newFrame, true);

                    EditorFrameSync.Sync(entity);
                    result.Snapped++;
                }
                catch (Exception ex)
                {
                    result.Failed.Add($"'{entity.Name}': {ex.Message}");
                }
            }

            result.Success = result.Snapped > 0;
            if (!result.Success && result.Error == null) result.Error = "Nothing landed - " + (result.Failed.Count > 0 ? result.Failed[0] : "no hits found.");
            return result;
        }

        // Pushes the cast start point out past whichever of the entity's own bounding-box corners
        // sits furthest opposite the cast direction, plus a small margin - guarantees the ray
        // begins outside the entity's own geometry regardless of its size or where its pivot sits
        // relative to its mesh, instead of the fixed tiny offset that used to cause self-hits.
        private static Vec3 ResolveCastStart(GameEntity entity, Vec3 origin, Vec3 castDir)
        {
            try
            {
                var box = entity.GetGlobalBoundingBox();
                float maxBehind = 0f;
                for (int xi = 0; xi < 2; xi++)
                {
                    float x = xi == 0 ? box.min.x : box.max.x;
                    for (int yi = 0; yi < 2; yi++)
                    {
                        float y = yi == 0 ? box.min.y : box.max.y;
                        for (int zi = 0; zi < 2; zi++)
                        {
                            float z = zi == 0 ? box.min.z : box.max.z;
                            var toCorner = new Vec3(x, y, z, 0f) - origin;
                            var behind = -Vec3.DotProduct(toCorner, castDir);
                            if (behind > maxBehind) maxBehind = behind;
                        }
                    }
                }
                return origin - castDir * (maxBehind + 0.1f);
            }
            catch
            {
                return origin - castDir * 0.05f;
            }
        }

        // Scatters the selection with small random XY jitter around the SELECTION'S OWN shared
        // center (not each entity's own current position - see below) and a random spin around
        // world-up, then drops each one via the same shrinkwrap logic above - processed one at a
        // time, in list order, so later pieces can land on top of earlier ones that already settled
        // this same call (each SetGlobalFrame happens before the next entity's raycast), giving a
        // naturally uneven rubble/debris pile instead of everything flattening to the ground.
        public static SnapResult SnapIntoPile(Scene scene, List<GameEntity> selection, float scatterRadius, float maxCastDistance = 300f)
        {
            var result = new SnapResult();
            if (scene == null || selection == null || selection.Count == 0)
            {
                result.Error = "Nothing selected to pile.";
                return result;
            }

            // Confirmed live bug: jittering each entity around its OWN current position never
            // consolidates anything - a selection of pieces already scattered across a scene just
            // gets a small random nudge each, still scattered. Every entity needs to jitter around
            // the SAME shared point (the selection's own average position) for this to actually
            // form a compact pile instead of "the same mess with extra noise."
            var pileCenter = ComputeCentroid(selection);

            var rng = new Random();
            foreach (var entity in selection)
            {
                if (!EntitySelector.IsValidEntity(entity)) continue;
                try
                {
                    var frame = entity.GetGlobalFrame();
                    var angle = (float)(rng.NextDouble() * 2.0 * Math.PI);
                    var dist = (float)rng.NextDouble() * scatterRadius;
                    var jitteredOrigin = pileCenter + new Vec3((float)Math.Cos(angle) * dist, (float)Math.Sin(angle) * dist, 0f, 0f);

                    var spinDegrees = (float)(rng.NextDouble() * 360.0);
                    var spunRotation = SpinAroundWorldUp(frame.rotation, spinDegrees);

                    var castOrigin = jitteredOrigin + new Vec3(0f, 0f, maxCastDistance * 0.25f, 0f);
                    if (!TryCast(scene, castOrigin, new Vec3(0f, 0f, -1f, 0f), maxCastDistance, entity, out var hitPoint))
                    {
                        result.Failed.Add($"'{entity.Name}': nothing hit within {maxCastDistance}m.");
                        continue;
                    }

                    var normal = EstimateNormal(scene, hitPoint, new Vec3(0f, 0f, -1f, 0f), entity) ?? new Vec3(0f, 0f, 1f, 0f);
                    var newFrame = new MatrixFrame(AlignUpToNormal(spunRotation, normal), hitPoint);

                    entity.SetGlobalFrame(ref newFrame, true);

                    EditorFrameSync.Sync(entity);
                    result.Snapped++;
                }
                catch (Exception ex)
                {
                    result.Failed.Add($"'{entity.Name}': {ex.Message}");
                }
            }

            result.Success = result.Snapped > 0;
            if (!result.Success && result.Error == null) result.Error = "Nothing landed - " + (result.Failed.Count > 0 ? result.Failed[0] : "no hits found.");
            return result;
        }

        private static Vec3 ComputeCentroid(List<GameEntity> selection)
        {
            var sum = new Vec3(0f, 0f, 0f, 0f);
            int count = 0;
            foreach (var entity in selection)
            {
                if (!EntitySelector.IsValidEntity(entity)) continue;
                sum = sum + entity.GetGlobalFrame().origin;
                count++;
            }
            return count > 0 ? sum / count : new Vec3(0f, 0f, 0f, 0f);
        }

        private static bool TryCast(Scene scene, Vec3 origin, Vec3 direction, float maxDistance, GameEntity ignoreSelf, out Vec3 hitPoint)
        {
            hitPoint = default;
            var rayBegin = origin - direction * 0.05f; // start just off the entity's own surface, not inside it
            var rayEnd = origin + direction * maxDistance;
            bool hit = scene.RayCastForClosestEntityOrTerrain(rayBegin, rayEnd, out float distance, out hitPoint, out WeakGameEntity hitEntity, 0.02f);
            return hit;
        }

        // Three-point normal estimate: the primary hit plus two more hits sampled a small distance
        // away along the cast direction's own tangent plane. Falls back to null (caller uses
        // world-up) if any of the three samples miss - a partial/garbage normal from an incomplete
        // sample would be worse than just not rotating at all.
        private static Vec3? EstimateNormal(Scene scene, Vec3 primaryHit, Vec3 castDir, GameEntity ignoreSelf)
        {
            var castLen = (float)Math.Sqrt(castDir.x * castDir.x + castDir.y * castDir.y + castDir.z * castDir.z);
            if (castLen < 0.0001f) return null;
            var dir = castDir / castLen;

            // Any vector not parallel to dir, then Gram-Schmidt to get two tangent axes.
            var reference = Math.Abs(dir.z) < 0.9f ? new Vec3(0f, 0f, 1f, 0f) : new Vec3(1f, 0f, 0f, 0f);
            var tangentA = Vec3Cross(dir, reference);
            var tLen = (float)Math.Sqrt(tangentA.x * tangentA.x + tangentA.y * tangentA.y + tangentA.z * tangentA.z);
            if (tLen < 0.0001f) return null;
            tangentA = tangentA / tLen;
            var tangentB = Vec3Cross(dir, tangentA);

            if (!TryCast(scene, primaryHit - dir * 10f + tangentA * NormalSampleOffset, dir, 20f, ignoreSelf, out var hitA)) return null;
            if (!TryCast(scene, primaryHit - dir * 10f + tangentB * NormalSampleOffset, dir, 20f, ignoreSelf, out var hitB)) return null;

            var edgeA = hitA - primaryHit;
            var edgeB = hitB - primaryHit;
            var normal = Vec3Cross(edgeA, edgeB);
            var nLen = (float)Math.Sqrt(normal.x * normal.x + normal.y * normal.y + normal.z * normal.z);
            if (nLen < 0.0001f) return null;
            normal = normal / nLen;

            // Normal must point roughly opposite the cast direction (i.e. back toward the caster) -
            // cross product handedness can flip depending on sample layout, this corrects it.
            if (Vec3.DotProduct(normal, dir) > 0f) normal = normal * -1f;
            return normal;
        }

        private static Vec3 Vec3Cross(Vec3 a, Vec3 b) => new Vec3(
            a.y * b.z - a.z * b.y,
            a.z * b.x - a.x * b.z,
            a.x * b.y - a.y * b.x,
            0f);

        // Rebuilds the rotation so local-up matches the given surface normal, preserving as much of
        // the original forward-facing direction as possible (projected onto the new tangent plane) -
        // same Gram-Schmidt approach already used for Mirror/Rotate elsewhere in this file's sibling
        // PrefabDistributor.cs, so a flat-ish object keeps facing roughly the way it did before,
        // just tilted to match the new surface instead of spinning to some arbitrary heading.
        //
        // originalRotation's own basis vectors encode BOTH orientation and per-axis scale in this
        // engine - GetLocalScale() derives an entity's scale from their magnitude (see
        // PrefabDistributor's MirrorGroup comments), and native SetGlobalFrame/Instantiate calls
        // respect whatever magnitude a Mat3 carries without renormalizing it. Building the new
        // basis purely from unit vectors (as this used to do outright) silently reset every
        // snapped entity's scale to (1,1,1) - confirmed live. Each axis's original magnitude is
        // captured up front and reapplied to the freshly-oriented unit vectors at the end instead.
        private static Mat3 AlignUpToNormal(Mat3 originalRotation, Vec3 normal)
        {
            var scaleSide = VecLength(originalRotation.s);
            var scaleForward = VecLength(originalRotation.f);
            var scaleUp = VecLength(originalRotation.u);

            var up = normal;
            var forward = originalRotation.f;
            var dot = Vec3.DotProduct(forward, up);
            var projectedForward = forward - up * dot;
            var pLen = (float)Math.Sqrt(projectedForward.x * projectedForward.x + projectedForward.y * projectedForward.y + projectedForward.z * projectedForward.z);
            if (pLen < 0.01f)
            {
                // Original forward was ~parallel to the new up (e.g. snapping something onto a
                // near-vertical wall) - fall back to the original side vector instead.
                projectedForward = originalRotation.s - up * Vec3.DotProduct(originalRotation.s, up);
                pLen = (float)Math.Sqrt(projectedForward.x * projectedForward.x + projectedForward.y * projectedForward.y + projectedForward.z * projectedForward.z);
                if (pLen < 0.01f) projectedForward = new Vec3(1f, 0f, 0f, 0f) - up * Vec3.DotProduct(new Vec3(1f, 0f, 0f, 0f), up);
                pLen = Math.Max(0.0001f, (float)Math.Sqrt(projectedForward.x * projectedForward.x + projectedForward.y * projectedForward.y + projectedForward.z * projectedForward.z));
            }
            var newForward = projectedForward / pLen;
            var newSide = Vec3Cross(newForward, up);
            var sLen = Math.Max(0.0001f, (float)Math.Sqrt(newSide.x * newSide.x + newSide.y * newSide.y + newSide.z * newSide.z));
            newSide = newSide / sLen;
            return new Mat3(newSide * scaleSide, newForward * scaleForward, up * scaleUp);
        }

        private static float VecLength(Vec3 v) => (float)Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);

        private static Mat3 SpinAroundWorldUp(Mat3 rotation, float angleDegrees)
        {
            float rad = angleDegrees * ((float)Math.PI / 180f);
            float cos = (float)Math.Cos(rad);
            float sin = (float)Math.Sin(rad);
            var newS = rotation.s * cos + rotation.f * sin;
            var newF = rotation.f * cos - rotation.s * sin;
            return new Mat3(newS, newF, rotation.u);
        }
    }
}
