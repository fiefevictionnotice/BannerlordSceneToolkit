using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using BannerlordSceneToolkit;

namespace MaterialSwapTool.Core
{
    // Recolors every INDIVIDUAL mesh slot whose material classifies into an armed category, using
    // Mesh.Color rather than MetaMesh.SetFactor1. Deliberately NOT a per-tick background process -
    // every call here is triggered by a discrete UI event (see ContinuousRecolorVM), matching the
    // lesson from the tick-patch freeze fix.
    //
    // This used to go through MetaMesh.SetFactor1, which tints the WHOLE MetaMesh at once - and a
    // typical multi-part entity (verified live: european_city_house_d/d2/d3) has exactly ONE
    // MetaMesh covering every mesh slot: walls, roof, doors, planks, every LOD tier, all of it. So
    // "recolor just the timberframe on this building" was never achievable when timberframe shared
    // a MetaMesh with stone/wood/roof - the old code refused rather than over-color, which is
    // correct but meant most real buildings just got skipped outright ("mixed materials").
    //
    // Mesh.Color turned out to be a real, live, PER-INDIVIDUAL-MESH rendering property after all -
    // confirmed via a user test scene (european_city_house_d3) where exactly one LOD5 submesh had
    // a visibly different Color while its MetaMesh's Factor1 was untouched. That overturned the
    // earlier assumption (Mesh.Color/Color2 looked like they sat among mesh-construction/vertex-
    // color methods, not verified as live rendering). Switching to it here means each mesh slot is
    // handled independently - no more "mixed MetaMesh, refuse" restriction needed at all, since
    // nothing here ever needs to touch a sibling mesh's color to tint one slot. One real unknown,
    // flagged rather than hidden: whether Mesh.Color is a multiplicative tint (matching Factor1's
    // visual behavior) or a literal color replacement hasn't been visually confirmed - worth a
    // live look after this deploys.
    public static class ContinuousRecolorEngine
    {
        public class Result
        {
            public int SlotsColored;
        }

        public static Result Apply(List<GameEntity> targets, HashSet<string> armedCategories, uint color, string batchId, List<ChangeLogEntry> logEntries, string sceneName)
        {
            var result = new Result();

            foreach (var target in targets)
            {
                if (!EntitySelector.IsValidEntity(target)) continue;

                // Composite prefabs (parent anchor + mesh data on children) need the full
                // hierarchy walked, not just the selected entity's own slots - see
                // EntitySelector.EnumerateSelfAndDescendants for why.
                foreach (var entity in EntitySelector.EnumerateSelfAndDescendants(target))
                {
                    if (!EntitySelector.IsValidEntity(entity)) continue;

                    for (int m = 0; m < entity.MultiMeshComponentCount; m++)
                    {
                        var meta = entity.GetMetaMesh(m);
                        if (meta == null || !meta.IsValid) continue;

                        for (int i = 0; i < meta.MeshCount; i++)
                        {
                            var mesh = meta.GetMeshAtIndex(i);
                            var materialName = mesh?.GetMaterial()?.Name;
                            var categories = MaterialCategoryInference.Classify(materialName);
                            if (categories.Count == 0) continue;
                            if (!categories.Exists(armedCategories.Contains)) continue;

                            var currentColor = mesh.Color;
                            if (currentColor == color) continue; // already this color - naturally idempotent, nothing to log

                            if (logEntries != null)
                            {
                                var frame = entity.GetGlobalFrame();
                                logEntries.Add(new ChangeLogEntry
                                {
                                    BatchId = batchId,
                                    TimestampUtc = DateTime.UtcNow,
                                    SceneName = sceneName,
                                    EntityName = entity.Name,
                                    // UID-stamped (2026-08-23, "you might have just totally broken
                                    // the undo?"): CR entries never carried UIDs, and RevertManager
                                    // NEVER auto-trusts a name+position match without one - so every
                                    // CR batch undid as all-low-confidence, i.e. did nothing.
                                    Uid = MaterialSwapEngine.EnsureTrackingUid(entity),
                                    MetaMeshIndex = m,
                                    MeshIndex = i,
                                    OldMeshColor = ColorHex.ToHex(currentColor),
                                    NewMeshColor = ColorHex.ToHex(color),
                                    PosX = frame.origin.x,
                                    PosY = frame.origin.y,
                                    PosZ = frame.origin.z,
                                    RotForwardX = frame.rotation.f.x,
                                    RotForwardY = frame.rotation.f.y,
                                    RotForwardZ = frame.rotation.f.z,
                                });
                            }

                            mesh.Color = color;
                            result.SlotsColored++;
                        }
                    }
                }
            }

            return result;
        }

        // Part B: bulk multi-category recoloring - each armed category carries its OWN color
        // (categoryColors) instead of the whole arm sharing one, applied in a single pass. A mesh
        // slot can classify into more than one category (Classify returns a List, not one value);
        // when more than one of its categories is in the palette, the first match in
        // MaterialCategoryInference.AllCategories order wins, for a deterministic result instead of
        // dictionary-iteration-order luck.
        // What colours are already ON the selection, grouped the way this tool thinks - by
        // category, not by material. Read-only: it walks exactly the same mesh traversal Apply
        // and ApplyPalette use, so what it reports is what those two would act on.
        //
        // A material can classify into SEVERAL categories (stone_wall_a is both "stone" and
        // "wall"), and that is kept rather than collapsed - its colour becomes a candidate for
        // every category it matches. Apply-time already resolves the overlap deterministically
        // (ApplyPalette takes the first armed category in AllCategories order), so recording all
        // of them here widens what you can seed without changing what gets painted.
        public class SampleResult
        {
            // category -> observed colour -> how many mesh slots had it. Ordered by count when read.
            public Dictionary<string, Dictionary<uint, int>> ColorsByCategory =
                new Dictionary<string, Dictionary<uint, int>>(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, int> SlotsByCategory =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // Materials that matched no category at all - the coverage gap, reported rather than
            // silently dropped, because these are exactly the ones worth adding in the editor.
            public HashSet<string> UncategorizedMaterials =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public int SlotsSeen;
        }

