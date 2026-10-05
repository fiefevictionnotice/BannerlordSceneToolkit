using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using BannerlordSceneToolkit;

namespace MaterialSwapTool.Core
{
    // Verified live (2026-08-16, european_city_house_d / _d2 via the Dump Slots diagnostic):
    // MultiMeshComponentCount is NOT the LOD indicator - a normal building has exactly ONE
    // MetaMesh component containing every mesh slot across every detail tier. The tiers are
    // distinguished by (a) a "lodN" segment inserted into the mesh's dot-separated name
    // ("house_d.6" full detail vs "house_d.lod5.6" the LOD5 copy of that same part) and
    // (b) GetLodMaskForMeshAtIndex. Some slots exist only at full detail (dropped entirely at
    // LOD5 - not a mismatch, that's the LOD system working as intended) and some exist only at
    // LOD5 (a merged/simplified part with no full-detail counterpart - also not a mismatch). A
    // TRUE mismatch is a slot that has a counterpart in both tiers whose materials OR colors
    // disagree - that's the signature of an edit (ours or manual) that touched one tier and not
    // the other.
    //
    // Color mismatches confirmed live too (2026-08-16): european_city_house_d and _d3 both showed
    // a non-white Mesh.Color on their LOD5 timber_frame_c slot while the full-detail tier of the
    // same part stayed white - a visible color "pop" when the LOD switches. This is a real,
    // independent failure mode from material mismatches (same detection shape, different field),
    // flagged separately per your original ask to add color-factor coverage to this checker.
    public static class LodMismatchChecker
    {
        public class Mismatch
        {
            public GameEntity Entity;
            public string EntityName;
            public int MetaMeshIndex;
            public int FullDetailMeshIndex;
            public int LodMeshIndex;
            public string FullDetailMeshName;
            public string LodMeshName;
            // Exactly one of these is non-null depending on what mismatched.
            public string MaterialFull;
            public string MaterialLod;
            public uint? ColorFull;
            public uint? ColorLod;
        }

        private class MeshSlot
        {
            public int MetaMeshIndex;
            public int MeshIndex;
            public string MeshName;
            public string Material;
            public uint Color;
        }

        public static List<Mismatch> Check(List<GameEntity> targets)
        {
            var mismatches = new List<Mismatch>();

            foreach (var target in targets)
            {
                if (!EntitySelector.IsValidEntity(target)) continue;

                // Composite prefabs (parent anchor + mesh data on children) need the full
                // hierarchy walked, not just the selected entity's own slots - see
                // EntitySelector.EnumerateSelfAndDescendants for why. Full/LOD pairs are matched
                // PER ENTITY (not merged across the whole hierarchy) since a base name like "6"
                // could legitimately repeat on unrelated children - only compare tiers found on the
                // same entity.
                foreach (var entity in EntitySelector.EnumerateSelfAndDescendants(target))
                {
                    if (!EntitySelector.IsValidEntity(entity)) continue;

                    var fullByBaseName = new Dictionary<string, MeshSlot>(StringComparer.OrdinalIgnoreCase);
                    var lodEntries = new List<(string baseName, MeshSlot slot)>();

                    for (int m = 0; m < entity.MultiMeshComponentCount; m++)
                    {
                        var meta = entity.GetMetaMesh(m);
                        if (meta == null || !meta.IsValid) continue;

                        for (int i = 0; i < meta.MeshCount; i++)
                        {
                            var mesh = meta.GetMeshAtIndex(i);
                            if (mesh == null) continue;
                            var material = mesh.GetMaterial()?.Name;
                            if (material == null) continue;

                            var slot = new MeshSlot { MetaMeshIndex = m, MeshIndex = i, MeshName = mesh.Name, Material = material, Color = mesh.Color };
                            var baseName = StripLodSegment(mesh.Name, out bool isLod);
                            if (isLod)
                                lodEntries.Add((baseName, slot));
                            else
                                fullByBaseName[baseName] = slot;
                        }
                    }

                    foreach (var (baseName, lod) in lodEntries)
                    {
                        if (!fullByBaseName.TryGetValue(baseName, out var full)) continue;

                        // A material mismatch is only actionable if the full-detail side carries a
                        // REAL material to copy down. When GetMaterial() reads back the engine's
                        // fallback shader (default_regular and its default_* siblings), that means
                        // "no material resolved here", not "this is the right material" - propagating
                        // it onto the LOD5 slot would overwrite a good LOD material with the engine
                        // placeholder. Skip those entirely so they're never offered or applied.
                        if (!string.Equals(full.Material, lod.Material, StringComparison.OrdinalIgnoreCase)
                            && !IsPlaceholderDefault(full.Material))
                        {
                            mismatches.Add(new Mismatch
                            {
                                Entity = entity,
                                EntityName = entity.Name,
                                MetaMeshIndex = full.MetaMeshIndex,
                                FullDetailMeshIndex = full.MeshIndex,
                                LodMeshIndex = lod.MeshIndex,
                                FullDetailMeshName = full.MeshName,
                                LodMeshName = lod.MeshName,
                                MaterialFull = full.Material,
                                MaterialLod = lod.Material,
                            });
                        }

                        if (full.Color != lod.Color)
                        {
                            mismatches.Add(new Mismatch
                            {
                                Entity = entity,
                                EntityName = entity.Name,
                                MetaMeshIndex = full.MetaMeshIndex,
                                FullDetailMeshIndex = full.MeshIndex,
                                LodMeshIndex = lod.MeshIndex,
                                FullDetailMeshName = full.MeshName,
                                LodMeshName = lod.MeshName,
                                ColorFull = full.Color,
                                ColorLod = lod.Color,
                            });
                        }
                    }
                }
            }

            return mismatches;
        }

