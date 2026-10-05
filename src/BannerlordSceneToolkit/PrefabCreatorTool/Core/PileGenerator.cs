using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using BannerlordSceneToolkit;

namespace PrefabCreatorTool.Core
{
    // Generates a scattered pile (or a whole scattered area) of instances from a whitelist of
    // (prefab, count, texture set, snap) entries - "30 rocks + 10 sticks, slapped together into a
    // debris pile" without hand-placing every piece. Built on the same raycast-and-settle
    // mechanism RaycastPlacement (in PrefabSwapperTool) already uses for its own Snap Into Pile
    // action, ported here rather than referenced since that lives in a different mod and this tool
    // has no assembly dependency on it - this port only needs the "cast down, estimate normal,
    // settle" half, not the "operate on an existing selection" half.
    //
    // Two placement modes share everything except how a candidate XY is chosen for one instance:
    // Generate (Single Pile) jitters around one reference point within a radius; GenerateInArea
    // (Scatter Area) samples uniformly across the combined top-down footprint of whatever's
    // selected - built for covering an irregular floor rather than one discrete pile.
    public static class PileGenerator
    {
        public class GenerateResult
        {
            public bool Success;
            public string Error;
            public GameEntity AnchorEntity;
            public List<GameEntity> Placed = new List<GameEntity>();
            public List<string> Failed = new List<string>();
            // Placed fine but its entry's texture set failed to apply - kept separate since the
            // piece is still physically there, just untextured.
            public List<string> TextureFailed = new List<string>();
            // Pieces whose entry asked for physics removal - stripped after the whole pile is
            // built, never during (see StripPhysics).
            public List<GameEntity> PendingPhysicsStrip = new List<GameEntity>();
            public int PhysicsCleared;
            public int Tagged;
        }

        private const string ReselectTagPrefix = "pile_";
        private const float MaxCastDistance = 300f;
        // Area mode retries a fixed number of times per instance before giving up on it - an
        // irregular footprint (an L-shaped room, a floor with a hole in it) means some random XY
        // samples will legitimately miss the actual floor, and a miss there should skip that one
        // instance rather than fabricate a placement with no real surface under it (unlike Single
        // Pile, which has one known reference height to fall back to).
        private const int AreaMaxAttemptsPerInstance = 5;

        // entries is read top-to-bottom the way the finished pile/area should LOOK (first entry =
        // top layer, last = base) but is walked in REVERSE here - the base layer has to physically
        // exist before anything can raycast down and settle onto it. This is the one place that
        // inversion happens; nothing upstream (the UI, the saved recipe) needs to think about it.
        public static GenerateResult Generate(Scene scene, MatrixFrame referenceFrame, List<PileEntry> entries, float scatterRadius, string anchorName, string placementTag = null)
        {
            var result = new GenerateResult();
            if (scene == null || entries == null || entries.Count == 0)
            {
                result.Error = "No entries to generate.";
                return result;
            }

            if (!TryCreateAnchor(scene, referenceFrame, anchorName, out var anchor, out var reselectTag, out var error))
            {
                result.Error = error;
                return result;
            }
            result.AnchorEntity = anchor;

            var rng = new Random();

            foreach (var entry in EnumerateBaseFirst(entries))
            {
                var preset = LoadPreset(entry, result);

                for (int i = 0; i < entry.Count; i++)
                {
                    MatrixFrame placeFrame;
                    try
                    {
                        placeFrame = entry.SnapToSurface
                            ? ResolveSnappedFrame(scene, referenceFrame, scatterRadius, rng)
                            : ResolveFlatFrame(referenceFrame, scatterRadius, rng);
                    }
                    catch (Exception ex)
                    {
                        result.Failed.Add($"'{entry.PrefabName}' instance {i}: placement failed: {ex.Message}");
                        continue;
                    }

                    PlaceOneInstance(scene, anchor, reselectTag, entry, placeFrame, preset, i, result, placementTag);
                }
            }

            // Every layer is down; only now is it safe to remove collision (see StripPhysics).
            if (result.PendingPhysicsStrip.Count > 0)
                result.PhysicsCleared = StripPhysics(result.PendingPhysicsStrip);

            // Editor freshness nudge (2026-08-23, "when i toggle game entities on or off ...
            // they stay visible. i have to save and reload"): the editor holds stale cached
            // state for runtime-created entities - same registry disease as the CopyFrom
            // ghosts - so visibility toggles and physics changes don't show until a rebuild.
            // UpdateSceneTree is the strongest reachable nudge; a save/reload remains the
            // full heal (documented in KNOWN-ISSUES).
            try { TaleWorlds.MountAndBlade.MBEditor.UpdateSceneTree(true); } catch { }

            result.Success = true;
            return result;
        }

