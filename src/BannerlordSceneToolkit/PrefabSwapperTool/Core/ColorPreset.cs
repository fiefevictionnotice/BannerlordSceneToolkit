using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace PrefabSwapperTool.Core
{
    // Ported from PrefabCreatorTool.Core.ColorPreset rather than referenced (the two mods stay
    // assembly-independent by design, per the crash A/B isolation decision) - but points at the
    // SAME folder on disk, so a texture set authored in either mod's browser is immediately usable
    // in the other's. Shared DATA, not a shared library.
    public class ColorPresetOverride
    {
        public string Material { get; set; }
        public string Color { get; set; }
    }

    public class ColorPreset
    {
        public string Name { get; set; } = "Untitled Preset";
        public string BasePrefabName { get; set; } = "";
        public Dictionary<string, ColorPresetOverride> Overrides { get; set; } = new Dictionary<string, ColorPresetOverride>(StringComparer.OrdinalIgnoreCase);
    }

    public static class ColorPresetStore
    {
        // Deliberately PrefabCreatorTool's own folder, not a PrefabSwapperTool-specific one - this
        // is what makes the data genuinely shared rather than a second, divergent copy.
        private static string PresetsDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "PrefabCreatorTool", "ColorPresets");

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string((name ?? "").Select(c => invalid.Contains(c) ? '_' : c).ToArray());
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

        // EXPORT / IMPORT. Same convention as the Material Swap tool's presets and the Prefab
        // Creator's pile recipes: a folder you can zip and send, its own subfolder so a scan for
        // one kind of file cannot swallow another, and an import that keeps YOUR copy on a name
        // clash rather than silently overwriting work.
        public static string ExportDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "PrefabSwapperTool", "Exports", "TextureSets");

        public static void OpenExportFolder()
        {
            Directory.CreateDirectory(ExportDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ExportDir) { UseShellExecute = true });
        }

        public static (int exported, string dir) Export(string name = null)
        {
            Directory.CreateDirectory(ExportDir);
            var wanted = string.IsNullOrWhiteSpace(name) ? ListNames() : new List<string> { name };
            int exported = 0;
            foreach (var n in wanted)
            {
                try
                {
                    var preset = Load(n);
                    File.WriteAllText(Path.Combine(ExportDir, SafeFileName(preset.Name) + ".json"),
                                      JsonConvert.SerializeObject(preset, Formatting.Indented));
                    exported++;
                }
                catch (Exception ex) { Log.Warn($"Texture set export failed for '{n}': {ex.Message}"); }
            }
            Log.Info($"[TextureSetIO] exported={exported} to {ExportDir}");
            return (exported, ExportDir);
        }

        // Validated, not trusted: a set with no pairs would import cleanly and then swap nothing,
        // which is harder to diagnose than a refusal.
        public static (int imported, int skipped, List<string> problems) Import(bool overwriteExisting)
        {
            var problems = new List<string>();
            int imported = 0, skipped = 0;

            if (!Directory.Exists(ExportDir))
                return (0, 0, new List<string> { "No Exports\\TextureSets folder yet - export something first, or drop .json files in there." });

            Directory.CreateDirectory(PresetsDir);
            foreach (var file in Directory.GetFiles(ExportDir, "*.json"))
            {
                var fileName = Path.GetFileName(file);
                try
                {
                    var preset = JsonConvert.DeserializeObject<ColorPreset>(File.ReadAllText(file));
                    if (preset == null || string.IsNullOrWhiteSpace(preset.Name))
                    { problems.Add($"{fileName}: not a texture set (no Name)"); skipped++; continue; }

                    var usable = preset.Overrides?.Count ?? 0;
                    if (usable == 0)
                    { problems.Add($"{fileName}: texture set '{preset.Name}' has no overrides"); skipped++; continue; }

                    var target = Path.Combine(PresetsDir, SafeFileName(preset.Name) + ".json");
                    if (File.Exists(target) && !overwriteExisting)
                    { problems.Add($"{fileName}: '{preset.Name}' already exists (kept yours)"); skipped++; continue; }

                    Save(preset);
                    imported++;
                }
                catch (Exception ex) { problems.Add($"{fileName}: {ex.Message}"); skipped++; }
            }

            Log.Info($"[TextureSetIO] imported={imported} skipped={skipped}");
            return (imported, skipped, problems);
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