        // Syncs each color-mismatched LOD5 slot to match its full-detail counterpart's color -
        // the full-detail tier is what you actually see up close, so that's the "correct" value;
        // LOD5 catching up eliminates the pop when the LOD switches. A color mismatch is never
        // intentional (nobody varies tint per LOD tier on purpose), so this always applies cleanly.
        public static int FixColorMismatches(List<Mismatch> mismatches, string batchId, List<ChangeLogEntry> logEntries, string sceneName)
        {
            int fixedCount = 0;
            foreach (var mm in mismatches.Where(m => m.ColorFull.HasValue))
            {
                if (!EntitySelector.IsValidEntity(mm.Entity)) continue;
                var meta = mm.Entity.GetMetaMesh(mm.MetaMeshIndex);
                if (meta == null || !meta.IsValid) continue;
                var lodMesh = meta.GetMeshAtIndex(mm.LodMeshIndex);
                if (lodMesh == null) continue;

                var oldColor = lodMesh.Color;
                var newColor = mm.ColorFull.Value;
                if (oldColor == newColor) continue;

                if (logEntries != null)
                {
                    var frame = mm.Entity.GetGlobalFrame();
                    logEntries.Add(new ChangeLogEntry
                    {
                        BatchId = batchId,
                        TimestampUtc = DateTime.UtcNow,
                        SceneName = sceneName,
                        EntityName = mm.EntityName,
                        MetaMeshIndex = mm.MetaMeshIndex,
                        MeshIndex = mm.LodMeshIndex,
                        OldMeshColor = ColorHex.ToHex(oldColor),
                        NewMeshColor = ColorHex.ToHex(newColor),
                        PosX = frame.origin.x,
                        PosY = frame.origin.y,
                        PosZ = frame.origin.z,
                        RotForwardX = frame.rotation.f.x,
                        RotForwardY = frame.rotation.f.y,
                        RotForwardZ = frame.rotation.f.z,
                    });
                }

                lodMesh.Color = newColor;
                fixedCount++;
            }
            return fixedCount;
        }