        // Scatter Area: same entry whitelist/layering, but instances are spread uniformly across
        // the combined top-down footprint of every entity passed in footprintSelection, instead of
        // jittered around one point - built for covering a floor (or several floor tiles selected
        // together) rather than one discrete pile. The anchor is centered on the footprint itself
        // (X/Y center, Z at its lowest point, identity rotation) rather than any one selected
        // entity's own frame, matching PrefabDistributor's BottomCenterOfGroup convention.
        public static GenerateResult GenerateInArea(Scene scene, List<GameEntity> footprintSelection, List<PileEntry> entries, string anchorName, string placementTag = null)
        {
            var result = new GenerateResult();
            if (scene == null || entries == null || entries.Count == 0)
            {
                result.Error = "No entries to generate.";
                return result;
            }
            if (!TryComputeFootprint(footprintSelection, out var min, out var max))
            {
                result.Error = "Couldn't read a bounding box from the selection.";
                return result;
            }

            var anchorFrame = MatrixFrame.Identity;
            anchorFrame.origin = new Vec3((min.x + max.x) / 2f, (min.y + max.y) / 2f, min.z, 0f);

            if (!TryCreateAnchor(scene, anchorFrame, anchorName, out var anchor, out var reselectTag, out var error))
            {
                result.Error = error;
                return result;
            }
            result.AnchorEntity = anchor;

            var rng = new Random();

            foreach (var entry in EnumerateBaseFirst(entries))
            {
                var preset = LoadPreset(entry, result);

                for (int i = 0; i < entry.Count; i++)
                {
                    if (!TryResolveAreaFrame(scene, min, max, entry.SnapToSurface, rng, out var placeFrame))
                    {
                        result.Failed.Add($"'{entry.PrefabName}' instance {i}: no surface found within the selected area after {AreaMaxAttemptsPerInstance} attempts.");
                        continue;
                    }

                    PlaceOneInstance(scene, anchor, reselectTag, entry, placeFrame, preset, i, result, placementTag);
                }
            }

            // Every layer is down; only now is it safe to remove collision (see StripPhysics).
            if (result.PendingPhysicsStrip.Count > 0)
                result.PhysicsCleared = StripPhysics(result.PendingPhysicsStrip);

            // Editor freshness nudge (2026-08-23, "when i toggle game entities on or off ...
            // they stay visible. i have to save and reload"): the editor holds stale cached
            // state for runtime-created entities - same registry disease as the CopyFrom
            // ghosts - so visibility toggles and physics changes don't show until a rebuild.
            // UpdateSceneTree is the strongest reachable nudge; a save/reload remains the
            // full heal (documented in KNOWN-ISSUES).
            try { TaleWorlds.MountAndBlade.MBEditor.UpdateSceneTree(true); } catch { }

            result.Success = true;
            return result;
        }

        private static IEnumerable<PileEntry> EnumerateBaseFirst(List<PileEntry> entries)
        {
            for (int e = entries.Count - 1; e >= 0; e--)
            {
                var entry = entries[e];
                if (!string.IsNullOrWhiteSpace(entry.PrefabName) && entry.Count >= 1) yield return entry;
            }
        }

        private static ColorPreset LoadPreset(PileEntry entry, GenerateResult result)
        {
            if (string.IsNullOrWhiteSpace(entry.PresetName)) return null;
            try { return ColorPresetStore.Load(entry.PresetName); }
            catch (Exception ex)
            {
                result.Failed.Add($"'{entry.PrefabName}': texture set '{entry.PresetName}' failed to load: {ex.Message}");
                return null;
            }
        }

