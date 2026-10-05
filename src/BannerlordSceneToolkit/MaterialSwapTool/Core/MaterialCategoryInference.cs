using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace MaterialSwapTool.Core
{
    public class CategoryDefinition
    {
        public List<string> Include { get; set; } = new List<string>();
        public List<string> Exclude { get; set; } = new List<string>();
    }

    // Category definitions used to be a hardcoded Dictionary baked into this file - fixing a gap
    // like "empire_wall_a doesn't match stone" meant editing code and waiting for a DLL redeploy.
    // Now they're user-editable at runtime via the Continuous Recolor category editor and persist
    // to a Documents-folder override file, same convention as Personal presets vs. BuiltIn ones:
    // ReferenceData\material_categories.json (source-controlled, deployed with the module) is the
    // factory-default fallback; Documents\...\MaterialSwapTool\material_categories.json, once it
    // exists, always wins and is never overwritten except by an explicit Save from the editor.
    //
    // A material can belong to MULTIPLE categories now (Classify returns a list, not a single best
    // guess) - "wall" in particular is meant to overlap with material-type categories (stone,
    // adobe, wood, timberframe) rather than compete with them, since walls exist in all of those
    // materials. Exclude patterns exist so a broad Include pattern (e.g. "empire_wall" catching
    // every empire_wall_* stone material) can carve out a specific false positive (empire_wall_
    // wicker_a/b, which is woven, not stone) without needing narrower, more fragile Include
    // patterns instead.
    public static class MaterialCategoryInference
    {
        private static Dictionary<string, CategoryDefinition> _categories;

        private static string ModuleDir => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "..", ".."));

        private static string ShippedDefaultPath => Path.Combine(ModuleDir, "ReferenceData", "material_categories.json");

        private static string UserOverridePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "MaterialSwapTool", "material_categories.json");

        private static Dictionary<string, CategoryDefinition> Categories
        {
            get
            {
                if (_categories == null) Load();
                return _categories;
            }
        }

        private static void Load()
        {
            var path = File.Exists(UserOverridePath) ? UserOverridePath : ShippedDefaultPath;
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    _categories = JsonConvert.DeserializeObject<Dictionary<string, CategoryDefinition>>(json)
                                  ?? new Dictionary<string, CategoryDefinition>(StringComparer.OrdinalIgnoreCase);
                    return;
                }
                Log.Warn($"MaterialCategoryInference: no category file found at '{path}' - no categories loaded.");
            }
            catch (Exception ex)
            {
                Log.Error("MaterialCategoryInference: failed to load category definitions: " + ex);
            }
            _categories = new Dictionary<string, CategoryDefinition>(StringComparer.OrdinalIgnoreCase);
        }

        // Re-reads from disk - called after the category editor saves changes, so edits take
        // effect immediately with no DLL redeploy needed.
        public static void Reload() => Load();

        public static string[] AllCategories => Categories.Keys.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray();

        public static Dictionary<string, CategoryDefinition> GetAllForEditing() =>
            Categories.ToDictionary(kvp => kvp.Key, kvp => new CategoryDefinition
            {
                Include = new List<string>(kvp.Value.Include),
                Exclude = new List<string>(kvp.Value.Exclude),
            }, StringComparer.OrdinalIgnoreCase);

        // Always writes to the user-override path - the shipped ReferenceData default is never
        // touched by runtime edits, so "delete my override file" is always a clean factory reset.
        public static void Save(Dictionary<string, CategoryDefinition> categories)
        {
            _categories = categories;
            try
            {
                var dir = Path.GetDirectoryName(UserOverridePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(UserOverridePath, JsonConvert.SerializeObject(categories, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Log.Error("MaterialCategoryInference: failed to save category definitions: " + ex);
            }
        }

        // --- Import / export (v0.7) -------------------------------------------------------
        //
        // Same Exports-folder convention as presets and palettes, own Categories subfolder for
        // the same reason palettes have one: a preset, a palette and a category file are all
        // "a .json" and a folder scan could not otherwise tell them apart.
        //
        // Import MERGES rather than replaces: unlike presets/palettes (collections of named
        // items where a clash keeps yours), the categories are ONE dictionary - replacing it
        // wholesale with an imported file would silently throw away every local edit. So new
        // categories are added, existing ones gain any patterns they were missing, and nothing
        // is ever removed. Deleting patterns stays a job for the Category Editor.
        public static string ExportDir => Path.Combine(MaterialSwapPreset.ExportDir, "Categories");

        public static void OpenExportFolder()
        {
            Directory.CreateDirectory(ExportDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ExportDir) { UseShellExecute = true });
        }

        public static (int categories, string dir) Export()
        {
            Directory.CreateDirectory(ExportDir);
            var snapshot = Categories;
            File.WriteAllText(Path.Combine(ExportDir, "material_categories.json"),
                JsonConvert.SerializeObject(snapshot, Formatting.Indented));
            Log.Info($"[CategoryIO] exported {snapshot.Count} categor(y/ies) to {ExportDir}");
            return (snapshot.Count, ExportDir);
        }

        public static (int categoriesAdded, int patternsAdded, List<string> problems) Import()
        {
            var problems = new List<string>();
            if (!Directory.Exists(ExportDir))
                return (0, 0, new List<string> { "No Exports\\Categories folder yet - export something first, or drop a .json file in there." });

            int categoriesAdded = 0, patternsAdded = 0;
            var working = GetAllForEditing();

            foreach (var file in Directory.GetFiles(ExportDir, "*.json"))
            {
                Dictionary<string, CategoryDefinition> incoming;
                try
                {
                    incoming = JsonConvert.DeserializeObject<Dictionary<string, CategoryDefinition>>(File.ReadAllText(file));
                }
                catch (Exception ex)
                {
                    problems.Add($"{Path.GetFileName(file)}: {ex.Message}");
                    continue;
                }

                // Validated rather than trusted, same policy as every other import: a file that
                // parses but carries no usable Include pattern would merge cleanly and change
                // nothing, which reads as "import is broken".
                if (incoming == null || !incoming.Any(kvp => kvp.Value?.Include?.Any(p => !string.IsNullOrWhiteSpace(p)) == true))
                {
                    problems.Add($"{Path.GetFileName(file)}: no category with an Include pattern - refused.");
                    continue;
                }

                foreach (var kvp in incoming)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key) || kvp.Value == null) continue;

                    if (!working.TryGetValue(kvp.Key, out var mine))
                    {
                        working[kvp.Key] = new CategoryDefinition
                        {
                            Include = (kvp.Value.Include ?? new List<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList(),
                            Exclude = (kvp.Value.Exclude ?? new List<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList(),
                        };
                        categoriesAdded++;
                        continue;
                    }

                    patternsAdded += MergePatterns(mine.Include, kvp.Value.Include);
                    patternsAdded += MergePatterns(mine.Exclude, kvp.Value.Exclude);
                }
            }

            if (categoriesAdded > 0 || patternsAdded > 0)
            {
                Save(working);
                Reload();
            }

            Log.Info($"[CategoryIO] import: +{categoriesAdded} categor(y/ies), +{patternsAdded} pattern(s), {problems.Count} problem(s)");
            return (categoriesAdded, patternsAdded, problems);
        }

        private static int MergePatterns(List<string> mine, List<string> theirs)
        {
            if (theirs == null) return 0;
            int added = 0;
            foreach (var pattern in theirs)
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;
                if (mine.Any(p => string.Equals(p, pattern, StringComparison.OrdinalIgnoreCase))) continue;
                mine.Add(pattern);
                added++;
            }
            return added;
        }

        // Every category whose Include patterns match and Exclude patterns don't - a material can
        // land in more than one, deliberately (see class comment).
        public static List<string> Classify(string materialName)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(materialName)) return result;
            var lower = materialName.ToLowerInvariant();
            foreach (var kvp in Categories)
            {
                var def = kvp.Value;
                bool included = def.Include.Any(p => !string.IsNullOrWhiteSpace(p) && lower.Contains(p.ToLowerInvariant()));
                if (!included) continue;
                bool excluded = def.Exclude.Any(p => !string.IsNullOrWhiteSpace(p) && lower.Contains(p.ToLowerInvariant()));
                if (!excluded) result.Add(kvp.Key);
            }
            return result;
        }

        public static bool IsCategory(string materialName, string category) =>
            Classify(materialName).Contains(category, StringComparer.OrdinalIgnoreCase);
    }
}
