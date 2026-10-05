using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;

namespace PrefabCreatorTool.Core
{
    public class MeshSlotDiff
    {
        public string SlotName;
        public uint AnchorColor;
        public uint VariantColor;
        public string AnchorMaterial;
        public string VariantMaterial;
        public bool ColorDiffers;
        public bool MaterialDiffers;
    }

    public class PrefabInstanceGroup
    {
        public GameEntity Anchor;
        public List<(GameEntity Entity, List<MeshSlotDiff> Diffs)> Variants = new List<(GameEntity, List<MeshSlotDiff>)>();
    }

    // Second detection mode, alongside VariantDetector's multi-entity clustering - confirmed
    // necessary via a live scene check (fief_prefab_creation_9_greebles_only): a table there is a
    // SINGLE entity with all its geometry as mesh slots in its own MetaMesh, placed twice a few
    // units apart with different per-mesh Factor1 values - structurally invisible to cluster-based
    // detection, which requires 2+ separate top-level entities. This handles that shape instead:
    // group live entities by NAME (which resolves to the prefab name for an unnamed placement like
    // <game_entity prefab="bd_table_c">), then diff each instance's own mesh-slot color/material
    // pattern against the first instance in that name-group.
    public static class PrefabInstanceColorDetector
    {
        public static List<PrefabInstanceGroup> Detect(List<GameEntity> allEntities, string requiredTag)
        {
            var candidates = allEntities
                .Where(e => EntitySelector.IsValidEntity(e) && !string.IsNullOrEmpty(e.Name))
                .Where(e => string.IsNullOrEmpty(requiredTag) || e.HasTag(requiredTag))
                .ToList();

            var results = new List<PrefabInstanceGroup>();
            foreach (var group in candidates.GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                var list = group.ToList();
                if (list.Count < 2) continue;

                var anchor = list[0];
                var anchorSlots = GetMeshSlots(anchor);
                if (anchorSlots.Count == 0) continue;

                var pgroup = new PrefabInstanceGroup { Anchor = anchor };
                for (int i = 1; i < list.Count; i++)
                {
                    var slots = GetMeshSlots(list[i]);
                    var diffs = DiffSlots(anchorSlots, slots);
                    if (diffs.Count > 0) pgroup.Variants.Add((list[i], diffs));
                }

                if (pgroup.Variants.Count > 0) results.Add(pgroup);
            }

            return results;
        }

        private static List<MeshSlotDiff> DiffSlots(Dictionary<string, (uint color, string material)> anchorSlots, Dictionary<string, (uint color, string material)> otherSlots)
        {
            var diffs = new List<MeshSlotDiff>();
            foreach (var kv in anchorSlots)
            {
                if (!otherSlots.TryGetValue(kv.Key, out var other)) continue; // different mesh layout entirely - not a same-shape instance, skip that slot rather than reject the whole pair

                var colorDiffers = kv.Value.color != other.color;
                var materialDiffers = !string.Equals(kv.Value.material, other.material, StringComparison.OrdinalIgnoreCase);
                if (!colorDiffers && !materialDiffers) continue;

                diffs.Add(new MeshSlotDiff
                {
                    SlotName = kv.Key,
                    AnchorColor = kv.Value.color,
                    VariantColor = other.color,
                    AnchorMaterial = kv.Value.material,
                    VariantMaterial = other.material,
                    ColorDiffers = colorDiffers,
                    MaterialDiffers = materialDiffers,
                });
            }
            return diffs;
        }

        private static Dictionary<string, (uint color, string material)> GetMeshSlots(GameEntity entity)
        {
            var result = new Dictionary<string, (uint, string)>(StringComparer.OrdinalIgnoreCase);
            for (int m = 0; m < entity.MultiMeshComponentCount; m++)
            {
                var meta = entity.GetMetaMesh(m);
                if (meta == null || !meta.IsValid) continue;
                for (int i = 0; i < meta.MeshCount; i++)
                {
                    var mesh = meta.GetMeshAtIndex(i);
                    if (mesh == null || string.IsNullOrEmpty(mesh.Name)) continue;
                    result[mesh.Name] = (mesh.Color, mesh.GetMaterial()?.Name);
                }
            }
            return result;
        }
    }
}
