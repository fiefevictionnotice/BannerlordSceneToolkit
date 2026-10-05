using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using BannerlordSceneToolkit;

namespace PrefabSwapperTool.Core
{
    // "Prefab Swapper and Distribution"'s distribution half: place N copies of one prefab either
    // along a native scene Path (TaleWorlds.Engine.Path - the same spline object the editor's own
    // Path tool authors, confirmed live via Scene.GetPathWithName/GetPathsWithNamePrefix) or in a
    // rectangular grid, in either of two spacing modes:
    //
    //   Absolute - spacing is a literal world-unit value the caller supplies, independent of the
    //   prefab's own size.
    //
    //   Relative - spacing is measured directly off the FIRST placed instance's own
    //   GetGlobalBoundingBox() (full hierarchy - itself plus every child, not just its own body),
    //   so items tile edge-to-edge based on their actual authored size - swap in a differently-sized
    //   prefab and the spacing follows automatically, matching how LivePrefabSwapper already leaves
    //   a new prefab's own scale untouched rather than stretching it to match whatever it replaced.
    //   Was GetLocalBoundingBox() (entity's own body only) until a real bug report: composite
    //   prefabs (columns, greebles - a parent anchor with the visible mesh on child entities) came
    //   back with a near-zero local box, collapsing measured spacing to ~0.01 units and piling every
    //   instance on top of the last one - see MeasureExtentAlongDirection.
    //
    // Axis convention (grid): a MatrixFrame's rotation basis is read as .s (side/right) = world
    // direction of the entity's own local X, .u (up) = local Z, .f (forward) = local Y - the
    // standard TaleWorlds convention already confirmed for path tangents (.f) elsewhere in this
    // codebase. Grid Axis1 (the "columns"/horizontal run) is measured along local X via .s; Axis2
    // (the "rows"/vertical stacking, e.g. wall courses) along local Z via .u. This is a reasonable
    // default for architectural props (width=X, height=Z) but is a convention, not a guarantee -
    // callers should sanity-check the first couple of placements before committing to a big run,
    // same as any first use of a new prefab's authored axes.
    //
    // Path distribution measures relative spacing along local Y (.f/forward) instead, since that's
    // the axis a path-instantiated frame's rotation aligns with the direction of travel.
    //
    // Every placement is parented under one freshly created, renamed empty anchor (GameEntity.
    // CreateEmpty + AddChild) - the same pattern Combine at Shared Origin and Mode 1/2 already use
    // in PrefabCreatorTool, so a whole distributed run can be re-selected, moved, or deleted as one
    // unit instead of leaving loose siblings behind.
    public static class PrefabDistributor
    {
        public const string ReselectTagPrefix = "distributed_";

        // Where the wrapping anchor ends up, independent of where each individual piece is placed:
        //
        //   ExactOriginPoint - the anchor sits exactly at the reference frame used to place things
        //   (the path's start frame, or the selected origin entity's own frame) - what this tool
        //   always did before this option existed.
        //
        //   BottomCenterOfGroup - the anchor is repositioned to the combined group's own
        //   bottom-center pivot (X/Y centered across every placed piece's world bounding box, Z at
        //   the lowest point) - same convention as PrefabCreatorTool's "New Prefab" anchor, right
        //   down to using identity rotation rather than inheriting the reference frame's facing,
        //   since this point is meant as a neutral prefab origin, not tied to any one piece. This
        //   is what you want if you're about to save the result as a reusable prefab and want its
        //   own internal origin sitting sensibly under the middle of the geometry instead of
        //   wherever the reference entity/path happened to be.
        //
        // Every piece is placed at its final WORLD frame first, independent of the anchor - the
        // anchor is created and positioned only afterward, then every already-placed piece is
        // AddChild'd onto it (autoLocalizeFrame recomputes each one's local offset from the
        // anchor's actual final frame, so nothing visually moves regardless of which mode is used).
        public enum AnchorMode { ExactOriginPoint, BottomCenterOfGroup }

        public class DistributeResult
        {
            public bool Success;
            public string Error;
            public GameEntity AnchorEntity;
            public List<GameEntity> Placed = new List<GameEntity>();
            public List<string> Failed = new List<string>();
            public bool PathSpacingWasShrunkToFit;
            public float PathSpacingUsed;
            // Fill-surfaces mode only: the grid dimensions that were derived from the target
            // surfaces' combined footprint, so status messages can report what was decided.
            public int DerivedCount1;
            public int DerivedCount2;
            // How many of Placed are auto-placed secondaries (PrefabCreatorTool pairing/family),
            // not requested base pieces - kept separate so "placed N of <requested count>" status
            // messages compare against base pieces only, not a count inflated by secondaries riding
            // along on each one.
            public int AutoPlacedSecondaryCount;
        }

        public static DistributeResult DistributeAlongPath(
            Scene scene, Path path, string prefabName, int requestedCount,
            bool relativeSpacing, float absoluteSpacing, string anchorName, AnchorMode anchorMode,
            float extraZRotationDegrees = 0f, bool fillToPathLength = false)
        {
            var result = new DistributeResult();
            if (scene == null || path == null || string.IsNullOrWhiteSpace(prefabName))
            {
                result.Error = "Missing scene, path, or prefab name.";
                return result;
            }
            if (!fillToPathLength && requestedCount < 1) { result.Error = "Count must be at least 1."; return result; }

            Log.Info($"[NativeTrace] ENTER PrefabDistributor.DistributeAlongPath prefab='{prefabName}' requestedCount={requestedCount} relative={relativeSpacing} fillToPathLength={fillToPathLength}");

            float totalLength;
            try { totalLength = path.GetTotalLength(); }
            catch (Exception ex) { result.Error = "Failed to read path length: " + ex.Message; return result; }

            MatrixFrame startFrame;
            try { startFrame = path.GetFrameForDistance(0f); }
            catch (Exception ex) { result.Error = "Failed to sample path start: " + ex.Message; return result; }

            var placed = new List<GameEntity>();
            float spacing = absoluteSpacing;

            // The first instance is always placed up front, before count is even decided - both
            // modes need its measured size first: fixed-count needs it to know whether later
            // instances would run off the end of the path, and fill-to-length needs it to know how
            // many instances actually fit.
            var firstFrame = startFrame;
            if (extraZRotationDegrees != 0f)
                firstFrame = new MatrixFrame(RotateAboutUp(startFrame.rotation, extraZRotationDegrees), startFrame.origin);

            Log.Info($"[NativeTrace] Instantiate '{prefabName}' (path instance 0)");
            GameEntity first;
            try { first = GameEntity.Instantiate(scene, prefabName.Trim(), firstFrame, true); }
            catch (Exception ex) { result.Error = "Failed to place the first instance: " + ex.Message; return result; }
            if (first == null) { result.Error = "Instantiate returned null for the first instance (unknown prefab name?)."; return result; }

            if (relativeSpacing)
            {
                // Measured along the path frame's own forward direction (the axis its rotation
                // aligns with travel) - via the entity's FULL hierarchy bounding box, same as Grid's
                // MeasureExtentAlongDirection and for the same reason: a composite prefab's root
                // alone (GetLocalBoundingBox) is often near-zero, which used to collapse spacing to
                // ~0.01 units and pile every instance on top of the last one.
                // The typed box is now added as a GAP on top of the measured extent, instead of
                // being ignored outright. Ignoring it meant "1" and "15" produced identical
                // spacing, which reads as broken however honestly the label admits it - and
                // DistributeInGrid has always used extent + Gap per axis, so path mode was the
                // odd one out. A gap of 0 reproduces the old edge-to-edge behaviour exactly.
                try
                {
                    var extent = Math.Max(0.01f, MeasureExtentAlongDirection(first, firstFrame.rotation.f));
                    spacing = Math.Max(0.01f, extent + absoluteSpacing);
                    Log.Info($"[Distribute] relative spacing: extent={extent:0.###} + gap={absoluteSpacing:0.###} = {spacing:0.###}");
                }
                catch { spacing = Math.Max(1f, absoluteSpacing); }
            }

            int count;
            if (fillToPathLength)
            {
                count = spacing > 0.0001f ? Math.Max(1, (int)Math.Floor(totalLength / spacing) + 1) : 1;
            }
            else
            {
                count = requestedCount;
                // Without this, spacing measured (or typed) larger than the path can actually fit
                // makes every instance past the overflow point clamp onto the exact same final
                // frame - looking like the tool "does one relative distance and then stops
                // adjusting" instead of spacing every instance out. Shrinking to fit guarantees
                // every requested instance lands at a distinct point along the path.
                if (count > 1 && spacing * (count - 1) > totalLength)
                {
                    spacing = totalLength / (count - 1);
                    result.PathSpacingWasShrunkToFit = true;
                }
            }

            result.PathSpacingUsed = spacing;
            placed.Add(first);
            AutoPlaceSecondaries(scene, prefabName, firstFrame, placed, result);

            for (int i = 1; i < count; i++)
            {
                float distance = Math.Min(i * spacing, totalLength);

                MatrixFrame frame;
                try { frame = path.GetFrameForDistance(distance); }
                catch (Exception ex) { result.Failed.Add($"instance {i}: failed to sample path at {distance}: {ex.Message}"); continue; }

                if (extraZRotationDegrees != 0f)
                    frame = new MatrixFrame(RotateAboutUp(frame.rotation, extraZRotationDegrees), frame.origin);

                Log.Info($"[NativeTrace] Instantiate '{prefabName}' (path instance {i})");
                GameEntity instance;
                try { instance = GameEntity.Instantiate(scene, prefabName.Trim(), frame, true); }
                catch (Exception ex) { result.Failed.Add($"instance {i}: {ex.Message}"); continue; }
                if (instance == null) { result.Failed.Add($"instance {i}: instantiate returned null (unknown prefab name?)"); continue; }

                placed.Add(instance);
                AutoPlaceSecondaries(scene, prefabName, frame, placed, result);
            }

            var anchor = CreateAnchorAndAdopt(scene, anchorMode, startFrame, placed, anchorName, result);
            if (anchor == null) return result;

            result.Success = true;
            Log.Info($"[NativeTrace] EXIT PrefabDistributor.DistributeAlongPath placed={result.Placed.Count} failed={result.Failed.Count}");
            return result;
        }

        // Which direction an axis moves in - Local follows the origin entity's own rotation (so
        // rotating that entity first angles the whole grid to match, e.g., a wall's actual angle);
        // World is always the fixed world axis regardless of how the origin entity is rotated.
        public enum DistributionAxis { LocalX, LocalY, LocalZ, WorldX, WorldY, WorldZ }