        private static bool TryCreateAnchor(Scene scene, MatrixFrame anchorFrame, string anchorName, out GameEntity anchor, out string reselectTag, out string error)
        {
            anchor = null;
            reselectTag = null;
            try { anchor = GameEntity.CreateEmpty(scene, true, false, true); }
            catch (Exception ex) { error = "CreateEmpty failed: " + ex.Message; return false; }
            if (anchor == null) { error = "CreateEmpty returned null."; return false; }

            anchor.Name = anchorName;
            anchor.SetGlobalFrame(ref anchorFrame, true);
            EditorFrameSync.Sync(anchor);
            reselectTag = ReselectTagPrefix + SafeTag(anchorName);
            anchor.AddTag(reselectTag);
            error = null;
            return true;
        }

        // POST-PLACEMENT PHYSICS STRIP.
        //
        // Deliberately a separate pass after every layer is down, not a flag honoured inside
        // PlaceOneInstance. Snapping works by raycasting straight down onto whatever already
        // exists, and RayCastForClosestEntityOrTerrain only sees things that HAVE collision - so
        // stripping a base layer at placement time would make every layer above it fall through
        // to the terrain instead of stacking. Build the pile, then strip.
        //
        // Children are included: a prefab's collision usually lives on its child entities, so
        // clearing only the root would leave the pile just as solid as before.
        // REWORKED 2026-08-23 ("the delete physics thing on the pile generator doesn't work"):
        // the old pass skipped any entity whose BodyFlag read None - but a prefab's collision
        // shape can sit on a child that reports None while still being perfectly solid, so
        // those pieces were never touched at all. Every entity in the tree now gets the full
        // treatment (RemovePhysics + flags zeroed - harmless on one that truly has nothing),
        // and a piece whose flags read back non-None afterwards is named in the log so "didn't
        // work" is never silent again.
        public static int StripPhysics(IEnumerable<GameEntity> entities)
        {
            int cleared = 0;
            int stillSolid = 0;
            foreach (var entity in entities ?? Enumerable.Empty<GameEntity>())
            {
                if (entity == null || entity.Pointer == UIntPtr.Zero) continue;
                foreach (var e in SelfAndDescendants(entity))
                {
                    try
                    {
                        var before = e.BodyFlag;
                        e.RemovePhysics(false);
                        try { e.SetBodyFlags(BodyFlags.None); } catch { }

                        // THE FLAG IS THE ACTUAL LEVER (2026-08-23, "still bugged" - the log
                        // showed the pass running and every BodyFlag reading None afterwards,
                        // yet pieces still collided): the editor's raycasts work off the
                        // REGISTERED physics body, which clearing flags does not unregister,
                        // and there is no physics-disable API - but EntityFlags.PhysicsDisabled
                        // provably kills collision (it is exactly what made CopyFrom clones
                        // unclickable). Set here on every entity in the piece's tree; the
                        // FlagRepair sweep never touches it on pile pieces (they carry none of
                        // its trigger flags). Note: shift-drag duplicating a stripped piece
                        // re-enables its collision (the duplicate cleanse clears this flag).
                        e.EntityFlags = e.EntityFlags | EntityFlags.PhysicsDisabled;

                        // Round three (2026-08-23, "the physics delete didn't work stilll"):
                        // clearing flags + PhysicsDisabled STILL left the registered body
                        // solid, so the body itself is the target now - disable its physics
                        // state outright, children included.
                        try { e.SetPhysicsState(false, true); } catch (Exception psEx) { Log.Warn($"[Pile] SetPhysicsState failed on '{e.Name}': {psEx.Message}"); }
                        cleared++;

                        var after = e.BodyFlag;
                        if (after != BodyFlags.None)
                        {
                            stillSolid++;
                            Log.Warn($"[Pile] physics NOT fully cleared on '{e.Name}': {before} -> {after}");
                        }
                    }
                    catch (Exception ex) { Log.Warn($"StripPhysics failed on '{e.Name}': {ex.Message}"); }
                }
            }
            Log.Info($"[Pile] StripPhysics processed {cleared} entit(ies), {stillSolid} still report body flags.");
            return cleared;
        }

