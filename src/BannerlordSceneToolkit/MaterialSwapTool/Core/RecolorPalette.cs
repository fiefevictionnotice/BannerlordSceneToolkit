using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MaterialSwapTool.Core
{
    // A saved category->color mapping for Continuous Recolor's palette mode (Part B) - lets you
    // save/load a whole multi-category recolor scheme at once instead of re-toggling and re-typing
    // colors by hand every time. One JSON file per palette, same personal-folder convention as
    // MaterialSwapPreset, deliberately without that class's BuiltIn/history/tags machinery - a
    // palette is just a name and a color per category, there's no equivalent need for versioning.
    public class RecolorPalette
    {
        public string Name { get; set; } = "Untitled Palette";
        public Dictionary<string, string> CategoryColors { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static string PersonalPalettesDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "MaterialSwapTool", "Palettes");

        public static List<string> ListNames()
        {
            if (!Directory.Exists(PersonalPalettesDir)) return new List<string>();
            return Directory.EnumerateFiles(PersonalPalettesDir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public void Save()
        {
            Directory.CreateDirectory(PersonalPalettesDir);
            var path = Path.Combine(PersonalPalettesDir, SafeFileName(Name) + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            Log.Info($"Saved recolor palette '{Name}' ({CategoryColors.Count} categor(y/ies)) to {path}");
        }

        public static RecolorPalette Load(string name)
        {
            var path = Path.Combine(PersonalPalettesDir, SafeFileName(name) + ".json");
            if (!File.Exists(path)) throw new FileNotFoundException($"No palette named '{name}' found in {PersonalPalettesDir}");
            return JsonConvert.DeserializeObject<RecolorPalette>(File.ReadAllText(path))
                   ?? throw new InvalidDataException($"Palette '{name}' failed to parse.");
        }

        // Shares MaterialSwapPreset's Exports folder deliberately - one place to look, one folder
        // to zip and send, rather than two parallel export locations to explain and keep straight.
        // Palettes land in an Exports\Palettes subfolder so an import of one can't pick up the
        // other: a preset and a palette are both "a .json with a Name" and would otherwise be
        // indistinguishable to a folder scan.
        public static string ExportDir => Path.Combine(MaterialSwapPreset.ExportDir, "Palettes");

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
                    var palette = Load(n);
                    File.WriteAllText(Path.Combine(ExportDir, SafeFileName(palette.Name) + ".json"),
                                      JsonConvert.SerializeObject(palette, Formatting.Indented));
                    exported++;
                }
                catch (Exception ex) { Log.Warn($"Palette export failed for '{n}': {ex.Message}"); }
            }
            return (exported, ExportDir);
        }

        // Validated like preset import: a file that parses but carries no category colours would
        // otherwise import as a palette that loads and changes nothing.
        public static (int imported, int skipped, List<string> problems) Import(bool overwriteExisting)
        {
            var problems = new List<string>();
            int imported = 0, skipped = 0;
            if (!Directory.Exists(ExportDir))
                return (0, 0, new List<string> { "No Exports\\Palettes folder yet - export something first, or drop .json files in there." });

            Directory.CreateDirectory(PersonalPalettesDir);
            foreach (var file in Directory.GetFiles(ExportDir, "*.json"))
            {
                var fileName = Path.GetFileName(file);
                try
                {
                    var palette = JsonConvert.DeserializeObject<RecolorPalette>(File.ReadAllText(file));
                    if (palette == null || string.IsNullOrWhiteSpace(palette.Name))
                    { problems.Add($"{fileName}: not a palette (no Name)"); skipped++; continue; }
                    if (palette.CategoryColors == null || palette.CategoryColors.Count == 0)
                    { problems.Add($"{fileName}: palette '{palette.Name}' has no category colours"); skipped++; continue; }

                    var target = Path.Combine(PersonalPalettesDir, SafeFileName(palette.Name) + ".json");
                    if (File.Exists(target) && !overwriteExisting)
                    { problems.Add($"{fileName}: '{palette.Name}' already exists (kept yours)"); skipped++; continue; }

                    palette.Save();
                    imported++;
                }
                catch (Exception ex) { problems.Add($"{fileName}: {ex.Message}"); skipped++; }
            }
            Log.Info($"[PaletteIO] imported={imported} skipped={skipped}");
            return (imported, skipped, problems);
        }

        public static void Delete(string name)
        {
            var path = Path.Combine(PersonalPalettesDir, SafeFileName(name) + ".json");
            if (File.Exists(path)) File.Delete(path);
        }

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }
    }
}