        // Local axes are returned UNIT-LENGTH (2026-08-23, "it's still not doing local
        // correctly"): this engine stores SCALE as the basis vectors' lengths, so returning the
        // raw basis handed callers a direction whose length was the origin entity's scale -
        // every cell offset (dir * i * spacing) then silently multiplied by that scale, and
        // Flatten deliberately preserves length, so it rode straight through. World axes were
        // unit all along, which is exactly why only Local misbehaved. A direction is a
        // direction; magnitude belongs to spacing alone.
        public static Vec3 ResolveAxisDirection(DistributionAxis axis, MatrixFrame originFrame)
        {
            Vec3 Unit(Vec3 v)
            {
                var length = Length(v);
                return length < 0.0001f ? v : v / length;
            }
            switch (axis)
            {
                case DistributionAxis.LocalX: return Unit(originFrame.rotation.s);
                case DistributionAxis.LocalY: return Unit(originFrame.rotation.f);
                case DistributionAxis.LocalZ: return Unit(originFrame.rotation.u);
                case DistributionAxis.WorldX: return new Vec3(1f, 0f, 0f, 0f);
                case DistributionAxis.WorldY: return new Vec3(0f, 1f, 0f, 0f);
                case DistributionAxis.WorldZ: return new Vec3(0f, 0f, 1f, 0f);
                default: return Unit(originFrame.rotation.s);
            }
        }

        public class GridAxisSpec
        {
            public Vec3 Direction;
            public int Count;
            public bool Relative; // true: spacing auto-measured from the first placed instance's own size. false: Absolute is used directly.
            public float Absolute;
            public float Gap; // always added on top of whichever base (measured or absolute) is used - the "breathing room" control.
        }

        public static DistributeResult DistributeInGrid(
            Scene scene, MatrixFrame originFrame, string prefabName,
            GridAxisSpec axis1, GridAxisSpec axis2, string anchorName, AnchorMode anchorMode)
        {
            var result = new DistributeResult();
            if (scene == null || string.IsNullOrWhiteSpace(prefabName))
            {
                result.Error = "Missing scene or prefab name.";
                return result;
            }
            if (axis1.Count < 1 || axis2.Count < 1) { result.Error = "Both counts must be at least 1."; return result; }

            Log.Info($"[NativeTrace] ENTER PrefabDistributor.DistributeInGrid prefab='{prefabName}' axis1Count={axis1.Count} axis2Count={axis2.Count}");

            // rotation.s/.f/.u (Local*) and the hand-built unit vectors (World*) in
            // ResolveAxisDirection are already unit-length - no normalization needed here.
            var dir1 = axis1.Direction;
            var dir2 = axis2.Direction;

            // Diagnostic for a reported bug: rotating the origin entity correctly rotates each
            // placed instance's own facing, but the grid's spread direction reportedly stays
            // world-aligned regardless. The code path here reads dir1/dir2 from the origin's own
            // rotation.s/.u for Local axes, which should already reflect any rotation - if that's
            // true, these logged vectors will show clearly non-axis-aligned components (e.g. both
            // X and Y nonzero) whenever the origin is rotated and Local is selected. If they come
            // out looking like a plain (1,0,0)/(0,0,1) even on a visibly rotated origin, the bug is
            // upstream of this method (ResolveAxisDirection or the frame read), not in the offset
            // math below.
            Log.Info($"[NativeTrace] DistributeInGrid dir1=({dir1.x:F4},{dir1.y:F4},{dir1.z:F4}) dir2=({dir2.x:F4},{dir2.y:F4},{dir2.z:F4}) " +
                     $"originFrame.rotation.s=({originFrame.rotation.s.x:F4},{originFrame.rotation.s.y:F4},{originFrame.rotation.s.z:F4}) " +
                     $"originFrame.rotation.f=({originFrame.rotation.f.x:F4},{originFrame.rotation.f.y:F4},{originFrame.rotation.f.z:F4}) " +
                     $"originFrame.rotation.u=({originFrame.rotation.u.x:F4},{originFrame.rotation.u.y:F4},{originFrame.rotation.u.z:F4})");

            float spacing1 = axis1.Absolute + axis1.Gap;
            float spacing2 = axis2.Absolute + axis2.Gap;
            bool measured = false;
            var placed = new List<GameEntity>();

            for (int i = 0; i < axis1.Count; i++)
            {
                for (int j = 0; j < axis2.Count; j++)
                {
                    var offset = dir1 * (i * (measured || !axis1.Relative ? spacing1 : 0f))
                               + dir2 * (j * (measured || !axis2.Relative ? spacing2 : 0f));
                    var frame = originFrame;
                    frame.origin = originFrame.origin + offset;

                    Log.Info($"[NativeTrace] Instantiate '{prefabName}' (grid instance {i},{j})");
                    GameEntity instance;
                    try { instance = GameEntity.Instantiate(scene, prefabName.Trim(), frame, true); }
                    catch (Exception ex) { result.Failed.Add($"instance ({i},{j}): {ex.Message}"); continue; }
                    if (instance == null) { result.Failed.Add($"instance ({i},{j}): instantiate returned null (unknown prefab name?)"); continue; }

                    if (!measured && (axis1.Relative || axis2.Relative))
                    {
                        // Projects the FIRST placed instance's own oriented bounding box onto each
                        // chosen axis direction, whatever it is (local or world) - not just its raw
                        // local X/Z extents - so relative spacing is correct regardless of which
                        // axis was picked or how the origin entity is rotated.
                        try
                        {
                            if (axis1.Relative) spacing1 = Math.Max(0.01f, MeasureExtentAlongDirection(instance, dir1)) + axis1.Gap;
                            if (axis2.Relative) spacing2 = Math.Max(0.01f, MeasureExtentAlongDirection(instance, dir2)) + axis2.Gap;
                        }
                        catch { /* keep the absolute+gap values already set as a fallback */ }
                        measured = true;

                        // The very first (0,0) instance was placed with zero offset regardless of
                        // spacing, so no re-placement is needed for it even now that spacing's known.
                    }

                    placed.Add(instance);
                    AutoPlaceSecondaries(scene, prefabName, frame, placed, result);
                }
            }

            var anchor = CreateAnchorAndAdopt(scene, anchorMode, originFrame, placed, anchorName, result);
            if (anchor == null) return result;

            result.Success = true;
            Log.Info($"[NativeTrace] EXIT PrefabDistributor.DistributeInGrid placed={result.Placed.Count} failed={result.Failed.Count}");
            return result;
        }

        // PATH FROM A SELECTION rather than from a prefab name (v0.7).
        //
        // The by-name path distributor never needed a selection - the PATH supplies both position
        // and facing, so "type what to place" was a complete instruction on its own, which is why
        // this was the last distributor to gain a selection source. The reasons to add it are the
        // same three as the grid's, and the third is strongest here: a multi-entity assembly (a
        // wall with its buttress and torch) repeated along a wall path is exactly what by-name
        // distribution cannot do, since it can only ever place one prefab.
        //
        // THE DIFFERENCE FROM THE GRID, and it is real rather than a copy: a grid steps in fixed
        // world directions, so each piece's offset from the group can be applied unchanged. A path
        // TURNS. Offsets are therefore taken in the FIRST source's local space (TransformToLocal)
        // and re-applied in each path frame's space (TransformToParent), so the assembly rotates
        // to follow the curve and keeps its internal arrangement - instead of every copy staying
        // world-aligned and the group visibly shearing apart on a bend.
        public static DistributeResult DistributeSelectionAlongPath(
            Scene scene, List<GameEntity> sources, Path path, int requestedCount,
            bool relativeSpacing, float absoluteSpacing, string anchorName, AnchorMode anchorMode,
            float extraZRotationDegrees = 0f, bool fillToPathLength = false)
        {
            var result = new DistributeResult();
            if (scene == null || path == null) { result.Error = "Missing scene or path."; return result; }

            var live = (sources ?? new List<GameEntity>()).Where(EntitySelector.IsValidEntity).ToList();
            if (live.Count == 0) { result.Error = "Nothing selected to distribute."; return result; }
            if (!fillToPathLength && requestedCount < 1) { result.Error = "Count must be at least 1."; return result; }

            float totalLength;
            try { totalLength = path.GetTotalLength(); }
            catch (Exception ex) { result.Error = "Failed to read path length: " + ex.Message; return result; }

            MatrixFrame startFrame;
            try { startFrame = path.GetFrameForDistance(0f); }
            catch (Exception ex) { result.Error = "Failed to sample path start: " + ex.Message; return result; }

            Log.Info($"[NativeTrace] ENTER PrefabDistributor.DistributeSelectionAlongPath sources={live.Count} " +
                     $"requestedCount={requestedCount} relative={relativeSpacing} fillToPathLength={fillToPathLength}");

            // The group's own arrangement, relative to its first member. Captured once, before
            // anything is placed, so later copies cannot drift as the scene changes under us.
            var reference = live[0].GetGlobalFrame();
            var localFrames = new List<MatrixFrame>(live.Count);
            foreach (var source in live)
            {
                var world = source.GetGlobalFrame();
                localFrames.Add(reference.TransformToLocal(world));
            }

            // Measured along the path frame's forward axis - the direction of travel, the same
            // axis the by-name path distributor measures on and for the same reason.
            float spacing = absoluteSpacing;
            if (relativeSpacing)
            {
                try
                {
                    var extent = Math.Max(0.01f, MeasureGroupExtentAlongDirection(live, startFrame.rotation.f));
                    spacing = Math.Max(0.01f, extent + absoluteSpacing);
                    Log.Info($"[Distribute] selection path spacing: extent={extent:0.###} + gap={absoluteSpacing:0.###} = {spacing:0.###}");
                }
                catch { spacing = Math.Max(1f, absoluteSpacing); }
            }

            int count;
            if (fillToPathLength)
            {
                count = spacing > 0.0001f ? Math.Max(1, (int)Math.Floor(totalLength / spacing) + 1) : 1;
            }
            else
            {
                count = requestedCount;
                if (count > 1 && spacing * (count - 1) > totalLength)
                {
                    spacing = totalLength / (count - 1);
                    result.PathSpacingWasShrunkToFit = true;
                }
            }
            result.PathSpacingUsed = spacing;

            var placed = new List<GameEntity>();

            // Starts at 1, not 0: the originals already sit at the path's start, the same
            // convention as the selection grid leaving cell (0,0) alone. A copy placed on top of
            // them would read as nothing having happened.
            for (int i = 1; i < count; i++)
            {
                float distance = Math.Min(i * spacing, totalLength);

                MatrixFrame pathFrame;
                try { pathFrame = path.GetFrameForDistance(distance); }
                catch (Exception ex) { result.Failed.Add($"instance {i}: failed to sample path at {distance}: {ex.Message}"); continue; }

                if (extraZRotationDegrees != 0f)
                    pathFrame = new MatrixFrame(RotateAboutUp(pathFrame.rotation, extraZRotationDegrees), pathFrame.origin);

                for (int s = 0; s < live.Count; s++)
                {
                    if (!EntitySelector.IsValidEntity(live[s])) continue;

                    var local = localFrames[s];
                    var frame = pathFrame.TransformToParent(local);   // the group, rotated onto the path

                    var copy = CloneSourceAt(scene, live[s], frame, out var error);
                    if (copy == null) { result.Failed.Add($"instance {i} '{live[s].Name}': {error}"); continue; }
                    placed.Add(copy);
                }
            }

            if (placed.Count == 0)
            {
                result.Error = result.Failed.Count > 0
                    ? "Nothing could be copied. " + string.Join("; ", result.Failed.Take(3))
                    : "Nothing to place - a count of 1 is just the selection you already have.";
                return result;
            }

            var anchor = CreateAnchorAndAdopt(scene, anchorMode, startFrame, placed, anchorName, result);
            if (anchor == null) return result;

            result.Success = true;
            Log.Info($"[NativeTrace] EXIT PrefabDistributor.DistributeSelectionAlongPath placed={result.Placed.Count} failed={result.Failed.Count}");
            return result;
        }