        // 0xFFFFFFFF is this engine family's identity/no-tint value (see ColorHex and
        // OverrideFillEngine, which skip it for the same reason). A white slot is "untinted",
        // not "tinted white", so it is not a colour worth seeding from.
        private const uint White = 0xFFFFFFFFu;

        public static SampleResult SampleSelection(List<GameEntity> targets)
        {
            var result = new SampleResult();
            if (targets == null) return result;

            foreach (var target in targets)
            {
                if (!EntitySelector.IsValidEntity(target)) continue;

                foreach (var entity in EntitySelector.EnumerateSelfAndDescendants(target))
                {
                    if (!EntitySelector.IsValidEntity(entity)) continue;

                    for (int m = 0; m < entity.MultiMeshComponentCount; m++)
                    {
                        var meta = entity.GetMetaMesh(m);
                        if (meta == null || !meta.IsValid) continue;

                        for (int i = 0; i < meta.MeshCount; i++)
                        {
                            var mesh = meta.GetMeshAtIndex(i);
                            var materialName = mesh?.GetMaterial()?.Name;
                            if (string.IsNullOrEmpty(materialName)) continue;

                            result.SlotsSeen++;

                            var categories = MaterialCategoryInference.Classify(materialName);
                            if (categories.Count == 0)
                            {
                                result.UncategorizedMaterials.Add(materialName);
                                continue;
                            }

                            uint color;
                            try { color = mesh.Color; } catch { continue; }

                            foreach (var category in categories)
                            {
                                result.SlotsByCategory.TryGetValue(category, out var slots);
                                result.SlotsByCategory[category] = slots + 1;

                                if (color == White) continue;   // untinted; nothing to sample

                                if (!result.ColorsByCategory.TryGetValue(category, out var counts))
                                {
                                    counts = new Dictionary<uint, int>();
                                    result.ColorsByCategory[category] = counts;
                                }
                                counts.TryGetValue(color, out var n);
                                counts[color] = n + 1;
                            }
                        }
                    }
                }
            }

            return result;
        }

        public static Result ApplyPalette(List<GameEntity> targets, Dictionary<string, uint> categoryColors, string batchId, List<ChangeLogEntry> logEntries, string sceneName)
        {
            var result = new Result();
            if (categoryColors == null || categoryColors.Count == 0) return result;

            var orderedCategories = MaterialCategoryInference.AllCategories
                .Where(categoryColors.ContainsKey)
                .ToList();

            foreach (var target in targets)
            {
                if (!EntitySelector.IsValidEntity(target)) continue;

                foreach (var entity in EntitySelector.EnumerateSelfAndDescendants(target))
                {
                    if (!EntitySelector.IsValidEntity(entity)) continue;

                    for (int m = 0; m < entity.MultiMeshComponentCount; m++)
                    {
                        var meta = entity.GetMetaMesh(m);
                        if (meta == null || !meta.IsValid) continue;

                        for (int i = 0; i < meta.MeshCount; i++)
                        {
                            var mesh = meta.GetMeshAtIndex(i);
                            var materialName = mesh?.GetMaterial()?.Name;
                            var categories = MaterialCategoryInference.Classify(materialName);
                            if (categories.Count == 0) continue;

                            var matchedCategory = orderedCategories.FirstOrDefault(categories.Contains);
                            if (matchedCategory == null) continue;
                            var color = categoryColors[matchedCategory];

                            var currentColor = mesh.Color;
                            if (currentColor == color) continue;

                            if (logEntries != null)
                            {
                                var frame = entity.GetGlobalFrame();
                                logEntries.Add(new ChangeLogEntry
                                {
                                    BatchId = batchId,
                                    TimestampUtc = DateTime.UtcNow,
                                    SceneName = sceneName,
                                    EntityName = entity.Name,
                                    // UID-stamped (2026-08-23, "you might have just totally broken
                                    // the undo?"): CR entries never carried UIDs, and RevertManager
                                    // NEVER auto-trusts a name+position match without one - so every
                                    // CR batch undid as all-low-confidence, i.e. did nothing.
                                    Uid = MaterialSwapEngine.EnsureTrackingUid(entity),
                                    MetaMeshIndex = m,
                                    MeshIndex = i,
                                    OldMeshColor = ColorHex.ToHex(currentColor),
                                    NewMeshColor = ColorHex.ToHex(color),
                                    PosX = frame.origin.x,
                                    PosY = frame.origin.y,
                                    PosZ = frame.origin.z,
                                    RotForwardX = frame.rotation.f.x,
                                    RotForwardY = frame.rotation.f.y,
                                    RotForwardZ = frame.rotation.f.z,
                                });
                            }

                            mesh.Color = color;
                            result.SlotsColored++;
                        }
                    }
                }
            }

            return result;
        }
    }
}