        // RE-SETTLE ("force down" / flatten).
        //
        // Re-runs the same downward raycast used at placement, on pieces that are already in the
        // scene: anything left floating (a layer that snapped onto something later deleted, or a
        // pile dragged to new ground) drops onto whatever is beneath it now.
        //
        // An entity cannot be allowed to hit ITSELF on the way down or nothing would move, so
        // each one is temporarily parked far above before casting.
        //
        // TWO RULES, both from 2026-08-23 ("stuff starts to float ... it like self replicates up
        // even higher. The initial result is ALWAYS more settled than after resettling"):
        //
        //   1. The cast starts just above the piece's OWN height, not above the whole pile. The
        //      old top-of-pile start took the FIRST hit on the way down - for a bottom piece
        //      that is the TOP of the pieces stacked above it, so every resettle perched pieces
        //      on their neighbours' heads and the pile ratcheted upward.
        //   2. A resettle may only move a piece DOWN (a small rise allowance covers popping out
        //      of interpenetrated ground). Whatever else the raycast finds, "more settled than
        //      before" is now guaranteed by construction.
        //
        // Processed lowest-first so a piece that lands somewhere new is already settled before
        // the piece above it casts down onto it.
        public class SettleResult
        {
            public int Moved;
            public int Missed;
            public int Skipped;
        }

        public static SettleResult SettleDown(Scene scene, List<GameEntity> entities)
        {
            var result = new SettleResult();
            if (scene == null || entities == null) return result;

            var ordered = entities
                .Where(e => e != null && e.Pointer != UIntPtr.Zero)
                .OrderBy(e => { try { return e.GetGlobalFrame().origin.z; } catch { return float.MaxValue; } })
                .ToList();

            foreach (var entity in ordered)
            {
                MatrixFrame frame;
                try { frame = entity.GetGlobalFrame(); }
                catch { result.Skipped++; continue; }

                var origin = frame.origin;

                // Lift out of the way first, so the cast cannot hit the piece's own collision.
                var parked = frame;
                parked.origin = new Vec3(origin.x, origin.y, origin.z + MaxCastDistance, 0f);
                try { entity.SetGlobalFrame(parked); } catch { result.Skipped++; continue; }

                // Rule 1: start just above the piece's own height (see header comment). The
                // clearance lets a piece resting exactly ON a surface re-find that surface, and
                // pop out of slightly-interpenetrated ground.
                const float CastClearance = 0.5f;
                var down = new Vec3(0f, 0f, -1f, 0f);
                var castFrom = new Vec3(origin.x, origin.y, origin.z + CastClearance, 0f);

                if (TryCast(scene, castFrom, down, MaxCastDistance, out var hitPoint)
                    && hitPoint.z <= origin.z + CastClearance)   // Rule 2: down (or the tiny pop-out), never up
                {
                    frame.origin = new Vec3(origin.x, origin.y, hitPoint.z, 0f);
                    if (Math.Abs(hitPoint.z - origin.z) > 0.001f) result.Moved++;
                }
                else
                {
                    result.Missed++;   // nothing underneath - put it back exactly where it was
                }

                try
                {
                    entity.SetGlobalFrame(frame);
                    EditorFrameSync.Sync(entity);   // without this the editor's triad stays at the old spot
                }
                catch (Exception ex) { Log.Warn($"SettleDown failed to place '{entity.Name}': {ex.Message}"); }
            }

            Log.Info($"[SettleDown] moved={result.Moved} missed={result.Missed} skipped={result.Skipped}");
            return result;
        }

        private static IEnumerable<GameEntity> SelfAndDescendants(GameEntity root)
        {
            if (root == null || root.Pointer == UIntPtr.Zero) yield break;
            yield return root;
            List<GameEntity> children;
            try { children = root.GetChildren().ToList(); } catch { yield break; }
            foreach (var child in children)
                foreach (var e in SelfAndDescendants(child))
                    yield return e;
        }