        // GRID FROM A SELECTION rather than from a prefab name (v0.7).
        //
        // Same grid maths as DistributeInGrid; what changes is what lands in each cell. Instead of
        // Instantiate(prefabName), each cell gets a CLONE of what you already have selected. Three
        // things that buys, none of which the by-name path can do:
        //
        //   1. It works on entities with NO saved prefab at all - a hand-assembled composite you
        //      have not turned into a prefab yet is exactly the thing you most want to tile, and
        //      Instantiate has no name to resolve for it.
        //   2. The copies keep their per-instance material and colour overrides. Instantiate
        //      rebuilds from the PREFAB, so a recoloured wall segment would tile as the bare
        //      original - the same trap that made mirrored copies revert until v0.7.
        //   3. A MULTI-ENTITY selection tiles as a unit, each piece keeping its offset from the
        //      others, so a small assembly (wall + buttress + torch) repeats as one arrangement.
        //
        // CELL (0,0) IS DELIBERATELY LEFT EMPTY: the selection itself already occupies it. Filling
        // it would stack an exact duplicate on top of the original, which reads as nothing having
        // happened until you move something and find two. The originals are also left un-parented
        // - the anchor adopts only the copies, so this never reorganises entities you did not ask
        // it to touch.
        //
        // Auto-placed pairing secondaries are skipped here on purpose: they are keyed by a real
        // prefab/family name, which a live clone need not have. Same call as
        // LivePrefabSwapper's CopyFrom path, for the same reason.
        public static DistributeResult DistributeSelectionInGrid(
            Scene scene, List<GameEntity> sources, MatrixFrame originFrame,
            GridAxisSpec axis1, GridAxisSpec axis2, string anchorName, AnchorMode anchorMode)
        {
            var result = new DistributeResult();
            if (scene == null || sources == null || sources.Count == 0)
            {
                result.Error = "Nothing selected to distribute.";
                return result;
            }
            if (axis1.Count < 1 || axis2.Count < 1) { result.Error = "Both counts must be at least 1."; return result; }

            var live = sources.Where(EntitySelector.IsValidEntity).ToList();
            if (live.Count == 0) { result.Error = "The selected entities are no longer valid."; return result; }

            Log.Info($"[NativeTrace] ENTER PrefabDistributor.DistributeSelectionInGrid sources={live.Count} " +
                     $"axis1Count={axis1.Count} axis2Count={axis2.Count}");

            var dir1 = axis1.Direction;
            var dir2 = axis2.Direction;

            // Measured off the SOURCES, which are already in the scene - no need to place a first
            // instance to measure like the by-name path does. The combined box of the whole
            // selection is what spaces correctly when several entities tile as one unit.
            float spacing1 = axis1.Absolute + axis1.Gap;
            float spacing2 = axis2.Absolute + axis2.Gap;
            try
            {
                if (axis1.Relative) spacing1 = Math.Max(0.01f, MeasureGroupExtentAlongDirection(live, dir1)) + axis1.Gap;
                if (axis2.Relative) spacing2 = Math.Max(0.01f, MeasureGroupExtentAlongDirection(live, dir2)) + axis2.Gap;
            }
            catch { /* keep absolute+gap as the fallback */ }

            Log.Info($"[Distribute] selection grid spacing: axis1={spacing1:0.###} axis2={spacing2:0.###}");

            var placed = new List<GameEntity>();
            for (int i = 0; i < axis1.Count; i++)
            {
                for (int j = 0; j < axis2.Count; j++)
                {
                    if (i == 0 && j == 0) continue;   // the originals already fill this cell

                    var offset = dir1 * (i * spacing1) + dir2 * (j * spacing2);

                    foreach (var source in live)
                    {
                        if (!EntitySelector.IsValidEntity(source)) continue;

                        // Each source keeps its own world frame plus the cell offset, so the
                        // group's internal arrangement (and each piece's own rotation) survives.
                        var frame = source.GetGlobalFrame();
                        frame.origin = frame.origin + offset;

                        var copy = CloneSourceAt(scene, source, frame, out var error);
                        if (copy == null) { result.Failed.Add($"cell ({i},{j}) '{source.Name}': {error}"); continue; }
                        placed.Add(copy);
                    }
                }
            }

            if (placed.Count == 0)
            {
                result.Error = result.Failed.Count > 0
                    ? "Nothing could be copied. " + string.Join("; ", result.Failed.Take(3))
                    : "Nothing to place - a 1x1 grid is just the selection you already have.";
                return result;
            }

            var anchor = CreateAnchorAndAdopt(scene, anchorMode, originFrame, placed, anchorName, result);
            if (anchor == null) return result;

            result.Success = true;
            Log.Info($"[NativeTrace] EXIT PrefabDistributor.DistributeSelectionInGrid placed={result.Placed.Count} failed={result.Failed.Count}");
            return result;
        }

