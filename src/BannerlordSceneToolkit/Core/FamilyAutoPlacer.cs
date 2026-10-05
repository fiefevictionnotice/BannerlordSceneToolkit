using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace BannerlordSceneToolkit
{
    // Cross-reads PrefabCreatorTool's own persisted Combos\*.json - a plain JSON file format, not a
    // shared assembly reference, so the two mods stay independent (per the earlier decision to keep
    // them separable for crash A/B diagnosis) while still letting a secondary marked "Auto-Place On
    // New Instance" in PrefabCreatorTool's pairing UI actually get placed here, the moment THIS mod
    // places a fresh instance of that base prefab (Swap Selected/All Matching). Mirrors
    // PrefabCreatorEngine.ApplyOffset's math exactly (small and self-contained, duplicated rather
    // than shared for the same independence reason) - only that inverse transform is needed here,
    // since CaptureOffset itself only ever runs on the PrefabCreatorTool side.
    public static class FamilyAutoPlacer
    {
        private class ColorPresetOverrideData
        {
            public string Material;
            public string Color;
        }

        private class ComboMemberData
        {
            public string PrefabName;
            public string PresetName;
            public bool AutoPlaceOnNewInstance;
            public bool HasOffset;
            public float OffsetX, OffsetY, OffsetZ;
            public float RotSX = 1f, RotSY, RotSZ;
            public float RotFX, RotFY = 1f, RotFZ;
            public float RotUX, RotUY, RotUZ = 1f;
            // Denormalized copy of PresetName's own overrides, written by PrefabCreatorTool at
            // pairing/cycle time (see PrefabCombo.ComboMember.ResolvedOverrides on that side) - this
            // is the only file this mod ever needs to open to know both WHERE to place a secondary
            // and WHAT it should look like, rather than also having to open ColorPresets\*.json.
            public Dictionary<string, ColorPresetOverrideData> ResolvedOverrides;
        }

        private class ComboData
        {
            public string BasePrefabName;
            public List<ComboMemberData> Members = new List<ComboMemberData>();
        }

        private static string CombosDir => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "PrefabCreatorTool", "Combos");

        private static string FamiliesPath => System.IO.Path.Combine(CombosDir, "_families.json");

        private static string SafeFileName(string name)
        {
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            return new string((name ?? "").Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        private static ComboData TryLoadCombo(string key)
        {
            try
            {
                var path = System.IO.Path.Combine(CombosDir, SafeFileName(key) + ".json");
                if (!File.Exists(path)) return null;
                return JsonConvert.DeserializeObject<ComboData>(File.ReadAllText(path));
            }
            catch { return null; }
        }

        private static List<string> GetFamiliesForBase(string basePrefabName)
        {
            try
            {
                if (!File.Exists(FamiliesPath)) return new List<string>();
                var families = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(File.ReadAllText(FamiliesPath));
                if (families == null) return new List<string>();
                return families.Where(kv => kv.Value != null && kv.Value.Any(m => string.Equals(m, basePrefabName, StringComparison.OrdinalIgnoreCase)))
                    .Select(kv => kv.Key).ToList();
            }
            catch { return new List<string>(); }
        }

        public class AutoPlaceResult
        {
            public List<GameEntity> Placed = new List<GameEntity>();
            public List<string> Failed = new List<string>();
            // Placement succeeded but applying the paired texture set to it did not - kept separate
            // from Failed since the secondary is still physically there, just in its default texture.
            public List<string> TextureFailed = new List<string>();
        }

        // Called right after placing a fresh instance of prefabName at worldFrame - looks up
        // whether it (or a family it belongs to) has any AutoPlaceOnNewInstance secondaries
        // defined, and places each one at its stored relative offset if so. Never throws - a
        // missing/corrupt PrefabCreatorTool data file just means nothing gets auto-placed, not a
        // failure of whichever swap/distribute action triggered this.
        public static AutoPlaceResult PlaceAutoSecondaries(Scene scene, string prefabName, MatrixFrame worldFrame)
        {
            var result = new AutoPlaceResult();
            try
            {
                if (scene == null || string.IsNullOrWhiteSpace(prefabName)) return result;

                var members = new List<ComboMemberData>();
                var direct = TryLoadCombo(prefabName);
                if (direct?.Members != null) members.AddRange(direct.Members);

                foreach (var family in GetFamiliesForBase(prefabName))
                {
                    var famCombo = TryLoadCombo("family__" + family);
                    if (famCombo?.Members != null) members.AddRange(famCombo.Members);
                }

                foreach (var member in members.Where(m => m.AutoPlaceOnNewInstance && !string.IsNullOrWhiteSpace(m.PrefabName)))
                {
                    try
                    {
                        var placeFrame = member.HasOffset ? ApplyOffset(worldFrame, member) : worldFrame;
                        var instance = GameEntity.Instantiate(scene, member.PrefabName.Trim(), placeFrame, true);
                        if (instance == null) { result.Failed.Add($"'{member.PrefabName}': instantiate returned null"); continue; }
                        result.Placed.Add(instance);

                        if (member.ResolvedOverrides != null && member.ResolvedOverrides.Count > 0)
                        {
                            try { ApplyResolvedOverrides(instance, member.ResolvedOverrides); }
                            catch (Exception ex) { result.TextureFailed.Add($"'{member.PrefabName}': {ex.Message}"); }
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Failed.Add($"'{member.PrefabName}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("FamilyAutoPlacer.PlaceAutoSecondaries failed: " + ex.Message);
            }
            return result;
        }

        // Mirrors PrefabCreatorTool's own ColorPresetApplier.Apply exactly (same two-pass
        // resolution: the target's own mesh slots by exact name, then its immediate children by
        // stripped duplicate-suffix name) - duplicated here rather than referenced since the two
        // mods stay assembly-independent by design. The override data itself arrives pre-resolved
        // on the ComboMemberData (see FamilyAutoPlacer's own ComboMemberData.ResolvedOverrides),
        // so this never needs to touch ColorPresets\*.json itself.
        private static void ApplyResolvedOverrides(GameEntity target, Dictionary<string, ColorPresetOverrideData> overrides)
        {
            for (int m = 0; m < target.MultiMeshComponentCount; m++)
            {
                var meta = target.GetMetaMesh(m);
                if (meta == null || !meta.IsValid) continue;
                for (int i = 0; i < meta.MeshCount; i++)
                {
                    var mesh = meta.GetMeshAtIndex(i);
                    if (mesh == null || string.IsNullOrEmpty(mesh.Name)) continue;
                    if (!overrides.TryGetValue(mesh.Name, out var ov)) continue;
                    if (!string.IsNullOrWhiteSpace(ov.Material)) mesh.SetMaterial(ov.Material);
                    if (!string.IsNullOrWhiteSpace(ov.Color) && ColorHex.TryParse(ov.Color, out var colorValue))
                        mesh.Color = colorValue;
                }
            }

            foreach (var child in target.GetChildren())
            {
                if (child == null || string.IsNullOrEmpty(child.Name)) continue;
                var baseName = StripDuplicateSuffix(child.Name);
                if (!overrides.TryGetValue(baseName, out var ov)) continue;

                for (int m = 0; m < child.MultiMeshComponentCount; m++)
                {
                    var meta = child.GetMetaMesh(m);
                    if (meta == null || !meta.IsValid) continue;
                    for (int i = 0; i < meta.MeshCount; i++)
                    {
                        var mesh = meta.GetMeshAtIndex(i);
                        if (mesh == null) continue;
                        if (!string.IsNullOrWhiteSpace(ov.Material)) mesh.SetMaterial(ov.Material);
                        if (!string.IsNullOrWhiteSpace(ov.Color) && ColorHex.TryParse(ov.Color, out var colorValue))
                            mesh.Color = colorValue;
                    }
                }
            }
        }

        private static string StripDuplicateSuffix(string name) => Regex.Replace(name ?? "", @"\.\d+$", "");

        private static MatrixFrame ApplyOffset(MatrixFrame anchor, ComboMemberData offset)
        {
            var origin = anchor.origin
                + anchor.rotation.s * offset.OffsetX
                + anchor.rotation.f * offset.OffsetY
                + anchor.rotation.u * offset.OffsetZ;

            var s = anchor.rotation.s * offset.RotSX + anchor.rotation.f * offset.RotSY + anchor.rotation.u * offset.RotSZ;
            var f = anchor.rotation.s * offset.RotFX + anchor.rotation.f * offset.RotFY + anchor.rotation.u * offset.RotFZ;
            var u = anchor.rotation.s * offset.RotUX + anchor.rotation.f * offset.RotUY + anchor.rotation.u * offset.RotUZ;

            return new MatrixFrame(new Mat3(s, f, u), origin);
        }
    }
}
