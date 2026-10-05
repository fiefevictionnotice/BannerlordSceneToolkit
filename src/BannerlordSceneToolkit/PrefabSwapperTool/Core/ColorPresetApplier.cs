using System.Linq;
using System.Text.RegularExpressions;
using TaleWorlds.Engine;
using BannerlordSceneToolkit;

namespace PrefabSwapperTool.Core
{
    // Ported from PrefabCreatorTool.Core.ColorPresetApplier - same two-pass resolution (own mesh
    // slots by exact name, then immediate children by stripped duplicate-suffix name).
    public static class ColorPresetApplier
    {
        private static string StripDuplicateSuffix(string name) => Regex.Replace(name ?? "", @"\.\d+$", "");

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
                    if (!preset.Overrides.TryGetValue(mesh.Name, out var ov)) continue;

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
    }
}