        // GRID DROPPED ONTO THE SURFACE BENEATH IT (v0.7).
        //
        // The same uniform grid as the two methods above, but every cell is raycast straight down
        // onto whatever is under it instead of sitting on the origin's flat plane. That is the
        // difference between a grid of columns that works on a hillside or a stepped terrace and
        // one that only works on a perfectly flat floor. Pile Generator has done this for years -
        // it is the same technique, minus the deliberate randomness: no jitter, no random spin,
        // uniform spacing (see SurfaceSnap, which is a verbatim port of the working implementation).
        //
        // WHY THE STEP DIRECTIONS ARE FLATTENED. dir1/dir2 come from ResolveAxisDirection, so with
        // Local axes selected they follow the origin entity's own rotation - that is what makes
        // "a grid aligned to this rotated building" work, and it already did before this method
        // existed. But if the origin entity is TILTED, its local axes point partly upward, and
        // stepping along them would change height on its own - which then fights the raycast that
        // is already supplying height, compressing the spacing you actually see in plan view. So
        // the step directions are projected flat here: the grid's PATTERN still follows the
        // entity's heading, while its HEIGHT comes entirely from the surface. A purely yawed
        // entity (the normal case) is unaffected, since its local X/Y are already horizontal.
        //
        // alignToSurface tilts each piece to sit flush with the ground under it. Off by default
        // and worth leaving off for anything that should stand upright: a column on a 1-in-10
        // slope should be vertical and simply taller on one side, not leaning.
        public static DistributeResult DistributeOntoSurface(
            Scene scene, string prefabName, List<GameEntity> sources, MatrixFrame originFrame,
            GridAxisSpec axis1, GridAxisSpec axis2, bool alignToSurface,
            string surfaceTargetNames, string anchorName, AnchorMode anchorMode,
            float entityRotationDegrees = 0f, float gridRotationDegrees = 0f)
        {
            var result = new DistributeResult();
            bool fromSelection = string.IsNullOrWhiteSpace(prefabName);

            if (scene == null) { result.Error = "No scene."; return result; }
            if (axis1.Count < 1 || axis2.Count < 1) { result.Error = "Both counts must be at least 1."; return result; }

            var live = (sources ?? new List<GameEntity>()).Where(EntitySelector.IsValidEntity).ToList();
            if (fromSelection && live.Count == 0) { result.Error = "Nothing selected to distribute."; return result; }

            // The origin's up vector is logged because a tilted origin is exactly the thing that
            // made "keep upright" look broken - (0,0,1) here means the origin itself is upright.
            Log.Info($"[NativeTrace] ENTER PrefabDistributor.DistributeOntoSurface " +
                     $"{(fromSelection ? $"sources={live.Count}" : $"prefab='{prefabName}'")} " +
                     $"axis1Count={axis1.Count} axis2Count={axis2.Count} alignToSurface={alignToSurface} " +
                     $"originUp=({originFrame.rotation.u.x:F3},{originFrame.rotation.u.y:F3},{originFrame.rotation.u.z:F3})");

            var dir1 = Flatten(axis1.Direction);
            var dir2 = Flatten(axis2.Direction);

            // ROTATION (GRID) - rotates the LATTICE itself (2026-08-23, "Extra heading only
            // rotates it after placement, it doesn't allow a non-world-aligned orientation of
            // the grid itself"): both step directions turn about world Z together, so the
            // whole cell pattern pivots around the origin. Independent of Rotation (Entity)
            // below, which spins each placed piece in place.
            if (Math.Abs(gridRotationDegrees) > 0.01f)
            {
                var gRad = gridRotationDegrees * (float)Math.PI / 180f;
                float gCos = (float)Math.Cos(gRad), gSin = (float)Math.Sin(gRad);
                Vec3 GridRotZ(Vec3 v) => new Vec3(v.x * gCos - v.y * gSin, v.x * gSin + v.y * gCos, v.z, 0f);
                dir1 = GridRotZ(dir1);
                dir2 = GridRotZ(dir2);
            }
            // An axis with a count of 1 never steps, so it doesn't need a usable direction. The
            // default Axis2 (Local Z, right for stacking wall courses in a flat grid) used to fail
            // this check even at count 1 - a single-row surface run errored out of the box, which
            // read as the whole feature being broken.
            if ((axis1.Count > 1 && Length(dir1) < 0.0001f) || (axis2.Count > 1 && Length(dir2) < 0.0001f))
            {
                result.Error = "A grid axis with a count above 1 points straight up - a grid dropped onto " +
                               "the ground can only spread horizontally, so pick X or Y for that axis.";
                return result;
            }

            // Relative spacing, selection mode: the sources exist up front, measure them now.
            // BY-NAME mode measures from the FIRST PLACED INSTANCE instead (below, exactly like
            // DistributeInGrid) - measuring from `live` here was the 2026-08-23 "Relative no
            // longer works with distribute onto surface" bug: in by-name mode `live` is empty
            // (or holds the ORIGIN entity, i.e. the surface), so FirstOrDefault() was null, the
            // measure threw, and the catch silently kept Absolute+Gap - which in Relative mode
            // is just the gap, piling every instance onto the same cell.
            float spacing1 = axis1.Absolute + axis1.Gap;
            float spacing2 = axis2.Absolute + axis2.Gap;
            if (fromSelection)
            {
                try
                {
                    if (axis1.Relative)
                        spacing1 = Math.Max(0.01f, MeasureGroupExtentAlongDirection(live, dir1)) + axis1.Gap;
                    if (axis2.Relative)
                        spacing2 = Math.Max(0.01f, MeasureGroupExtentAlongDirection(live, dir2)) + axis2.Gap;
                }
                catch { /* keep absolute+gap */ }
            }
            bool measuredFromInstance = false;

            var placed = new List<GameEntity>();
            int missed = 0;

            // CONFIRMED REPORT, fixed 2026-08-22: by-name placement BORROWS its heading rotation
            // from another entity (the origin entity here; in fill mode, the first captured
            // TARGET SURFACE) - and this engine encodes scale as the basis vectors' lengths, so a
            // scaled terrace's scale rode along inside the borrowed rotation and every placed
            // prefab inherited it. Borrowing a heading must not borrow a scale: normalized to
            // unit length once here, so instances keep the prefab's own authored scale.
            // AlignUpToNormal preserves lengths, so unit in = unit out. The SELECTION branch is
            // deliberately untouched - there the rotation is each source piece's own, and its
            // scale is real content (the same reason AlignUpToNormal preserves lengths at all).
            var headingRotation = NormalizeBasisLengths(originFrame.rotation);

            // ROTATION (ENTITY) for the by-name branch (2026-08-23, "impossible for the column's
            // rotation to apply because I was using source prefab name ... I have to have the
            // target surfaces selected"): fill mode consumes the selection as targets, so a
            // by-name source had no way to carry its own rotation - the heading always came
            // from the surface. Typed degrees rotate the placed pieces' heading about world Z
            // ON TOP of whatever the surface/origin supplied; 0 = unchanged. Selection-mode
            // sources keep their own authored rotations and ignore this.
            if (Math.Abs(entityRotationDegrees) > 0.01f)
            {
                var rad = entityRotationDegrees * (float)Math.PI / 180f;
                float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
                Vec3 RotZ(Vec3 v) => new Vec3(v.x * cos - v.y * sin, v.x * sin + v.y * cos, v.z, 0f);
                headingRotation = new Mat3(RotZ(headingRotation.s), RotZ(headingRotation.f), RotZ(headingRotation.u));
            }

            for (int i = 0; i < axis1.Count; i++)
            {
                for (int j = 0; j < axis2.Count; j++)
                {
                    // In selection mode the originals already occupy the first cell.
                    if (fromSelection && i == 0 && j == 0) continue;

                    var offset = dir1 * (i * spacing1) + dir2 * (j * spacing2);
                    var cellPoint = originFrame.origin + offset;

                    // May be several comma-separated names - SurfaceSnap parses them; a hit on
                    // ANY of the named entities (or their children) counts as ground.
                    var hit = SurfaceSnap.CastDown(scene, cellPoint, surfaceTargetNames);
                    if (!hit.Found) { missed++; continue; }

                    if (fromSelection)
                    {
                        foreach (var source in live)
                        {
                            if (!EntitySelector.IsValidEntity(source)) continue;

                            var sourceFrame = source.GetGlobalFrame();
                            // Each piece keeps its offset from the group, measured horizontally,
                            // then lands at the surface height found for this cell.
                            var withinGroup = Flatten(sourceFrame.origin - originFrame.origin);
                            // Unlike the by-name branch, upright mode here keeps each source's
                            // rotation AS AUTHORED rather than erecting it: a selection is real
                            // content, and force-verticalizing every piece would straighten the
                            // deliberately-leaning members of an assembly. The by-name branch's
                            // rotation is just a heading borrowed from the origin entity, where
                            // any tilt is accidental - hence the difference.
                            var rotation = alignToSurface
                                ? SurfaceSnap.AlignUpToNormal(sourceFrame.rotation, hit.Normal)
                                : sourceFrame.rotation;
                            var frame = new MatrixFrame(rotation, hit.Point + withinGroup);

                            var copy = CloneSourceAt(scene, source, frame, out var error);
                            if (copy == null) { result.Failed.Add($"cell ({i},{j}) '{source.Name}': {error}"); continue; }
                            placed.Add(copy);
                        }
                        continue;
                    }

                    // CONFIRMED REPORT, fixed 2026-08-22: "keep upright" used to copy
                    // originFrame.rotation VERBATIM, so a tilted origin entity (one placed with
                    // the editor's own align-to-ground, or a piece from a previous tilt-mode run)
                    // tilted every placed instance in BOTH modes - the toggle looked dead. The
                    // toggle state itself arrived correctly (alignToSurface=False in the log), the
                    // tilt rode in on the origin's own frame. Upright mode now actively ERECTS the
                    // rotation: same AlignUpToNormal, but against world up - heading survives,
                    // pitch/roll are zeroed, and an already-upright origin is untouched.
                    // (headingRotation, not originFrame.rotation: see the scale note above.)
                    var prefabRotation = SurfaceSnap.AlignUpToNormal(headingRotation,
                        alignToSurface ? hit.Normal : new Vec3(0f, 0f, 1f, 0f));
                    var prefabFrame = new MatrixFrame(prefabRotation, hit.Point);

                    GameEntity instance;
                    try { instance = GameEntity.Instantiate(scene, prefabName.Trim(), prefabFrame, true); }
                    catch (Exception ex) { result.Failed.Add($"cell ({i},{j}): {ex.Message}"); continue; }
                    if (instance == null) { result.Failed.Add($"cell ({i},{j}): instantiate returned null (unknown prefab name?)"); continue; }

                    // Relative spacing, by-name mode: measured from the FIRST placed instance -
                    // the same pattern DistributeInGrid has always used (the prefab's real size
                    // is only known once one exists). Later cells use the corrected spacing; the
                    // first instance sat at zero offset regardless, so it needs no re-placement.
                    if (!measuredFromInstance && (axis1.Relative || axis2.Relative))
                    {
                        try
                        {
                            // Bounds recomputed before measuring (2026-08-23, rglEntity.h:2068
                            // "!is_bounding_box_dirty()" assert on surface mode): a freshly
                            // Instantiated entity's bounding box can still be dirty on the very
                            // next call, and reading it then trips the engine's own assert.
                            try { instance.RecomputeBoundingBox(); } catch { }
                            if (axis1.Relative) spacing1 = Math.Max(0.01f, MeasureExtentAlongDirection(instance, dir1)) + axis1.Gap;
                            if (axis2.Relative) spacing2 = Math.Max(0.01f, MeasureExtentAlongDirection(instance, dir2)) + axis2.Gap;
                            Log.Info($"[Distribute] surface relative spacing from first instance: s1={spacing1:0.###} s2={spacing2:0.###}");
                        }
                        catch { /* keep absolute+gap */ }
                        measuredFromInstance = true;
                    }

                    placed.Add(instance);
                    AutoPlaceSecondaries(scene, prefabName, prefabFrame, placed, result);
                }
            }

            if (missed > 0)
                result.Failed.Add($"{missed} cell(s) had nothing beneath them and were skipped");

            if (placed.Count == 0)
            {
                result.Error = result.Failed.Count > 0
                    ? "Nothing was placed. " + string.Join("; ", result.Failed.Take(3))
                    : "Nothing was placed.";
                return result;
            }

            var anchor = CreateAnchorAndAdopt(scene, anchorMode, originFrame, placed, anchorName, result);
            if (anchor == null) return result;

            result.Success = true;
            Log.Info($"[NativeTrace] EXIT PrefabDistributor.DistributeOntoSurface placed={result.Placed.Count} missed={missed} failed={result.Failed.Count}");
            return result;
        }

