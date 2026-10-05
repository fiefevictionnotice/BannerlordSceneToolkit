using System;
using System.Linq;
using System.Text.RegularExpressions;
using TaleWorlds.Engine;
using BannerlordSceneToolkit;

namespace PrefabCreatorTool.Core
{
    public static class ColorPresetApplier
    {
        private static string StripDuplicateSuffix(string name) => Regex.Replace(name ?? "", @"\.\d+$", "");

        // Applies every override in the preset that matches something on the target: first tries
        // the target's OWN mesh slots by exact name (single-entity case), then its immediate
        // children by stripped base name (cluster case), applying material and/or color to every
        // mesh slot of a matched child. Returns how many overrides actually landed, so the caller
        // can tell a preset that mostly doesn't apply to this target from one that worked.
        public static int Apply(GameEntity target, ColorPreset preset)
        {
            if (target == null || preset == null) return 0;
            int applied = 0;

            for (int m = 0; m < target.MultiMeshComponentCount; m++)
            {
                var meta = target.GetMetaMesh(m);
                if (meta == null || !meta.IsValid) continue;
                for (int i = 0; i < meta.MeshCount; i++)
                {
                    var mesh = meta.GetMeshAtIndex(i);
                    if (mesh == null || string.IsNullOrEmpty(mesh.Name)) continue;

                    // Matched by the NORMALIZED mesh name too (2026-08-23, "doesn't work when I
                    // made my own and tried to apply it"): a simple prop's meshes carry dot
                    // suffixes and LOD segments ('log_beech_c.1', 'log_beech_c.lod2'), so the
                    // exact match against a key like 'log_beech_c' found nothing - and with no
                    // child entities either, the preset landed zero overrides, silently.
                    var meshKey = StripDuplicateSuffix(
                        MaterialSwapTool.Core.LodMismatchChecker.StripLodSegment(mesh.Name, out _));
                    if (!preset.Overrides.TryGetValue(mesh.Name, out var ov) &&
                        !preset.Overrides.TryGetValue(meshKey, out ov)) continue;

                    if (!string.IsNullOrWhiteSpace(ov.Material)) mesh.SetMaterial(ov.Material);
                    if (!string.IsNullOrWhiteSpace(ov.Color) && ColorHex.TryParse(ov.Color, out var c)) mesh.Color = c;
                    applied++;
                }
            }

            foreach (var child in target.GetChildren())
            {
                if (!EntitySelector.IsValidEntity(child)) continue;
                var baseName = StripDuplicateSuffix(child.Name);
                if (!preset.Overrides.TryGetValue(baseName, out var ov)) continue;

                for (int m = 0; m < child.MultiMeshComponentCount; m++)
                {
                    var meta = child.GetMetaMesh(m);
                    if (meta == null || !meta.IsValid) continue;
                    for (int i = 0; i < meta.MeshCount; i++)
                    {
                        var mesh = meta.GetMeshAtIndex(i);
                        if (mesh == null) continue;
                        if (!string.IsNullOrWhiteSpace(ov.Material)) mesh.SetMaterial(ov.Material);
                        if (!string.IsNullOrWhiteSpace(ov.Color) && ColorHex.TryParse(ov.Color, out var c)) mesh.Color = c;
                    }
                }
                applied++;
            }

            return applied;
        }

        // Builds a ColorPreset directly from a Mode 2b (same-prefab-instance) detection result -
        // the "turn what I already hand-placed into a reusable preset" workflow.
        public static ColorPreset FromInstanceDiff(string presetName, string basePrefabName, System.Collections.Generic.List<MeshSlotDiff> diffs)
        {
            var preset = new ColorPreset { Name = presetName, BasePrefabName = basePrefabName };
            foreach (var d in diffs)
            {
                preset.Overrides[d.SlotName] = new ColorPresetOverride
                {
                    Material = d.MaterialDiffers ? d.VariantMaterial : null,
                    Color = d.ColorDiffers ? ColorHex.ToHex(d.VariantColor) : null,
                };
            }
            return preset;
        }

        // Builds a ColorPreset from a Mode 2a (cluster) detection result - keyed by each differing
        // child's own stripped base name instead of a mesh slot name.
        public static ColorPreset FromClusterDiff(string presetName, string basePrefabName, System.Collections.Generic.List<PartMatch> parts)
        {
            var preset = new ColorPreset { Name = presetName, BasePrefabName = basePrefabName };
            foreach (var p in parts.Where(p => p.MaterialDiffers))
            {
                var key = StripDuplicateSuffix(p.VariantMember.Name);
                preset.Overrides[key] = new ColorPresetOverride { Material = p.VariantMaterial };
            }
            return preset;
        }
    }
}