        private static void PlaceOneInstance(Scene scene, GameEntity anchor, string reselectTag, PileEntry entry, MatrixFrame placeFrame, ColorPreset preset, int instanceIndex, GenerateResult result, string placementTag)
        {
            GameEntity instance;
            try
            {
                if (entry.DeletePhysics)
                {
                    // BROKEN-OUT COPY so the physics removal actually SAVES (2026-08-23, "they
                    // have physics after reload"): a plain Instantiate serializes as a bare
                    // <game_entity prefab="X"> REFERENCE, and the scene loader rebuilds physics
                    // from the prefab definition on every load - nothing stripped at runtime
                    // can survive that, which is why three rounds of runtime stripping all
                    // "failed". A CopyFrom product serializes broken-out, with explicit
                    // components and its own <physics> node only for a body that exists at
                    // save time - so the post-generation StripPhysics finally sticks across
                    // save/reload. Costs the piece its prefab identity (it saves as loose
                    // meshes), which is exactly the trade a no-collision decoration wants.
                    var template = GameEntity.Instantiate(scene, entry.PrefabName.Trim(), placeFrame, true);
                    if (template == null) { result.Failed.Add($"'{entry.PrefabName}' instance {instanceIndex}: instantiate returned null (unknown prefab name?)"); return; }
                    try
                    {
                        instance = GameEntity.CopyFrom(scene, template, true, true);
                    }
                    finally
                    {
                        try { scene.RemoveEntity(template, 0); } catch { }
                    }
                    if (instance == null) { result.Failed.Add($"'{entry.PrefabName}' instance {instanceIndex}: CopyFrom returned null."); return; }

                    // Same flag cleanse every CopyFrom product needs (unselectable/unsaveable
                    // otherwise - see PrefabDistributor.CloneSourceAt).
                    try { PrefabSwapperTool.Core.PrefabDistributor.ClearRuntimeFlagsInTree(instance, 0); } catch { }
                    var pf = placeFrame;
                    try { instance.SetGlobalFrame(ref pf, true); } catch { }

                    // Marked so physics-stripped broken-out pieces are findable as a group
                    // (2026-08-23, "at a minimum they need to be tagged") - on top of the
                    // pile's own reselect tag and any placement tag. Auto-prefabbing them
                    // back together is the prefab-consolidation ROADMAP item.
                    try { instance.AddTag("pile_nophys"); } catch { }
                }
                else
                {
                    instance = GameEntity.Instantiate(scene, entry.PrefabName.Trim(), placeFrame, true);
                }
            }
            catch (Exception ex) { result.Failed.Add($"'{entry.PrefabName}' instance {instanceIndex}: {ex.Message}"); return; }
            if (instance == null) { result.Failed.Add($"'{entry.PrefabName}' instance {instanceIndex}: instantiate returned null (unknown prefab name?)"); return; }

            try { anchor.AddChild(instance, true); }
            catch (Exception ex) { result.Failed.Add($"'{entry.PrefabName}' instance {instanceIndex}: failed to parent under anchor: {ex.Message}"); }

            if (preset != null)
            {
                try
                {
                    // Zero matches is a FAILURE, not a quiet no-op (2026-08-23): a preset whose
                    // override keys fit nothing on this prefab used to apply "successfully"
                    // while changing nothing, which reads as the texture system being broken.
                    if (ColorPresetApplier.Apply(instance, preset) == 0)
                        result.TextureFailed.Add($"'{entry.PrefabName}' instance {instanceIndex}: texture set '{preset.Name}' matched 0 mesh/child names on this prefab");
                }
                catch (Exception ex) { result.TextureFailed.Add($"'{entry.PrefabName}' instance {instanceIndex}: {ex.Message}"); }
            }

            instance.AddTag(reselectTag);

            // Optional user tag, on top of the internal reselect tag rather than instead of it -
            // losing the reselect tag would break the tool's own "select what I just made".
            if (!string.IsNullOrWhiteSpace(placementTag))
            {
                try { instance.AddTag(placementTag.Trim()); result.Tagged++; }
                catch (Exception ex) { Log.Warn($"Could not tag '{entry.PrefabName}' instance {instanceIndex}: {ex.Message}"); }
            }

            if (entry.DeletePhysics) result.PendingPhysicsStrip.Add(instance);

            result.Placed.Add(instance);
        }