        // FILL TARGET SURFACES: the same drop-onto-surface placement as DistributeOntoSurface,
        // but the grid's FOOTPRINT is derived from the captured target surfaces instead of typed
        // as counts - cells cover the surfaces' combined world bounding box, and the per-cell
        // filtered raycast then trims that rectangle to the surfaces' actual silhouette for free
        // (a cell over an AABB corner that isn't really above a target finds nothing it's allowed
        // to land on and is skipped, exactly like any other missed cell).
        //
        // The footprint comes from ENTITY REFERENCES (the ones captured by Use Selected Entities),
        // not from a scene-wide name lookup: a bounding box is a scene-wide claim in a way a
        // raycast is not, and a same-named entity across the map would silently inflate the box
        // to span both. The NAME filter is still what each cell's ray honours - both are needed.
        //
        // Prefab-name source only: filling an area with copies of a selection would need the
        // selection for the CONTENT, and it's already needed here for the TARGETS.
        //
        // Cell size: per-axis, spacing runs along world X (axis 1) and world Y (axis 2) - the
        // combined AABB is world-aligned, so world-aligned steps are the only ones that actually
        // tile it. Relative spacing measures a probe instance of the prefab (placed above the
        // box, measured, removed - same measure-the-real-thing principle as the path's first
        // instance) plus the gap; absolute uses the typed value plus gap.
        public static DistributeResult DistributeFillSurfaces(
            Scene scene, string prefabName, List<GameEntity> targetSurfaces,
            bool relative1, float absolute1, float gap1,
            bool relative2, float absolute2, float gap2,
            bool alignToSurface, string surfaceTargetNames, string anchorName, AnchorMode anchorMode,
            int maxTotal, float entityRotationDegrees = 0f, float gridRotationDegrees = 0f)
        {
            var result = new DistributeResult();
            if (scene == null) { result.Error = "No scene."; return result; }
            if (string.IsNullOrWhiteSpace(prefabName))
            {
                result.Error = "Fill Target Surfaces places a named prefab - switch Source to Prefab Name.";
                return result;
            }

            var targets = (targetSurfaces ?? new List<GameEntity>()).Where(EntitySelector.IsValidEntity).ToList();
            if (targets.Count == 0)
            {
                result.Error = "No target surfaces - select the surfaces to fill before clicking Distribute.";
                return result;
            }

            // Combined world bounding box of every captured surface.
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            bool any = false;
            foreach (var target in targets)
            {
                BoundingBox box;
                try { box = target.GetGlobalBoundingBox(); }
                catch { continue; }
                minX = Math.Min(minX, box.min.x); minY = Math.Min(minY, box.min.y); minZ = Math.Min(minZ, box.min.z);
                maxX = Math.Max(maxX, box.max.x); maxY = Math.Max(maxY, box.max.y); maxZ = Math.Max(maxZ, box.max.z);
                any = true;
            }
            if (!any) { result.Error = "Could not measure the captured surfaces' bounding boxes."; return result; }

            float extentX = maxX - minX;
            float extentY = maxY - minY;

            Log.Info($"[NativeTrace] ENTER PrefabDistributor.DistributeFillSurfaces prefab='{prefabName}' " +
                     $"targets={targets.Count} footprint={extentX:F2}x{extentY:F2} alignToSurface={alignToSurface}");

            // LATTICE FOLLOWS THE SURFACE'S OWN AXES (2026-08-23, "empire_column_a was angled
            // 35 degrees and the grid it generated was made aligned to world axis"): the steps
            // used to be hard world X/Y - the only directions that trivially tile a
            // world-aligned bounding box - which made every fill on a rotated surface come out
            // world-aligned. The lattice now walks the FIRST target's flattened, unit local X/Y
            // instead, and covers the AABB by projecting its corners into lattice coordinates
            // (the lattice start shifts outside the box when rotated; cells that end up over
            // nothing miss their filtered raycast and are skipped like any other missed cell).
            // Falls back to world X/Y when the target's axes flatten degenerate (a wall-like
            // vertical "surface") or non-perpendicular - identical to the old behaviour there.
            var latticeRotation = MatrixFrame.Identity.rotation;
            try { latticeRotation = targets[0].GetGlobalFrame().rotation; } catch { }
            var latticeBasis = NormalizeBasisLengths(latticeRotation);
            var d1 = Flatten(latticeBasis.s);
            var d2 = Flatten(latticeBasis.f);
            float l1 = Length(d1), l2 = Length(d2);
            bool rotatedLattice = l1 > 0.5f && l2 > 0.5f;
            if (rotatedLattice)
            {
                d1 = d1 / l1;
                d2 = d2 / l2;
                if (Math.Abs(Vec3.DotProduct(d1, d2)) > 0.2f) rotatedLattice = false;
            }
            if (!rotatedLattice)
            {
                d1 = new Vec3(1f, 0f, 0f, 0f);
                d2 = new Vec3(0f, 1f, 0f, 0f);
            }

            // ROTATION (GRID): pivots the fill lattice on top of the surface-derived (or world
            // fallback) directions. Applied here so coverage projection and the relative-spacing
            // probe both use the final directions; DistributeOntoSurface gets 0 below since the
            // rotation is already baked into the axis specs.
            if (Math.Abs(gridRotationDegrees) > 0.01f)
            {
                var gRad = gridRotationDegrees * (float)Math.PI / 180f;
                float gCos = (float)Math.Cos(gRad), gSin = (float)Math.Sin(gRad);
                Vec3 GridRotZ(Vec3 v) => new Vec3(v.x * gCos - v.y * gSin, v.x * gSin + v.y * gCos, v.z, 0f);
                d1 = GridRotZ(d1);
                d2 = GridRotZ(d2);
            }

            float spacing1 = absolute1 + gap1;
            float spacing2 = absolute2 + gap2;
            if (relative1 || relative2)
            {
                // A probe instance, placed above the box so it can't collide with anything being
                // measured, measured along the LATTICE directions (world X/Y when un-rotated),
                // then removed. Same measure-the-real-thing principle as the path distributor's
                // first instance.
                var probeFrame = MatrixFrame.Identity;
                probeFrame.origin = new Vec3(minX, minY, maxZ + 5f, 0f);
                GameEntity probe = null;
                try
                {
                    probe = GameEntity.Instantiate(scene, prefabName.Trim(), probeFrame, true);
                    if (probe == null) { result.Error = "Instantiate returned null for the measuring probe (unknown prefab name?)."; return result; }
                    // Same dirty-bounds guard as the surface first-instance measure.
                    try { probe.RecomputeBoundingBox(); } catch { }
                    if (relative1) spacing1 = Math.Max(0.01f, MeasureExtentAlongDirection(probe, d1)) + gap1;
                    if (relative2) spacing2 = Math.Max(0.01f, MeasureExtentAlongDirection(probe, d2)) + gap2;
                }
                catch (Exception ex) { result.Error = "Failed to measure the prefab: " + ex.Message; return result; }
                finally
                {
                    try { probe?.Remove(0); } catch { }
                }
            }
            spacing1 = Math.Max(0.01f, spacing1);
            spacing2 = Math.Max(0.01f, spacing2);

            // AABB coverage in lattice coordinates: project the box's four corners onto d1/d2.
            float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
            foreach (var (cx, cy) in new[] { (0f, 0f), (extentX, 0f), (0f, extentY), (extentX, extentY) })
            {
                float u = cx * d1.x + cy * d1.y;
                float v = cx * d2.x + cy * d2.y;
                if (u < uMin) uMin = u; if (u > uMax) uMax = u;
                if (v < vMin) vMin = v; if (v > vMax) vMax = v;
            }

            int count1 = Math.Max(1, (int)Math.Floor((uMax - uMin) / spacing1) + 1);
            int count2 = Math.Max(1, (int)Math.Floor((vMax - vMin) / spacing2) + 1);
            result.DerivedCount1 = count1;
            result.DerivedCount2 = count2;

            if (count1 * count2 > maxTotal)
            {
                // The count surprises people ("I only selected 2 surfaces?!"), so the message
                // spells out the arithmetic: cells = footprint / spacing. The number of captured
                // SURFACES only shapes the footprint - the prefab's own size (relative spacing,
                // gap 0) sets the resolution, and filling two plazas edge-to-edge with a 1-unit
                // column is legitimately hundreds of cells. (A rotated lattice covers the box's
                // diagonal, so its counts run a bit higher than footprint/spacing - the extra
                // cells fall outside the surfaces and are skipped, but they count toward the cap.)
                result.Error = $"This footprint is {extentX:F1} x {extentY:F1} units and the cell size is " +
                               $"{spacing1:F2} x {spacing2:F2} (prefab size/absolute + gap), so filling it needs a " +
                               $"{count1}x{count2} grid = {count1 * count2} cells - above the Max Instances cap of {maxTotal}. " +
                               "Cell count is footprint DIVIDED BY spacing; how many surfaces were captured doesn't matter. " +
                               "Increase Gap (or absolute spacing) to thin the grid, or raise Max Instances.";
                return result;
            }

            // Heading for the placed pieces comes from the first captured surface (a terrace's
            // paving usually aligns with the terrace); upright mode erects it, tilt mode replaces
            // it per cell anyway. Origin sits at the lattice's min corner (shifted outside the
            // box when the lattice is rotated), z at the box TOP so every cell's cast starts
            // above the surfaces.
            var originFrame = MatrixFrame.Identity;
            originFrame.rotation = latticeRotation;
            originFrame.origin = new Vec3(minX + d1.x * uMin + d2.x * vMin,
                                          minY + d1.y * uMin + d2.y * vMin,
                                          maxZ, 0f);

            var axis1 = new GridAxisSpec
            {
                Direction = d1,
                Count = count1, Relative = false, Absolute = spacing1, Gap = 0f,   // gap already folded in
            };
            var axis2 = new GridAxisSpec
            {
                Direction = d2,
                Count = count2, Relative = false, Absolute = spacing2, Gap = 0f,
            };

            var placedResult = DistributeOntoSurface(scene, prefabName, null, originFrame, axis1, axis2,
                alignToSurface, surfaceTargetNames, anchorName, anchorMode, entityRotationDegrees, gridRotationDegrees: 0f);
            placedResult.DerivedCount1 = count1;
            placedResult.DerivedCount2 = count2;

            Log.Info($"[NativeTrace] EXIT PrefabDistributor.DistributeFillSurfaces derived={count1}x{count2} " +
                     $"placed={placedResult.Placed.Count} failed={placedResult.Failed.Count}");
            return placedResult;
        }

        // Each basis vector rescaled to unit length, directions kept - strips the SCALE a
        // borrowed rotation carries (this engine encodes scale as basis length) without touching
        // its orientation. Degenerate (near-zero) basis vectors are left as-is rather than
        // divided into NaN.
        private static Mat3 NormalizeBasisLengths(Mat3 rotation)
        {
            Vec3 Unit(Vec3 v)
            {
                var length = Length(v);
                return length < 0.0001f ? v : v / length;
            }
            return new Mat3(Unit(rotation.s), Unit(rotation.f), Unit(rotation.u));
        }

        // Drops the vertical component and renormalises - see DistributeOntoSurface for why the
        // grid steps horizontally even when the origin entity is tilted.
        private static Vec3 Flatten(Vec3 v)
        {
            var flat = new Vec3(v.x, v.y, 0f, 0f);
            var length = Length(flat);
            return length < 0.0001f ? flat : flat / length * Length(v);
        }

        private static float Length(Vec3 v) => (float)Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);

