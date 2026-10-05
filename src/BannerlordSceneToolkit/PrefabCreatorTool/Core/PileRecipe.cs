using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace PrefabCreatorTool.Core
{
    // One whitelisted prefab entry in a pile recipe - "N copies of this prefab, optionally
    // textured, optionally snapped to whatever's below". List ORDER is the layer order: entries
    // are authored top-to-bottom the way the pile should look (first entry = top layer, last =
    // base), but PileGenerator actually PLACES them in reverse (last entry first) since a snapped
    // entry needs whatever it's landing on to already exist. See PileGenerator.Generate.
    public class PileEntry
    {
        public string PrefabName { get; set; } = "";
        public int Count { get; set; } = 1;
        public string PresetName { get; set; } // optional saved ColorPreset ("texture set") name
        public bool SnapToSurface { get; set; } = true;

        // Strip collision from this entry's pieces once the whole pile is built. NOT applied at
        // placement time: layers above this one raycast DOWN onto it to settle, and a piece with no
        // physics is invisible to that raycast, so stripping early would drop everything above it
        // straight through to the terrain. See PileGenerator.StripPhysics.
        public bool DeletePhysics { get; set; } = false;
    }

    public class PileRecipe
    {
        public string Name { get; set; } = "Untitled Pile";
        public float ScatterRadius { get; set; } = 1.5f;

        // Optional tag applied to every piece this recipe places, on top of the internal
        // pile_<anchor> reselect tag. Saved with the recipe so a pile that is meant to be findable
        // later stays findable without retyping it every time.
        public string PlacementTag { get; set; } = "";
        public List<PileEntry> Entries { get; set; } = new List<PileEntry>();
    }

    public static class PileRecipeStore
    {
        private static string RecipesDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "PrefabCreatorTool", "PileRecipes");

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string((name ?? "").Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        public static List<string> ListNames()
        {
            if (!Directory.Exists(RecipesDir)) return new List<string>();
            return Directory.EnumerateFiles(RecipesDir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static void Save(PileRecipe recipe)
        {
            if (recipe == null || string.IsNullOrWhiteSpace(recipe.Name)) return;
            Directory.CreateDirectory(RecipesDir);
            var path = Path.Combine(RecipesDir, SafeFileName(recipe.Name) + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(recipe, Formatting.Indented));
            Log.Info($"Saved pile recipe '{recipe.Name}' ({recipe.Entries.Count} entry(ies)) to {path}");
        }

        public static PileRecipe Load(string name)
        {
            var path = Path.Combine(RecipesDir, SafeFileName(name) + ".json");
            if (!File.Exists(path)) throw new FileNotFoundException($"No pile recipe named '{name}' found in {RecipesDir}");
            return JsonConvert.DeserializeObject<PileRecipe>(File.ReadAllText(path))
                   ?? throw new InvalidDataException($"Recipe '{name}' failed to parse.");
        }

        // EXPORT / IMPORT.
        //
        // Same shape as RecolorPalette's, but under PrefabCreatorTool rather than MaterialSwapTool
        // - a pile recipe belongs to this tool, and pointing it at the other tool's Exports folder
        // would make "zip my Exports and send it" mean two different things depending on which
        // panel you asked. Recipes get their own subfolder for the same reason palettes do: a
        // recipe and a colour preset are both "a .json with a Name" and a folder scan cannot tell
        // them apart otherwise.
        public static string ExportDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "PrefabCreatorTool", "Exports", "PileRecipes");

        public static void OpenExportFolder()
        {
            Directory.CreateDirectory(ExportDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ExportDir) { UseShellExecute = true });
        }

        // name = null exports every saved recipe; pass one to export just that recipe.
        public static (int exported, string dir) Export(string name = null)
        {
            Directory.CreateDirectory(ExportDir);
            var wanted = string.IsNullOrWhiteSpace(name) ? ListNames() : new List<string> { name };
            int exported = 0;
            foreach (var n in wanted)
            {
                try
                {
                    var recipe = Load(n);
                    File.WriteAllText(Path.Combine(ExportDir, SafeFileName(recipe.Name) + ".json"),
                                      JsonConvert.SerializeObject(recipe, Formatting.Indented));
                    exported++;
                }
                catch (Exception ex) { Log.Warn($"Pile recipe export failed for '{n}': {ex.Message}"); }
            }
            Log.Info($"[PileRecipeIO] exported={exported} to {ExportDir}");
            return (exported, ExportDir);
        }

        // Validated rather than trusted: a file that parses but has no usable entries would import
        // as a recipe that loads fine and then generates nothing, which is worse than refusing it.
        // Existing recipes are kept by default - an import should never silently overwrite work.
        public static (int imported, int skipped, List<string> problems) Import(bool overwriteExisting)
        {
            var problems = new List<string>();
            int imported = 0, skipped = 0;

            if (!Directory.Exists(ExportDir))
                return (0, 0, new List<string> { "No Exports\\PileRecipes folder yet - export something first, or drop .json files in there." });

            Directory.CreateDirectory(RecipesDir);
            foreach (var file in Directory.GetFiles(ExportDir, "*.json"))
            {
                var fileName = Path.GetFileName(file);
                try
                {
                    var recipe = JsonConvert.DeserializeObject<PileRecipe>(File.ReadAllText(file));
                    if (recipe == null || string.IsNullOrWhiteSpace(recipe.Name))
                    { problems.Add($"{fileName}: not a pile recipe (no Name)"); skipped++; continue; }

                    var usable = recipe.Entries?.Count(e => !string.IsNullOrWhiteSpace(e.PrefabName)) ?? 0;
                    if (usable == 0)
                    { problems.Add($"{fileName}: recipe '{recipe.Name}' has no entries with a prefab name"); skipped++; continue; }

                    var target = Path.Combine(RecipesDir, SafeFileName(recipe.Name) + ".json");
                    if (File.Exists(target) && !overwriteExisting)
                    { problems.Add($"{fileName}: '{recipe.Name}' already exists (kept yours)"); skipped++; continue; }

                    Save(recipe);
                    imported++;
                }
                catch (Exception ex) { problems.Add($"{fileName}: {ex.Message}"); skipped++; }
            }

            Log.Info($"[PileRecipeIO] imported={imported} skipped={skipped}");
            return (imported, skipped, problems);
        }

        public static void Delete(string name)
        {
            var path = Path.Combine(RecipesDir, SafeFileName(name) + ".json");
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