        // Combined world-space bounding box across every selected entity - same "min/max across
        // GetGlobalBoundingBox of everything selected" technique as PrefabDistributor's
        // ComputeBottomCenterPivot, just returning the full box instead of collapsing it to one
        // pivot point.
        private static bool TryComputeFootprint(List<GameEntity> selection, out Vec3 min, out Vec3 max)
        {
            min = default; max = default;
            if (selection == null || selection.Count == 0) return false;

            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            bool any = false;

            foreach (var entity in selection)
            {
                BoundingBox box;
                try { box = entity.GetGlobalBoundingBox(); }
                catch { continue; }

                minX = Math.Min(minX, box.min.x); minY = Math.Min(minY, box.min.y); minZ = Math.Min(minZ, box.min.z);
                maxX = Math.Max(maxX, box.max.x); maxY = Math.Max(maxY, box.max.y); maxZ = Math.Max(maxZ, box.max.z);
                any = true;
            }

            if (!any) return false;
            min = new Vec3(minX, minY, minZ, 0f);
            max = new Vec3(maxX, maxY, maxZ, 0f);
            return true;
        }

        // Uniform-random XY within [min,max], cast from above the footprint's own highest point
        // (not a fixed offset from one reference entity, since the selected area's own height can
        // vary a lot more than a single pile's small radius would). Retries up to
        // AreaMaxAttemptsPerInstance times before the caller treats this instance as a miss.
        private static bool TryResolveAreaFrame(Scene scene, Vec3 min, Vec3 max, bool snap, Random rng, out MatrixFrame frame)
        {
            var baseRotation = MatrixFrame.Identity.rotation;

            for (int attempt = 0; attempt < AreaMaxAttemptsPerInstance; attempt++)
            {
                var x = (float)(min.x + rng.NextDouble() * (max.x - min.x));
                var y = (float)(min.y + rng.NextDouble() * (max.y - min.y));
                var spinDegrees = (float)(rng.NextDouble() * 360.0);
                var spunRotation = SpinAroundWorldUp(baseRotation, spinDegrees);

                if (!snap)
                {
                    frame = new MatrixFrame(spunRotation, new Vec3(x, y, (min.z + max.z) / 2f, 0f));
                    return true;
                }

                var castOrigin = new Vec3(x, y, max.z + 5f, 0f);
                if (TryCast(scene, castOrigin, new Vec3(0f, 0f, -1f, 0f), MaxCastDistance, out var hitPoint))
                {
                    var normal = EstimateNormal(scene, hitPoint) ?? new Vec3(0f, 0f, 1f, 0f);
                    frame = new MatrixFrame(AlignUpToNormal(spunRotation, normal), hitPoint);
                    return true;
                }
            }

            frame = default;
            return false;
        }

        // World-flat XY jitter around the reference point, raycast straight down from well above
        // it, settle onto whatever's hit (terrain, or an already-placed lower layer of this same
        // pile) with a random spin and the surface normal applied. Falls back to a flat placement
        // if nothing is hit within range, rather than failing the whole entry - reasonable for a
        // small single-point pile with one known reference height, unlike the whole-area case.
        private static MatrixFrame ResolveSnappedFrame(Scene scene, MatrixFrame referenceFrame, float scatterRadius, Random rng)
        {
            var scatterPoint = JitterXY(referenceFrame.origin, scatterRadius, rng);
            var castOrigin = scatterPoint + new Vec3(0f, 0f, MaxCastDistance * 0.25f, 0f);
            var spinDegrees = (float)(rng.NextDouble() * 360.0);
            var spunRotation = SpinAroundWorldUp(referenceFrame.rotation, spinDegrees);

            if (!TryCast(scene, castOrigin, new Vec3(0f, 0f, -1f, 0f), MaxCastDistance, out var hitPoint))
                return new MatrixFrame(spunRotation, new Vec3(scatterPoint.x, scatterPoint.y, referenceFrame.origin.z, 0f));

            var normal = EstimateNormal(scene, hitPoint) ?? new Vec3(0f, 0f, 1f, 0f);
            return new MatrixFrame(AlignUpToNormal(spunRotation, normal), hitPoint);
        }