        // Syncs each material-mismatched LOD5 slot to match its full-detail counterpart's material,
        // same "full detail is what you actually see up close, so it wins" convention as color
        // above. UNLIKE color, this direction is a judgment call, not a certainty - a LOD tier's
        // material can legitimately be a deliberate cheaper substitute (e.g. a simplified flat
        // material swapped in for a detailed one specifically to cut cost at distance), not
        // necessarily a bug. The caller (MaterialSwapVM) confirm-gates this with an explicit warning
        // about that before calling it - unlike color, which just runs.
        public static int FixMaterialMismatches(List<Mismatch> mismatches, string batchId, List<ChangeLogEntry> logEntries, string sceneName)
        {
            int fixedCount = 0;
            foreach (var mm in mismatches.Where(m => m.MaterialFull != null))
            {
                if (!EntitySelector.IsValidEntity(mm.Entity)) continue;
                var meta = mm.Entity.GetMetaMesh(mm.MetaMeshIndex);
                if (meta == null || !meta.IsValid) continue;
                var lodMesh = meta.GetMeshAtIndex(mm.LodMeshIndex);
                if (lodMesh == null) continue;

                var oldMaterial = lodMesh.GetMaterial()?.Name;
                var newMaterial = mm.MaterialFull;
                // Never stamp the engine's fallback shader onto a slot - it's not a real material,
                // it's "nothing resolved". Belt-and-suspenders: Check already filters these out.
                if (IsPlaceholderDefault(newMaterial)) continue;
                if (string.Equals(oldMaterial, newMaterial, StringComparison.OrdinalIgnoreCase)) continue;

                if (logEntries != null)
                {
                    var frame = mm.Entity.GetGlobalFrame();
                    logEntries.Add(new ChangeLogEntry
                    {
                        BatchId = batchId,
                        TimestampUtc = DateTime.UtcNow,
                        SceneName = sceneName,
                        EntityName = mm.EntityName,
                        MetaMeshIndex = mm.MetaMeshIndex,
                        MeshIndex = mm.LodMeshIndex,
                        OldMaterial = oldMaterial,
                        NewMaterial = newMaterial,
                        PosX = frame.origin.x,
                        PosY = frame.origin.y,
                        PosZ = frame.origin.z,
                        RotForwardX = frame.rotation.f.x,
                        RotForwardY = frame.rotation.f.y,
                        RotForwardZ = frame.rotation.f.z,
                    });
                }

                lodMesh.SetMaterial(newMaterial);
                fixedCount++;
            }
            return fixedCount;
        }

        // The engine's built-in fallback shaders. GetMaterial() returns one of these when a mesh
        // slot has no real material resolved (helper/utility meshes, or a slot whose material didn't
        // load). They are never legitimate art materials, so the mismatch fixer must not treat one
        // as the authoritative value to copy onto another slot - it would silently blank a good
        // material to the engine default. Matches "default" and the whole "default_*" family
        // (default_regular, default_regular_doubleuv, default_skinning, default_overlay*, ...).
        public static bool IsPlaceholderDefault(string materialName)
        {
            if (string.IsNullOrWhiteSpace(materialName)) return true;
            var n = materialName.Trim();
            return n.Equals("default", StringComparison.OrdinalIgnoreCase)
                || n.StartsWith("default_", StringComparison.OrdinalIgnoreCase);
        }

        // "house_d.lod5.6" -> ("house_d.6", isLod=true). "house_d.lod5" (bare) -> ("house_d", true).
        // "house_d.6" (no lod segment) -> ("house_d.6", false), unchanged.
        // Public (2026-08-23): MaterialSwapEngine seeds its weighted-target pick by the
        // LOD-stripped part name - all tiers of a part must share one roll - and
        // ApplyMaterialLayout pairs slots by it. This is the established "same part,
        // different tier" identity; don't reinvent it elsewhere.
        public static string StripLodSegment(string meshName, out bool isLod)
        {
            isLod = false;
            if (string.IsNullOrEmpty(meshName)) return meshName;

            var parts = meshName.Split('.');
            var lodIndex = Array.FindIndex(parts, p => p.StartsWith("lod", StringComparison.OrdinalIgnoreCase));
            if (lodIndex < 0) return meshName;

            isLod = true;
            var kept = parts.Where((_, i) => i != lodIndex).ToArray();
            return string.Join(".", kept);
        }
    }
}