        // Faithful copy, by the best route available for this entity. EntityCloner first: it
        // resolves the REAL prefab (GetPrefabName) and copies per-mesh material/colour overrides
        // onto the result, and it is the proven path - shift-drag repeat uses it. When there is no
        // prefab behind the entity at all, GameEntity.CopyFrom clones the live instance directly,
        // which is the only thing that works for hand-assembled geometry.
        // internal, not private: RepeatLastTransform's copy-replay clones through here too -
        // it used EntityCloner directly and was the last path still failing on prefab-less
        // sources ("Nothing could be copied. ... has no prefab name" via Shift+R, 2026-08-22).
        internal static GameEntity CloneSourceAt(Scene scene, GameEntity source, MatrixFrame frame, out string error)
        {
            error = null;

            var clone = EntityCloner.Clone(scene, source, frame);
            if (clone.Success) return clone.Instance;

            try
            {
                // REVERTED 2026-08-23: CopyFromPrefab was tried here briefly (a distinct clone
                // factory routing through the prefab pipeline, hunting the unselectable-clone
                // registration bug) and is DAMAGING - it treats the live source as a template
                // and the source lost children ("my prefab got halfway destroyed", corroborated
                // by save-file child counts). NEVER reintroduce it against live scene entities.
                // Scene.AttachEntity was removed in the same revert - unknown native semantics
                // are no longer acceptable in this path after that. CopyFrom is the only clone
                // route for prefab-less sources; its products stay unselectable until the scene
                // is saved (documented limitation - the editor's click registry is rebuilt on
                // save and no reachable managed API does the same).
                var copy = GameEntity.CopyFrom(scene, source, true, true);
                if (copy == null) { error = clone.Error; return null; }

                // CONFIRMED IN CC_76 (tool.log 22:14:57): CopyFrom marks its product
                // "DontSaveToScene, NonModifiableFromEditor" - a runtime entity. The first flag
                // made copies VANISH from scene saves (three empty-anchor runs before the log
                // line proved it); the second leaves the surviving copy unselectable/untouchable
                // in the editor, which reads as "broken" even once it persists. Both cleared:
                // a clone the user asked for should behave like any other placed entity.
                try
                {
                    var flags = copy.EntityFlags;
                    Log.Info($"[Clone] CopyFrom('{source.Name}') flags={flags}");
                    // RECURSIVE since 2026-08-23 ("some of the entities I make with shift+r
                    // become at least temporarily impossible to select"): CopyFrom copies the
                    // WHOLE hierarchy and each child carries its own flags - clearing only the
                    // root left children NonModifiableFromEditor, and viewport clicks land on
                    // CHILD meshes (that's where collision lives), so the copy as a whole
                    // couldn't be picked.
                    ClearRuntimeFlagsInTree(copy, 0);
                }
                catch (Exception flagEx) { Log.Warn($"[Clone] flag adjust failed for '{source.Name}': {flagEx.Message}"); }

                copy.SetGlobalFrame(ref frame, true);
                RefreshSubtreeFrames(copy, 0);
                EditorFrameSync.Sync(copy);

                // (AttachEntity removed in the 2026-08-23 revert - see the comment above.)
                try { copy.SetReadyToRender(true); } catch { }
                try { TaleWorlds.MountAndBlade.MBEditor.UpdateSceneTree(true); } catch { }
                return copy;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        // A "bare anchor" is an organizational shell: no prefab behind it, no meshes of its own,
        // only children - exactly what CreateAnchorAndAdopt makes. Mirroring one as an opaque
        // unit forces the CopyFrom-then-flip path with its stale-winding problem (see
        // MirrorGroup); its children are the real content and mirror cleanly one by one.
        // Non-anchors (anything with a prefab or own meshes) pass through unchanged.
        private static void ExpandBareAnchors(GameEntity entity, List<GameEntity> into, int depth)
        {
            if (entity == null || depth > 8) return;

            bool bare = false;
            try
            {
                string prefabName = null;
                try { prefabName = entity.GetPrefabName(); } catch { }
                if (string.IsNullOrWhiteSpace(prefabName) && entity.MultiMeshComponentCount == 0)
                {
                    foreach (var child in entity.GetChildren()) { bare = true; break; }
                }
            }
            catch { }

            if (!bare) { into.Add(entity); return; }
            try { foreach (var child in entity.GetChildren()) ExpandBareAnchors(child, into, depth + 1); }
            catch { into.Add(entity); }
        }

        // Clears DontSaveToScene + NonModifiableFromEditor + WaitUntilReady on an entity AND
        // every descendant - CopyFrom stamps all three on the whole copied hierarchy. The first
        // makes the copy vanish on save, the second makes it unclickable, and the third
        // (2026-08-23, "the final entity ... hover highlights but I can't click it") leaves the
        // NEWEST copy click-proof until the engine gets around to processing it - hover uses a
        // different path than click-select, which is why it highlights but won't select. Safe to
        // clear: the copy's source was fully loaded, so nothing is actually still streaming.
        // Depth-capped, exception-swallowing.
        // SCENE-WIDE REPAIR for copies contaminated before the recursive flag clear existed
        // (2026-08-23, "i'm still unable to select some of the created copies"): pre-fix
        // CopyFrom products had their ROOT cleared but children left flagged, and the editor's
        // own shift-drag duplicates of those inherit the flagged children - so the contamination
        // outlived the code fix and spread. The SIGNATURE is what makes this safe to run on the
        // whole scene: a candidate must be a TRUE ROOT (no parent) whose OWN flags are clean but
        // whose descendants carry the runtime flags. Legitimately-flagged engine/editor helper
        // entities have flagged ROOTS, so they can never match; a clean root with flagged
        // children exists in exactly one way - our pre-fix copies and their descendants.
        private const EntityFlags RuntimeCopyFlags =
            EntityFlags.DontSaveToScene | EntityFlags.NonModifiableFromEditor | EntityFlags.WaitUntilReady;

        public static (int subtrees, int cleared) RepairFlaggedCopySubtrees(Scene scene)
        {
            int subtrees = 0, cleared = 0;
            if (scene == null) return (0, 0);

            var all = new List<GameEntity>();
            try { scene.GetEntities(ref all); } catch { return (0, 0); }

            foreach (var entity in all)
            {
                if (!EntitySelector.IsValidEntity(entity)) continue;
                try
                {
                    GameEntity parent = null;
                    try { parent = entity.Parent; } catch { }
                    if (parent != null && parent.Pointer != UIntPtr.Zero) continue;   // not a root
                    if ((entity.EntityFlags & RuntimeCopyFlags) != 0) continue;       // flagged root = not ours

                    int count = ClearBadFlagsInDescendants(entity, 0);
                    if (count > 0)
                    {
                        subtrees++;
                        cleared += count;
                        Log.Info($"[FlagRepair] '{entity.Name}': cleared runtime flags on {count} descendant(s).");
                    }
                }
                catch { }
            }

            Log.Info($"[FlagRepair] scene sweep: {subtrees} subtree(s), {cleared} descendant(s) repaired.");
            return (subtrees, cleared);
        }

        private static int ClearBadFlagsInDescendants(GameEntity entity, int depth)
        {
            if (entity == null || depth > 16) return 0;
            int count = 0;
            try
            {
                foreach (var child in entity.GetChildren())
                {
                    try
                    {
                        var flags = child.EntityFlags;
                        if ((flags & RuntimeCopyFlags) != 0)
                        {
                            child.EntityFlags = flags & ~RuntimeCopyFlags;
                            count++;
                        }
                    }
                    catch { }
                    count += ClearBadFlagsInDescendants(child, depth + 1);
                }
            }
            catch { }
            return count;
        }

        internal static void ClearRuntimeFlagsInTree(GameEntity entity, int depth)
        {
            if (entity == null || depth > 16) return;
            try
            {
                var flags = entity.EntityFlags;
                // PhysicsDisabled joined the clear list 2026-08-23: CopyFromPrefab clones came
                // out flagged PhysicsDisabled (logged), and the editor's CLICK-select raycasts
                // against physics - hover highlighting reads render data, which is exactly the
                // observed highlight-but-never-select. No physics-enable API exists; the flag is
                // the lever. Safe to clear broadly here: sources whose collision was genuinely
                // removed (Pile Generator's Physics: Delete uses RemovePhysics) have no bodies
                // for the flag to re-enable.
                var cleared = flags & ~(EntityFlags.DontSaveToScene | EntityFlags.NonModifiableFromEditor
                                        | EntityFlags.WaitUntilReady | EntityFlags.PhysicsDisabled);
                if (cleared != flags) entity.EntityFlags = cleared;

                foreach (var child in entity.GetChildren())
                    ClearRuntimeFlagsInTree(child, depth + 1);
            }
            catch { }
        }

        // CONFIRMED SYMPTOM 2026-08-22 (CC_76 double mirror): setting a handedness-FLIPPING
        // frame on an already-built subtree (the CopyFrom path - the copy exists first, the
        // mirrored frame lands on it afterward) leaves the children rendering INSIDE-OUT until
        // the scene is saved and reloaded. The reload looking correct is the proof that the
        // DATA is right and only per-node cached render state is stale - a fresh Instantiate
        // directly AT a mirrored frame never shows this, because it builds with the right
        // winding from birth. Re-setting each node's own (unchanged) global frame is the
        // strongest refresh available from managed code; if the engine still won't recompute
        // winding live, save + reload remains the documented workaround.
        private static void RefreshSubtreeFrames(GameEntity root, int depth)
        {
            if (root == null || depth > 16) return;
            try
            {
                foreach (var child in root.GetChildren())
                {
                    try
                    {
                        var frame = child.GetGlobalFrame();
                        child.SetGlobalFrame(ref frame, true);
                    }
                    catch { }
                    RefreshSubtreeFrames(child, depth + 1);
                }
            }
            catch { }
        }

        // Combined world bounding box of a whole selection, projected onto a direction - the
        // group equivalent of MeasureExtentAlongDirection, so a multi-entity unit spaces by its
        // real footprint instead of by whichever piece happened to be first.
        private static float MeasureGroupExtentAlongDirection(List<GameEntity> entities, Vec3 axisDirUnit)
        {
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            bool any = false;

            foreach (var entity in entities)
            {
                BoundingBox box;
                try { box = entity.GetGlobalBoundingBox(); }
                catch { continue; }

                minX = Math.Min(minX, box.min.x); minY = Math.Min(minY, box.min.y); minZ = Math.Min(minZ, box.min.z);
                maxX = Math.Max(maxX, box.max.x); maxY = Math.Max(maxY, box.max.y); maxZ = Math.Max(maxZ, box.max.z);
                any = true;
            }
            if (!any) return 0f;

            float min = float.MaxValue, max = float.MinValue;
            for (int xi = 0; xi < 2; xi++)
            {
                float x = xi == 0 ? minX : maxX;
                for (int yi = 0; yi < 2; yi++)
                {
                    float y = yi == 0 ? minY : maxY;
                    for (int zi = 0; zi < 2; zi++)
                    {
                        float z = zi == 0 ? minZ : maxZ;
                        float proj = x * axisDirUnit.x + y * axisDirUnit.y + z * axisDirUnit.z;
                        if (proj < min) min = proj;
                        if (proj > max) max = proj;
                    }
                }
            }
            return max - min;
        }

        // Creates the wrapping anchor at whichever position anchorMode calls for, then AddChild's
        // every already-placed piece onto it. Placing pieces at their final world frame BEFORE the
        // anchor exists (rather than parenting as each one is created, like this used to work) is
        // what makes BottomCenterOfGroup possible at all - the group's own bounding box can't be
        // known until every piece is actually down.
        private static GameEntity CreateAnchorAndAdopt(
            Scene scene, AnchorMode anchorMode, MatrixFrame referenceFrame,
            List<GameEntity> placed, string anchorName, DistributeResult result)
        {
            var anchorFrame = referenceFrame;
            if (anchorMode == AnchorMode.BottomCenterOfGroup && placed.Count > 0)
            {
                var pivot = ComputeBottomCenterPivot(placed);
                anchorFrame = MatrixFrame.Identity;
                anchorFrame.origin = pivot;
            }
            // CONFIRMED IN CC_76, fixed 2026-08-22: the reference frame comes from a real scene
            // entity, and when that entity is SCALED (a squished 0.918x0.699 wall variant), the
            // anchor inherited its non-uniform scale in the basis lengths. Children compensate at
            // AddChild time so the result LOOKS right - until anything is manually rotated under
            // (or with) the anchor in the editor, at which point non-uniform parent scale turns
            // rotation into SHEAR and the group visibly skews. An anchor is a reference point
            // with a heading, not a scaled object: unit basis, always.
            anchorFrame.rotation = NormalizeBasisLengths(anchorFrame.rotation);

            Log.Info($"[NativeTrace] CreateEmpty (anchor '{anchorName}')");
            GameEntity anchor;
            try { anchor = GameEntity.CreateEmpty(scene, true, false, true); }
            catch (Exception ex) { result.Error = "CreateEmpty failed: " + ex.Message; return null; }
            if (anchor == null) { result.Error = "CreateEmpty returned null."; return null; }

            anchor.Name = anchorName;
            Log.Info($"[NativeTrace] SetGlobalFrame (anchor '{anchorName}')");
            anchor.SetGlobalFrame(ref anchorFrame, true);
            EditorFrameSync.Sync(anchor);
            var reselectTag = ReselectTagPrefix + SafeTag(anchorName);
            anchor.AddTag(reselectTag);
            result.AnchorEntity = anchor;

            foreach (var instance in placed)
            {
                Log.Info($"[NativeTrace] AddChild '{instance.Name}' -> anchor '{anchorName}'");
                try { anchor.AddChild(instance, true); }
                catch (Exception ex) { result.Failed.Add($"'{instance.Name}': failed to parent under anchor: {ex.Message}"); }

                instance.AddTag(reselectTag);
                result.Placed.Add(instance);
            }

            return anchor;
        }

        // X/Y centered across the combined world bounding box of every placed piece, Z at the
        // lowest point - same convention as PrefabCreatorTool's PivotMath.ComputeBottomCenterPivot
        // (duplicated here rather than shared across mods, matching how infrastructure has been
        // kept independent between them all session).
        private static Vec3 ComputeBottomCenterPivot(List<GameEntity> entities)
        {
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            bool any = false;

            foreach (var entity in entities)
            {
                BoundingBox box;
                try { box = entity.GetGlobalBoundingBox(); }
                catch { continue; }

                minX = Math.Min(minX, box.min.x); minY = Math.Min(minY, box.min.y); minZ = Math.Min(minZ, box.min.z);
                maxX = Math.Max(maxX, box.max.x); maxY = Math.Max(maxY, box.max.y); maxZ = Math.Max(maxZ, box.max.z);
                any = true;
            }

            if (!any) return entities[0].GetGlobalFrame().origin;
            return new Vec3((minX + maxX) / 2f, (minY + maxY) / 2f, minZ, 0f);
        }

        // True mirror (reflection), not the 180-degree-rotation trick this replaces. A rotation
        // always has determinant +1 - it preserves chirality/handedness, so rotating an asymmetric
        // piece 180 degrees never actually produces its mirror image, just a spun copy of the same
        // shape. This instead reflects EVERY entity in the group individually - its own position
        // AND its own rotation matrix - through a plane that passes through the group's own
        // bottom-center pivot (so the result shares the same origin/center as the original, no
        // separate plane position needed) with a normal along the chosen axis. Reflecting each of
        // an entity's rotation basis vectors (not just negating a position) is what correctly
        // mirrors asymmetric children instead of just moving them to a mirrored position while
        // leaving their own shape un-reflected.
        //
        // Confirmed at the API level (not yet confirmed live in-game): TaleWorlds.Library.Mat3 has
        // an IsLeftHanded() method - the type explicitly supports negative-determinant (mirrored)
        // matrices as a valid state - and GetLocalScale() derives scale purely from basis-vector
        // LENGTH, which reflection preserves, so scale comes out correct automatically. Neither
        // SetGlobalFrame nor SetLocalFrame do any renormalization - whatever Mat3 is passed goes
        // straight to native code. Test on one clearly-asymmetric piece before trusting a big run;
        // this is the one thing static analysis can't confirm (native rendering/physics behavior
        // for a mirrored frame).
        public static DistributeResult MirrorGroup(
            Scene scene, List<GameEntity> selection, DistributionAxis mirrorAxis, MatrixFrame referenceFrame,
            string anchorName, AnchorMode anchorMode, Vec3? customPivot = null)
        {
            var result = new DistributeResult();
            if (scene == null || selection == null || selection.Count == 0)
            {
                result.Error = "Nothing selected to mirror.";
                return result;
            }

            // customPivot lets the mirror plane sit anywhere, not just at the group's own
            // bottom-center - e.g. a V shape mirrored across a point below its own base ends up as
            // a taller upside-down V spanning further away, rather than folding back onto itself.
            // Each entity is still independently reflected (own position AND own rotation, not a
            // rotation of the group around that point) regardless of which pivot is used.
            var pivot = customPivot ?? ComputeBottomCenterPivot(selection);
            var normal = ResolveAxisDirection(mirrorAxis, referenceFrame);
            var normalLen = (float)Math.Sqrt(normal.x * normal.x + normal.y * normal.y + normal.z * normal.z);
            if (normalLen > 0.0001f) normal = normal / normalLen;

            // BARE ANCHORS EXPAND TO THEIR CHILDREN (2026-08-22). Mirroring a previous run's
            // anchor - the natural way to "mirror that whole group again" - used to clone the
            // anchor SHELL via CopyFrom (it has no prefab) and flip its frame afterward, and a
            // handedness flip applied to an already-built subtree leaves the children rendering
            // inside-out until save+reload (confirmed live in CC_76; the saved data was right).
            // Mirroring the anchor's CHILDREN individually instead means prefab-backed pieces go
            // through Instantiate directly AT their final frames - which renders correctly at
            // any handedness, confirmed by every single-mirror run. A double mirror's final
            // frames are right-handed again anyway, so this path avoids the flip entirely.
            var pieces = new List<GameEntity>();
            foreach (var entity in selection) ExpandBareAnchors(entity, pieces, 0);
            if (pieces.Count != selection.Count)
                Log.Info($"[Mirror] expanded {selection.Count} selected into {pieces.Count} pieces (bare anchors replaced by their children).");
            if (pieces.Count == 0)
            {
                result.Error = "The selection is only empty anchor(s) - nothing visible to mirror.";
                return result;
            }

            Log.Info($"[NativeTrace] ENTER PrefabDistributor.MirrorGroup selectionCount={selection.Count} pieces={pieces.Count} axis={mirrorAxis} customPivot={customPivot.HasValue}");

            var placed = new List<GameEntity>();
            foreach (var entity in pieces)
            {
                if (!EntitySelector.IsValidEntity(entity)) continue;

                var frame = entity.GetGlobalFrame();
                var reflectedFrame = new MatrixFrame(ReflectMat3(frame.rotation, normal), ReflectPoint(frame.origin, pivot, normal));

                // FIXED v0.7: this used to Instantiate by entity.Name, which broke on any entity
                // whose display name differs from its prefab - and Material Swap's own "Rename
                // Changed" option (which appends _mst) guarantees exactly that mismatch.
                // EntityCloner resolves the REAL prefab via GetPrefabName() and, as a bonus,
                // copies per-mesh material/color overrides across, so a mirrored copy of a
                // recolored building keeps its recolor instead of silently reverting to the
                // bare prefab's look.
                //
                // FIXED 2026-08-22: now CloneSourceAt, not EntityCloner directly - Mirror was the
                // only clone consumer WITHOUT the GameEntity.CopyFrom fallback, so an entity with
                // no saved prefab behind it (a hand-assembled/combined piece - confirmed report:
                // fief_aserai_villa_arch_mod wall in CC_76) could tile in a grid but not mirror.
                // The failure reason is also logged now, not just shown in the status line.
                Log.Info($"[NativeTrace] Clone '{entity.Name}' (mirrored copy)");
                var copy = CloneSourceAt(scene, entity, reflectedFrame, out var cloneError);
                if (copy == null)
                {
                    Log.Warn($"[Mirror] clone of '{entity.Name}' failed: {cloneError}");
                    result.Failed.Add($"'{entity.Name}': {cloneError}");
                    continue;
                }

                placed.Add(copy);

                // Auto-place secondaries are keyed by the real prefab name too, not the display
                // name - same resolution EntityCloner just did.
                string autoPlaceKey = null;
                try { autoPlaceKey = entity.GetPrefabName(); } catch { }
                if (!string.IsNullOrWhiteSpace(autoPlaceKey))
                    AutoPlaceSecondaries(scene, autoPlaceKey, reflectedFrame, placed, result);
            }

            // FIXED 2026-08-22: a mirror where every clone failed used to fall through, CREATE an
            // empty anchor, and report Success - the user saw "_Mirrored" appear in the outliner
            // with nothing under it, then understandably tried to mirror THAT (an empty entity
            // with no prefab, guaranteed to fail again). Total failure is now an error, no anchor.
            if (placed.Count == 0)
            {
                result.Error = result.Failed.Count > 0
                    ? "Nothing could be mirrored. " + string.Join("; ", result.Failed.Take(3))
                    : "Nothing could be mirrored.";
                return result;
            }

            // CONFIRMED BUG, fixed 2026-08-20: referenceFrame was handed straight to the anchor,
            // so in ExactOriginPoint mode the mirrored group's anchor was placed at the SOURCE
            // entity's position - literally identical coordinates to the thing being mirrored.
            // Verified in fief_material_test_4: a source ..._a_b sits at (289.671, 131.695, 0) and
            // the _Mirrored anchor was stamped at exactly (289.671, 131.695, 0) too. The children
            // were reflected correctly, so the content looked right while the group's origin sat
            // somewhere unrelated - which is what forced the children to carry a large local
            // offset instead of sitting near their own anchor.
            //
            // referenceFrame has two unrelated jobs here and only one was correct: it supplies the
            // AXIS BASIS for ResolveAxisDirection above (mirroring "along local X" needs some
            // entity's orientation to define local X), which is legitimate. Reusing it verbatim as
            // the anchor's placement never was. The anchor is the mirrored group's own origin, so
            // it belongs at the source origin REFLECTED ACROSS THE SAME PLANE the children were.
            //
            // Origin only, not the rotation: the reflected children already carry the mirroring in
            // their own frames (scale -1 on the mirror axis). Reflecting the anchor's basis too
            // would apply it twice - the same mistake as copying a whole MatrixFrame in
            // PivotAligner when only the origin was wanted.
            var anchorReference = referenceFrame;
            if (anchorMode == AnchorMode.ExactOriginPoint)
            {
                anchorReference.origin = ReflectPoint(referenceFrame.origin, pivot, normal);
                Log.Info($"[NativeTrace] MirrorGroup anchor origin reflected " +
                         $"({referenceFrame.origin.x:F3},{referenceFrame.origin.y:F3},{referenceFrame.origin.z:F3}) -> " +
                         $"({anchorReference.origin.x:F3},{anchorReference.origin.y:F3},{anchorReference.origin.z:F3})");
            }

            var anchor = CreateAnchorAndAdopt(scene, anchorMode, anchorReference, placed, anchorName, result);
            if (anchor == null) return result;

            result.Success = true;
            Log.Info($"[NativeTrace] EXIT PrefabDistributor.MirrorGroup placed={result.Placed.Count} failed={result.Failed.Count}");
            return result;
        }

        // A rigid rotation, unlike MirrorGroup - it preserves handedness (determinant +1), so
        // there's no need to re-instantiate fresh copies the way reflection does. Each selected
        // entity is moved in place: its own position rotated around the pivot, and its own
        // rotation matrix rotated by the same amount, both via the same axis-angle (Rodrigues)
        // formula. Shares MirrorGroup's pivot convention - group's own bottom-center by default,
        // or an explicit customPivot - and the same DistributionAxis picker for which axis to spin
        // around, so the two operations (mirror vs. simple rotate) read as a matched pair in the UI.
        public static DistributeResult RotateGroup(
            Scene scene, List<GameEntity> selection, DistributionAxis rotationAxis, float angleDegrees,
            MatrixFrame referenceFrame, Vec3? customPivot = null)
        {
            var result = new DistributeResult();
            if (scene == null || selection == null || selection.Count == 0)
            {
                result.Error = "Nothing selected to rotate.";
                return result;
            }

            var pivot = customPivot ?? ComputeBottomCenterPivot(selection);
            var axis = ResolveAxisDirection(rotationAxis, referenceFrame);
            var axisLen = (float)Math.Sqrt(axis.x * axis.x + axis.y * axis.y + axis.z * axis.z);
            if (axisLen > 0.0001f) axis = axis / axisLen;

            Log.Info($"[NativeTrace] ENTER PrefabDistributor.RotateGroup selectionCount={selection.Count} axis={rotationAxis} angle={angleDegrees}");

            foreach (var entity in selection)
            {
                if (!EntitySelector.IsValidEntity(entity)) continue;

                var frame = entity.GetGlobalFrame();
                var rotatedOrigin = pivot + RotateVectorAroundAxis(frame.origin - pivot, axis, angleDegrees);
                var rotatedRotation = new Mat3(
                    RotateVectorAroundAxis(frame.rotation.s, axis, angleDegrees),
                    RotateVectorAroundAxis(frame.rotation.f, axis, angleDegrees),
                    RotateVectorAroundAxis(frame.rotation.u, axis, angleDegrees));
                var newFrame = new MatrixFrame(rotatedRotation, rotatedOrigin);

                Log.Info($"[NativeTrace] SetGlobalFrame '{entity.Name}' (rotate)");
                try { entity.SetGlobalFrame(ref newFrame, true); EditorFrameSync.Sync(entity); }
                catch (Exception ex) { result.Failed.Add($"'{entity.Name}': {ex.Message}"); continue; }
                result.Placed.Add(entity);
            }

            result.Success = true;
            Log.Info($"[NativeTrace] EXIT PrefabDistributor.RotateGroup placed={result.Placed.Count} failed={result.Failed.Count}");
            return result;
        }

        // Rodrigues' rotation formula - rotates v around a unit axis by angleDegrees. Used for both
        // position (relative to the pivot) and each rotation-basis vector, so an arbitrary axis
        // (not just world-up) works correctly for Local X/Y/Z choices too.
        private static Vec3 RotateVectorAroundAxis(Vec3 v, Vec3 axisUnit, float angleDegrees)
        {
            float rad = angleDegrees * ((float)Math.PI / 180f);
            float cos = (float)Math.Cos(rad);
            float sin = (float)Math.Sin(rad);
            var cross = new Vec3(
                axisUnit.y * v.z - axisUnit.z * v.y,
                axisUnit.z * v.x - axisUnit.x * v.z,
                axisUnit.x * v.y - axisUnit.y * v.x,
                0f);
            var dot = Vec3.DotProduct(axisUnit, v);
            return v * cos + cross * sin + axisUnit * (dot * (1f - cos));
        }

        private static Vec3 ReflectPoint(Vec3 point, Vec3 planePoint, Vec3 planeNormalUnit)
        {
            var rel = point - planePoint;
            var d = rel.x * planeNormalUnit.x + rel.y * planeNormalUnit.y + rel.z * planeNormalUnit.z;
            return point - planeNormalUnit * (2f * d);
        }

        private static Vec3 ReflectVector(Vec3 v, Vec3 planeNormalUnit)
        {
            var d = v.x * planeNormalUnit.x + v.y * planeNormalUnit.y + v.z * planeNormalUnit.z;
            return v - planeNormalUnit * (2f * d);
        }

        private static Mat3 ReflectMat3(Mat3 rotation, Vec3 planeNormalUnit) =>
            new Mat3(ReflectVector(rotation.s, planeNormalUnit), ReflectVector(rotation.f, planeNormalUnit), ReflectVector(rotation.u, planeNormalUnit));

        // Spins a path-sampled frame around its own up axis (.u) - a path frame's forward (.f)
        // already points along the direction of travel, so this is what "the prefab is facing
        // the wrong way along the path" needs: a fixed correction (90/180/270, or anything in
        // between) applied identically to every instance before it's placed.
        private static Mat3 RotateAboutUp(Mat3 rotation, float angleDegrees)
        {
            float rad = angleDegrees * ((float)Math.PI / 180f);
            float cos = (float)Math.Cos(rad);
            float sin = (float)Math.Sin(rad);
            var newS = rotation.s * cos + rotation.f * sin;
            var newF = rotation.f * cos - rotation.s * sin;
            return new Mat3(newS, newF, rotation.u);
        }

        // Extent of an entity's FULL hierarchy (itself plus every child), projected onto an
        // arbitrary world direction - correct for ANY axis choice (local or world), unlike just
        // reading a box's raw x/y/z which is only meaningful if the direction happens to be one of
        // the entity's own local axes.
        //
        // Deliberately GetGlobalBoundingBox(), not GetLocalBoundingBox() (which this used to call,
        // re-deriving world corners by hand from origin + rotation.s/.f/.u*x/y/z) - GetLocalBoundingBox
        // only covers the entity's OWN body, not its children, and a great many real prefabs
        // (columns, greebles, anything composite) are a parent anchor with the actual visible mesh
        // living on child entities. On one of those, the local box comes back near-zero, the
        // measured spacing collapses to ~0.01 units, and every placed instance lands almost exactly
        // on top of the last one - "distribution gets stuck at a certain distance" is exactly what
        // that looks like. GetGlobalBoundingBox already returns world-space min/max (confirmed by
        // its existing use in ComputeBottomCenterPivot to pivot a whole multi-entity footprint), so
        // no rotation re-application is needed here, unlike the old local-box version.
        private static float MeasureExtentAlongDirection(GameEntity entity, Vec3 axisDirUnit)
        {
            BoundingBox box;
            try { box = entity.GetGlobalBoundingBox(); }
            catch { return 0f; }

            float min = float.MaxValue, max = float.MinValue;
            for (int xi = 0; xi < 2; xi++)
            {
                float x = xi == 0 ? box.min.x : box.max.x;
                for (int yi = 0; yi < 2; yi++)
                {
                    float y = yi == 0 ? box.min.y : box.max.y;
                    for (int zi = 0; zi < 2; zi++)
                    {
                        float z = zi == 0 ? box.min.z : box.max.z;
                        float proj = x * axisDirUnit.x + y * axisDirUnit.y + z * axisDirUnit.z;
                        if (proj < min) min = proj;
                        if (proj > max) max = proj;
                    }
                }
            }

            return max - min;
        }

        // Places any secondary marked Auto-Place On New Instance for prefabName (or a family it
        // belongs to) right after a base instance lands - same mechanism LivePrefabSwapper already
        // used for Swap, now wired in here too so a Distribute/Mirror run doesn't silently skip it.
        // Appending straight into the same local `placed` list means CreateAnchorAndAdopt parents,
        // tags, and result-tracks the secondaries exactly like any other piece in the run, with no
        // separate bookkeeping. On a big run (a 5x5 grid, a 20-instance path) this means every base
        // that has an auto-place secondary configured now genuinely spawns N extra entities where it
        // silently spawned zero before - worth knowing about before running something huge.
        private static void AutoPlaceSecondaries(Scene scene, string prefabName, MatrixFrame frame, List<GameEntity> placed, DistributeResult result)
        {
            var auto = FamilyAutoPlacer.PlaceAutoSecondaries(scene, prefabName.Trim(), frame);
            placed.AddRange(auto.Placed);
            result.AutoPlacedSecondaryCount += auto.Placed.Count;
            foreach (var f in auto.Failed) result.Failed.Add($"auto-placed secondary: {f}");
            foreach (var f in auto.TextureFailed) result.Failed.Add($"auto-placed secondary texture: {f}");
        }

        private static string SafeTag(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "untitled";
            var cleaned = new string(name.Trim().Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_').ToArray());
            return cleaned.Length == 0 ? "untitled" : cleaned;
        }
    }
}
