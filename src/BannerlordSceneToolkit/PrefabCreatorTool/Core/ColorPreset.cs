using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace PrefabCreatorTool.Core
{
    public class ColorPresetOverride
    {
        // Either or both may be set - a preset entry can retexture (Material), retint (Color), or
        // both, matching the two independent mechanisms seen on real placed furniture (mesh
        // material name, and per-mesh Factor1 color).
        public string Material { get; set; }
        public string Color { get; set; }
    }

    // A named, reusable "recolor this specific placed prefab like this" definition - the standalone
    // alternative to hand-authoring N separately-colored prefab variants. Deliberately its own
    // system, independent of MaterialSwapTool's presets (per user's decision - cross-import/export
    // deferred rather than coupling the two mods together now).
    //
    // Overrides is keyed by PART KEY, which ColorPresetApplier resolves two ways: first as a mesh
    // slot name on the target entity itself (the single-entity-with-many-mesh-slots case, e.g.
    // "bd_table_c.0"), then as a child entity's own stripped base name (the multi-entity cluster
    // case, e.g. "pile_of_books_a"). One preset shape covers both detection modes.
    public class ColorPreset
    {
        public string Name { get; set; } = "Untitled Preset";
        public string BasePrefabName { get; set; } = "";
        public Dictionary<string, ColorPresetOverride> Overrides { get; set; } = new Dictionary<string, ColorPresetOverride>(StringComparer.OrdinalIgnoreCase);
    }

    public static class ColorPresetStore
    {
        private static string PresetsDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "PrefabCreatorTool", "ColorPresets");

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        public static List<string> ListNames()
        {
            if (!Directory.Exists(PresetsDir)) return new List<string>();
            return Directory.EnumerateFiles(PresetsDir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<ColorPreset> ListForBasePrefab(string basePrefabName)
        {
            return ListNames()
                .Select(TryLoad)
                .Where(p => p != null && string.Equals(p.BasePrefabName, basePrefabName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public static void Save(ColorPreset preset)
        {
            Directory.CreateDirectory(PresetsDir);
            var path = Path.Combine(PresetsDir, SafeFileName(preset.Name) + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(preset, Formatting.Indented));
            Log.Info($"Saved color preset '{preset.Name}' ({preset.Overrides.Count} override(s)) to {path}");
        }

        public static ColorPreset Load(string name)
        {
            var path = Path.Combine(PresetsDir, SafeFileName(name) + ".json");
            if (!File.Exists(path)) throw new FileNotFoundException($"No color preset named '{name}' found in {PresetsDir}");
            return JsonConvert.DeserializeObject<ColorPreset>(File.ReadAllText(path))
                   ?? throw new InvalidDataException($"Preset '{name}' failed to parse.");
        }

        private static ColorPreset TryLoad(string name)
        {
            try { return Load(name); }
            catch (Exception ex) { Log.Warn($"Skipping unreadable preset '{name}': {ex.Message}"); return null; }
        }

        public static void Delete(string name)
        {
            var path = Path.Combine(PresetsDir, SafeFileName(name) + ".json");
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