        private static MatrixFrame ResolveFlatFrame(MatrixFrame referenceFrame, float scatterRadius, Random rng)
        {
            var scatterPoint = JitterXY(referenceFrame.origin, scatterRadius, rng);
            var spinDegrees = (float)(rng.NextDouble() * 360.0);
            var spunRotation = SpinAroundWorldUp(referenceFrame.rotation, spinDegrees);
            return new MatrixFrame(spunRotation, new Vec3(scatterPoint.x, scatterPoint.y, referenceFrame.origin.z, 0f));
        }

        private static Vec3 JitterXY(Vec3 center, float radius, Random rng)
        {
            var angle = (float)(rng.NextDouble() * 2.0 * Math.PI);
            var dist = (float)rng.NextDouble() * radius;
            return center + new Vec3((float)Math.Cos(angle) * dist, (float)Math.Sin(angle) * dist, 0f, 0f);
        }

        private static bool TryCast(Scene scene, Vec3 origin, Vec3 direction, float maxDistance, out Vec3 hitPoint)
        {
            hitPoint = default;
            var rayBegin = origin - direction * 0.05f;
            var rayEnd = origin + direction * maxDistance;
            return scene.RayCastForClosestEntityOrTerrain(rayBegin, rayEnd, out float distance, out hitPoint, out WeakGameEntity hitEntity, 0.02f);
        }

        // Three-point normal estimate (primary hit plus two nearby samples) - same technique as
        // RaycastPlacement.EstimateNormal in PrefabSwapperTool. Falls back to null (caller uses
        // world-up) if any sample misses.
        private static Vec3? EstimateNormal(Scene scene, Vec3 primaryHit)
        {
            const float sampleOffset = 0.15f;
            var dir = new Vec3(0f, 0f, -1f, 0f);
            var tangentA = new Vec3(1f, 0f, 0f, 0f);
            var tangentB = new Vec3(0f, 1f, 0f, 0f);

            if (!TryCast(scene, primaryHit - dir * 10f + tangentA * sampleOffset, dir, 20f, out var hitA)) return null;
            if (!TryCast(scene, primaryHit - dir * 10f + tangentB * sampleOffset, dir, 20f, out var hitB)) return null;

            var edgeA = hitA - primaryHit;
            var edgeB = hitB - primaryHit;
            var normal = Vec3Cross(edgeA, edgeB);
            var nLen = (float)Math.Sqrt(normal.x * normal.x + normal.y * normal.y + normal.z * normal.z);
            if (nLen < 0.0001f) return null;
            normal = normal / nLen;

            if (Vec3.DotProduct(normal, dir) > 0f) normal = normal * -1f;
            return normal;
        }

        private static Vec3 Vec3Cross(Vec3 a, Vec3 b) => new Vec3(
            a.y * b.z - a.z * b.y,
            a.z * b.x - a.x * b.z,
            a.x * b.y - a.y * b.x,
            0f);

        // Rebuilds the rotation so local-up matches the given surface normal, preserving as much of
        // the original forward-facing direction as possible - same approach as RaycastPlacement/
        // PrefabDistributor's own mirror/rotate math elsewhere in these mods.
        //
        // originalRotation's own basis vectors encode both orientation and per-axis scale in this
        // engine (GetLocalScale() derives scale from their magnitude, confirmed by RaycastPlacement's
        // sibling fix after live scale-reset was reported on Snap to Surface) - each axis's original
        // magnitude is captured up front and reapplied to the freshly-oriented unit vectors, instead
        // of building the result purely from unit vectors and silently collapsing scale to (1,1,1).
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
                projectedForward = originalRotation.s - up * Vec3.DotProduct(originalRotation.s, up);
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

        private static string SafeTag(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "untitled";
            var cleaned = new System.Text.StringBuilder();
            foreach (var c in name.Trim())
                cleaned.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
            return cleaned.Length == 0 ? "untitled" : cleaned.ToString();
        }
    }
}
